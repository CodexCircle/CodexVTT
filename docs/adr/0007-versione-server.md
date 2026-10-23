# ADR-0007 — Versione server: la stessa UI nel browser, core come servizio

- Stato: Proposto
- Data: 2026-10-23

## Contesto

L'app desktop mostra la UI in una HybridWebView di MAUI. Nelle macchine virtuali la WebView non funziona, e
chi lascia lavorare Whisper su un PC (o una VM) vuole poterlo usare da un altro dispositivo e chiudere la
pagina senza interrompere le trascrizioni.

## Decisione

- Nuovo progetto `CodexVtt.Server` (ASP.NET Core, Kestrel): serve la UI React compilata con `npm run build:server`
  e porta gli stessi messaggi JSON del bridge su un WebSocket (`/bridge`). Core, bridge e contratto restano gli stessi.
- **Docker è il modo di distribuzione principale** (`Dockerfile`, `docker-compose.yml`): immagine Linux
  (`aspnet:10.0` + ffmpeg), un solo volume `/data`, token obbligatorio. Funziona dove la WebView no (VM, server).
  In alternativa gira come servizio di Windows (`Microsoft.Extensions.Hosting.WindowsServices`,
  `scripts/installa-servizio.ps1`) o da riga di comando. Il processo del core non dipende da nessuna pagina aperta.
- **Audio su Linux**: `CodexVtt.Audio.FFmpeg` esegue `ffprobe`/`ffmpeg` del sistema (PCM s16le 16 kHz mono su stdout).
  Nessuna libreria di terzi nel repository né nell'app Windows (ADR-0004 resta valido lì): ffmpeg è un pacchetto
  dell'immagine Docker, con la sua licenza, e non apre connessioni di rete (file locali). Senza ffmpeg il server
  accetta solo WAV (decoder gestito).
- **La coda batch vive nel core** (`BridgeDispatcher.Queue.cs`), non più nella UI: continua con la pagina chiusa.
  Chi riapre la pagina legge `queue.get` e `transcription.active` e ritrova lo stato. L'app desktop usa la stessa coda.
- **File**: niente dialog nativi. I file audio si caricano con `POST /api/audio` (stesso esito di `audio.pickMany`),
  gli export si scaricano da `GET /api/export`. La UI nasconde importazione modelli, apertura cartelle e cambio
  cartella cronologia (`system.info.host == "server"`); i comandi corrispondenti rispondono `not_available_on_server`.
- **Accesso**: senza `CodexVtt:AccessToken` il server risponde solo a richieste da localhost (default `127.0.0.1:5185`).
  Per esporlo si configura un indirizzo e un token: aprendo `/?token=…` il browser riceve un cookie HttpOnly
  SameSite=Strict. Il WebSocket accetta solo la stessa origine. Il server non fa TLS: fuori dalla LAN va
  messo dietro un reverse proxy HTTPS o una VPN.
- **Privacy** (ADR-0006): l'unico accesso a Internet resta quello di `CodexVtt.Models.Download`. `CodexVtt.Server` è il
  nuovo punto di ascolto in rete, ma non apre connessioni verso l'esterno. I log non contengono nomi di file.

## Conseguenze

- I dati stanno nel profilo dell'account del servizio (`%LOCALAPPDATA%\CodexVTT.Data`, `Documenti\CodexVTT\history`),
  cambiabili con `CodexVtt:DataRoot` e `CodexVtt:HistoryDirectory`.
- I file caricati restano in `uploads` (nella cartella dati) finché il servizio non riparte (poi vengono cancellati).
- La coda è in memoria: un riavvio del container la perde (le trascrizioni già finite restano nella cronologia).
- Ogni pagina collegata riceve tutti gli eventi: più persone con lo stesso token vedono la stessa coda.

## Punti aperti

- TLS integrato, più utenti con permessi diversi, limite di caricamenti e pulizia dei file caricati a lavoro finito.
- Immagine solo `linux-x64` (runtime Whisper CPU/NoAvx); `arm64` non provato.
