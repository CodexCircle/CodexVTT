# Registro dei rischi

Issue: #1. P = probabilità, I = impatto (B bassa, M media, A alta). Aggiornare a ogni milestone.

| ID | Rischio | P | I | Mitigazione | Owner/issue |
|---|---|---|---|---|---|
| R-01 | La shell MAUI su Windows (WinUI 3, unpackaged, HybridWebView) presenta bug o limiti bloccanti | M | A | Core e bridge indipendenti dalla shell; fallback WinUI/WPF + WebView2 previsto in ADR-0001; prototipo shell presto in #7 | #7 |
| R-02 | Prestazioni CPU insufficienti per modelli di qualità (medium/large) sui PC tipici | A | M | Raccomandazione adattiva; `small` / `large-v3-turbo-q5_0` come compromesso; RTF atteso mostrato prima dell'avvio; spike GPU post-V1 | #4, #9 |
| R-03 | Whisper.net 1.9.x non supporta Windows 10 | A | M | V1 dichiara Windows 11 come requisito; messaggio chiaro dell'installer | #2, #8 |
| R-04 | CPU senza AVX2/FMA/F16C | B | M | Runtime NoAvx incluso e caricato in fallback; test dedicato | #3, #9 |
| R-05 | Dipendenza da un binding mantenuto da un singolo maintainer (Whisper.net) | M | M | Versione fissata; adattatore isolato (`CodexVtt.Transcription.Whisper`); alternativa: P/Invoke diretto a whisper.cpp o ONNX | #3 |
| R-06 | Disponibilità o modifica dei file su Hugging Face; blocco degli host in reti aziendali | M | M | URL a revisione fissata + SHA-256; import manuale da file; mirror proprio come opzione | #4 |
| R-07 | Modello manomesso o corrotto | B | A | Verifica SHA-256 obbligatoria prima dell'uso; catalogo incorporato nell'app firmata | #4 |
| R-08 | Assenza di firma del codice (scelta accettata per V1) → SmartScreen, minore fiducia, possibili blocchi antivirus | A | M | Checksum SHA-256 pubblicati; guida per SmartScreen; pipeline pronta per firmare in futuro | #8 |
| R-17 | La cartella della cronologia (es. `Documenti`) può essere sincronizzata da OneDrive o simili, portando le trascrizioni nel cloud senza che CodexVTT lo faccia | M | M | Avviso nella guida; cartella modificabile nelle impostazioni | #2 |
| R-09 | Esfiltrazione involontaria tramite dipendenze (telemetria SDK), WebView (CDN) o log | B | A | Test di architettura sulle dipendenze; CSP; allowlist di host; nessuna telemetria; test di traffico | #9 |
| R-10 | Parsing di file audio malevoli | B | A | Decodifica tramite Media Foundation aggiornato dal sistema; validazione; nessun codec di terzi in V1 | #5 |
| R-11 | Memoria esaurita con file lunghi o modelli grandi | M | M | Streaming/WAV temporaneo; controllo RAM prima di caricare il modello; errore tipizzato `insufficient_memory` | #3, #5 |
| R-12 | Formati non supportati dai codec installati (Opus/Vorbis) | M | B | Messaggio con soluzione; elenco formati chiaro | #5 |
| R-13 | Qualità di trascrizione italiana insufficiente con i modelli piccoli | M | M | WER misurato su fixture italiane; raccomandazione minima `small` per l'italiano se i dati lo confermano | #9 |
| R-14 | Spazio disco insufficiente (modelli fino a ~3 GB) | M | B | Controllo prima del download; spazio richiesto mostrato | #4 |
| R-15 | Allucinazioni di Whisper (testo inventato su silenzio o musica) presentate come trascrizione affidabile | M | M | Avviso in UI; VAD (Silero tramite whisper.cpp) da valutare in #3; segmenti con probabilità esposte | #3, #2 |
| R-16 | Spike e test Whisper verificati solo su runner CI (2 vCPU), non ancora su PC reali | M | M | Spike in CI con firewall superato; `run-spike.ps1` per i benchmark su PC reali | #9 |
