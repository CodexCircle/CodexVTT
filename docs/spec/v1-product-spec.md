# Specifica di prodotto — CodexVTT V1 (Windows)

- Issue: #2
- Stato: Proposta; decisioni del maintainer del 2026-10-14 incorporate (Windows 11, 4 ore, nessuna traduzione, cronologia, interfaccia IT/EN, installer non firmato)
- Riferimenti: [ADR](../adr/README.md), [perimetro V1](../architecture/perimetro-v1.md)

I requisiti hanno identificativi stabili (`REQ-xxx`) usati dai test e dalla checklist di accettazione.

## 1. Obiettivo e criterio di successo

Un utente Windows installa CodexVTT, installa o seleziona un modello Whisper locale, carica un file audio supportato e ottiene una trascrizione verificabile **senza alcun servizio di inferenza online**, anche con la rete disattivata dopo l'installazione del modello.

## 2. Persone e scenari

| Persona | Bisogno | Scenario principale |
|---|---|---|
| Studente | "Sbobinare" lezioni registrate (1–3 h) | Importa MP3/M4A dal telefono, trascrive, esporta TXT |
| Professionista | Verbali di riunioni riservate | Trascrive offline, esporta TXT con intestazione, nessun dato in cloud |
| Creatore di contenuti | Sottotitoli | Esporta SRT/VTT con timestamp |

Flussi principali:

1. **Primo avvio:** benvenuto, profilo del PC, modello consigliato con spazio e requisiti, poi download (con consenso) o import da file.
2. **Trascrizione:** scegli file, eventualmente lingua, avvia, segui il progresso (con possibilità di annullare), leggi il risultato.
3. **Export:** TXT, SRT o VTT con dialog "Salva con nome".
4. **Gestione modelli:** elenco, stato, download, verifica, rimozione, cambio modello.
5. **Uso offline:** tutti i flussi tranne il download funzionano senza rete.

## 3. Requisiti di sistema

| | Minimo | Consigliato |
|---|---|---|
| Sistema operativo | Windows 11 x64 | Windows 11 x64 aggiornato |
| CPU | x64; AVX2/FMA/F16C consigliati (senza: modalità compatibile più lenta) | 8+ core logici |
| RAM | 4 GB (modelli tiny/base) | 16 GB |
| Disco | 500 MB + modello (75 MB – 3 GB) | 5 GB liberi |
| GPU | Non usata in V1 | — |

- **REQ-001** Requisito minimo **Windows 11 x64** (confermato). Installer e documentazione lo indicano chiaramente; Windows 10 non è supportato.
- **REQ-002** Se manca il runtime nativo (VC++), oppure se la CPU non supporta il runtime standard, l'app mostra un messaggio con l'azione correttiva (`runtime_unavailable`), senza crash.

## 4. Audio

- **REQ-010** Formati supportati: WAV, MP3, M4A/AAC, WMA, FLAC. OGG/Opus sono supportati solo se il codec è presente in Windows; altrimenti si mostra l'errore `audio_codec_missing` con la soluzione.
- **REQ-011** Il formato si valida dal contenuto, non solo dall'estensione. Se i file sono corrotti, vuoti o non supportati, l'app restituisce errori tipizzati.
- **REQ-012** Durata massima: **4 ore**. Oltre questa durata compare l'errore `audio_too_long`. La dimensione non ha un limite separato.
- **REQ-013** La memoria resta limitata anche per file lunghi: l'elaborazione avviene a blocchi di circa 10 minuti, tagliati nei silenzi.
- **REQ-014** L'app mostra nome, durata, formato e dimensione del file scelto.
- **REQ-015** I file temporanei vengono rimossi a fine lavoro, all'annullamento e al riavvio successivo a un crash.

## 5. Lingue e opzioni

- **REQ-020** Lingua: rilevamento automatico (default) oppure scelta manuale (italiano, inglese, spagnolo, francese, tedesco, portoghese; elenco estendibile).
- **REQ-021** **Nessuna traduzione in V1**: l'opzione `translate` non è esposta né nell'interfaccia né nel bridge. Un'eventuale aggiunta è tracciata nella issue #17.
- **REQ-022** La lingua predefinita si imposta nelle impostazioni.

## 6. Modelli

- **REQ-030** Catalogo: tiny, base, small, large-v3-turbo-q5_0, medium, large-v3 (GGML, MIT). Per ognuno si mostrano dimensione, RAM stimata, quantizzazione, licenza e stato.
- **REQ-031** Stati: non installato, download in corso, verifica in corso, da verificare, pronto, danneggiato.
- **REQ-032** Raccomandazione automatica in base a RAM, core, estensioni CPU e disco, con motivazione leggibile. Vengono proposti solo modelli compatibili.
- **REQ-033** Override: l'utente può usare qualunque modello installato. I modelli oltre le capacità rilevate sono segnalati ("troppo pesante per il processore", "RAM insufficiente") ma non bloccati.
- **REQ-034** Il download parte solo dopo una conferma che mostra dimensione, host (huggingface.co) e la frase "Nessun audio o testo viene inviato". È possibile annullarlo e riprenderlo.
- **REQ-035** Ogni modello viene verificato con SHA-256 rispetto al catalogo prima dell'uso. Un modello che non supera la verifica non viene mai usato.
- **REQ-036** È possibile importare da file un modello già presente su disco, se il suo hash è nel catalogo.
- **REQ-037** Il modello effettivamente usato (nome, file, runtime) è visibile durante e dopo la trascrizione ed è incluso nell'intestazione opzionale dell'export.
- **REQ-038** L'app non preinstalla alcun modello: l'installer resta leggero e il modello si sceglie al primo avvio.

## 7. Trascrizione

- **REQ-040** Una trascrizione alla volta. Durante la trascrizione l'avanzamento è mostrato in percentuale e tempo elaborato.
- **REQ-041** L'annullamento ferma il lavoro in pochi secondi e libera le risorse.
- **REQ-042** Il risultato mostra il testo completo e la vista a segmenti con timestamp.
- **REQ-043** Avviso permanente: Whisper può sbagliare o inventare frasi su silenzi e musica.
- **REQ-044** Cronologia: ogni trascrizione completata viene salvata **automaticamente** in una **cartella visibile** sul disco (default `Documenti\CodexVTT\history`, modificabile nelle impostazioni).
  - Ogni trascrizione ha **file propri** con lo stesso nome base `AAAA-MM-GG HH.mm.ss - <nome audio>`: un `.txt` leggibile (con intestazione) e un `.json` con segmenti e metadati, usato per riaprirla.
  - La sezione Cronologia elenca le trascrizioni (dalla più recente), le riapre, le esporta in TXT/SRT/VTT e le elimina (entrambi i file, dopo conferma); un pulsante apre la cartella in Esplora file.
  - Se il salvataggio fallisce (cartella non scrivibile) la trascrizione resta visibile e compare l'errore `history_save_failed`; se è stato possibile metterla al sicuro nella cartella dati locale (`history-recovery`, tipico con "Accesso alle cartelle controllato" di Defender) l'errore è `history_folder_blocked`, con il pulsante **Cambia cartella…**.

## 8. Export

- **REQ-050** Formati TXT, SRT e WebVTT, codificati in UTF-8 senza BOM, con salvataggio atomico.
- **REQ-051** Il nome predefinito è quello del file audio con la nuova estensione. La destinazione si sceglie con il dialog nativo.
- **REQ-052** Intestazione opzionale (TXT e VTT): file, modello e hash, lingua, data, e la nota "elaborata localmente".
- **REQ-053** Copia del testo negli appunti.

## 9. Privacy e rete

- **REQ-060** Audio, testo e metadati non vengono mai inviati a servizi online.
- **REQ-061** Le uniche connessioni sono il download dei modelli, avviato dall'utente, verso host in allowlist. La WebView blocca ogni altra richiesta.
- **REQ-062** Nessuna telemetria e nessun crash report online. I log restano locali, ruotati e senza testo trascritto.
- **REQ-063** Il badge "Elaborazione locale" e la sezione Privacy nelle impostazioni spiegano il comportamento.
- **REQ-064** Con la rete disattivata, avvio, gestione dei modelli installati, trascrizione ed export funzionano.

## 10. Errori

Ogni errore mostra un titolo, l'azione correttiva e il codice. I testi sono in `ui/src/i18n/it.ts`.

| Codice | Messaggio | Azione |
|---|---|---|
| `audio_unsupported` | Formato audio non supportato | Usare WAV, MP3, M4A/AAC, WMA, FLAC, OGG, Opus |
| `audio_codec_missing` | Manca il codec | Estensioni multimediali dallo Store o conversione |
| `audio_corrupted` / `audio_empty` / `audio_too_long` (corrotto anche se il contenuto non è del formato dell'estensione o i campioni decodificati sono meno della metà della durata dichiarata) | File danneggiato / vuoto / troppo lungo | Riesportare / altro file / dividere |
| `model_not_installed` | Nessun modello installato | Sezione Modelli |
| `model_hash_mismatch` | Modello danneggiato | Rimuovere e riscaricare |
| `model_not_verified` | Modello non ancora scaricabile | Import da file o aggiornamento |
| `insufficient_disk` / `insufficient_memory` | Spazio / memoria insufficiente | Liberare risorse o modello più piccolo |
| `download_failed` | Download non riuscito | Riprovare (ripresa automatica) |
| `runtime_unavailable` | Motore non avviabile | Installare VC++ Redistributable |
| `transcription_failed` | Trascrizione non riuscita | Riprovare o cambiare modello |
| `export_failed` | Impossibile salvare | Altra cartella o nome |
| `history_folder_blocked` | Defender impedisce di scrivere nella cronologia; copia in `history-recovery` | Consentire CodexVTT o cambiare cartella |
| `history_save_failed` / `history_not_found` | Cronologia non salvabile / voce non trovata | Controllare la cartella della cronologia |

## 11. Accessibilità e lingua dell'interfaccia

- **REQ-070** Interfaccia in **italiano e inglese**. La lingua si sceglie nelle impostazioni; per default segue quella di sistema (italiano se il sistema è in italiano, altrimenti inglese). Sono tradotti anche le motivazioni della raccomandazione, i messaggi d'errore, l'intestazione degli export e i dialog nativi di Windows.
- **REQ-071** Uso completo da tastiera, focus visibile, ruoli ARIA (barra di avanzamento, avvisi, dialog), contrasto adeguato in tema chiaro e scuro.
- **REQ-072** Animazioni (mascotte, #15) disattivate con `prefers-reduced-motion` e disattivabili dalle impostazioni; non sono mai l'unico veicolo di un'informazione.

## 12. Non-obiettivi V1

Vedi [perimetro V1](../architecture/perimetro-v1.md): traduzione (#17), GPU, Windows 10/ARM64, player sincronizzato, diarization, microfono in tempo reale, job multipli, modelli fuori catalogo, Store/MSI, firma del codice, account e qualunque inferenza online.

## 13. Checklist di accettazione V1 (input per #9)

- [ ] Installazione su Windows 11 pulito senza privilegi di amministratore (REQ-001, #8).
- [ ] Primo avvio: raccomandazione motivata, download con conferma, verifica hash (REQ-032…035).
- [ ] Import da file di un modello valido, rifiuto di un file estraneo (REQ-036).
- [ ] Rete disattivata: trascrizione di un MP3 e di un WAV di riferimento con testo verificabile (REQ-064).
- [ ] Annullamento entro pochi secondi (REQ-041).
- [ ] Export TXT/SRT/VTT validi, intestazione con modello usato (REQ-050…052, REQ-037).
- [ ] Errori: file corrotto, formato non supportato, modello danneggiato mostrano messaggio e azione (§10).
- [ ] Cattura del traffico durante import → trascrizione → export: nessuna connessione (REQ-060…062).
- [ ] Uso completo da tastiera e con screen reader sui flussi principali (REQ-071).
- [ ] Cronologia: ogni trascrizione crea i propri file `.txt` e `.json` nella cartella visibile; riapertura, export ed eliminazione funzionano; cambio cartella (REQ-044).
- [ ] Interfaccia completa in italiano e in inglese, cambio lingua dalle impostazioni (REQ-070).
- [ ] File audio di circa 4 ore accettato, oltre le 4 ore rifiutato con messaggio (REQ-012).
- [ ] Aggiornamento e disinstallazione (#8); la cronologia in `Documenti` non viene toccata dalla disinstallazione.
