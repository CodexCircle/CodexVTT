using System.Text.Json;
using CodexVtt.Core.Benchmark;
using CodexVtt.Core.Errors;
using CodexVtt.Core.Models;
using CodexVtt.Core.Transcription;

namespace CodexVtt.Bridge;

/// <summary>
/// Test bench dei modelli: i brani ufficiali (italiano e inglese, puliti e con rumore) o un file scelto dall'utente vengono
/// trascritti con ogni modello installato e le misure si salvano in benchmark.json. Servono a stimare il tempo della coda
/// prima di avviarla e, con i brani ufficiali, a consigliare il modello per lingua. I brani si scaricano solo su richiesta
/// esplicita, dagli indirizzi fissati nel codice; un file scelto dall'utente non usa la rete.
/// </summary>
public sealed partial class BridgeDispatcher
{
    /// <summary>Durata del file di riferimento: abbastanza lungo da misurare, abbastanza corto da non durare ore con tutti i modelli.</summary>
    public const double BenchmarkMinSeconds = 10;

    public const double BenchmarkMaxSeconds = 600;

    private readonly Lock _benchmarkLock = new();
    private BenchmarkStore _benchmarkResults = null!;
    private BenchmarkRunner _benchmark = null!;
    private BenchmarkAudioPreparer _benchmarkAudio = null!;
    private CancellationTokenSource? _benchmarkCancel;

    private void InitBenchmark()
    {
        _benchmarkResults = new BenchmarkStore(Path.Combine(_paths.Root, "benchmark.json"));
        _benchmark = new BenchmarkRunner(_transcription, _models, _benchmarkResults);
        _benchmarkAudio = new BenchmarkAudioPreparer(_audio, Path.Combine(_paths.Root, "benchmark", "noise"));
        _benchmark.Progress += p => Emit("benchmark.progress", p);
    }

    private (string Fingerprint, string AppVersion) BenchmarkContext() =>
        (BenchmarkStore.Fingerprint(_hardware.Probe(_paths.Models).WithGpuAcceleration(_settings.Load().UseGpu)), AppVersion());

    private string? InstalledIdentity(string modelId) =>
        _models.Catalog.Find(modelId) is { } model && _models.Status(model).State is not (ModelState.NotInstalled or ModelState.Downloading)
            ? BenchmarkStore.ModelIdentity(model)
            : null;

    private object BenchmarkSnapshot()
    {
        var (fingerprint, version) = BenchmarkContext();
        var views = _benchmarkResults.Views(fingerprint, InstalledIdentity, version);
        return new
        {
            Running = _benchmark.Running,
            Entries = views.Select(v => new
            {
                v.Entry.ModelId,
                v.Entry.Runtime,
                v.Entry.Threads,
                v.Entry.AudioSeconds,
                v.Entry.LoadSeconds,
                v.Entry.Speed,
                v.Entry.MeasuredAt,
                v.Entry.Accuracy,
                v.Entry.ClipId,
                ClipTitle = v.Entry.ClipId is { } clipId ? BenchmarkClips.Find(clipId)?.Title : null,
                v.Entry.Language,
                v.Entry.Scenario,
                Stale = v.Stale,
            }).ToList(),
            // Affidabilità per modello e lingua (pulito, rumore, media dei due), solo dalle misure ancora valide.
            Reliability = views.Where(v => v.IsValid).GroupBy(v => v.Entry.ModelId)
                .SelectMany(g => BenchmarkReliability.ByLanguage(g.Select(v => v.Entry)).Select(r => new { ModelId = g.Key, r.Language, r.Clean, r.Noisy, r.Overall }))
                .ToList(),
        };
    }

    private async Task<object?> BenchmarkStartAsync(JsonElement? payload, CancellationToken ct)
    {
        EnsureNothingRunning();
        var request = payload is { ValueKind: JsonValueKind.Object } ? Read<BenchmarkStartRequest>(payload) : new BenchmarkStartRequest(false);
        var settings = _settings.Load();
        var jobs = new List<BenchmarkJob>();
        string label;
        if (request.UseReference == true)
        {
            // Il download parte solo da questa richiesta esplicita dell'utente (ADR-0006) e solo dagli indirizzi fissati nel codice.
            var provider = _referenceFile ?? throw new CodexVttException(ErrorCodes.NotAvailableOnServer, "File di riferimento non disponibile.");
            var files = new Dictionary<string, string>();
            foreach (var source in BenchmarkClips.Downloads())
                files[source.Id] = await provider.EnsureAsync(source, ct).ConfigureAwait(false);
            foreach (var clip in BenchmarkClips.All)
            {
                var handle = _benchmarkAudio.Prepare(clip, files[BenchmarkClips.SourceOf(clip).Id]);
                jobs.Add(new BenchmarkJob(clip.Id, clip.Language, clip.ScenarioKey, handle.AudioId, handle.Info.Duration.TotalSeconds, clip.ReferenceText));
            }

            label = BenchmarkReference.Title;
        }
        else
        {
            var path = await _shell.PickAudioFileAsync(ct).ConfigureAwait(false);
            if (path is null)
                return null;
            var handle = _audio.Register(path);
            var seconds = handle.Info.Duration.TotalSeconds;
            if (seconds is < BenchmarkMinSeconds or > BenchmarkMaxSeconds)
            {
                throw new CodexVttException(ErrorCodes.BenchmarkAudioInvalid, "File di riferimento di durata non adatta.",
                    new Dictionary<string, string> { ["min"] = ((int)BenchmarkMinSeconds).ToString(System.Globalization.CultureInfo.InvariantCulture), ["max"] = ((int)BenchmarkMaxSeconds).ToString(System.Globalization.CultureInfo.InvariantCulture) });
            }

            jobs.Add(new BenchmarkJob(null, settings.Language, null, handle.AudioId, seconds, null));
            label = handle.Info.FileName;
        }

        var (fingerprint, version) = BenchmarkContext();
        var threads = ThreadLimit();
        CancellationTokenSource cts;
        lock (_benchmarkLock)
        {
            EnsureNothingRunning();
            cts = _benchmarkCancel = new CancellationTokenSource();
        }

        _ = Task.Run(async () =>
        {
            try
            {
                var outcomes = await _benchmark.RunAsync(jobs, fingerprint, version, threads, cts.Token).ConfigureAwait(false);
                Emit("benchmark.completed", new { Outcomes = outcomes, Snapshot = BenchmarkSnapshot() });
            }
            catch (OperationCanceledException)
            {
                Emit("benchmark.cancelled", new { Snapshot = BenchmarkSnapshot() });
            }
            catch (CodexVttException ex)
            {
                Emit("benchmark.failed", new { Error = ToError(ex), Snapshot = BenchmarkSnapshot() });
            }
            finally
            {
                lock (_benchmarkLock)
                {
                    _benchmarkCancel?.Dispose();
                    _benchmarkCancel = null;
                }
            }
        }, CancellationToken.None);
        return new { Started = true, FileName = label, Duration = jobs.Sum(j => j.AudioSeconds) };
    }

    /// <summary>I file ufficiali (indirizzo, dimensione, hash), i brani che ne derivano e la licenza, da mostrare prima di scaricare.</summary>
    private object BenchmarkReferenceInfo()
    {
        var files = BenchmarkClips.Downloads().Select(c => (Clip: c, Cached: _referenceFile?.CachedPath(c) is not null)).ToList();
        return new
        {
            Available = _referenceFile is not null,
            BenchmarkReference.Host,
            BenchmarkReference.License,
            Files = files.Select(f => new { f.Clip.Id, f.Clip.Language, f.Clip.Title, f.Clip.Url, f.Clip.SizeBytes, f.Clip.Sha256, f.Cached }).ToList(),
            Clips = BenchmarkClips.All.Select(c => new { c.Id, c.Language, Scenario = c.ScenarioKey, c.Title, Seconds = c.ApproximateSeconds }).ToList(),
            TotalBytes = files.Sum(f => f.Clip.SizeBytes),
            MissingBytes = files.Where(f => !f.Cached).Sum(f => f.Clip.SizeBytes),
            Cached = files.All(f => f.Cached),
        };
    }

    private object BenchmarkCancel()
    {
        lock (_benchmarkLock)
            _benchmarkCancel?.Cancel();
        return new { Cancelling = true };
    }

    /// <summary>Stima del tempo per file di queste durate con il modello indicato, dalla misura valida; senza misura niente numeri inventati.</summary>
    private object BenchmarkEstimate(BenchmarkEstimateRequest request)
    {
        var (fingerprint, version) = BenchmarkContext();
        var entry = _benchmarkResults.Lookup(fingerprint, request.ModelId, InstalledIdentity, version);
        if (entry is null)
            return new { Basis = "none" };
        return new
        {
            Basis = "benchmark",
            Seconds = BenchmarkEstimator.EstimateSeconds(entry, request.Durations ?? []),
            entry.MeasuredAt,
            entry.Runtime,
        };
    }

    /// <summary>Il test e la trascrizione si escludono a vicenda: un solo lavoro alla volta, come per la coda.</summary>
    private void EnsureNothingRunning()
    {
        if (_benchmark.Running || _transcription.Current is not null || _queueRunning)
            throw new CodexVttException(ErrorCodes.TranscriptionBusy, "È già in corso una trascrizione.");
    }
}

public sealed record BenchmarkStartRequest(bool? UseReference);

public sealed record BenchmarkEstimateRequest(string ModelId, IReadOnlyList<double>? Durations);

/// <summary>Richiesta di consiglio: la lingua scelta in Home (it, en, auto); assente = quella delle impostazioni.</summary>
public sealed record RecommendRequest(string? Language);
