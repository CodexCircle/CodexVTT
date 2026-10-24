using System.Diagnostics;
using CodexVtt.Core.Errors;
using CodexVtt.Core.Evaluation;
using CodexVtt.Core.Models;
using CodexVtt.Core.Transcription;

namespace CodexVtt.Core.Benchmark;

/// <summary>
/// Esito (o avanzamento) di un passo del test: un modello su un brano. <c>Index</c> e <c>Total</c> contano i passi
/// (modelli per brani). <c>Phase</c>: running, done, failed. <c>ClipId</c> è null con un file scelto dall'utente.
/// </summary>
public sealed record BenchmarkProgress(string ModelId, int Index, int Total, string Phase, double? Speed = null, double? LoadSeconds = null, string? ErrorCode = null, double? Accuracy = null, string? ClipId = null);

/// <summary>
/// Un brano pronto per il test: l'audio già registrato, la lingua in cui va trascritto e, se c'è, il testo con cui confrontare.
/// <c>ClipId</c> e <c>Scenario</c> (clean, noisy) sono null con un file scelto dall'utente, che non ha un testo di riferimento.
/// </summary>
public sealed record BenchmarkJob(string? ClipId, string Language, string? Scenario, string AudioId, double AudioSeconds, string? ReferenceText);

/// <summary>
/// Test bench: trascrive ogni brano con ogni modello pronto e salva tempo fisso, velocità e, se c'è il testo, affidabilità.
/// Passa dal <see cref="TranscriptionService"/> (un job alla volta): non parte se c'è una trascrizione in corso, usa il limite
/// di CPU in vigore tramite <c>threads</c> e si annulla con il token. Nessuna rete: l'audio è già sul PC.
/// </summary>
public sealed class BenchmarkRunner(TranscriptionService transcription, ModelStore models, BenchmarkStore store)
{
    private int _running;

    /// <summary>Vero dal primo all'ultimo job del test: chi ascolta gli eventi di trascrizione deve ignorarli (niente cronologia).</summary>
    public bool Running => Volatile.Read(ref _running) == 1;

    public event Action<BenchmarkProgress>? Progress;

    /// <exception cref="CodexVttException"><see cref="ErrorCodes.TranscriptionBusy"/> se c'è già una trascrizione o un altro test.</exception>
    public async Task<IReadOnlyList<BenchmarkProgress>> RunAsync(
        IReadOnlyList<BenchmarkJob> jobs,
        string fingerprint,
        string appVersion,
        int? threads,
        CancellationToken ct)
    {
        if (transcription.Current is not null || Interlocked.CompareExchange(ref _running, 1, 0) != 0)
            throw new CodexVttException(ErrorCodes.TranscriptionBusy, "È già in corso una trascrizione.");

        var outcomes = new List<BenchmarkProgress>();
        try
        {
            var ready = models.List().Where(s => s.State is ModelState.Ready).Select(s => s.Model).ToList();
            if (ready.Count == 0 || jobs.Count == 0)
                return outcomes;

            // Un nuovo test sostituisce i precedenti: restano solo le misure di questa esecuzione.
            store.Clear();
            var total = ready.Count * jobs.Count;

            // Il primo modello paga da solo i costi una tantum del processo (runtime nativo, driver video, cache del disco):
            // senza una prova a vuoto il suo tempo fisso risultava più alto di quello dei modelli più grandi provati dopo.
            Publish(new BenchmarkProgress(ready[0].Id, 0, total, "running", ClipId: jobs[0].ClipId));
            var warmUp = await MeasureAsync(ready[0], jobs[0], 0, total, fingerprint, appVersion, threads, record: false, ct).ConfigureAwait(false);
            if (warmUp.ErrorCode == ErrorCodes.TranscriptionBusy)
                return outcomes;

            // Un modello alla volta su tutti i brani: se il test si ferma a metà i modelli già provati sono completi.
            var step = 0;
            foreach (var model in ready)
            {
                foreach (var job in jobs)
                {
                    ct.ThrowIfCancellationRequested();
                    if (step > 0)
                        Publish(new BenchmarkProgress(model.Id, step, total, "running", ClipId: job.ClipId));
                    var outcome = await MeasureAsync(model, job, step, total, fingerprint, appVersion, threads, record: true, ct).ConfigureAwait(false);
                    step++;
                    outcomes.Add(outcome);
                    Publish(outcome);
                    // Se nel frattempo è partita un'altra trascrizione il test si ferma: non deve rubarle il posto.
                    if (outcome.ErrorCode == ErrorCodes.TranscriptionBusy)
                        return outcomes;
                }
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
        BenchmarkJob job,
        int index,
        int total,
        string fingerprint,
        string appVersion,
        int? threads,
        bool record,
        CancellationToken ct)
    {
        var clock = Stopwatch.StartNew();
        double? firstWork = null;
        void OnProgress(JobProgress p)
        {
            if (firstWork is null && p.Processed > TimeSpan.Zero)
                firstWork = clock.Elapsed.TotalSeconds;
        }

        // I modelli che non rilevano la lingua la vogliono esplicita: per il test vale quella del brano (con un file proprio,
        // quella delle impostazioni), o italiano se è «auto».
        var effective = model.RequiresLanguage && job.Language == "auto" ? "it" : job.Language;
        transcription.Progress += OnProgress;
        try
        {
            var result = await transcription.RunToCompletionAsync(job.AudioId, model.Id, new TranscriptionOptions(effective, Translate: false, Threads: threads), ct).ConfigureAwait(false);
            clock.Stop();
            var measured = BenchmarkEstimator.Measure(job.AudioSeconds, clock.Elapsed.TotalSeconds, firstWork ?? 0);
            if (measured is null)
                return new BenchmarkProgress(model.Id, index, total, "failed", ErrorCode: ErrorCodes.TranscriptionFailed, ClipId: job.ClipId);
            var (load, speed) = measured.Value;
            if (!record)
                return new BenchmarkProgress(model.Id, index, total, "running", ClipId: job.ClipId);
            // Affidabilità: solo se c'è un testo con cui confrontare (brani ufficiali), come 100 meno il WER in percentuale.
            double? accuracy = job.ReferenceText is null ? null : Math.Round(Math.Clamp((1 - WordErrorRate.Compute(job.ReferenceText, result.Text)) * 100, 0, 100), 1);
            store.Record(fingerprint, new BenchmarkEntry(model.Id, BenchmarkStore.ModelIdentity(model), result.Model.Runtime, threads, job.AudioSeconds, load, speed,
                DateTimeOffset.UtcNow, appVersion, accuracy, job.ClipId, job.ClipId is null ? null : job.Language, job.Scenario));
            return new BenchmarkProgress(model.Id, index, total, "done", speed, load, Accuracy: accuracy, ClipId: job.ClipId);
        }
        catch (CodexVttException ex)
        {
            return new BenchmarkProgress(model.Id, index, total, "failed", ErrorCode: ex.Code, ClipId: job.ClipId);
        }
        finally
        {
            transcription.Progress -= OnProgress;
        }
    }

    private void Publish(BenchmarkProgress progress) => Progress?.Invoke(progress);
}
