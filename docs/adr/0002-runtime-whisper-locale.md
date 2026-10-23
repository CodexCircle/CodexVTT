# ADR-0002 — Runtime Whisper locale

- Stato: Proposto (spike Windows eseguito in CI)
- Data: 2026-10-14
- Issue: #1

## Contesto

CodexVTT deve eseguire modelli OpenAI Whisper sul PC dell'utente, da C#/.NET, senza servizi online. Il criterio è eseguire un modello Whisper in locale, non usare necessariamente la libreria Python originale `openai-whisper`. Il runtime scelto determina dimensione della distribuzione, prestazioni CPU/GPU, formato dei modelli, requisiti di sistema e riuso su mobile (V3).

## Opzioni considerate

| Opzione | Pro | Contro |
|---|---|---|
| **A. whisper.cpp tramite Whisper.net** (NuGet, MIT) | Binding .NET maturo con `IAsyncEnumerable` di segmenti, progresso e cancellazione; runtime nativi precompilati per Windows x64/x86/ARM64, Linux, macOS, **Android e iOS**; varianti CPU, NoAvx, CUDA, Vulkan, OpenVINO, CoreML; modelli GGML quantizzabili (q5/q8); nessun Python | Dipendenza nativa con requisiti CPU (AVX/AVX2/FMA/F16C) e VC++ Redistributable; la versione 1.9.x dichiara **Windows 11 / Server 2022** come minimo; le release seguono il ritmo di un singolo maintainer |
| B. ONNX Runtime + modelli Whisper ONNX | Runtime Microsoft, DirectML su qualunque GPU Windows | Encoder, decoder, tokenizer, beam search e timestamp da implementare o integrare a mano; pipeline più fragile; modelli più grandi |
| C. Sidecar Python (`openai-whisper` o `faster-whisper`) | Implementazione di riferimento, CTranslate2 veloce | Python e PyTorch da distribuire (centinaia di MB, o GB con CUDA); processi separati; impraticabile su mobile |
| D. Windows ML / API di sistema | Integrato nel sistema operativo | Non è Whisper; controllo e disponibilità del modello non garantiti |

## Evidenze dello spike

Lo spike [`spike/runtime-probe`](../../spike/runtime-probe/README.md) usa Whisper.net 1.9.1 con `Whisper.net.Runtime` su .NET 10.

| Verifica | Ambiente | Esito |
|---|---|---|
| Restore e build del pacchetto, inclusi gli asset nativi per win-x64/x86/arm64, linux, macOS, android, ios | Linux x64 (CI container) | ✅ |
| Caricamento del runtime nativo CPU (`LoadedLibrary = Cpu`, AVX2/FMA/F16C rilevati) | Linux x64 | ✅ |
| Modello mancante, hash errato e file modello corrotto producono errori gestiti (`WhisperModelLoadException`), senza crash del processo | Linux x64 | ✅ |
| Trascrizione reale di `jfk.wav` con `ggml-base` (hash verificato), **con l'eseguibile bloccato in uscita dal firewall**: testo esatto, RTF 0,95 su 2 vCPU, picco RAM 398 MB | Windows 10.0.26100 x64 (runner GitHub Actions) | ✅ run CI del 2026-10-14 (repository precedente) |
| Test di integrazione Whisper tramite `TranscriptionService` (trascrizione + annullamento) | Windows 10.0.26100 x64 (runner GitHub Actions) | ✅ |
| Benchmark su PC reali con hardware tipico (base, small, large-v3-turbo-q5_0) | PC Windows 11 | ⏳ da fare con `run-spike.ps1` (#9) |

## Decisione

1. **Runtime V1: whisper.cpp tramite Whisper.net 1.9.x**, con il pacchetto `Whisper.net` più i runtime **`Whisper.net.Runtime`** (CPU) e **`Whisper.net.Runtime.NoAvx`** (fallback per CPU senza AVX2). La versione è fissata con lock file e aggiornata solo con test di regressione (#9).
2. **Solo CPU in V1.** L'accelerazione GPU non è necessaria per il criterio di successo e aumenta molto la superficie di distribuzione:
   - CUDA richiede il toolkit installato sul PC dell'utente;
   - Vulkan e OpenVINO hanno prerequisiti propri.

   L'ordine di caricamento dei runtime è fissato esplicitamente con `RuntimeOptions.RuntimeLibraryOrder`. Uno **spike GPU (Vulkan)** è proposto come follow-up post-V1.
3. **Formato modelli: GGML** (`ggml-*.bin`), comprese le varianti quantizzate. Dettagli in ADR-0003.
4. **Requisiti minimi V1: Windows 11 x64** (confermato dal maintainer il 2026-10-14), VC++ Redistributable 2015-2022 x64 (gestito dall'installer, ADR-0005), CPU x64. Windows 10 è fuori supporto Microsoft da ottobre 2025 e non è dichiarato da Whisper.net 1.9.x: non è supportato in V1. ARM64 è un non-obiettivo di V1, anche se il runtime esiste.
5. **Isolamento:** Whisper.net è referenziato solo da `CodexVtt.Transcription.Whisper` (ADR-0001), dietro un'interfaccia del core. Così si può sostituire il runtime (ad esempio con ONNX) senza toccare UI e dominio.
6. **Non si usa `WhisperGgmlDownloader`** di Whisper.net: scarica dalla rete senza verifica di hash e senza la policy di ADR-0006. Il download è gestito da `CodexVtt.Models.Download`.
7. **Parametri V1:**
   - lingua `auto` (rilevamento) o scelta dall'utente;
   - thread = core logici − 1;
   - traduzione verso l'inglese (`WithTranslate`) disponibile solo se inclusa nella specifica #2;
   - output a segmenti con `Start`/`End`, che servono per SRT/VTT e sono la base per V2.

## Conseguenze

- Su CPU moderne i modelli `base` e `small` sono attesi utilizzabili, `medium` e `large` lenti. Le soglie reali escono dai benchmark (#4, #9).
- Il pacchetto include le librerie native win-x64 (qualche MB). I modelli sono separati (ADR-0003).
- La rilevazione delle estensioni CPU (#4) decide tra runtime standard e NoAvx e va mostrata all'utente.
- Su V3, Whisper.net offre runtime Android/iOS e CoreML: lo studio di fattibilità (#11) parte da qui.

## Punti aperti

- Benchmark su PC reali (tempi, RTF, RAM di picco) per calibrare le soglie del catalogo (#9).
- Comportamento del caricamento runtime su CPU senza AVX2: va verificato su una macchina reale o emulata (#9).
- Valutare il preview 1.9.2 di Whisper.net solo se corregge problemi riscontrati.
