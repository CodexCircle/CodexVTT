# Revisione tecnica per aree (issue #1)

Questo documento raccoglie i pareri tecnici che hanno portato agli ADR.

**Metodo:** questa prima tornata è stata condotta da un singolo agente (Claude Code), che ha esaminato ogni area dal punto di vista del ruolo indicato. Ogni parere è supportato da:

- ispezione dei pacchetti reali (Whisper.net 1.9.1 e relativi runtime, versioni NuGet correnti);
- uno spike eseguibile (`spike/runtime-probe`).

Se si vogliono pareri indipendenti, ad esempio agenti separati per area o revisori umani, vanno aggiunti qui come sezioni "Parere aggiuntivo" prima di accettare gli ADR.

## Architettura software

- Separare il core (dominio, nessuna rete, nessuna UI) da adattatori nativi e shell. Su questo poggia il riuso in V3/V4 e la verificabilità della privacy.
- Un contratto JSON versionato tra UI e core, indipendente dal trasporto, evita che la UI dipenda dalle firme C#. Lo stesso contratto può passare su HTTP in V4.
- La UI non deve manipolare percorsi file arbitrari: usa identificativi opachi.
- **→ ADR-0001**

## Runtime ML / Whisper

- whisper.cpp è oggi il runtime Whisper più adatto a un'app .NET distribuita a utenti finali: nativo, quantizzabile, multipiattaforma, senza Python.
- Whisper.net ne è il binding .NET di riferimento. Il pacchetto 1.9.1 include binari nativi per Windows x64/x86/ARM64, Android e iOS, e offre segmenti in streaming, progresso, cancellazione, rilevamento lingua e VAD.
- Attenzione ai requisiti: Windows 11 / Server 2022, VC++ Redistributable, AVX/AVX2/FMA/F16C (altrimenti runtime NoAvx).
- ONNX Runtime con DirectML è interessante per la GPU, ma richiede di reimplementare decoding e tokenizer: va tenuto come alternativa, non come prima scelta.
- **→ ADR-0002**

## Distribuzione modelli

- I modelli non vanno nell'installer: le dimensioni sono troppo variabili e la scelta dipende dalla macchina.
- Serve un catalogo con hash SHA-256 e URL a revisione fissata, con verifica obbligatoria prima dell'uso.
- L'import da file copre gli scenari offline e aziendali.
- Non usare il downloader integrato di Whisper.net, che non verifica gli hash.
- **→ ADR-0003**

## Audio

- Media Foundation copre i formati comuni senza distribuire codec né assumere obblighi LGPL.
- FFmpeg resta l'opzione per il server Linux (V4).
- I file lunghi vanno gestiti in streaming.
- **→ ADR-0004**

## Release engineering

- L'installazione per utente senza privilegi di amministratore (Velopack) riduce gli attriti.
- MSIX senza certificato è poco pratico per il sideload.
- La firma del codice è la decisione esterna più importante per l'adozione: va presa presto.
- Gli aggiornamenti in V1 devono essere solo su richiesta, per coerenza con la postura privacy.
- **→ ADR-0005**

## Sicurezza e privacy

- La garanzia "nessun dato esce" deve essere strutturale:
  - un solo progetto con `HttpClient`;
  - allowlist degli host;
  - test di architettura su dipendenze e namespace di rete;
  - CSP stretta;
  - nessun SDK di telemetria.
- Verifica empirica con cattura del traffico.
- **→ ADR-0006**

## UX

- Prima del download l'utente deve vedere: modello consigliato e perché, spazio, RAM, velocità attesa.
- Il modello usato deve restare visibile durante e dopo la trascrizione.
- Gli errori devono indicare l'azione correttiva (codec mancante, spazio, RAM, modello corrotto).
- Va aggiunto un avviso sulle possibili allucinazioni di Whisper su silenzio o musica.
- **→ #2, #6**

## Qualità

- Servono fixture con licenza libera e testo di riferimento (anche italiane), con WER per modello.
- La matrice hardware deve includere almeno una macchina senza GPU con 8 GB di RAM.
- Il test offline e la cattura del traffico sono criteri di rilascio, non attività opzionali.
- **→ #9**

## Legale e licenze

- La catena runtime + modelli + binding è interamente MIT.
- I punti da presidiare sono:
  - redistribuzione dei componenti Microsoft (VC++, Windows App SDK);
  - eventuale FFmpeg in futuro;
  - modelli di diarization in V2, spesso con termini restrittivi.
- La licenza del progetto CodexVTT è ancora da definire.
- **→ [licenze](licenze.md)**
