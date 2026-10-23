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

    /// <summary>Primo avanzamento reale (dopo caricamento del modello): da qui si misura la velocità per il tempo rimanente.</summary>
    internal long? BaselineTimestamp { get; set; }

    internal TimeSpan BaselineProcessed { get; set; }

    // --- pausa (vedi TranscriptionService.Pause): lo stato è protetto da PauseGate
    internal Lock PauseGate { get; } = new();

    internal bool PauseRequested { get; set; }

    internal TaskCompletionSource? ResumeSignal { get; set; }

    /// <summary>Chiamata corrente al motore: annullandola si ferma davvero il calcolo.</summary>
    internal CancellationTokenSource? SliceCancellation { get; set; }

    internal long PausedAtTimestamp { get; set; }
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
    private readonly TimeProvider _time;
    private readonly ConcurrentDictionary<string, TranscriptionJob> _jobs = new();
    private readonly SemaphoreSlim _single = new(1, 1);
    private TranscriptionJob? _current;

    public TranscriptionService(
        ModelStore models,
        AudioLibrary audio,
        ITranscriptionEngine engine,
        ILogger<TranscriptionService> logger,
        AudioChunker? chunker = null,
        TimeProvider? timeProvider = null)
    {
        _time = timeProvider ?? TimeProvider.System;
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

    /// <summary>
    /// Mette in pausa un job in corso. Il calcolo si ferma subito (si annulla la chiamata al motore, quindi la CPU torna
    /// libera); i segmenti già prodotti restano e alla ripresa si riparte dalla fine dell'ultimo. Il modello resta in memoria.
    /// </summary>
    public void Pause(string jobId)
    {
        var job = Get(jobId);
        lock (job.PauseGate)
        {
            if (job.PauseRequested || job.Status is JobStatus.Completed or JobStatus.Cancelled or JobStatus.Failed)
                return;
            job.PauseRequested = true;
            job.PausedAtTimestamp = _time.GetTimestamp();
            job.ResumeSignal = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            job.SliceCancellation?.Cancel();
        }
    }

    /// <summary>Riprende un job in pausa. Il tempo trascorso in pausa non conta nella stima del tempo rimanente.</summary>
    public void Resume(string jobId)
    {
        var job = Get(jobId);
        lock (job.PauseGate)
        {
            if (!job.PauseRequested)
                return;
            job.PauseRequested = false;
            lock (job.Partial)
            {
                if (job.BaselineTimestamp is { } baseline)
                    job.BaselineTimestamp = baseline + (_time.GetTimestamp() - job.PausedAtTimestamp);
            }

            job.ResumeSignal?.TrySetResult();
        }
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
            // Alcuni motori (Canary) non rilevano la lingua: meglio dirlo subito che dopo minuti di decodifica.
            if (model.RequiresLanguage && string.Equals(options.Language, "auto", StringComparison.OrdinalIgnoreCase))
            {
                throw new CodexVttException(ErrorCodes.LanguageRequired, $"Il modello {model.DisplayName} richiede di scegliere la lingua.",
                    new Dictionary<string, string> { ["model"] = model.DisplayName });
            }

            var modelPath = await _models.RequireReadyAsync(job.ModelId, ct).ConfigureAwait(false);
            Report(job, JobStatus.Decoding, 0, TimeSpan.Zero, info.Duration);

            await using var session = await _engine.LoadAsync(model, modelPath, ct).ConfigureAwait(false);
            using var reader = _audio.Open(job.AudioId);

            string? language = null;
            var decoded = TimeSpan.Zero;
            foreach (var chunk in _chunker.Split(reader))
            {
                ct.ThrowIfCancellationRequested();
                // Dentro il blocco si può ripartire da un punto qualsiasi dopo una pausa: «from» è il primo campione ancora da fare.
                var from = 0;
                while (from < chunk.Samples.Length)
                {
                    await WaitWhilePausedAsync(job, chunk.Offset + SamplesToTime(from), info.Duration, ct).ConfigureAwait(false);

                    var sliceOffset = chunk.Offset + SamplesToTime(from);
                    var slice = chunk.Samples.AsMemory(from);
                    var sliceDuration = SamplesToTime(slice.Length);
                    var chunkProgress = new SyncProgress<int>(p =>
                    {
                        var processed = sliceOffset + (sliceDuration * (p / 100d));
                        Report(job, JobStatus.Transcribing, Percent(processed, info.Duration), processed, info.Duration);
                    });

                    using var sliceCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                    lock (job.PauseGate)
                    {
                        job.SliceCancellation = sliceCts;
                        if (job.PauseRequested)
                            sliceCts.Cancel();
                    }

                    var lastEnd = TimeSpan.Zero;
                    var produced = false;
                    try
                    {
                        await foreach (var segment in session.TranscribeAsync(slice, options, chunkProgress, sliceCts.Token).ConfigureAwait(false))
                        {
                            produced = true;
                            lastEnd = segment.End > lastEnd ? segment.End : lastEnd;
                            // [BLANK_AUDIO], [MUSIC]…: marcatori di Whisper, non parlato.
                            if (SpecialTokens.IsMarker(segment.Text))
                                continue;
                            var shifted = segment with { Start = segment.Start + sliceOffset, End = segment.End + sliceOffset };
                            lock (job.Partial)
                                job.Partial.Add(shifted);
                        }

                    }
                    catch (OperationCanceledException) when (!ct.IsCancellationRequested && sliceCts.IsCancellationRequested)
                    {
                        // Fermato da una pausa: lo gestisce il controllo qui sotto.
                    }
                    finally
                    {
                        lock (job.PauseGate)
                            job.SliceCancellation = null;
                    }

                    ct.ThrowIfCancellationRequested();
                    if (sliceCts.IsCancellationRequested)
                    {
                        // Pausa (anche se il motore ha chiuso senza eccezione): si riparte dalla fine dell'ultimo segmento
                        // completo, o dall'inizio della fetta se non ne è uscito nessuno. Il lavoro già consegnato non si rifà.
                        if (produced)
                            from = Math.Min(chunk.Samples.Length, from + Math.Max(1, TimeToSamples(lastEnd)));
                        // Un resto di pochi centesimi di secondo non è parlato: non si rilancia il motore per quello.
                        if (chunk.Samples.Length - from < TimeToSamples(TimeSpan.FromSeconds(0.3)))
                            from = chunk.Samples.Length;
                    }
                    else
                    {
                        from = chunk.Samples.Length;
                    }
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
                new ModelUsage(model.Id, model.DisplayName, model.File, model.Fingerprint, session.Runtime),
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

    private static TimeSpan SamplesToTime(int samples) => TimeSpan.FromSeconds((double)samples / AudioConstants.WhisperSampleRate);

    private static int TimeToSamples(TimeSpan time) => (int)Math.Round(time.TotalSeconds * AudioConstants.WhisperSampleRate);

    /// <summary>Se è stata chiesta una pausa, lo comunica e aspetta la ripresa (o l'annullamento).</summary>
    private async Task WaitWhilePausedAsync(TranscriptionJob job, TimeSpan processed, TimeSpan total, CancellationToken ct)
    {
        Task? wait;
        lock (job.PauseGate)
            wait = job.PauseRequested ? job.ResumeSignal?.Task : null;
        if (wait is null)
            return;

        Report(job, JobStatus.Paused, Percent(processed, total), processed, total);
        LogPaused(job.JobId);
        await wait.WaitAsync(ct).ConfigureAwait(false);
        Report(job, JobStatus.Transcribing, Percent(processed, total), processed, total);
        LogResumed(job.JobId);
    }

    /// <summary>Servono almeno questi secondi, sia di audio sia reali, prima di fidarsi della velocità misurata.</summary>
    private static readonly TimeSpan MinEstimateWindow = TimeSpan.FromSeconds(2);

    private void Report(TranscriptionJob job, JobStatus status, int percent, TimeSpan processed, TimeSpan total)
    {
        TimeSpan? remaining = null;
        double? speed = null;
        lock (job.Partial)
        {
            if (job.Status is JobStatus.Completed or JobStatus.Cancelled or JobStatus.Failed && job.Status != status)
                return;
            job.Status = status;

            if (status == JobStatus.Completed)
            {
                remaining = TimeSpan.Zero;
            }
            else if (status == JobStatus.Transcribing && processed > TimeSpan.Zero)
            {
                var now = _time.GetTimestamp();
                if (job.BaselineTimestamp is not { } start)
                {
                    job.BaselineTimestamp = now;
                    job.BaselineProcessed = processed;
                }
                else
                {
                    var elapsed = _time.GetElapsedTime(start, now);
                    var done = processed - job.BaselineProcessed;
                    if (elapsed >= MinEstimateWindow && done >= MinEstimateWindow)
                    {
                        speed = done.TotalSeconds / elapsed.TotalSeconds;
                        remaining = TimeSpan.FromSeconds(Math.Max(0, (total - processed).TotalSeconds / speed.Value));
                    }
                }
            }
        }

        Progress?.Invoke(new JobProgress(job.JobId, status, percent, processed, total, remaining, speed));
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

    [LoggerMessage(Level = LogLevel.Information, Message = "Trascrizione {JobId} in pausa")]
    private partial void LogPaused(string jobId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Trascrizione {JobId} ripresa")]
    private partial void LogResumed(string jobId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Trascrizione {JobId} annullata")]
    private partial void LogCancelled(string jobId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Trascrizione {JobId} fallita: {Code}")]
    private partial void LogFailed(Exception ex, string jobId, string code);
}
