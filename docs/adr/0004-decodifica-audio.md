# ADR-0004 — Decodifica e normalizzazione audio

- Stato: Proposto
- Data: 2026-10-14
- Issue: #1 (implementazione in #5)

## Contesto

whisper.cpp richiede PCM mono a 16 kHz. Gli utenti caricheranno MP3, M4A, WAV e altri formati, anche molto lunghi. Il decoder influisce su:

- licenze;
- dimensione del pacchetto;
- formati supportati;
- sicurezza, perché il parsing di file non fidati è superficie d'attacco.

## Opzioni considerate

| Opzione | Pro | Contro |
|---|---|---|
| **Windows Media Foundation** tramite NAudio (MIT) o API WinRT | Già nel sistema operativo, nessun binario da distribuire, codec mantenuti e aggiornati da Windows Update, nessun obbligo LGPL | Formati limitati ai codec installati (Opus/Vorbis possono richiedere estensioni dello Store); solo Windows, quindi serve un altro decoder in V3/V4 |
| FFmpeg (librerie o eseguibile) | Supporta praticamente tutto; multipiattaforma | Distribuzione da decine di MB; obblighi LGPL (dynamic linking, sorgenti, avvisi) o GPL secondo la build; brevetti codec; patch di sicurezza a carico nostro |
| Solo WAV (decodifica nostra) | Banale | Non soddisfa i casi d'uso reali |

## Decisione

1. **V1 usa Media Foundation** dietro l'interfaccia `IAudioDecoder` del core. L'implementazione `CodexVtt.Audio.Windows` è basata su NAudio (`MediaFoundationReader` + resampler) e produce PCM float 16 kHz mono in streaming. In #5 si può scegliere `AudioGraph`/WinRT se più adatto.
2. **Formati V1** (da confermare in #2 con fixture per ciascuno):
   - WAV, MP3, M4A/AAC, WMA, FLAC: decodificatori presenti in Windows 11;
   - OGG/Opus: solo se il codec è disponibile sul sistema. Altrimenti l'errore `audio_codec_missing` indica come installarlo;
   - tracce audio di contenitori video (MP4): fuori scope V1, salvo diversa indicazione in #2.
3. **Validazione:** l'app controlla il contenuto reale (header/container), non solo l'estensione. Per la durata e la dimensione massime definite in #2, una durata a zero o un file illeggibile producono un errore tipizzato.
4. **Streaming:** l'audio non viene mai caricato per intero in memoria come float. Il decoder produce blocchi e il core li passa al runtime, oppure usa un file WAV temporaneo a 16 kHz in `temp\`, eliminato a fine job, all'annullamento e al riavvio dopo un crash. La scelta tra le due strade si fa in #5 con misure di memoria su file di 1 h o più.
5. **Nessun FFmpeg** nel pacchetto V1. FFmpeg resta l'opzione per V3/V4 (Linux nel container) o per formati aggiuntivi, con valutazione licenze dedicata.

## Conseguenze

- Pacchetto più piccolo e nessun obbligo di licenza aggiuntivo per la decodifica.
- Il supporto di alcuni formati dipende dai codec installati: va comunicato chiaramente in UI e nella documentazione.
- In V4 (server Linux) servirà un'altra implementazione di `IAudioDecoder`.

## Punti aperti

- Disponibilità di Opus/Vorbis su un'installazione pulita di Windows 11 (#9).
- Streaming verso il runtime oppure WAV temporaneo: da misurare in #5.
