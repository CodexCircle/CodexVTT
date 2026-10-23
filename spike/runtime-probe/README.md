# Spike: runtime Whisper locale (ADR-0002)

Programma console minimo che verifica Whisper.net 1.9.1 (whisper.cpp) su .NET 10. Esegue in sequenza:

1. verifica l'hash SHA-256 del modello;
2. carica il runtime nativo e il modello GGML locale;
3. trascrive un WAV a 16 kHz mono senza rete;
4. misura caricamento, tempo di trascrizione, RTF, memoria di picco e latenza di annullamento;
5. scrive un report JSON.

Il programma non scarica nulla. Il codice è usa e getta: non è la base dell'implementazione (#3).

## Esecuzione su un PC Windows 11

Prerequisiti: .NET SDK 10, VC++ Redistributable x64.

```powershell
cd spike\runtime-probe
.\run-spike.ps1 -Model base          # usa il campione jfk.wav di whisper.cpp
.\run-spike.ps1 -Model small         # ripetere con small
.\run-spike.ps1 -Model small -Audio C:\percorso\audio-italiano.mp3 -Language it   # richiede ffmpeg nel PATH
```

Lo script procede in due fasi:

1. **Fase online:** scarica modello e campione. È l'unico momento in cui usa la rete.
2. **Fase offline:** chiede di disattivare la rete, controlla che `huggingface.co` non sia raggiungibile, poi esegue trascrizione e test di annullamento.

Con `-AssumeOffline` lo script non aspetta l'Invio (utile senza console interattiva) ma controlla comunque che la rete non sia raggiungibile.

I report finiscono in `results\` e vanno allegati all'issue #9.

Testo atteso per `jfk.wav`: *"And so my fellow Americans, ask not what your country can do for you, ask what you can do for your country."*

## Uso diretto

```text
codexvtt-runtime-probe --model <ggml.bin> --audio <file.wav 16kHz mono>
                     [--sha256 <hex>] [--language auto|it|en|...] [--threads N] [--gpu]
                     [--cancel-after <secondi>] [--json <report.json>] [--network-check]
```

## Risultati

| Data | Ambiente | Modello | Esito |
|---|---|---|---|
| 2026-10-14 | Linux x64 (container CI, .NET 10.0.112), CPU con AVX2/FMA/AVX512 | — | Build OK; runtime nativo `Cpu` caricato; `model_missing`, `model_hash_mismatch` e modello corrotto (`WhisperModelLoadException`) gestiti senza crash. Trascrizione reale non eseguita: Hugging Face e GitHub non sono raggiungibili dall'ambiente. |
| 2026-10-14 | GitHub Actions `windows-latest` (Windows 10.0.26100, 2 vCPU, 8 GB, AVX2/FMA/F16C), **eseguibile bloccato in uscita dal firewall** (`--network-check`: connessione bloccata) | base (sha256 `60ed5bc3…2efe`, verificato contro il catalogo) | ✅ `jfk.wav` (11 s) trascritto esattamente: *"And so my fellow Americans, ask not what your country can do for you, ask what you can do for your country."*; runtime `Cpu`; caricamento 0,4 s; trascrizione 10,4 s (RTF 0,95 su 2 core); picco RAM 398 MB. run CI del 2026-10-14 (repository precedente) |
| _da fare_ | PC Windows 11 reale | base, small | Da eseguire con `run-spike.ps1` quando sarà disponibile una macchina (benchmark su hardware tipico, #9) |
