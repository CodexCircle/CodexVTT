# ADR-0006 — Privacy, uso della rete e non-esfiltrazione

- Stato: Proposto
- Data: 2026-10-14
- Issue: #1 (verifica in #9)

## Contesto

Vincoli non negoziabili del progetto:

- inferenza solo locale;
- audio, testo e metadati di trascrizione mai inviati a servizi online;
- rete solo per distribuire o aggiornare modelli su richiesta;
- trascrizione possibile offline dopo l'installazione del modello.

Questi vincoli devono essere garantiti dalla struttura del codice e verificabili con test, non solo dichiarati.

## Decisione

1. **Rete ammessa solo in tre casi, tutti avviati dall'utente:**
   - download dei modelli dagli host del catalogo (ADR-0003);
   - verifica e download degli aggiornamenti dell'app dalle release di GitHub (ADR-0005, ADR-0008): su richiesta, oppure
     all'avvio **solo se l'utente ha attivato** il controllo automatico (spento di default).
   - download del **file di riferimento del Test bench** (una lettura in italiano de «Le avventure di Pinocchio», capitolo 6, registrazione LibriVox
     di pubblico dominio) da `archive.org`, **solo dopo la conferma dell'utente** in un dialog che mostra indirizzo, SHA-256 e licenza. L'indirizzo, la
     dimensione e l'hash sono fissati nel codice (`BenchmarkReference`); il file si scarta se non coincidono. Il download sta in
     `CodexVtt.Models.Download` (`ReferenceFileDownloader`), con allowlist `archive.org` e redirect controllati. In alternativa l'utente sceglie un proprio file e non c'è rete.

   Nessuna richiesta di rete durante la trascrizione, l'export o la chiusura, né all'avvio senza quella scelta.
2. **Un solo punto di uscita:** `HttpClient` esiste solo in `CodexVtt.Models.Download` e nel componente di update. Il client usa un handler con **allowlist di host** (es. `huggingface.co` e i suoi CDN, host del feed release). Una richiesta verso un host non in lista fallisce e viene registrata nel log. Le richieste contengono solo URL di file pubblici: nessun identificativo utente o dato della trascrizione.
3. **Vincoli architetturali verificati da test** (`CodexVtt.Architecture.Tests`):
   - `CodexVtt.Core`, `CodexVtt.Transcription.Whisper`, `CodexVtt.Audio.Windows` e `CodexVtt.Bridge` non referenziano `System.Net.Http`, `System.Net.Sockets` né `System.Net.WebSockets`;
   - nessun pacchetto di telemetria o crash reporting (Application Insights, Sentry, App Center e simili) è presente nel grafo di dipendenze.
4. **WebView isolata:**
   - CSP `default-src 'self'` senza `connect-src` esterni;
   - asset solo locali, nessun CDN;
   - navigazione verso origini esterne bloccata; i link esterni (es. documentazione) si aprono nel browser di sistema solo su click esplicito;
   - DevTools disabilitati in Release;
   - il controllo di reputazione di SmartScreen del runtime è disattivato (`IsReputationCheckingRequired = false` e `--disable-features=msSmartScreenProtection`), insieme ai servizi di configurazione e ai report di rete di Edge: la WebView non apre mai URL esterni, e `WebViewSecurity.IsLocal` lascia passare solo l'origine locale. Il runtime WebView2 è un componente di Windows e può restare del traffico di servizio che l'app non controlla (#19).
5. **Nessuna telemetria e nessun crash report online in V1.** Le diagnostiche restano in `%LOCALAPPDATA%\CodexVTT.Data\logs`. Un'eventuale telemetria futura richiede un ADR dedicato, opt-in esplicito ed esclusione di qualunque contenuto.
6. **Dati locali:**
   - file temporanei in `temp\`, eliminati a fine job e ripuliti all'avvio;
   - log senza testo trascritto né percorsi completi (salvo debug attivato dall'utente);
   - cronologia delle trascrizioni: ogni trascrizione viene salvata **solo in locale**, in file `.txt` e `.json` in una cartella visibile scelta dall'utente (default `Documenti\CodexVTT\history`). CodexVTT non la sincronizza né la invia: se l'utente sceglie una cartella sincronizzata da un servizio cloud (es. OneDrive), la sincronizzazione dipende da quel servizio. La documentazione lo segnala.
7. **Dichiarazione in app:** l'elaborazione è locale. L'app indica quando sta usando la rete (download) e verso quale host.
8. **Verifica (#9):**
   - test di trascrizione con rete disattivata;
   - cattura del traffico (firewall con logging o proxy) durante un ciclo completo import → trascrizione → export, con atteso zero connessioni;
   - test che l'allowlist blocchi host non ammessi.
9. **Mobile e server:** nessun fallback online è autorizzato da questo ADR. Una scelta diversa per il mobile richiede l'issue #12 con requisiti propri di privacy, consenso e autenticazione.

## Eccezione documentata: runtime WebView2

Il runtime WebView2 (componente di Windows, non codice di CodexVTT) apre all'avvio una connessione del suo processo browser verso `substrate.office.com` (Microsoft 365), circa 0,2 secondi dopo l'avvio del processo WebView. Verificato con `pktmon` (issue #54): il contenuto è cifrato e non contiene dati di trascrizione o audio, che non passano dal codice di CodexVTT.

- Non è bloccabile con i flag di runtime disponibili (provati: metriche, DNS-over-HTTPS, servizi in background). Un blocco efficace richiede una policy di sistema con privilegi amministrativi, fuori dall'ambito dell'app.
- Il DNS-over-HTTPS di sistema verso `dns.google` è invece eliminato: la WebView non risolve nomi (`host-resolver-rules`, 0.3.4).
- Eccezione accettata dal maintainer. Va rivista se il runtime cambia comportamento o se CodexVTT passa a un motore web diverso.

## Conseguenze

- Le funzioni che richiedono la rete (download, update) sono sempre esplicite e visibili all'utente.
- Diagnosticare problemi sul campo richiede che l'utente condivida i log manualmente: è un costo accettato.
- Ogni nuova dipendenza va controllata contro le regole del punto 3.
