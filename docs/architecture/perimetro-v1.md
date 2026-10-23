# Perimetro V1 e non-obiettivi

Issue: #1. La specifica di dettaglio è in #2.

## Criterio di successo

Un utente Windows installa CodexVTT, installa o seleziona un modello Whisper locale, carica un file audio supportato e ottiene una trascrizione verificabile **con la rete disattivata** dopo l'installazione del modello.

## In scope V1

| Area | Contenuto | Issue |
|---|---|---|
| Piattaforma | Windows 11 x64, installazione per utente | #8 |
| Shell e UI | .NET MAUI + HybridWebView + React; interfaccia in italiano e inglese; accessibilità di base | #6, #7 |
| Modelli | Catalogo GGML (tiny → large-v3); download esplicito con hash SHA-256; import da file; rimozione; raccomandazione in base all'hardware con override; stato e requisiti visibili | #4 |
| Trascrizione | whisper.cpp/Whisper.net su CPU (+ NoAvx); lingua automatica o manuale; progresso; annullamento; un job alla volta; modello usato registrato | #3 |
| Audio | WAV, MP3, M4A/AAC, WMA, FLAC (+ OGG/Opus se il codec è presente); validazione; file lunghi | #5 |
| Output | Visualizzazione testo; copia; export TXT, SRT e VTT dai segmenti | #5, #6 |
| Cronologia | Salvataggio automatico di ogni trascrizione in file propri (`.txt` + `.json`) in una cartella visibile (`Documenti\CodexVTT\history`, modificabile); riapertura, export, eliminazione | #2, #6 |
| Privacy | Nessuna rete salvo download e aggiornamenti richiesti; nessuna telemetria; log locali senza contenuti | ADR-0006, #9 |
| Distribuzione | Installer Velopack non firmato, upgrade, uninstall, prerequisiti inclusi | #8 |
| Qualità | Test unitari, di integrazione e di architettura; benchmark; test offline e di non-esfiltrazione; acceptance run | #9 |

## Non-obiettivi V1

- Traduzione della trascrizione (issue #17).
- Firma del codice dell'installer.
- Accelerazione GPU (CUDA, Vulkan, OpenVINO, DirectML). È proposto uno spike post-V1.
- Windows 10, Windows ARM64, macOS e Linux desktop.
- Player sincronizzato, timestamp di parola, editing dei segmenti (V2, #10).
- Speaker diarization o voice matching (V2, #10).
- Trascrizione in tempo reale da microfono.
- Coda di più job in parallelo e trascrizione batch di cartelle.
- Modelli personalizzati o fine-tuned fuori catalogo.
- Microsoft Store/MSIX e MSI per il deploy aziendale.
- Account utente, sincronizzazione cloud e qualunque inferenza online (V3/V4, #11–#13).
- Telemetria e crash reporting online.

## Decisioni di prodotto (maintainer, 2026-10-14)

- Requisito minimo **Windows 11 x64**.
- **Nessuna traduzione** in V1; eventuale aggiunta futura nella issue #17.
- **Cronologia** salvata in file separati in una cartella visibile sul disco.
- Durata massima dell'audio: **4 ore**.
- Interfaccia in **italiano e inglese**.
- Installer **non firmato** con certificato (vedi ADR-0005).
- Licenza del progetto: decisione del maintainer nella issue #16.
