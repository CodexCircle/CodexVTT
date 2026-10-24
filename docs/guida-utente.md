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
| Parakeet TDT 0.6B v3 *(anteprima)* | ~670 MB | Molto veloce anche sulla CPU, rileva la lingua da solo |
| Canary 1B v2 *(anteprima)* | ~1,4 GB | Qualità simile a Parakeet, più lento; **richiede di scegliere la lingua** |
| Cohere Transcribe *(anteprima)* | ~2,9 GB | 14 lingue, pesante; **richiede di scegliere la lingua**, senza tempi per frase |

I modelli *anteprima* sono motori nuovi, provati su pochi PC: non vengono mai consigliati in automatico, ma li puoi scegliere. Girano sulla CPU. Accanto a ogni modello vedi l'**affidabilità** per l'italiano (100 meno l'errore sulle parole) quando c'è un valore noto: oggi è quello pubblicato dagli autori del modello, non una misura di CodexVTT, e su registrazioni rumorose è più bassa. I modelli di NVIDIA richiedono di citare l'autore (licenza CC-BY-4.0): l'indicazione è sulla scheda del modello.

### Scheda video (GPU)

Se il PC ha una scheda video NVIDIA, AMD o Intel con i driver aggiornati, Whisper la usa da solo (puoi spegnerla in **Impostazioni → Prestazioni**; si applica al riavvio). In **Modelli** vedi la scheda trovata e, nel risultato, il runtime usato: «Vulkan (GPU)» oppure «Cpu». Senza scheda compatibile si usa la CPU. Parakeet, Canary e Cohere girano sempre sulla CPU.

## Trascrivere

La schermata **Home** ha a sinistra l'elenco dei file (la coda di lavoro e, sotto, le trascrizioni **Recenti**), al centro l'editor e a destra le proprietà del file scelto.

1. In **Home** premi **Scegli file** (WAV, MP3, M4A, AAC, WMA, FLAC; OGG/Opus se il codec è installato), fino a **4 ore** di durata ciascuno. Con più file nasce una coda.
2. Scegli il modello e la lingua (oppure lascia «Rilevamento automatico»).
3. Premi **Avvia trascrizione** (o **Avvia coda**). **Pausa** e **Annulla** sono in basso a sinistra. Durante il lavoro vedi la percentuale e il **tempo rimanente**: è una stima calcolata dalla velocità reale del tuo PC con quel modello, compare dopo qualche secondo e con una coda indica anche il tempo per tutti i file.
4. Scegli un file dall'elenco per vederne subito la trascrizione, senza aprirlo: un file in coda o in lavorazione mostra il suo stato, uno finito mostra l'editor. **Nuova trascrizione** riporta alla scelta dei file.

Whisper può sbagliare o inventare frasi su silenzi o musica: ricontrolla i passaggi importanti.

### L'editor

- Ogni **segmento** ha il suo testo, modificabile, e i tempi di inizio e fine: cliccarli sposta il cursore della timeline. Con **Annulla modifica** e **Ripristina modifica** torni indietro; il pulsante tra due segmenti li unisce.
- Il **parlante** di ogni segmento si assegna a mano (clic sul nome a sinistra); in basso, **+** aggiunge un parlante, e nel pannello a destra si rinominano. Il riconoscimento automatico dei parlanti non c'è ancora e i parlanti non finiscono nei file esportati.
- In basso la **timeline** mostra i segmenti nel tempo, con zoom e velocità di riproduzione. Per ora il lettore muove soltanto il cursore: l'audio non si ascolta ancora.
- **Controllo ortografico** (pannello a destra) usa un piccolo dizionario locale degli errori più comuni («perchè», «sopratutto», «un pò»…): **Accetta** applica la correzione, **Ignora** la scarta.
- **Copia testo** copia il testo corretto. Le correzioni vivono solo nella finestra: **Esporta** salva il testo originale, e l'indicatore in alto lo ricorda con «Modifiche non salvate».

## Cronologia

Ogni trascrizione completata viene salvata automaticamente nella cartella **Documenti\CodexVTT\history**, in due file con lo stesso nome (data, ora e nome dell'audio):

- un `.txt` con il testo, che puoi aprire con qualunque editor;
- un `.json` con i segmenti e i tempi, che CodexVTT usa per riaprire la trascrizione.

Dalla sezione **Cronologia** (o dall'elenco **Recenti** di Home) puoi riaprire una trascrizione, esportarla di nuovo, eliminarla oppure aprire la cartella in Esplora file. Con **Cambia cartella…** scegli dove salvarle.

Se su Windows è attivo **Accesso alle cartelle controllato** (Sicurezza di Windows → Protezione da virus e minacce → Protezione ransomware), Defender può impedire a CodexVTT di scrivere in `Documenti`. In quel caso CodexVTT te lo dice, mette la trascrizione al sicuro in `%LOCALAPPDATA%\CodexVTT.Data\history-recovery` e ti propone **Cambia cartella…**. Per usare `Documenti` consenti `CodexVtt.App.exe` tra le app consentite; lo stesso vale per l'export nella cartella proposta dal dialogo di salvataggio.

Quando scegli una cartella con **Cambia cartella…** e **Testa cartella** non riesce a scrivere, CodexVTT mostra «Windows ha negato la scrittura», lo stato di Accesso alle cartelle controllato (se leggibile) e il percorso dell'app da consentire in Sicurezza di Windows → Protezione da virus e minacce → Protezione ransomware → Consenti app tramite Accesso alle cartelle controllato. Dopo averla aggiunta, rifai il test.

**Se il blocco non compare in Cronologia protezione.** CodexVTT non può scrivere in quella cronologia né leggerla: la riempie solo Defender, e per questo tipo di blocco non sempre crea una voce visibile (o la mostra con un filtro diverso da quello impostato). Il registro affidabile è Visualizzatore eventi → Registri applicazioni e servizi → Microsoft → Windows → Windows Defender → Operational, evento **1123** (accesso bloccato) o **1124** (solo audit); può servire eseguirlo come amministratore. Se l'evento c'è, il nome dell'app e il percorso indicano cosa autorizzare.

Se la cartella è sincronizzata da OneDrive o da un servizio simile, le trascrizioni verranno copiate nel cloud da quel servizio, non da CodexVTT: scegli una cartella non sincronizzata se vuoi che restino solo sul PC.

## Esportare

Il pulsante **Esporta** in alto a destra apre il menu: **Esporta TXT** salva il testo semplice, **Esporta SRT** e **Esporta VTT** salvano i sottotitoli con i tempi (l'SRT è anche in **Sottotitoli**, nel pannello a destra). Nelle impostazioni puoi aggiungere un'intestazione con modello, lingua e data.

## Lingua dell'interfaccia

CodexVTT è in italiano e in inglese. Per impostazione predefinita usa la lingua di Windows; puoi cambiarla in **Impostazioni** → **Lingua dell'interfaccia**.

**Aspetto** (in **Impostazioni**) decide se usare il tema chiaro, quello scuro o seguire Windows; il pulsante con la luna o il sole in alto a destra li alterna al volo.

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
