# ADR-0008 — Aggiornamenti da GitHub

- Stato: Proposto
- Data: 2026-10-23
- Modifica: ADR-0006 (rete), ADR-0005 (distribuzione)

## Contesto

CodexVTT vive in un repository pubblico su GitHub e si distribuisce con l'installer Velopack (ADR-0005), che già
sa aggiornare l'app ma finora nessuno lo chiamava: ogni versione si reinstallava a mano. Il repository non ha
GitHub Actions: le release si costruiscono dal PC (`scripts/release-locale.ps1`).

## Decisione

- **Fonte**: le release del repository indicato in `CodexVtt.Core.ProjectInfo.RepositoryUrl` (unico punto da cambiare
  se il progetto si sposta; lo legge anche lo script di rilascio). Velopack usa `GithubSource`: legge le release
  pubbliche (non draft; le pre-release solo se la versione installata è già una pre-release) e scarica i pacchetti
  `*-full.nupkg` / `*-delta.nupkg` indicati da `releases.win.json`, verificandone l'SHA-256.
- **Quando**: mai di nascosto. Il pulsante «Controlla aggiornamenti» (Impostazioni) cerca una versione nuova; la
  casella «Controlla gli aggiornamenti all'avvio» (spenta di default) lo fa a ogni avvio. L'installazione parte solo
  dopo un clic su «Installa X e riavvia».
- **Cosa esce dal PC**: richieste HTTPS a `api.github.com` / `github.com` / `objects.githubusercontent.com` per leggere
  l'elenco delle release e scaricare i pacchetti. Nessun audio, testo, identificativo o statistica; GitHub vede
  l'indirizzo IP, come per qualsiasi download. Nessun token: il repository è pubblico.
- **Allowlist**: per gli aggiornamenti non c'è l'allowlist di host di `Models.Download` (ADR-0006): Velopack segue i
  collegamenti degli asset che GitHub indica nella release del repository configurato. Il rischio è contenuto perché
  l'indirizzo di partenza è fisso nel codice e i pacchetti sono verificati con SHA-256 dal feed della stessa release;
  non c'è firma del codice (punto aperto).
- **Dove**: `CodexVtt.Updates` è l'unico assembly che riferisce Velopack (lo verifica un test di architettura).
  Core, Bridge, Whisper, audio e download dei modelli restano senza rete, tranne `CodexVtt.Models.Download` (ADR-0006).
  La WebView non fa richieste: il controllo lo esegue il processo .NET. Il server/Docker non si aggiorna da solo.
- **Build non installate** (sviluppo, server, container): stato `Unsupported`, nessuna richiesta.
- **Prove senza GitHub**: `CODEXVTT_UPDATE_SOURCE` (cartella o indirizzo di un feed Velopack) sostituisce la fonte.
- **Rilascio**: `release-locale.ps1` scarica le release precedenti (per i delta), costruisce il pacchetto con `vpk pack`
  e pubblica con `gh release create` **tutti** i file di `installer/`, feed incluso. Una release incompleta o in
  bozza non viene vista dall'app. Non c'è firma del codice (ADR-0005): SmartScreen avvisa alla prima installazione.
- **Docker**: l'aggiornamento è `git pull && docker compose up -d --build` (o un'immagine pubblicata, se si decide di
  farlo); la scheda Impostazioni non mostra l'aggiornamento nel server.

## Conseguenze

- Cambiare repository (rename, trasferimento) non rompe gli aggiornamenti delle copie già installate finché GitHub
  reindirizza il vecchio indirizzo; per sicurezza la prima release dopo uno spostamento va pubblicata anche nel
  vecchio repository, oppure l'utente reinstalla dall'ultima release.
- Una release pubblica è irrevocabile in pratica: chi ha l'app la vedrà. Provare sempre con `-DryRun` e con
  `CODEXVTT_UPDATE_SOURCE` prima di pubblicare.
- Il privacy badge dell'app e il testo in Impostazioni dichiarano che la rete serve a modelli e aggiornamenti.

## Punti aperti

- Firma del codice e delle release; canale «anteprima» scelto dall'utente; aggiornamento del server in Docker
  (immagine su un registro pubblico) con la stessa scheda.
