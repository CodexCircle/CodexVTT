# ADR-0001 — Architettura, stack e contratto core ↔ UI

- Stato: Proposto
- Data: 2026-10-14
- Issue: #1

## Contesto

CodexVTT V1 è un'app desktop Windows che trascrive audio con un modello Whisper eseguito localmente. Lo stack previsto è C#/.NET, .NET MAUI e React. Le fasi successive aggiungeranno timestamp e speaker (V2), mobile Android/iOS (V3) e server (V4). L'architettura V1 deve:

- isolare il codice che accede alla rete (solo download dei modelli) dal codice che tratta audio e testo;
- permettere di riusare il core in V3 (MAUI mobile) e V4 (servizio HTTP) senza riscriverlo;
- tenere la UI React sviluppabile e testabile senza la shell nativa.

## Opzioni considerate

### Shell desktop

| Opzione | Pro | Contro |
|---|---|---|
| **A. .NET MAUI + `HybridWebView` (React)** | Stack richiesto; stessa shell e stesso bridge riusabili su Android/iOS (V3); interop JS ↔ .NET integrata; su Windows usa WebView2 | Il target Windows di MAUI (WinUI 3 / Windows App SDK) è meno maturo di WPF; `HybridWebView` è disponibile solo da .NET 9 |
| B. WPF o WinUI 3 + WebView2 diretto | Desktop molto maturo, controllo fine su WebView2 | Non riusabile per il mobile, quindi V3 richiede una seconda shell |
| C. MAUI con UI nativa XAML (senza React) | Nessuna WebView | Contraddice lo stack richiesto; non riusabile su web per V4 |
| D. Electron/Tauri + core .NET in sidecar | UI web matura | Due runtime da distribuire, IPC tra processi, fuori dallo stack |

### Contratto core ↔ UI

| Opzione | Note |
|---|---|
| **Messaggi JSON versionati su `HybridWebView`** (richiesta/risposta + eventi) | Indipendente dal trasporto: lo stesso contratto può viaggiare su HTTP/WebSocket in V4 |
| Chiamate dirette `InvokeDotNet` metodo per metodo | Più semplice, ma lega la UI alla firma dei metodi C# e non porta eventi di progresso |
| Server HTTP locale (localhost) | Aggiunge una superficie di rete locale, anche se solo in loopback: da evitare per la postura privacy |

## Decisione

1. **Shell: .NET MAUI (.NET 10 LTS) con `HybridWebView`** che ospita una UI **React + TypeScript + Vite**. Gli asset sono compilati e inclusi nell'app (`Resources/Raw/wwwroot`). Su Windows il motore è WebView2. Target V1: **Windows 11 x64**; vedi ADR-0002 per il motivo.
2. **Fallback documentato.** Se la shell MAUI su Windows si rivela inadeguata (packaging, lifecycle, bug bloccanti), si passa a WinUI 3 o WPF + WebView2 mantenendo lo stesso contratto del bridge e lo stesso core. Il costo del cambio resta limitato alla sola shell.
3. **Struttura della solution:**

   ```text
   src/
     CodexVtt.Core/              net10.0, nessuna UI e nessun riferimento a System.Net.Http
                               dominio: job di trascrizione, segmenti, export, profilo hardware,
                               regole di selezione, catalogo modelli (lettura), errori tipizzati
     CodexVtt.Transcription.Whisper/  adattatore Whisper.net (unica dipendenza nativa di inferenza)
     CodexVtt.Audio.Windows/     decoder Media Foundation (ADR-0004), dietro IAudioDecoder
     CodexVtt.Models.Download/   UNICO progetto con HttpClient: download e verifica modelli (ADR-0003/0006)
     CodexVtt.Bridge/            DTO del contratto, dispatcher dei comandi, serializzazione con source generator
     CodexVtt.App/               app MAUI (net10.0-windows10.0.xxxxx): DI, lifecycle, HybridWebView, dialog file
   ui/                         React + TypeScript + Vite; bridge reale e mock
   tests/
     CodexVtt.Core.Tests/        unitari
     CodexVtt.Architecture.Tests/ vincoli di dipendenza (es. Core non referenzia System.Net.Http)
     CodexVtt.Integration.Tests/ trascrizione reale con modello piccolo e fixture
   docs/  spike/
   ```

4. **Contratto del bridge:** buste JSON versionate.

   ```jsonc
   // UI → core
   { "v": 1, "id": "c-42", "type": "transcription.start", "payload": { "audioId": "...", "modelId": "base", "language": "auto" } }
   // core → UI (risposta)
   { "v": 1, "id": "c-42", "type": "result", "ok": true, "payload": { "jobId": "..." } }
   { "v": 1, "id": "c-42", "type": "result", "ok": false, "error": { "code": "model_not_verified", "message": "...", "details": {} } }
   // core → UI (evento)
   { "v": 1, "type": "event", "event": "transcription.progress", "payload": { "jobId": "...", "percent": 37 } }
   ```

   I comandi iniziali sono: `system.profile`, `models.list`, `models.recommend`, `models.download`, `models.cancelDownload`, `models.import`, `models.remove`, `models.select`, `audio.pick`, `audio.inspect`, `transcription.start`, `transcription.cancel`, `transcription.get`, `export.save`, `settings.get` e `settings.set`. Il dettaglio va nell'issue #7.

   - **Codici d'errore** stabili (`snake_case`), mappati ai testi del catalogo di #2.
   - **Percorsi file:** la UI non li riceve mai in forma libera. I file scelti dall'utente tramite dialog nativi vengono registrati dal core e referenziati con un `audioId` opaco. Così si riduce la superficie di attacco dalla WebView.
   - I DTO vivono in `CodexVtt.Bridge`. I tipi TypeScript sono generati o verificati da test di contratto, per evitare divergenze.

5. **Configurazione e dati:** directory per utente `%LOCALAPPDATA%\CodexVTT.Data\` (fuori dalla cartella gestita da Velopack, #18) con `models\`, `temp\`, `logs\` e `settings.json`. La directory dei modelli è configurabile.
6. **Logging:** `Microsoft.Extensions.Logging` con sink su file locale e rotazione. Il testo trascritto e i percorsi audio completi non finiscono nei log, salvo modalità debug attivata dall'utente.
7. **Errori:** eccezioni di dominio con codice, mappate a `error.code` al confine del bridge. Le eccezioni non gestite vengono registrate nel log e mostrate all'utente con un messaggio generico, senza invio online (ADR-0006).
8. **Concorrenza:** un job di trascrizione alla volta in V1. L'inferenza gira fuori dal thread UI e la cancellazione passa da `CancellationToken`.

## Conseguenze

- Il core non dipende né da MAUI né dalla WebView. È riusabile in V3 (MAUI mobile, dove Whisper.net ha runtime Android/iOS) e in V4 (ASP.NET Core).
- La regola "solo `CodexVtt.Models.Download` usa la rete" è verificabile con test di architettura (#9).
- Il mock del bridge permette di sviluppare e testare la UI (#6) in parallelo alla shell (#7).
- Si accetta il rischio di maturità di MAUI su Windows (R-01 in [rischi](../architecture/rischi.md)), mitigato dal fallback del punto 2.

## Punti aperti

- Versione esatta del Windows App SDK e modalità unpackaged: da chiudere nell'issue #8 insieme ad ADR-0005.
- Generazione automatica dei tipi TypeScript dai DTO: va scelto lo strumento nell'issue #7.
