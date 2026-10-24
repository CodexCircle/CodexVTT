using System.Text.Json;
using CodexVtt.Core.Benchmark;
using CodexVtt.Core.Errors;
using CodexVtt.Core.Models;
using CodexVtt.Core.Transcription;

namespace CodexVtt.Bridge;

/// <summary>
/// Test bench dei modelli: un file di riferimento scelto dall'utente viene trascritto con ogni modello installato e la
/// velocità misurata si salva in benchmark.json. Le misure servono a stimare il tempo della coda prima di avviarla.
/// Nessun accesso alla rete: il file è già sul PC e parte solo da un'azione esplicita.
/// </summary>
public sealed partial class BridgeDispatcher
{
    /// <summary>Durata del file di riferimento: abbastanza lungo da misurare, abbastanza corto da non durare ore con tutti i modelli.</summary>
    public const double BenchmarkMinSeconds = 10;

    public const double BenchmarkMaxSeconds = 600;

    private readonly Lock _benchmarkLock = new();
    private BenchmarkStore _benchmarkResults = null!;
    private BenchmarkRunner _benchmark = null!;
    private CancellationTokenSource? _benchmarkCancel;

    private void InitBenchmark()
    {
        _benchmarkResults = new BenchmarkStore(Path.Combine(_paths.Root, "benchmark.json"));
        _benchmark = new BenchmarkRunner(_transcription, _models, _benchmarkResults);
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
        return new
        {
            Running = _benchmark.Running,
            Entries = _benchmarkResults.Views(fingerprint, InstalledIdentity, version).Select(v => new
            {
                v.Entry.ModelId,
                v.Entry.Runtime,
                v.Entry.Threads,
                v.Entry.AudioSeconds,
                v.Entry.LoadSeconds,
                v.Entry.Speed,
                v.Entry.MeasuredAt,
                Stale = v.Stale,
            }).ToList(),
        };
    }

    private async Task<object?> BenchmarkStartAsync(JsonElement? payload, CancellationToken ct)
    {
        EnsureNothingRunning();
        var request = payload is { ValueKind: JsonValueKind.Object } ? Read<BenchmarkStartRequest>(payload) : new BenchmarkStartRequest(false);
        string? path;
        if (request.UseReference == true)
        {
            // Il download parte solo da questa richiesta esplicita dell'utente (ADR-0006) e solo dall'indirizzo fissato nel codice.
            var provider = _referenceFile ?? throw new CodexVttException(ErrorCodes.NotAvailableOnServer, "File di riferimento non disponibile.");
            path = await provider.EnsureAsync(ct).ConfigureAwait(false);
        }
        else
        {
            path = await _shell.PickAudioFileAsync(ct).ConfigureAwait(false);
        }

        if (path is null)
            return null;
        var handle = _audio.Register(path);
        var seconds = handle.Info.Duration.TotalSeconds;
        if (seconds is < BenchmarkMinSeconds or > BenchmarkMaxSeconds)
        {
            throw new CodexVttException(ErrorCodes.BenchmarkAudioInvalid, "File di riferimento di durata non adatta.",
                new Dictionary<string, string> { ["min"] = ((int)BenchmarkMinSeconds).ToString(System.Globalization.CultureInfo.InvariantCulture), ["max"] = ((int)BenchmarkMaxSeconds).ToString(System.Globalization.CultureInfo.InvariantCulture) });
        }

        var (fingerprint, version) = BenchmarkContext();
        var settings = _settings.Load();
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
                var outcomes = await _benchmark.RunAsync(handle.AudioId, seconds, fingerprint, version, settings.Language, threads, cts.Token).ConfigureAwait(false);
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
        return new { Started = true, handle.Info.FileName, Duration = seconds };
    }

    /// <summary>Indirizzo, dimensione, hash e licenza del file ufficiale, da mostrare prima di scaricarlo.</summary>
    private object BenchmarkReferenceInfo() => new
    {
        Available = _referenceFile is not null,
        BenchmarkReference.Url,
        BenchmarkReference.Title,
        BenchmarkReference.SizeBytes,
        BenchmarkReference.Sha256,
        BenchmarkReference.License,
        Cached = _referenceFile?.CachedPath() is not null,
    };

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
