# ADR-0003 — Catalogo, distribuzione e verifica dei modelli

- Stato: Proposto
- Data: 2026-10-14
- Issue: #1 (implementazione in #4 e #8)

## Contesto

I modelli Whisper vanno da decine di MB a qualche GB. Devono stare sul PC, essere verificati prima dell'uso, essere scelti in base all'hardware con override manuale, e l'app deve mostrare spazio, requisiti e stato. La rete può servire solo a scaricarli su richiesta esplicita (ADR-0006).

## Opzioni considerate

### Origine dei file

| Opzione | Pro | Contro |
|---|---|---|
| **Hugging Face `ggerganov/whisper.cpp`, URL fissato a una revisione (commit) del repository** | Fonte di riferimento dei file GGML, CDN affidabile, nessun costo | Dipendenza da un terzo; il contenuto di `main` può cambiare (mitigato da revisione fissata + hash) |
| Mirror proprio (release GitHub o storage) | Controllo completo | Costi e manutenzione; i file più grandi superano i limiti degli asset di release |
| Conversione locale dai pesi PyTorch | Nessun file GGML di terzi | Richiede Python: escluso |

### Distribuzione con l'installer

| Opzione | Pro | Contro |
|---|---|---|
| **Installer senza modelli + procedura guidata al primo avvio + import da file** | Installer leggero; l'utente sceglie il modello e vede spazio e requisiti prima del download | Serve la rete una volta, oppure un file modello ottenuto altrove |
| Modello piccolo (`base`, ~142 MiB) incluso | Funziona offline subito | Installer più pesante; `base` non è sempre il modello migliore per la macchina; aggiornamenti dell'app più pesanti |
| Pacchetto "offline" separato (installer + modelli) | Utile per ambienti senza rete | Più artefatti da mantenere |

## Decisione

1. **Catalogo modelli incluso nell'app** (`models-catalog.json` incorporato come risorsa e versionato con l'app). Ogni voce contiene:
   - `id`, nome visualizzato, `file`, `quantization`;
   - `sizeBytes`, `sha256`, `url` (revisione fissata);
   - stime di RAM (`ramRequiredBytes`) e di prestazioni (`speedClass`, `qualityClass`);
   - lingue (multilingua / solo inglese) e licenza (MIT).

   Il catalogo non viene scaricato dalla rete in V1: si aggiorna con l'app.
2. **Profili iniziali.** Le dimensioni vengono dalla documentazione di whisper.cpp, così come la memoria indicativa; hash e misure vanno fissati in #4.

   | id | File | Dimensione | Memoria indicativa | Uso previsto |
   |---|---|---|---|---|
   | `tiny` | ggml-tiny.bin | ~75 MiB | ~0,3 GB | PC molto limitati, prove |
   | `base` | ggml-base.bin | ~142 MiB | ~0,4 GB | Minimo consigliato |
   | `small` | ggml-small.bin | ~466 MiB | ~0,9 GB | Default atteso per PC medi |
   | `medium` | ggml-medium.bin | ~1,5 GiB | ~2,1 GB | Qualità alta, lento su CPU |
   | `large-v3-turbo-q5_0` | ggml-large-v3-turbo-q5_0.bin | ~547 MiB | da misurare | Miglior rapporto qualità/costo, da validare |
   | `large-v3` | ggml-large-v3.bin | ~2,9 GiB | ~3,9 GB | Solo override manuale su PC potenti |

   Le varianti solo inglese (`*.en`) restano fuori dal catalogo V1, perché l'utenza attesa è italiana. Si possono aggiungere se #2 lo richiede.
3. **Selezione adattiva deterministica** in `CodexVtt.Core`. È una funzione pura `(ProfiloHardware, Catalogo) → Raccomandazione + motivazione`.
   - **Esclusione:** sono esclusi i modelli che superano la RAM disponibile con un margine, oppure lo spazio disco libero.
   - **Scelta:** tra i modelli rimasti si sceglie il più accurato la cui velocità stimata sia accettabile (soglia di RTF definita in #2 o #4) per i core e le estensioni CPU rilevati.
   - **Fonte delle soglie:** vengono dai benchmark (#9) e sono parte del catalogo, non del codice.
4. **Override manuale** sempre consentito tra i modelli installati. Se il modello supera le capacità rilevate, l'app mostra un avviso esplicito ma non lo blocca, salvo spazio disco insufficiente.
5. **Download** (solo `CodexVtt.Models.Download`, solo su azione esplicita):
   - controllo spazio prima di iniziare;
   - scrittura in `models\.partial\<id>.part`, con ripresa via HTTP Range;
   - hash SHA-256 calcolato durante il download;
   - spostamento atomico in `models\<file>` solo se l'hash coincide, altrimenti il file viene eliminato e lo stato diventa `corrotto`.
6. **Verifica all'uso:** prima del primo uso nella sessione si confrontano dimensione e hash. Il ricalcolo completo dell'hash si fa all'installazione e on demand; all'avvio basta un controllo veloce di dimensione e data di modifica, con ricalcolo se cambiano.
7. **Import manuale** di un file `ggml-*.bin` già presente sul disco: viene accettato se l'hash corrisponde a una voce del catalogo. I modelli personalizzati fuori catalogo sono un non-obiettivo di V1.
8. **Stati del modello:** `non_installato`, `in_download`, `in_verifica`, `pronto` e `corrotto`, esposti alla UI con dimensione, requisiti e motivazione della raccomandazione.
9. **Installer senza modelli.** Il primo avvio guida l'utente: mostra la raccomandazione con spazio e requisiti, scarica previo consenso oppure importa da file. Un eventuale pacchetto offline con modelli è un artefatto opzionale valutato in #8.
10. **Il modello effettivamente usato** (id, file, hash) viene salvato nel risultato di ogni trascrizione e mostrato in UI ed export (#3, #6).

## Conseguenze

- Il catalogo è l'unica fonte di verità su URL e hash. Cambiare la revisione dei modelli richiede una nuova versione dell'app e quindi una revisione del codice.
- Nessuna chiamata di rete avviene all'avvio o durante la trascrizione.
- I benchmark di #9 alimentano le soglie del catalogo.

## Punti aperti

- Hash SHA-256 e revisione Hugging Face da fissare scaricando e verificando i file (#4).
- Inclusione di `large-v3-turbo-q5_0`: dipende da qualità e prestazioni misurate su CPU.
- Mirror proprio dei modelli: da rivalutare se la dipendenza da Hugging Face diventa un rischio, ad esempio per disponibilità o blocchi aziendali.
