# Politica di sicurezza

CodexVTT trascrive l'audio **sul tuo computer**: l'audio e i testi non vengono inviati a nessun servizio. Le uniche connessioni in uscita sono quelle che avvii tu: il download dei modelli (da Hugging Face, con verifica SHA-256) e, se lo attivi, il controllo degli aggiornamenti su GitHub.

## Versioni supportate

Le correzioni di sicurezza escono solo per **l'ultima versione pubblicata** (vedi [Releases](https://github.com/CodexCircle/CodexVTT/releases)). Se usi una versione precedente, aggiorna prima di segnalare.

## Come segnalare una vulnerabilità

**Non aprire una issue pubblica** per un problema di sicurezza.

Usa la segnalazione privata di GitHub: [Security → Report a vulnerability](https://github.com/CodexCircle/CodexVTT/security/advisories/new). Solo i maintainer possono leggerla.

Indica, se puoi:

- versione di CodexVTT e sistema operativo (o immagine Docker);
- se usi l'app Windows o la versione server;
- i passaggi per riprodurre il problema e l'effetto che ha;
- una tua idea di correzione, se ce l'hai.

Risponderemo appena possibile, ti terremo aggiornato sulla correzione e, se lo desideri, ti citeremo nelle note di rilascio. Non possiamo promettere tempi precisi: il progetto è mantenuto da volontari.

## Cosa rientra nell'ambito

- **Versione server**: accesso senza il token, aggirare il controllo di origine (same-origin) dei WebSocket, esposizione involontaria in rete, lettura o scrittura di file fuori dalla cartella dei dati.
- **Aggiornamenti**: qualunque modo per far installare un pacchetto non pubblicato da questo repository.
- **Modelli**: download che aggira la verifica SHA-256 o scrive fuori dalla cartella dei modelli.
- **Interfaccia**: script iniettabili nell'interfaccia (XSS), aggiramento della Content-Security-Policy.
- **Privacy**: qualunque invio non voluto di audio, testi o dati personali.

Non rientrano: problemi di modelli di terze parti (segnalali ai loro autori), attacchi che richiedono già il controllo del computer o del token di accesso, e la mancanza di HTTPS quando la versione server è esposta in rete senza un proxy che lo fornisca (la guida spiega come proteggerla).

## Consigli per chi usa la versione server

- Lascia il servizio su `localhost`, oppure mettilo dietro un reverse proxy con HTTPS.
- Tratta il token di accesso come una password e rigeneralo se pensi sia stato visto.
- Tieni aggiornata l'immagine Docker o il servizio all'ultima versione.
