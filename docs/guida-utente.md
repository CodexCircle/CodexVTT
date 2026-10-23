# Guida rapida di CodexVTT

CodexVTT trascrive file audio in testo **sul tuo PC**, con i modelli Whisper di OpenAI. Audio e testo non vengono mai inviati su Internet.

## Requisiti

- Windows 11 a 64 bit
- Almeno 4 GB di RAM (16 GB consigliati per i modelli più accurati)
- Spazio su disco per il modello: da 75 MB a 3 GB

## Installazione

1. Scarica `CodexVTT-win-Setup.exe` dalla pagina delle release.
2. Avvialo: CodexVTT si installa nel tuo profilo utente, senza privilegi di amministratore.
3. L'installer non è firmato con un certificato, quindi Windows mostra "Windows ha protetto il PC" (SmartScreen). Prima di procedere, verifica che il checksum SHA-256 del file coincida con quello pubblicato con la release (in PowerShell: `Get-FileHash .\CodexVTT-win-Setup.exe`), poi scegli "Ulteriori informazioni" → "Esegui comunque".

## Primo avvio: scegliere un modello

1. Apri la sezione **Modelli**: vedi le caratteristiche del PC e il modello consigliato, con il motivo della scelta.
2. Premi **Scarica…** sul modello che preferisci. CodexVTT mostra la dimensione e il sito di provenienza (huggingface.co) e chiede conferma.
3. Al termine il modello viene verificato (SHA-256) e diventa **Pronto**.

Senza connessione puoi usare **Importa modello da file…** con un file `ggml-*.bin` ufficiale copiato da un altro computer.

| Modello | Dimensione | Quando usarlo |
|---|---|---|
| Tiny / Base | 75–150 MB | PC poco potenti, prove veloci |
| Small | ~490 MB | Buon compromesso per la maggior parte dei PC |
| Large v3 Turbo (q5_0) | ~570 MB | Qualità alta su PC con 8+ core |
| Medium / Large v3 | 1,5–3 GB | Massima qualità, molto lenti senza un PC potente |

## Trascrivere

1. Sezione **Trascrivi** → **Scegli un file audio…** (WAV, MP3, M4A, AAC, WMA, FLAC; OGG/Opus se il codec è installato), fino a **4 ore** di durata.
2. Lascia "Rilevamento automatico" oppure scegli la lingua.
3. Premi **Avvia trascrizione**. Puoi annullare in qualunque momento.
4. Leggi il testo o la vista per segmenti con i tempi. Sotto il titolo trovi il modello usato.

Whisper può sbagliare o inventare frasi su silenzi o musica: ricontrolla i passaggi importanti.

## Cronologia

Ogni trascrizione completata viene salvata automaticamente nella cartella **Documenti\CodexVTT\history**, in due file con lo stesso nome (data, ora e nome dell'audio):

- un `.txt` con il testo, che puoi aprire con qualunque editor;
- un `.json` con i segmenti e i tempi, che CodexVTT usa per riaprire la trascrizione.

Dalla sezione **Cronologia** puoi riaprire una trascrizione, esportarla di nuovo, eliminarla oppure aprire la cartella in Esplora file. Con **Cambia cartella…** scegli dove salvarle.

Se su Windows è attivo **Accesso alle cartelle controllato** (Sicurezza di Windows → Protezione da virus e minacce → Protezione ransomware), Defender può impedire a CodexVTT di scrivere in `Documenti`. In quel caso CodexVTT te lo dice, mette la trascrizione al sicuro in `%LOCALAPPDATA%\CodexVTT.Data\history-recovery` e ti propone **Cambia cartella…**. Per usare `Documenti` consenti `CodexVtt.App.exe` tra le app consentite; lo stesso vale per l'export nella cartella proposta dal dialogo di salvataggio.

Se la cartella è sincronizzata da OneDrive o da un servizio simile, le trascrizioni verranno copiate nel cloud da quel servizio, non da CodexVTT: scegli una cartella non sincronizzata se vuoi che restino solo sul PC.

## Esportare

**Esporta TXT** salva il testo semplice, **Esporta SRT** e **Esporta VTT** salvano i sottotitoli con i tempi. Nelle impostazioni puoi aggiungere un'intestazione con modello, lingua e data.

## Lingua dell'interfaccia

CodexVTT è in italiano e in inglese. Per impostazione predefinita usa la lingua di Windows; puoi cambiarla in **Impostazioni** → **Lingua dell'interfaccia**.

In **Impostazioni** → **Tema** scegli i colori dell'app: **Sabbia** (predefinito, toni beige e tortora) o **Natale** (bordeaux, verde e crema). **Aspetto** decide se usare la versione chiara, quella scura o seguire Windows; ogni tema ha entrambe.

## Privacy

- La trascrizione avviene interamente sul tuo PC; funziona anche senza Internet.
- CodexVTT si collega alla rete solo per scaricare un modello quando lo chiedi (verso Hugging Face) e per cercare nuove versioni su GitHub quando premi «Controlla aggiornamenti» in Impostazioni, o all'avvio se attivi tu la casella corrispondente (spenta di default). Nessun audio né testo viene inviato.
- Nessun account, nessuna telemetria.
- I log diagnostici restano in `%LOCALAPPDATA%\CodexVTT.Data\logs` e non contengono il testo trascritto.

## Dove sono i file

| Cosa | Percorso |
|---|---|
| Modelli | `%LOCALAPPDATA%\CodexVTT.Data\models` |
| Impostazioni | `%LOCALAPPDATA%\CodexVTT.Data\settings.json` |
| Log | `%LOCALAPPDATA%\CodexVTT.Data\logs` |
| Cronologia delle trascrizioni | `Documenti\CodexVTT\history` (modificabile) |

Aggiornare CodexVTT non cancella modelli, impostazioni e log.

## Disinstallazione

Disinstalla CodexVTT da **Impostazioni → App installate** (o dal Pannello di controllo). Durante la disinstallazione una procedura guidata chiede:

1. se eliminare anche i **dati interni** di CodexVTT: modelli Whisper scaricati (con la loro dimensione), log, impostazioni e file temporanei in `%LOCALAPPDATA%\CodexVTT.Data`;
2. una conferma definitiva, se hai risposto sì.

Il pulsante predefinito è **No**: senza una risposta esplicita i dati restano sul PC. La cronologia in `Documenti\CodexVTT` non viene mai toccata: è un file tuo, da cancellare a mano se vuoi.

## Problemi comuni

| Messaggio | Soluzione |
|---|---|
| Il motore di trascrizione non si avvia | Installa "Microsoft Visual C++ Redistributable (x64)" e riavvia |
| Manca il codec per questo formato | Installa le estensioni multimediali dal Microsoft Store o converti in MP3/WAV |
| Il modello è danneggiato | Sezione Modelli → Rimuovi → Scarica di nuovo |
| Memoria insufficiente | Chiudi altre applicazioni o usa un modello più piccolo |
