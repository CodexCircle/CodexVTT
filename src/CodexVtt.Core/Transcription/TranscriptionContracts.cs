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

/// <summary>Runtime di inferenza locale (ADR-0002, ADR-0009). L'implementazione non deve usare la rete.</summary>
public interface ITranscriptionEngine
{
    /// <param name="model">Voce del catalogo (motore, lingue, file).</param>
    /// <param name="modelPath">File del modello, oppure cartella per i modelli a più file.</param>
    Task<ITranscriptionSession> LoadAsync(CodexVtt.Core.Models.ModelDescriptor model, string modelPath, CancellationToken ct);
}

/// <summary>Sceglie il motore in base al campo <c>engine</c> del modello.</summary>
public sealed class EngineRegistry : ITranscriptionEngine
{
    private readonly IReadOnlyDictionary<string, ITranscriptionEngine> _engines;

    public EngineRegistry(IReadOnlyDictionary<string, ITranscriptionEngine> engines) => _engines = engines;

    public Task<ITranscriptionSession> LoadAsync(CodexVtt.Core.Models.ModelDescriptor model, string modelPath, CancellationToken ct) =>
        _engines.TryGetValue(model.Engine, out var engine)
            ? engine.LoadAsync(model, modelPath, ct)
            : throw new CodexVtt.Core.Errors.CodexVttException(CodexVtt.Core.Errors.ErrorCodes.EngineUnavailable,
                $"Nessun motore disponibile per «{model.Engine}».",
                new Dictionary<string, string> { ["engine"] = model.Engine });
}

public enum JobStatus
{
    Queued,
    Decoding,
    Transcribing,
    Completed,
    Cancelled,
    Failed,

    /// <summary>In pausa: il motore è fermo e non usa la CPU; si riprende dal punto raggiunto.</summary>
    Paused,
}

/// <param name="Remaining">Tempo stimato alla fine del file, dalla velocità misurata finora; assente finché è troppo presto per stimarlo.</param>
/// <param name="Speed">Secondi di audio trascritti per secondo reale (2 = il doppio del tempo reale); serve a stimare anche i file in coda.</param>
public sealed record JobProgress(string JobId, JobStatus Status, int Percent, TimeSpan Processed, TimeSpan Total, TimeSpan? Remaining = null, double? Speed = null);
