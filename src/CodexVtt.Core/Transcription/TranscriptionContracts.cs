namespace CodexVtt.Core.Transcription;

public sealed record TranscriptSegment(TimeSpan Start, TimeSpan End, string Text, float? Probability);

public sealed record TranscriptionOptions(string Language = "auto", bool Translate = false, int? Threads = null);

/// <summary>Modello effettivamente usato per una trascrizione (registrato e mostrato all'utente).</summary>
public sealed record ModelUsage(string ModelId, string DisplayName, string File, string Sha256, string Runtime);

public sealed record TranscriptionResult(
    string AudioFileName,
    TimeSpan AudioDuration,
    string? DetectedLanguage,
    ModelUsage Model,
    IReadOnlyList<TranscriptSegment> Segments,
    TimeSpan Elapsed,
    DateTimeOffset CompletedAt)
{
    public string Text => string.Join(' ', Segments.Select(s => s.Text.Trim()).Where(t => t.Length > 0));
}

/// <summary>Sessione di un modello caricato in memoria.</summary>
public interface ITranscriptionSession : IAsyncDisposable
{
    /// <summary>Nome del runtime nativo caricato (es. "Cpu", "CpuNoAvx").</summary>
    string Runtime { get; }

    /// <summary>
    /// Trascrive un blocco PCM 16 kHz mono. I timestamp sono relativi all'inizio del blocco.
    /// <paramref name="progress"/> riceve la percentuale (0-100) del blocco.
    /// </summary>
    IAsyncEnumerable<TranscriptSegment> TranscribeAsync(
        ReadOnlyMemory<float> samples,
        TranscriptionOptions options,
        IProgress<int>? progress,
        CancellationToken ct);

    /// <summary>Lingua rilevata nell'ultimo blocco, se disponibile.</summary>
    string? DetectedLanguage { get; }
}

/// <summary>Runtime di inferenza locale (ADR-0002). L'implementazione non deve usare la rete.</summary>
public interface ITranscriptionEngine
{
    Task<ITranscriptionSession> LoadAsync(string modelPath, CancellationToken ct);
}

public enum JobStatus
{
    Queued,
    Decoding,
    Transcribing,
    Completed,
    Cancelled,
    Failed,
}

public sealed record JobProgress(string JobId, JobStatus Status, int Percent, TimeSpan Processed, TimeSpan Total);
