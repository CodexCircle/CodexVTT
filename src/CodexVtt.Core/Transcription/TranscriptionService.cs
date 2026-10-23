using System.Collections.Concurrent;
using System.Diagnostics;
using Microsoft.Extensions.Logging;
using CodexVtt.Core.Audio;
using CodexVtt.Core.Errors;
using CodexVtt.Core.Models;

namespace CodexVtt.Core.Transcription;

public sealed record TranscriptionJob(string JobId, string AudioId, string ModelId)
{
    public JobStatus Status { get; internal set; } = JobStatus.Queued;

    public TranscriptionResult? Result { get; internal set; }

    public CodexVttException? Error { get; internal set; }

    public IReadOnlyList<TranscriptSegment> PartialSegments => Partial;

    internal List<TranscriptSegment> Partial { get; } = [];

    internal CancellationTokenSource Cancellation { get; } = new();
}

/// <summary>
/// Orchestrazione locale di una trascrizione: verifica il modello, decodifica a blocchi,
/// esegue l'inferenza, aggrega progresso e segmenti. Un job alla volta in V1.
/// </summary>
public sealed partial class TranscriptionService : IDisposable
{
    private readonly ModelStore _models;
    private readonly AudioLibrary _audio;
    private readonly ITranscriptionEngine _engine;
    private readonly AudioChunker _chunker;
    private readonly ILogger<TranscriptionService> _logger;
    private readonly ConcurrentDictionary<string, TranscriptionJob> _jobs = new();
    private readonly SemaphoreSlim _single = new(1, 1);
    private TranscriptionJob? _current;

    public TranscriptionService(
        ModelStore models,
        AudioLibrary audio,
        ITranscriptionEngine engine,
        ILogger<TranscriptionService> logger,
        AudioChunker? chunker = null)
    {
        _models = models;
        _audio = audio;
        _engine = engine;
        _logger = logger;
        _chunker = chunker ?? AudioChunker.Default;
    }

    public event Action<JobProgress>? Progress;

    public event Action<TranscriptionJob>? Finished;

    /// <summary>Job in esecuzione, se c'è: serve alla UI che riapre la pagina mentre il core lavora (versione server).</summary>
    public TranscriptionJob? Current => Volatile.Read(ref _current);

    public TranscriptionJob Get(string jobId) =>
        _jobs.TryGetValue(jobId, out var job)
            ? job
            : throw new CodexVttException(ErrorCodes.TranscriptionNotFound, "Trascrizione non trovata.");

    /// <summary>Avvia un job in background e restituisce subito il suo identificativo.</summary>
    public TranscriptionJob Start(string audioId, string modelId, TranscriptionOptions options)
    {
        var info = _audio.Info(audioId);
        if (_models.Catalog.Find(modelId) is null)
            throw new CodexVttException(ErrorCodes.ModelUnknown, $"Modello sconosciuto: {modelId}");
        if (!_single.Wait(0))
            throw new CodexVttException(ErrorCodes.TranscriptionBusy, "È già in corso una trascrizione.");

        var job = new TranscriptionJob(Guid.NewGuid().ToString("N"), audioId, modelId);
        _jobs[job.JobId] = job;
        Volatile.Write(ref _current, job);
        _ = Task.Run(async () =>
        {
            try
            {
                await RunAsync(job, info, options).ConfigureAwait(false);
            }
            finally
            {
                Interlocked.CompareExchange(ref _current, null, job);
                _single.Release();
                Finished?.Invoke(job);
            }
        });
        return job;
    }

    public async Task<TranscriptionResult> RunToCompletionAsync(string audioId, string modelId, TranscriptionOptions options, CancellationToken ct)
    {
        var job = Start(audioId, modelId, options);
        await using var registration = ct.Register(() => Cancel(job.JobId)).ConfigureAwait(false);
        var done = new TaskCompletionSource();
        void OnFinished(TranscriptionJob j)
        {
            if (j.JobId == job.JobId)
                done.TrySetResult();
        }

        Finished += OnFinished;
        try
        {
            if (job.Status is JobStatus.Completed or JobStatus.Failed or JobStatus.Cancelled)
                done.TrySetResult();
            await done.Task.ConfigureAwait(false);
        }
        finally
        {
            Finished -= OnFinished;
        }

        return job.Status switch
        {
            JobStatus.Completed => job.Result!,
            JobStatus.Cancelled => throw new OperationCanceledException(ct),
            _ => throw job.Error ?? new CodexVttException(ErrorCodes.TranscriptionFailed, "Trascrizione non riuscita."),
        };
    }

    public void Dispose()
    {
        foreach (var job in _jobs.Values)
            job.Cancellation.Cancel();
        _single.Dispose();
    }

    public void Cancel(string jobId)
    {
        if (_jobs.TryGetValue(jobId, out var job))
            job.Cancellation.Cancel();
    }

    private static readonly TimeSpan MinCheckedDuration = TimeSpan.FromSeconds(5);
    private const double MinDecodedFraction = 0.5;

    private async Task RunAsync(TranscriptionJob job, AudioInfo info, TranscriptionOptions options)
    {
        var ct = job.Cancellation.Token;
        var watch = Stopwatch.StartNew();
        try
        {
            var model = _models.Catalog.Find(job.ModelId)!;
            var modelPath = await _models.RequireReadyAsync(job.ModelId, ct).ConfigureAwait(false);
            Report(job, JobStatus.Decoding, 0, TimeSpan.Zero, info.Duration);

            await using var session = await _engine.LoadAsync(modelPath, ct).ConfigureAwait(false);
            using var reader = _audio.Open(job.AudioId);

            string? language = null;
            var decoded = TimeSpan.Zero;
            foreach (var chunk in _chunker.Split(reader))
            {
                ct.ThrowIfCancellationRequested();
                var chunkProgress = new SyncProgress<int>(p =>
                {
                    var processed = chunk.Offset + (chunk.Duration * (p / 100d));
                    Report(job, JobStatus.Transcribing, Percent(processed, info.Duration), processed, info.Duration);
                });

                await foreach (var segment in session.TranscribeAsync(chunk.Samples, options, chunkProgress, ct).ConfigureAwait(false))
                {
                    // [BLANK_AUDIO], [MUSIC]…: marcatori di Whisper, non parlato.
                    if (SpecialTokens.IsMarker(segment.Text))
                        continue;
                    var shifted = segment with { Start = segment.Start + chunk.Offset, End = segment.End + chunk.Offset };
                    lock (job.Partial)
                        job.Partial.Add(shifted);
                }

                decoded += chunk.Duration;
                language ??= session.DetectedLanguage;
                var done = chunk.Offset + chunk.Duration;
                Report(job, JobStatus.Transcribing, Percent(done, info.Duration), done, info.Duration);
            }

            // La durata dell'header può essere stimata: se i campioni realmente decodificati sono molto meno,
            // il file è troncato o danneggiato e la trascrizione non sarebbe affidabile.
            if (info.Duration >= MinCheckedDuration && decoded < info.Duration * MinDecodedFraction)
            {
                throw new CodexVttException(ErrorCodes.AudioCorrupted, "Il file audio è troncato o danneggiato.",
                    new Dictionary<string, string>
                    {
                        ["declaredSeconds"] = ((int)info.Duration.TotalSeconds).ToString(System.Globalization.CultureInfo.InvariantCulture),
                        ["decodedSeconds"] = ((int)decoded.TotalSeconds).ToString(System.Globalization.CultureInfo.InvariantCulture),
                    });
            }

            job.Result = new TranscriptionResult(
                info.FileName,
                info.Duration,
                language,
                new ModelUsage(model.Id, model.DisplayName, model.File, model.Sha256 ?? "", session.Runtime),
                job.Partial.ToList(),
                watch.Elapsed,
                DateTimeOffset.Now);
            Report(job, JobStatus.Completed, 100, info.Duration, info.Duration);
            LogCompleted(job.JobId, model.Id, session.Runtime, info.Duration, watch.Elapsed);
        }
        catch (OperationCanceledException)
        {
            Report(job, JobStatus.Cancelled, 0, TimeSpan.Zero, info.Duration);
            LogCancelled(job.JobId);
        }
        catch (CodexVttException ex)
        {
            Fail(job, ex, info);
        }
        catch (Exception ex)
        {
            Fail(job, new CodexVttException(ErrorCodes.TranscriptionFailed, "Errore durante la trascrizione.", inner: ex), info);
        }
    }

    private void Fail(TranscriptionJob job, CodexVttException error, AudioInfo info)
    {
        job.Error = error;
        Report(job, JobStatus.Failed, 0, TimeSpan.Zero, info.Duration);
        LogFailed(error, job.JobId, error.Code);
    }

    private void Report(TranscriptionJob job, JobStatus status, int percent, TimeSpan processed, TimeSpan total)
    {
        lock (job.Partial)
        {
            if (job.Status is JobStatus.Completed or JobStatus.Cancelled or JobStatus.Failed && job.Status != status)
                return;
            job.Status = status;
        }

        Progress?.Invoke(new JobProgress(job.JobId, status, percent, processed, total));
    }

    private static int Percent(TimeSpan processed, TimeSpan total) =>
        total <= TimeSpan.Zero ? 0 : (int)Math.Clamp(processed / total * 100, 0, 99);

    /// <summary>IProgress sincrono: evita che aggiornamenti in ritardo arrivino dopo la fine del job.</summary>
    private sealed class SyncProgress<T>(Action<T> handler) : IProgress<T>
    {
        public void Report(T value) => handler(value);
    }

    // I log non contengono testo trascritto né percorsi (ADR-0006).
    [LoggerMessage(Level = LogLevel.Information, Message = "Trascrizione {JobId} completata con {ModelId} ({Runtime}): audio {Duration}, tempo {Elapsed}")]
    private partial void LogCompleted(string jobId, string modelId, string runtime, TimeSpan duration, TimeSpan elapsed);

    [LoggerMessage(Level = LogLevel.Information, Message = "Trascrizione {JobId} annullata")]
    private partial void LogCancelled(string jobId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Trascrizione {JobId} fallita: {Code}")]
    private partial void LogFailed(Exception ex, string jobId, string code);
}
