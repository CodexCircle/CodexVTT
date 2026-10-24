using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging.Abstractions;
using CodexVtt.Core.Audio;
using CodexVtt.Core.Benchmark;
using CodexVtt.Core.Errors;
using CodexVtt.Core.Hardware;
using CodexVtt.Core.Models;
using CodexVtt.Core.Transcription;

namespace CodexVtt.Core.Tests;

public class BenchmarkEstimatorTests
{
    private static BenchmarkEntry Entry(double load, double speed) =>
        new("base", "id", "Cpu", null, 60, load, speed, DateTimeOffset.UnixEpoch, "2.0.0");

    [Fact]
    public void Estimate_is_the_fixed_time_plus_duration_over_speed()
    {
        // Caricamento 5 s, 4× il tempo reale: 60 s + 120 s di audio = 180 s / 4 = 45 s, più il fisso.
        Assert.Equal(50, BenchmarkEstimator.EstimateSeconds(Entry(5, 4), [60, 120]), 6);
    }

    [Fact]
    public void The_fixed_time_is_paid_once_for_the_whole_queue()
    {
        var one = BenchmarkEstimator.EstimateSeconds(Entry(10, 2), [100]);
        var three = BenchmarkEstimator.EstimateSeconds(Entry(10, 2), [100, 100, 100]);

        Assert.Equal(60, one, 6);
        Assert.Equal(160, three, 6);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(double.NaN)]
    public void Unusable_speed_or_durations_give_no_estimate(double value)
    {
        Assert.Equal(0, BenchmarkEstimator.EstimateSeconds(Entry(5, value), [60]));
        Assert.Equal(0, BenchmarkEstimator.EstimateSeconds(Entry(5, 4), [value]));
        Assert.Equal(0, BenchmarkEstimator.EstimateSeconds(Entry(5, 4), []));
    }

    [Fact]
    public void Measure_separates_loading_from_the_work_on_the_audio()
    {
        // 90 s di audio in 30 s totali, di cui 10 s per caricare il modello: 90 / 20 = 4,5× dopo il caricamento.
        var (load, speed) = BenchmarkEstimator.Measure(90, 30, 10)!.Value;

        Assert.Equal(10, load, 6);
        Assert.Equal(4.5, speed, 6);
    }

    [Fact]
    public void Measure_falls_back_to_the_total_time_when_the_work_is_too_short_to_measure()
    {
        var (load, speed) = BenchmarkEstimator.Measure(10, 2, 2)!.Value;

        Assert.Equal(0, load);
        Assert.Equal(5, speed, 6);
        Assert.Null(BenchmarkEstimator.Measure(0, 2, 1));
        Assert.Null(BenchmarkEstimator.Measure(10, 0, 0));
    }
}

public sealed class BenchmarkStoreTests : IDisposable
{
    private readonly TempDir _dir = new();
    private readonly BenchmarkStore _store;

    public BenchmarkStoreTests() => _store = new BenchmarkStore(Path.Combine(_dir.Path, "benchmark.json"));

    public void Dispose() => _dir.Dispose();

    private static BenchmarkEntry Entry(string model = "base", string runtime = "Cpu", string identity = "sha-base", double speed = 4, string version = "2.0.0") =>
        new(model, identity, runtime, 8, 60, 3, speed, DateTimeOffset.UtcNow, version);

    private static string? Identity(string id) => id switch { "base" => "sha-base", "small" => "sha-small", _ => null };

    [Fact]
    public void Measures_survive_a_restart_and_are_valid_for_the_same_hardware_model_and_version()
    {
        _store.Record("pc-1", Entry());

        var reloaded = new BenchmarkStore(Path.Combine(_dir.Path, "benchmark.json"));

        var found = reloaded.Lookup("pc-1", "base", Identity, "2.0.0");
        Assert.NotNull(found);
        Assert.Equal(4, found.Speed);
    }

    [Fact]
    public void A_new_measure_replaces_the_old_one_for_the_same_model_and_runtime()
    {
        _store.Record("pc-1", Entry(speed: 4));
        _store.Record("pc-1", Entry(speed: 6));
        _store.Record("pc-1", Entry(runtime: "Vulkan (GPU)", speed: 20));

        var views = _store.Views("pc-1", Identity, "2.0.0");

        Assert.Equal(2, views.Count);
        Assert.Equal(6, views.Single(v => v.Entry.Runtime == "Cpu").Entry.Speed);
    }

    [Fact]
    public void Different_hardware_marks_everything_stale_and_a_new_measure_drops_the_old_ones()
    {
        _store.Record("pc-1", Entry("base"));

        Assert.All(_store.Views("pc-2", Identity, "2.0.0"), v => Assert.Equal(BenchmarkStaleReason.HardwareChanged, v.Stale));
        Assert.Null(_store.Lookup("pc-2", "base", Identity, "2.0.0"));

        _store.Record("pc-2", Entry("small", identity: "sha-small"));

        var views = _store.Views("pc-2", Identity, "2.0.0");
        Assert.Equal("small", Assert.Single(views).Entry.ModelId);
        Assert.True(views[0].IsValid);
    }

    [Fact]
    public void A_changed_model_file_or_a_new_app_version_invalidates_the_measure()
    {
        _store.Record("pc-1", Entry());

        Assert.Equal(BenchmarkStaleReason.ModelChanged, _store.Views("pc-1", _ => "altro-hash", "2.0.0").Single().Stale);
        Assert.Equal(BenchmarkStaleReason.ModelChanged, _store.Views("pc-1", _ => null, "2.0.0").Single().Stale);
        Assert.Equal(BenchmarkStaleReason.RuntimeChanged, _store.Views("pc-1", Identity, "2.1.0").Single().Stale);
        Assert.Null(_store.Lookup("pc-1", "base", Identity, "2.1.0"));
    }

    [Fact]
    public void A_corrupted_file_is_treated_as_empty()
    {
        File.WriteAllText(Path.Combine(_dir.Path, "benchmark.json"), "{ non è json");

        Assert.Empty(_store.Read().Entries);
        _store.Record("pc-1", Entry());
        Assert.Single(_store.Read().Entries);
    }

    [Fact]
    public void The_fingerprint_changes_with_cores_gpu_and_gpu_acceleration()
    {
        var pc = TestData.Hardware(cores: 8);
        var gpu = pc with { GpuDetails = [new GpuDevice("RTX 3060", "dedicata", 1, 12L << 30)] };

        Assert.Equal(BenchmarkStore.Fingerprint(pc), BenchmarkStore.Fingerprint(TestData.Hardware(cores: 8)));
        Assert.NotEqual(BenchmarkStore.Fingerprint(pc), BenchmarkStore.Fingerprint(TestData.Hardware(cores: 16)));
        Assert.NotEqual(BenchmarkStore.Fingerprint(pc), BenchmarkStore.Fingerprint(gpu));
        Assert.NotEqual(BenchmarkStore.Fingerprint(gpu), BenchmarkStore.Fingerprint(gpu.WithGpuAcceleration(true)));
    }
}

public sealed class BenchmarkRunnerTests : IDisposable
{
    private static readonly byte[] BaseContent = TestData.Bytes("base", 100);
    private static readonly byte[] SmallContent = TestData.Bytes("small", 100);
    private readonly TempDir _dir = new();
    private readonly AudioLibrary _audio = new([new WavDecoder()], TimeSpan.FromHours(3));
    private readonly SlowEngine _engine = new();
    private readonly ModelStore _models;
    private readonly BenchmarkStore _results;
    private readonly TranscriptionService _service;
    private readonly BenchmarkRunner _runner;

    public BenchmarkRunnerTests()
    {
        _models = new ModelStore(_dir.AppPaths(), TestData.Catalog(models: [("base", BaseContent, 2, 2, 0), ("small", SmallContent, 6, 3, 0)]));
        _results = new BenchmarkStore(Path.Combine(_dir.Path, "benchmark.json"));
        _service = new TranscriptionService(_models, _audio, _engine, NullLogger<TranscriptionService>.Instance,
            new AudioChunker(TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(0.5)));
        _runner = new BenchmarkRunner(_service, _models, _results);
    }

    public void Dispose()
    {
        _service.Dispose();
        _dir.Dispose();
    }

    private async Task InstallAsync()
    {
        await _models.ImportAsync(_dir.File("base.bin", BaseContent));
        await _models.ImportAsync(_dir.File("small.bin", SmallContent));
    }

    private static BenchmarkJob Job(string audioId, double seconds, string? text = null, string? clipId = null, string language = "it", string? scenario = null) =>
        new(clipId, language, scenario, audioId, seconds, text);

    private string Audio(double seconds) => _audio.Register(_dir.File($"ref{seconds}.wav", TestData.Wav(16000, 1, seconds))).AudioId;

    [Fact]
    public async Task Measures_every_installed_model_and_saves_the_result_for_this_hardware()
    {
        await InstallAsync();
        var events = new List<BenchmarkProgress>();
        _runner.Progress += events.Add;

        var outcomes = await _runner.RunAsync([Job(Audio(4), 4)], "pc-1", "2.0.0", 4, CancellationToken.None);

        Assert.Equal(["base", "small"], outcomes.Select(o => o.ModelId));
        Assert.All(outcomes, o => Assert.Equal("done", o.Phase));
        Assert.All(outcomes, o => Assert.True(o.Speed > 0));
        var saved = _results.Views("pc-1", id => BenchmarkStore.ModelIdentity(_models.Catalog.Find(id)!), "2.0.0");
        Assert.Equal(2, saved.Count);
        Assert.All(saved, v => Assert.True(v.IsValid));
        Assert.All(saved, v => Assert.Equal(4, v.Entry.Threads)); // il limite di CPU in vigore viaggia con la misura
        Assert.Equal("Slow", saved[0].Entry.Runtime);
        Assert.Equal(["running", "done", "running", "done"], events.Select(e => e.Phase));
        Assert.False(_runner.Running);
    }

    [Fact]
    public async Task A_new_test_discards_the_measurements_of_the_previous_ones()
    {
        await InstallAsync();
        _results.Record("pc-1", new BenchmarkEntry("medium", "sha-medium", "Cpu", 4, 4, 1, 2, DateTimeOffset.UtcNow, "2.0.0"));

        await _runner.RunAsync([Job(Audio(4), 4)], "pc-1", "2.0.0", 4, CancellationToken.None);

        Assert.Equal(["base", "small"], _results.Read().Entries.Select(e => e.ModelId).Order());
    }

    [Fact]
    public async Task Does_not_start_while_a_transcription_is_running_and_a_transcription_cannot_start_during_the_test()
    {
        await InstallAsync();
        _engine.Delay = TimeSpan.FromMilliseconds(300);
        var running = _service.Start(Audio(2), "base", new TranscriptionOptions("it"));

        var error = await Assert.ThrowsAsync<CodexVttException>(() => _runner.RunAsync([Job(Audio(4), 4)], "pc-1", "2.0.0", null, CancellationToken.None));
        Assert.Equal(ErrorCodes.TranscriptionBusy, error.Code);
        Assert.False(_runner.Running);
        _service.Cancel(running.JobId);
        while (_service.Current is not null)
            await Task.Delay(10);

        _engine.Delay = TimeSpan.FromMilliseconds(150);
        var test = _runner.RunAsync([Job(Audio(4), 4)], "pc-1", "2.0.0", null, CancellationToken.None);
        await Task.Delay(40);
        Assert.True(_runner.Running);
        var other = Assert.Throws<CodexVttException>(() => _service.Start(Audio(2), "base", new TranscriptionOptions("it")));
        Assert.Equal(ErrorCodes.TranscriptionBusy, other.Code);
        await test;
    }

    [Fact]
    public async Task Can_be_cancelled_and_keeps_what_was_already_measured()
    {
        await InstallAsync();
        using var cts = new CancellationTokenSource();
        _runner.Progress += p =>
        {
            if (p.Phase == "done" && p.ModelId == "base") cts.Cancel();
        };

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => _runner.RunAsync([Job(Audio(4), 4)], "pc-1", "2.0.0", null, cts.Token));

        Assert.False(_runner.Running);
        Assert.Equal(["base"], _results.Read().Entries.Select(e => e.ModelId));
    }

    [Fact]
    public async Task A_model_that_fails_is_reported_and_does_not_stop_the_others()
    {
        await InstallAsync();
        _engine.FailFor = "base";

        var outcomes = await _runner.RunAsync([Job(Audio(4), 4)], "pc-1", "2.0.0", null, CancellationToken.None);

        Assert.Equal("failed", outcomes[0].Phase);
        Assert.Equal(ErrorCodes.TranscriptionFailed, outcomes[0].ErrorCode);
        Assert.Equal("done", outcomes[1].Phase);
        Assert.Equal(["small"], _results.Read().Entries.Select(e => e.ModelId));
    }

    [Fact]
    public async Task Measures_every_clip_with_every_model_and_records_the_language_and_scenario()
    {
        await InstallAsync();
        var events = new List<BenchmarkProgress>();
        _runner.Progress += events.Add;
        // Il motore di prova dice sempre «testo»: contro «testo» è perfetto, contro «altro» sbaglia l'unica parola.
        BenchmarkJob[] jobs = [Job(Audio(1), 1, "testo", "it-clean-1", "it", "clean"), Job(Audio(1), 1, "altro", "en-noisy-1", "en", "noisy")];

        var outcomes = await _runner.RunAsync(jobs, "pc-1", "2.0.0", null, CancellationToken.None);

        Assert.Equal([("base", "it-clean-1"), ("base", "en-noisy-1"), ("small", "it-clean-1"), ("small", "en-noisy-1")], outcomes.Select(o => (o.ModelId, o.ClipId)));
        Assert.All(events, e => Assert.Equal(4, e.Total));
        Assert.Equal([100.0, 0.0, 100.0, 0.0], outcomes.Select(o => o.Accuracy));
        var saved = _results.Read().Entries;
        Assert.Equal(4, saved.Count);
        var english = saved.Single(e => e.ModelId == "base" && e.ClipId == "en-noisy-1");
        Assert.Equal("en", english.Language);
        Assert.Equal("noisy", english.Scenario);
        Assert.Equal(0, english.Accuracy);
    }

    [Fact]
    public async Task A_file_chosen_by_the_user_is_saved_without_clip_language_or_scenario()
    {
        await _models.ImportAsync(_dir.File("base.bin", BaseContent));

        await _runner.RunAsync([Job(Audio(1), 1, null, null, "it")], "pc-1", "2.0.0", null, CancellationToken.None);

        var entry = Assert.Single(_results.Read().Entries);
        Assert.Null(entry.ClipId);
        Assert.Null(entry.Language);
        Assert.Null(entry.Scenario);
    }

    [Fact]
    public async Task Models_not_installed_are_skipped()
    {
        await _models.ImportAsync(_dir.File("base.bin", BaseContent));

        var outcomes = await _runner.RunAsync([Job(Audio(2), 2)], "pc-1", "2.0.0", null, CancellationToken.None);

        Assert.Equal("base", Assert.Single(outcomes).ModelId);
    }

    [Fact]
    public async Task Accuracy_is_measured_against_the_reference_text_only_when_there_is_one()
    {
        await _models.ImportAsync(_dir.File("base.bin", BaseContent));

        // Un secondo di audio sta in una sola finestra: il motore di prova dice «testo» una volta sola (con più finestre lo ripeterebbe).
        // Il motore di prova dice sempre «testo»: contro «Testo.» è identico, contro «testo altro» manca una parola su due.
        var exact = await _runner.RunAsync([Job(Audio(1), 1, "Testo.")], "pc-1", "2.0.0", null, CancellationToken.None);
        Assert.Equal(100, Assert.Single(exact).Accuracy);
        Assert.Equal(100, Assert.Single(_results.Read().Entries).Accuracy);

        var half = await _runner.RunAsync([Job(Audio(1), 1, "testo altro")], "pc-1", "2.0.0", null, CancellationToken.None);
        Assert.Equal(50, Assert.Single(half).Accuracy);

        var none = await _runner.RunAsync([Job(Audio(1), 1)], "pc-1", "2.0.0", null, CancellationToken.None);
        Assert.Null(Assert.Single(none).Accuracy);
    }

    [Fact]
    public async Task The_first_model_is_warmed_up_once_and_only_the_measured_run_is_saved()
    {
        await _models.ImportAsync(_dir.File("base.bin", BaseContent));

        var outcomes = await _runner.RunAsync([Job(Audio(2), 2)], "pc-1", "2.0.0", null, CancellationToken.None);

        Assert.Single(outcomes);
        Assert.Equal(2, _engine.Sessions); // prova a vuoto + misura
        Assert.Single(_results.Read().Entries);
    }

    private sealed class SlowEngine : ITranscriptionEngine
    {
        public int Sessions { get; private set; }

        public TimeSpan Delay { get; set; } = TimeSpan.FromMilliseconds(20);

        public string? FailFor { get; set; }

        public Task<ITranscriptionSession> LoadAsync(ModelDescriptor model, string modelPath, CancellationToken ct)
        {
            Sessions++;
            return Task.FromResult<ITranscriptionSession>(new Session(this, model.Id));
        }

        private sealed class Session(SlowEngine owner, string modelId) : ITranscriptionSession
        {
            public string Runtime => "Slow";

            public string? DetectedLanguage => "it";

            public async IAsyncEnumerable<TranscriptSegment> TranscribeAsync(
                ReadOnlyMemory<float> samples,
                TranscriptionOptions options,
                IProgress<int>? progress,
                [EnumeratorCancellation] CancellationToken ct)
            {
                if (owner.FailFor == modelId)
                    throw new CodexVttException(ErrorCodes.TranscriptionFailed, "errore di prova");
                await Task.Delay(owner.Delay, ct);
                progress?.Report(100);
                yield return new TranscriptSegment(TimeSpan.Zero, TimeSpan.FromSeconds(samples.Length / 16000.0), "testo", 0.9f);
            }

            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }
}

public class BenchmarkRecommenderTests
{
    private static (BenchmarkEntry Entry, ModelDescriptor Model) Measured(string id, double speed, double? accuracy, int quality = 1, bool preview = false)
    {
        var model = TestData.Catalog(models: [(id, TestData.Bytes(id, 100), 2, 2, 0)]).Models[0] with { QualityRank = quality, Preview = preview };
        return (new BenchmarkEntry(id, "x", "Cpu", null, 60, 2, speed, DateTimeOffset.UtcNow, "2.0.0", accuracy, accuracy is null ? null : "it-clean-1", accuracy is null ? null : "it", accuracy is null ? null : "clean"), model);
    }

    [Fact]
    public void Picks_the_most_reliable_model_that_keeps_up_with_real_time()
    {
        var picked = BenchmarkRecommender.Pick([Measured("tiny", 40, 80), Measured("base", 20, 90), Measured("large", 0.5, 97)]);

        Assert.Equal("base", picked!.Model.Id);
        Assert.True(picked.ByAccuracy);
    }

    [Fact]
    public void Within_half_a_point_of_reliability_the_faster_model_wins()
    {
        var picked = BenchmarkRecommender.Pick([Measured("base", 20, 93.8), Measured("small", 8, 94.0)]);

        Assert.Equal("base", picked!.Model.Id);
    }

    [Fact]
    public void Without_reliability_the_higher_quality_usable_model_wins_and_previews_are_never_picked()
    {
        var picked = BenchmarkRecommender.Pick([Measured("tiny", 40, null, quality: 1), Measured("small", 5, null, quality: 3), Measured("parakeet", 30, null, quality: 9, preview: true)]);

        Assert.Equal("small", picked!.Model.Id);
        Assert.False(picked.ByAccuracy);
    }

    [Fact]
    public void When_nothing_keeps_up_with_real_time_the_fastest_is_picked_and_with_no_measures_nothing()
    {
        Assert.Equal("tiny", BenchmarkRecommender.Pick([Measured("tiny", 0.8, 70), Measured("base", 0.4, 90)])!.Model.Id);
        Assert.Null(BenchmarkRecommender.Pick([]));
    }
}
