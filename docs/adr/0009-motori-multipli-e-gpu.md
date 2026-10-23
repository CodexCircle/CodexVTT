# ADR-0009 — Motori di trascrizione multipli, modelli a più file e GPU

- Stato: Proposto
- Data: 2026-10-23
- Modifica: ADR-0002 (runtime), ADR-0003 (catalogo e distribuzione dei modelli)

## Contesto

Fino alla 1.0 CodexVTT eseguiva solo Whisper (whisper.cpp tramite Whisper.net), sulla CPU: un modello era un solo
file GGML e il runtime GPU era escluso (ADR-0002). Volevamo aggiungere altri modelli locali per l'italiano (NVIDIA
Parakeet TDT 0.6B v3, Canary-1B-v2 e Cohere Transcribe) con un'indicazione di affidabilità, scaricabili dall'app, e
usare la scheda video quando c'è.

## Decisione

### Motori
- `ITranscriptionEngine.LoadAsync` riceve la voce del catalogo e `EngineRegistry` sceglie il motore dal campo `engine`:
  `whisper` (Whisper.net), `sherpa-transducer` (Parakeet), `sherpa-canary` (Canary), `sherpa-cohere` (Cohere).
- Parakeet, Canary e Cohere girano con **sherpa-onnx 2.0.0** (Apache-2.0, ONNX Runtime) in `CodexVtt.Transcription.Sherpa`,
  sulla CPU, in int8. Il blocco da trascrivere (10 minuti) è diviso in finestre di circa 28 secondi tagliate nei punti
  silenziosi. Con i timestamp dei token (Parakeet) i segmenti seguono le frasi; senza (Canary, Cohere) una finestra è un segmento.
- **I runtime nativi stanno nell'installer, non si scaricano** (sicurezza e aggiornamenti): +22 MB circa per sherpa-onnx e ONNX
  Runtime su Windows. Si scaricano solo i pesi, che sono dati verificati con SHA-256.
- Canary e Cohere non rilevano la lingua: `requiresLanguage`. L'interfaccia non li avvia con «Rilevamento automatico» e il core
  rifiuta subito la richiesta con `language_required`.
- Il modello Canary usa origine e destinazione uguali (trascrive, non traduce): richiede sherpa-onnx ≥ 2.0 (le 1.13 convertono
  in inglese le lingue non supportate senza avvisare).

### Catalogo v2 e download
- Un modello è un file (Whisper, come prima) oppure una **cartella** `models\<id>` con più file (`files[]` con percorso,
  dimensione e SHA-256), una `source` propria fissata a un commit di Hugging Face, `engine`, `preview`, `requiresLanguage`,
  `attribution` e `accuracy`. Nessun host nuovo nell'allowlist: tutto resta su `huggingface.co`/`hf.co` (ADR-0006 invariato).
- `ModelDownloader` scarica i file in sequenza con ripresa e verifica per file, progresso aggregato sui byte del modello;
  `ModelStore` installa la cartella in un colpo solo (rinomina atomica) solo quando tutti i file sono verificati e scrive un
  marker per file. Un file mancante o alterato rende il modello «danneggiato» e non viene mai usato.
- `tools/pin_catalog.py --check` verifica hash e dimensioni di tutti i modelli; i modelli a più file si fissano uno alla
  volta (`--model`) perché sono export di terzi.
- Tutti e tre i nuovi modelli sono **anteprime**: mai raccomandati da soli, segnalati nell'interfaccia. Canary è un export della
  community (non NVIDIA); Parakeet e Cohere sono convertiti dai maintainer di sherpa-onnx.

### Affidabilità
- Definizione: 100 − WER% sull'italiano. Per ora è il valore **pubblicato dagli autori** (FLEURS-it dalle schede di
  nvidia/parakeet-tdt-0.6b-v3: 3,00% e nvidia/canary-1b-v2: 3,07%), segnalato come «non misurato da CodexVTT» e riferito al modello
  originale, non all'export int8. Per Whisper e Cohere non c'è ancora un numero e l'app lo dice («non ancora misurata»).
- `tools/CodexVtt.Bench` scarica i modelli con lo stesso downloader dell'app, li esegue sullo stesso audio e calcola tempo, velocità e
  WER (`CodexVtt.Core.Evaluation.WordErrorRate`, stessa normalizzazione per riferimento e ipotesi). È lo strumento per misurare
  tutti i modelli su un insieme comune (issue dedicata); non si inventano numeri nel frattempo.

### GPU
- Whisper prova il runtime **Vulkan** (`Whisper.net.Runtime.Vulkan`, funziona con schede NVIDIA, AMD e Intel con i loro driver)
  solo se `GpuProbe` trova davvero una scheda video con Vulkan (interrogando il driver, senza caricare nessun motore), la CPU ha AVX2
  e l'impostazione «Usa la scheda video» è attiva (di default sì). Altrimenti resta sul runtime CPU, come prima.
- Il runtime nativo si sceglie una volta per processo: cambiare l'impostazione richiede di riavviare.
- Il nome del runtime nel risultato dice la verità: «Vulkan (GPU)» solo se whisper.cpp ha scelto la scheda; se il runtime è caricato ma
  non trova dispositivi risulta «Cpu». La scheda trovata si vede nella scheda Modelli. `CODEXVTT_WHISPER_LOG=1` stampa i messaggi di ggml.
- **Parakeet, Canary e Cohere restano sulla CPU**: la GPU in sherpa-onnx richiede i pacchetti CUDA di ONNX Runtime (centinaia di MB e il
  toolkit NVIDIA), fuori dal perimetro di questa versione.
- Nel container Docker la GPU non è disponibile (servono driver e runtime NVIDIA nel container).

## Conseguenze

- Installer più grande (Vulkan ≈ 57 MB, sherpa-onnx ≈ 22 MB su win-x64) e immagine Docker più grande.
- Misurato su una CPU i5-13400F con 6 core (VM), audio italiano di prova: Whisper Small ≈ 2,3× il tempo reale, Parakeet 0,35×,
  Canary 0,54×, Cohere 0,82×. Nel catalogo `relativeCost` è calibrato su queste misure (2, 4 e 6).
- Il percorso GPU **non è stato provato su una scheda reale** (la macchina di sviluppo è una VM senza GPU): è coperto da test di
  logica e dal ripiego su CPU, ma va verificato su NVIDIA, AMD e Intel (issue dedicata).

## Punti aperti

- Misurare l'affidabilità di tutti i modelli su FLEURS-it con `CodexVtt.Bench` e sostituire i valori pubblicati.
- GPU anche per sherpa-onnx (CUDA/DirectML), selezione del backend in Impostazioni, supporto GPU nel container.
- Licenza dell'export Canary della community e del repository Cohere (verificate sulle schede il 2026-10-23, nessun testo di licenza nel repository dell'export).
