using System.Diagnostics;
using CodexVtt.Core.Errors;
using CodexVtt.Core.Models;
using CodexVtt.Core.Transcription;

namespace CodexVtt.Core.Benchmark;

/// <summary>Esito (o avanzamento) del test su un modello. <c>Phase</c>: running, done, failed.</summary>
public sealed record BenchmarkProgress(string ModelId, int Index, int Total, string Phase, double? Speed = null, double? LoadSeconds = null, string? ErrorCode = null);

/// <summary>
/// Test bench: trascrive lo stesso file di riferimento con ogni modello pronto e salva tempo fisso e velocità.
/// Passa dal <see cref="TranscriptionService"/> (un job alla volta): non parte se c'è una trascrizione in corso, usa il limite
/// di CPU in vigore tramite <c>threads</c> e si annulla con il token. Nessuna rete: il file di riferimento è un audio già sul PC.
/// </summary>
public sealed class BenchmarkRunner(TranscriptionService transcription, ModelStore models, BenchmarkStore store)
{
    private int _running;

    /// <summary>Vero dal primo all'ultimo job del test: chi ascolta gli eventi di trascrizione deve ignorarli (niente cronologia).</summary>
    public bool Running => Volatile.Read(ref _running) == 1;

    public event Action<BenchmarkProgress>? Progress;

    /// <exception cref="CodexVttException"><see cref="ErrorCodes.TranscriptionBusy"/> se c'è già una trascrizione o un altro test.</exception>
    public async Task<IReadOnlyList<BenchmarkProgress>> RunAsync(
        string audioId,
        double audioSeconds,
        string fingerprint,
        string appVersion,
        string language,
        int? threads,
        CancellationToken ct)
    {
        if (transcription.Current is not null || Interlocked.CompareExchange(ref _running, 1, 0) != 0)
            throw new CodexVttException(ErrorCodes.TranscriptionBusy, "È già in corso una trascrizione.");

        var outcomes = new List<BenchmarkProgress>();
        try
        {
            var ready = models.List().Where(s => s.State is ModelState.Ready).Select(s => s.Model).ToList();
            // Il primo modello paga da solo i costi una tantum del processo (runtime nativo, driver video, cache del disco):
            // senza una prova a vuoto il suo tempo fisso risultava più alto di quello dei modelli più grandi provati dopo.
            if (ready.Count > 0)
            {
                Publish(new BenchmarkProgress(ready[0].Id, 0, ready.Count, "running"));
                var warmUp = await MeasureAsync(ready[0], 0, ready.Count, audioId, audioSeconds, fingerprint, appVersion, language, threads, ct, record: false).ConfigureAwait(false);
                if (warmUp.ErrorCode == ErrorCodes.TranscriptionBusy)
                    return outcomes;
            }

            for (var i = 0; i < ready.Count; i++)
            {
                ct.ThrowIfCancellationRequested();
                var model = ready[i];
                if (i > 0)
                    Publish(new BenchmarkProgress(model.Id, i, ready.Count, "running"));
                var outcome = await MeasureAsync(model, i, ready.Count, audioId, audioSeconds, fingerprint, appVersion, language, threads, ct).ConfigureAwait(false);
                outcomes.Add(outcome);
                Publish(outcome);
                // Se nel frattempo è partita un'altra trascrizione il test si ferma: non deve rubarle il posto.
                if (outcome.ErrorCode == ErrorCodes.TranscriptionBusy)
                    break;
            }

            return outcomes;
        }
        finally
        {
            Volatile.Write(ref _running, 0);
        }
    }

    private async Task<BenchmarkProgress> MeasureAsync(
        ModelDescriptor model,
        int index,
        int total,
        string audioId,
        double audioSeconds,
        string fingerprint,
        string appVersion,
        string language,
        int? threads,
        CancellationToken ct,
        bool record = true)
    {
        var clock = Stopwatch.StartNew();
        double? firstWork = null;
        void OnProgress(JobProgress p)
        {
            if (firstWork is null && p.Processed > TimeSpan.Zero)
                firstWork = clock.Elapsed.TotalSeconds;
        }

        // I modelli che non rilevano la lingua la vogliono esplicita: per il test vale quella delle impostazioni, o italiano.
        var effective = model.RequiresLanguage && language == "auto" ? "it" : language;
        transcription.Progress += OnProgress;
        try
        {
            var result = await transcription.RunToCompletionAsync(audioId, model.Id, new TranscriptionOptions(effective, Translate: false, Threads: threads), ct).ConfigureAwait(false);
            clock.Stop();
            var measured = BenchmarkEstimator.Measure(audioSeconds, clock.Elapsed.TotalSeconds, firstWork ?? 0);
            if (measured is null)
                return new BenchmarkProgress(model.Id, index, total, "failed", ErrorCode: ErrorCodes.TranscriptionFailed);
            var (load, speed) = measured.Value;
            if (!record)
                return new BenchmarkProgress(model.Id, index, total, "running");
            store.Record(fingerprint, new BenchmarkEntry(model.Id, BenchmarkStore.ModelIdentity(model), result.Model.Runtime, threads, audioSeconds, load, speed, DateTimeOffset.UtcNow, appVersion));
            return new BenchmarkProgress(model.Id, index, total, "done", speed, load);
        }
        catch (CodexVttException ex)
        {
            return new BenchmarkProgress(model.Id, index, total, "failed", ErrorCode: ex.Code);
        }
        finally
        {
            transcription.Progress -= OnProgress;
        }
    }

    private void Publish(BenchmarkProgress progress) => Progress?.Invoke(progress);
}
