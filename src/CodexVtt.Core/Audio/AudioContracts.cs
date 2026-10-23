namespace CodexVtt.Core.Audio;

/// <summary>Metadati del file audio mostrati in UI e allegati al risultato.</summary>
public sealed record AudioInfo(
    string FileName,
    string Format,
    TimeSpan Duration,
    int SampleRate,
    int Channels,
    long SizeBytes);

/// <summary>
/// Sorgente PCM normalizzata: float mono a 16 kHz, letta a blocchi per non caricare in memoria
/// file lunghi (ADR-0004).
/// </summary>
public interface IPcmReader : IDisposable
{
    /// <summary>Riempie <paramref name="buffer"/>; restituisce 0 a fine stream.</summary>
    int Read(Span<float> buffer);
}

/// <summary>Decodifica di un file audio in PCM 16 kHz mono. Implementazioni: WAV gestito e Media Foundation.</summary>
public interface IAudioDecoder
{
    /// <summary>True se il decoder sa gestire il file (in base al contenuto, non solo all'estensione).</summary>
    bool CanDecode(string path);

    AudioInfo Inspect(string path);

    IPcmReader OpenPcm16kMono(string path);
}

public static class AudioConstants
{
    public const int WhisperSampleRate = 16_000;
}
