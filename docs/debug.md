# Toolset di debug

Strumenti per diagnosticare CodexVTT senza dover riprodurre a schermo (issue #38). Tutto resta locale: nessuna
richiesta di rete aggiuntiva (ADR-0006).

## Log

- File: `%LOCALAPPDATA%\CodexVTT.Data\logs\codexvtt.log` (rotazione a 2 MB, 5 file).
- Livello minimo, in ordine di precedenza:
  1. argomento `--debug` (equivale a livello `Debug` più log diagnostici della WebView);
  2. variabile d'ambiente `CODEXVTT_LOG_LEVEL` (`Trace`, `Debug`, `Information`, `Warning`, `Error`);
  3. variabile `CODEXVTT_DEBUG=1` (equivale a `--debug`);
  4. `DiagnosticLogLevel` in `settings.json`;
  5. default `Information`.

Esempio da terminale (la finestra si apre normalmente):

```bash
set CODEXVTT_LOG_LEVEL=Debug
"%LOCALAPPDATA%\CodexVTT\current\CodexVtt.App.exe"
```

### Cosa viene registrato

- Avvio, versione, livello di log attivo; eventi della finestra (attivazione, chiusura).
- Con `--debug`: navigazioni della WebView, richieste bloccate (solo schema, host e percorso, senza query string),
  crash del processo WebView, messaggi della console JavaScript ed eccezioni non gestite.
- Per ogni comando del bridge: nome del comando, durata ed esito. **Mai il payload**.
- Download dei modelli e trascrizioni completate (solo metadati, come prima).

> In modalità debug la console JavaScript può contenere testo della UI. Leggi il log prima di condividerlo.

## Modalità senza GUI

Eseguono solo con modelli già installati e verificati, non aprono finestre né rete.

```bash
"%LOCALAPPDATA%\CodexVTT\current\CodexVtt.App.exe" --diagnose "C:\percorso\cartella-output"
```

Verifica: runtime WebView2, cartelle scrivibili (root, modelli, log, temp, cronologia), ogni modello installato
(hash ricalcolato senza modificare il marker), ultime 200 righe del log. Scrive `diagnostic-report.json` e stampa
un riepilogo. Exit code `0` se tutti i controlli passano, `2` altrimenti, `1` per errori di avvio.

```bash
"%LOCALAPPDATA%\CodexVTT\current\CodexVtt.App.exe" --headless-transcribe "C:\audio\file.wav" --model small --format srt --out "C:\output"
```

Import, trascrizione ed export senza finestra. Opzioni: `--model` (default `small`), `--format txt|srt|vtt`
(default `txt`), `--language` (default `auto`), `--out` obbligatorio. Il testo trascritto non va su console né nel log.

## DevTools

In build Release le DevTools restano disattivate. Con `--debug` o `CODEXVTT_DEBUG=1` vengono abilitate, così da
ispezionare la UI quando serve. La scelta è esplicita e non cambia il comportamento predefinito (ADR-0006).

## Build locale

Richiede Node 20+, .NET SDK 10 e il workload MAUI:

```bash
cd ui
npm ci
npm run build:app
cd ..
dotnet build src/CodexVtt.App/CodexVtt.App.csproj -f net10.0-windows10.0.19041.0 -c Release
```

Test: `dotnet test tests/CodexVtt.Core.Tests` e `dotnet test tests/CodexVtt.Architecture.Tests`.
