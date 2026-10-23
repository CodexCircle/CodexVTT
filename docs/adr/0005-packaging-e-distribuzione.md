# ADR-0005 — Packaging, installazione e aggiornamenti

- Stato: Proposto
- Data: 2026-10-14
- Issue: #1 (implementazione in #8)

## Contesto

CodexVTT deve installarsi come applicazione Windows avviabile, con build ripetibile, aggiornamento e disinstallazione puliti, e funzionare offline dopo l'installazione del modello. Le app MAUI su Windows sono pacchettizzate MSIX di default, ma possono essere pubblicate *unpackaged*. Servono anche i prerequisiti: VC++ Redistributable per whisper.cpp e WebView2 Runtime, preinstallato su Windows 11.

## Opzioni considerate

| Opzione | Pro | Contro |
|---|---|---|
| **MAUI unpackaged self-contained + Velopack** (MIT) | Installazione per utente senza privilegi di amministratore; delta update; prerequisiti (VC++) gestibili come bootstrap; build da CLI ripetibile in CI; disinstallazione da "App installate" | Prodotto di una comunità più piccola; senza firma, avvisi SmartScreen |
| MSIX | Formato moderno, installazione e rimozione pulite, Microsoft Store | Il sideload richiede firma con certificato attendibile; vincoli su filesystem virtualizzato e percorsi; aggiornamenti tramite App Installer |
| WiX (MSI) | Standard per il deploy aziendale (GPO/Intune) | Più complesso da mantenere; aggiornamenti da gestire a parte; licenza e condizioni d'uso delle versioni recenti da verificare |
| Inno Setup | Semplice, molto diffuso | Niente auto-update; script proprietario |

## Decisione

1. **V1: MAUI pubblicato unpackaged, self-contained, win-x64, con installer Velopack.**
   - L'installazione è per utente (`%LOCALAPPDATA%`), senza privilegi di amministratore.
   - Shortcut nel menu Start.
   - I dati (modelli, impostazioni, log) stanno in `%LOCALAPPDATA%\CodexVTT.Data`, fuori dalla cartella gestita da Velopack: aggiornamento e disinstallazione rimuovono solo l'app e non toccano i dati, che l'utente può cancellare a mano (#8, #18). Dalla 0.2.1 l'avvio sposta lì i dati della vecchia posizione, se presenti.
2. **Prerequisiti:** le DLL del VC++ Redistributable x64 sono distribuite **app-local**, copiate dalla cartella `VC\Redist\MSVC\…\Microsoft.VC143.CRT` di Visual Studio, che è il metodo di redistribuzione previsto da Microsoft. L'installazione resta così completamente offline. Il bootstrap online di Velopack (`--framework vcredist`) è stato scartato perché scaricherebbe componenti durante l'installazione. Il WebView2 Runtime viene verificato all'avvio, con un messaggio chiaro se manca.
3. **Aggiornamenti dell'app:** nessun controllo automatico in V1. L'utente può usare "Verifica aggiornamenti" dalle impostazioni. L'unica richiesta di rete scarica il feed delle release e non invia contenuti (ADR-0006). L'eventuale controllo automatico all'avvio sarà un'opzione disattivata di default.
4. **Build ripetibile:** uno script/CI (GitHub Actions `windows-latest`) esegue in sequenza:
   - build React;
   - `dotnet publish` dell'app MAUI;
   - `vpk pack`;
   - pubblicazione di artefatti con versione SemVer e checksum SHA-256.
5. **Firma del codice: nessuna in V1** (decisione del maintainer, 2026-10-14). L'installer non è firmato con un certificato. La documentazione spiega l'avviso SmartScreen ("Ulteriori informazioni" → "Esegui comunque") e come verificare il checksum SHA-256 pubblicato con la release. La pipeline resta predisposta: se in futuro si acquista un certificato, basta aggiungere il passo di firma.
6. **MSIX/Store e MSI per il deploy aziendale** sono non-obiettivi V1, da riconsiderare dopo il rilascio.

## Conseguenze

- Il primo avvio non richiede amministratore e i modelli restano nella directory dati dell'utente.
- Senza firma, l'esperienza di installazione mostra un avviso: è un rischio di adozione (R-08).
- Velopack introduce una dipendenza di build. Il fallback è Inno Setup, con aggiornamenti manuali.

## Punti aperti

- Gestione dei modelli in disinstallazione e upgrade: da verificare in #8 e #9.
