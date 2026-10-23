# Matrice licenze

Issue: #1. Aggiornare a ogni nuova dipendenza (controllo in #9). Lo stato "verificato" indica che la licenza è stata letta nel pacchetto o nel repository alla data indicata.

| Componente | Uso | Licenza | Obblighi principali | Stato |
|---|---|---|---|---|
| Pesi OpenAI Whisper | Modelli | MIT (repository `openai/whisper`) | Avviso di copyright e licenza nella documentazione e nella schermata "Informazioni" | Da riportare nel NOTICE |
| File GGML `ggerganov/whisper.cpp` (Hugging Face) | Modelli convertiti | MIT | Come sopra | Da verificare sulla scheda del modello alla revisione fissata (#4) |
| whisper.cpp / ggml | Runtime nativo (incluso in Whisper.net.Runtime) | MIT | Avviso nel NOTICE | Verificato 2026-10-14 (LICENSE "The ggml authors") |
| Whisper.net 1.9.1 + Whisper.net.Runtime | Binding .NET | MIT | Avviso nel NOTICE | Verificato 2026-10-14 (nuspec/LICENSE) |
| .NET 10 / .NET MAUI | Runtime e shell | MIT | Avviso nel NOTICE | Noto, da confermare nel NOTICE generato |
| Windows App SDK | Dipendenza di MAUI su Windows | Licenza Microsoft (redistribuibile) | Termini di redistribuzione Microsoft | Da verificare in #8 |
| Microsoft Edge WebView2 Runtime | Motore della WebView | Licenza Microsoft; preinstallato su Windows 11 | Nessuna redistribuzione se si usa il runtime di sistema | Da verificare in #8 |
| Microsoft Visual C++ Redistributable | Prerequisito di whisper.cpp | Licenza Microsoft (redistribuibile) | Redistribuire solo i pacchetti ufficiali | Da verificare in #8 |
| NAudio.Wasapi / NAudio.Core 3.1.0 | Decodifica audio (Media Foundation) | MIT | Avviso nel NOTICE | Verificato 2026-10-14 (nuspec) |
| Codec di Windows (Media Foundation) | Decodifica | Parte del sistema operativo | Nessuno (non redistribuiti) | — |
| React, Vite, TypeScript | UI | MIT / Apache-2.0 (TypeScript) | Avvisi nel NOTICE; per Apache-2.0 riportare la licenza | Da generare automaticamente (#6) |
| Illustrazione della lontra (mascotte Tina), svgapp.ai/mascots/otter | Mascotte | Da verificare | Da definire in base alla licenza del sito | Da verificare prima del rilascio |
| Illustrazione del panda rosso (mascotte Giulio), svgapp.ai/mascots/pandi | Mascotte | Da verificare | Da definire in base alla licenza del sito | Da verificare prima del rilascio |
| Velopack 1.2.161 (libreria e `vpk`) | Installer e aggiornamenti | MIT | Avviso nel NOTICE | Da verificare sul pacchetto (#8) |
| FFmpeg | Solo nell'immagine Docker (pacchetto Ubuntu, programma esterno) | LGPL/GPL secondo la build | Non è nel repository né nell'app per Windows; se l'immagine viene pubblicata va indicato | Escluso dall'app (ADR-0004), ammesso nel container (ADR-0007) |
| sherpa-onnx (`org.k2fsa.sherpa.onnx` 2.0.0) | Motore di Parakeet e Canary | Apache-2.0 | Avviso nel NOTICE | Verificato 2026-10-23 (nuspec) |
| ONNX Runtime (incluso in sherpa-onnx) | Inferenza | MIT | Avviso nel NOTICE | Verificato 2026-10-23 |
| NVIDIA Parakeet TDT 0.6B v3 (export int8 di csukuangfj) | Modello | CC-BY-4.0 | **Attribuzione obbligatoria**: mostrata nell'app (scheda Modelli) e nel NOTICE | Verificato 2026-10-23 (scheda del modello) |
| NVIDIA Canary-1B-v2 (export int8 della community, bnoordhuis) | Modello | CC-BY-4.0 | Come sopra, più l'indicazione che l'export non è ufficiale | Verificato 2026-10-23 (scheda dell'export) |
| Cohere Transcribe 03-2026 | **Non incluso** | Apache-2.0 (export ONNX della community) | — | Rimandato (vedi ADR-0009) |

## Regole

1. **Licenze ammesse senza revisione:** MIT, BSD, Apache-2.0, ISC e componenti di sistema Microsoft.
2. **Licenze che richiedono un ADR:** LGPL/GPL/AGPL, licenze "source available", modelli con termini d'uso restrittivi. Questo vale in particolare per i modelli di diarization in V2, ad esempio quelli con accesso condizionato su Hugging Face.
3. **File NOTICE:** un `THIRD-PARTY-NOTICES.md` generato in build (#8) e consultabile dalla schermata "Informazioni".
4. **Licenza del progetto CodexVTT:** Apache License 2.0 (file `LICENSE`, avvisi di terzi in `NOTICE`), decisa dal maintainer il 2026-10-23. Le illustrazioni delle mascotte non sono coperte da questa licenza finché non si verifica quella di svgapp.ai.
