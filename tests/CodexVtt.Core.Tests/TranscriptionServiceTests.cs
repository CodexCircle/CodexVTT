using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using CodexVtt.Core.Audio;
using CodexVtt.Core.Errors;
using CodexVtt.Core.Logging;
using CodexVtt.Core.Models;
using CodexVtt.Core.Transcription;

namespace CodexVtt.Core.Tests;

public sealed class TranscriptionServiceTests : IDisposable
{
    private static readonly byte[] ModelContent = TestData.Bytes("model", 100);
    private readonly TempDir _dir = new();
    private readonly ModelStore _store;
    private readonly AudioLibrary _audio = new([new WavDecoder()], TimeSpan.FromHours(3));
    private readonly FakeEngine _engine = new();
    private readonly TranscriptionService _service;

    public TranscriptionServiceTests()
    {
        _store = new ModelStore(_dir.AppPaths(), TestData.Catalog(models: ("base", ModelContent, 2, 2, 0)));
        _service = new TranscriptionService(_store, _audio, _engine, NullLogger<TranscriptionService>.Instance,
            new AudioChunker(TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(0.5)));
    }

    public void Dispose()
    {
        _service.Dispose();
        _dir.Dispose();
    }

    private async Task InstallModelAsync() => await _store.ImportAsync(_dir.File("base.bin", ModelContent));

    private string RegisterAudio(double seconds) => _audio.Register(_dir.File($"a{seconds}.wav", TestData.Wav(16000, 1, seconds))).AudioId;

    [Fact]
    public async Task Transcribes_in_chunks_with_absolute_timestamps_and_model_usage()
    {
        await InstallModelAsync();
        var audioId = RegisterAudio(5);
        var progress = new List<JobProgress>();
        _service.Progress += progress.Add;

        var result = await _service.RunToCompletionAsync(audioId, "base", new TranscriptionOptions("it"), CancellationToken.None);

        Assert.Equal(3, _engine.ChunkCount);
        Assert.Equal(3, result.Segments.Count);
        Assert.Equal(TimeSpan.Zero, result.Segments[0].Start);
        Assert.True(result.Segments[1].Start > TimeSpan.FromSeconds(1.4));
        Assert.True(result.Segments[2].End <= TimeSpan.FromSeconds(5.01));
        Assert.Equal("chunk-0 chunk-1 chunk-2", result.Text);
        Assert.Equal("base", result.Model.ModelId);
        Assert.Equal(TestData.Sha256(ModelContent), result.Model.Sha256);
        Assert.Equal("Fake", result.Model.Runtime);
        Assert.Equal("it", result.DetectedLanguage);
        Assert.Equal(JobStatus.Completed, progress[^1].Status);
        Assert.Equal(100, progress[^1].Percent);
        Assert.True(progress.Select(p => p.Percent).SequenceEqual(progress.Select(p => p.Percent).Order()));
        Assert.True(_engine.Disposed);
    }

    [Fact]
    public async Task A_model_that_needs_the_language_refuses_automatic_detection_before_decoding()
    {
        var catalog = TestData.Catalog(models: ("base", ModelContent, 2, 2, 0));
        var needy = new ModelCatalog(catalog.Source, catalog.Selection, [catalog.Models[0] with { RequiresLanguage = true }]);
        var store = new ModelStore(_dir.AppPaths(), needy);
        await store.ImportAsync(_dir.File("needy.bin", ModelContent));
        using var service = new TranscriptionService(store, _audio, _engine, NullLogger<TranscriptionService>.Instance);
        var audioId = RegisterAudio(1);

        var error = await Assert.ThrowsAsync<CodexVttException>(() =>
            service.RunToCompletionAsync(audioId, "base", new TranscriptionOptions("auto"), CancellationToken.None));

        Assert.Equal(ErrorCodes.LanguageRequired, error.Code);
        Assert.Equal(0, _engine.ChunkCount);
        var result = await service.RunToCompletionAsync(audioId, "base", new TranscriptionOptions("it"), CancellationToken.None);
        Assert.NotEmpty(result.Segments);
    }

    [Fact]
    public async Task Whisper_markers_are_not_kept_as_speech()
    {
        await InstallModelAsync();
        _engine.TextOverride = "[BLANK_AUDIO]";
        var audioId = RegisterAudio(1);

        var result = await _service.RunToCompletionAsync(audioId, "base", new TranscriptionOptions("it"), CancellationToken.None);

        Assert.Empty(result.Segments);
        Assert.Equal("", result.Text);
    }

    [Fact]
    public async Task Audio_with_far_fewer_samples_than_declared_is_reported_as_corrupted()
    {
        await InstallModelAsync();
        var audio = new AudioLibrary([new TruncatedDecoder()], TimeSpan.FromHours(1));
        using var service = new TranscriptionService(_store, audio, _engine, NullLogger<TranscriptionService>.Instance);
        var audioId = audio.Register(_dir.File("troncato.mp3", [1, 2, 3])).AudioId;

        var ex = await Assert.ThrowsAsync<CodexVttException>(() =>
            service.RunToCompletionAsync(audioId, "base", new TranscriptionOptions(), CancellationToken.None));

        Assert.Equal(ErrorCodes.AudioCorrupted, ex.Code);
    }

    [Theory]
    [InlineData("[BLANK_AUDIO]", true)]
    [InlineData(" [MUSIC] ", true)]
    [InlineData("[Music]", true)]
    [InlineData("ciao a tutti", false)]
    [InlineData("[ciao] disse", false)]
    [InlineData("", false)]
    public void Special_tokens_are_recognised(string text, bool expected) => Assert.Equal(expected, SpecialTokens.IsMarker(text));

    /// <summary>Dichiara 60 secondi ma ne decodifica 10, come un MP3 troncato con l'header intatto.</summary>
    private sealed class TruncatedDecoder : IAudioDecoder
    {
        public bool CanDecode(string path) => true;

        public AudioInfo Inspect(string path) => new("troncato.mp3", "mp3", TimeSpan.FromSeconds(60), 16000, 1, 3);

        public IPcmReader OpenPcm16kMono(string path) => new Pcm(new float[16000 * 10]);

        private sealed class Pcm(float[] data) : IPcmReader
        {
            private int _position;

            public int Read(Span<float> buffer)
            {
                var count = Math.Min(buffer.Length, data.Length - _position);
                data.AsSpan(_position, count).CopyTo(buffer);
                _position += count;
                return count;
            }

            public void Dispose()
            {
            }
        }
    }

    [Fact]
    public async Task Model_not_installed_fails_with_typed_error()
    {
        var audioId = RegisterAudio(1);

        var ex = await Assert.ThrowsAsync<CodexVttException>(() =>
            _service.RunToCompletionAsync(audioId, "base", new TranscriptionOptions(), CancellationToken.None));

        Assert.Equal(ErrorCodes.ModelNotInstalled, ex.Code);
        Assert.Equal(0, _engine.ChunkCount);
    }

    [Fact]
    public async Task Log_file_never_contains_transcribed_text()
    {
        await InstallModelAsync();
        var audioId = RegisterAudio(3);
        var fileLogs = new FileLoggerProvider(_dir.AppPaths().Logs, LogLevel.Debug);
        using var service = new TranscriptionService(_store, _audio, _engine,
            new Logger<TranscriptionService>(new LoggerFactory([fileLogs])),
            new AudioChunker(TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(0.5)));

        var result = await service.RunToCompletionAsync(audioId, "base", new TranscriptionOptions("it"), CancellationToken.None);
        var log = await File.ReadAllTextAsync(fileLogs.CurrentFile);

        Assert.Contains("chunk-0", result.Text);
        Assert.Contains("completata", log);
        Assert.DoesNotContain("chunk-", log);
    }

    [Fact]
    public async Task Cancellation_stops_job_and_releases_engine()
    {
        await InstallModelAsync();
        _engine.Delay = TimeSpan.FromSeconds(30);
        var audioId = RegisterAudio(3);
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));

        var started = DateTime.UtcNow;
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            _service.RunToCompletionAsync(audioId, "base", new TranscriptionOptions(), cts.Token));

        Assert.True(DateTime.UtcNow - started < TimeSpan.FromSeconds(5));
        Assert.True(_engine.Disposed);
    }

    [Fact]
    public async Task Cancelled_job_leaves_no_temporary_files()
    {
        await InstallModelAsync();
        _engine.Delay = TimeSpan.FromSeconds(30);
        var audioId = RegisterAudio(3);
        var temp = _dir.AppPaths().Temp;
        Directory.CreateDirectory(temp);
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            _service.RunToCompletionAsync(audioId, "base", new TranscriptionOptions(), cts.Token));

        Assert.Empty(Directory.EnumerateFileSystemEntries(temp));
    }

    [Fact]
    public async Task Only_one_job_at_a_time()
    {
        await InstallModelAsync();
        _engine.Delay = TimeSpan.FromSeconds(30);
        var audioId = RegisterAudio(1);

        var first = _service.Start(audioId, "base", new TranscriptionOptions());
        var ex = Assert.Throws<CodexVttException>(() => _service.Start(audioId, "base", new TranscriptionOptions()));
        _service.Cancel(first.JobId);

        Assert.Equal(ErrorCodes.TranscriptionBusy, ex.Code);
    }

    [Fact]
    public async Task Engine_failure_is_reported_as_transcription_failed()
    {
        await InstallModelAsync();
        _engine.Throw = new InvalidOperationException("native crash");
        var audioId = RegisterAudio(1);

        var ex = await Assert.ThrowsAsync<CodexVttException>(() =>
            _service.RunToCompletionAsync(audioId, "base", new TranscriptionOptions(), CancellationToken.None));

        Assert.Equal(ErrorCodes.TranscriptionFailed, ex.Code);
        Assert.IsType<InvalidOperationException>(ex.InnerException);
        Assert.True(_engine.Disposed);
    }

    [Fact]
    public async Task Remaining_time_comes_from_the_measured_speed_and_is_zero_when_done()
    {
        await InstallModelAsync();
        var clock = new ManualTime();
        using var service = new TranscriptionService(_store, _audio, new SteppingEngine(clock), NullLogger<TranscriptionService>.Instance,
            new AudioChunker(TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(1)), clock);
        var reports = new List<JobProgress>();
        service.Progress += reports.Add;

        // 8 secondi di audio, 2 secondi reali ogni 25%: velocità 1 (tempo reale).
        await service.RunToCompletionAsync(RegisterAudio(8), "base", new TranscriptionOptions("it"), CancellationToken.None);

        var transcribing = reports.Where(r => r.Status == JobStatus.Transcribing).ToList();
        Assert.Null(transcribing[0].Remaining);
        Assert.Null(transcribing[0].Speed);
        Assert.Equal(1.0, transcribing[1].Speed!.Value, 3);
        Assert.Equal(TimeSpan.FromSeconds(4), transcribing[1].Remaining);
        Assert.Equal(TimeSpan.FromSeconds(2), transcribing[2].Remaining);
        Assert.Equal(TimeSpan.Zero, reports[^1].Remaining);
        Assert.Equal(JobStatus.Completed, reports[^1].Status);
        // Prima che il motore abbia prodotto avanzamento non si inventa nessuna stima.
        Assert.All(reports.Where(r => r.Status is JobStatus.Queued or JobStatus.Decoding), r => Assert.Null(r.Remaining));
    }

    [Fact]
    public async Task No_estimate_is_given_when_progress_is_too_short_to_trust()
    {
        await InstallModelAsync();
        var clock = new ManualTime();
        using var service = new TranscriptionService(_store, _audio, new SteppingEngine(clock, secondsPerStep: 0.5), NullLogger<TranscriptionService>.Instance,
            new AudioChunker(TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(1)), clock);
        var reports = new List<JobProgress>();
        service.Progress += reports.Add;

        await service.RunToCompletionAsync(RegisterAudio(8), "base", new TranscriptionOptions("it"), CancellationToken.None);

        // Mezzo secondo reale per passo: dopo 1,5 secondi non c'è ancora una finestra di 2 secondi.
        Assert.All(reports.Where(r => r.Status == JobStatus.Transcribing).Take(3), r => Assert.Null(r.Remaining));
    }

    private sealed class ManualTime : TimeProvider
    {
        private long _milliseconds;

        public override long TimestampFrequency => 1000;

        public override long GetTimestamp() => _milliseconds;

        public void Advance(double seconds) => _milliseconds += (long)(seconds * 1000);
    }

    private sealed class SteppingEngine(ManualTime clock, double secondsPerStep = 2) : ITranscriptionEngine
    {
        public Task<ITranscriptionSession> LoadAsync(ModelDescriptor model, string modelPath, CancellationToken ct) =>
            Task.FromResult<ITranscriptionSession>(new Session(clock, secondsPerStep));

        private sealed class Session(ManualTime clock, double secondsPerStep) : ITranscriptionSession
        {
            public string Runtime => "Stepping";

            public string? DetectedLanguage => null;

            public async IAsyncEnumerable<TranscriptSegment> TranscribeAsync(
                ReadOnlyMemory<float> samples,
                TranscriptionOptions options,
                IProgress<int>? progress,
                [EnumeratorCancellation] CancellationToken ct)
            {
                await Task.CompletedTask;
                foreach (var percent in new[] { 25, 50, 75 })
                {
                    progress?.Report(percent);
                    clock.Advance(secondsPerStep);
                }

                yield return new TranscriptSegment(TimeSpan.Zero, TimeSpan.FromSeconds(samples.Length / 16000.0), "testo", 0.9f);
            }

            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }

    private sealed class FakeEngine : ITranscriptionEngine
    {
        public int ChunkCount { get; private set; }

        public bool Disposed { get; private set; }

        public TimeSpan Delay { get; set; }

        public Exception? Throw { get; set; }

        public string? TextOverride { get; set; }

        public Task<ITranscriptionSession> LoadAsync(ModelDescriptor model, string modelPath, CancellationToken ct)
        {
            Assert.True(File.Exists(modelPath));
            return Task.FromResult<ITranscriptionSession>(new Session(this));
        }

        private sealed class Session(FakeEngine owner) : ITranscriptionSession
        {
            public string Runtime => "Fake";

            public string? DetectedLanguage { get; private set; }

            public async IAsyncEnumerable<TranscriptSegment> TranscribeAsync(
                ReadOnlyMemory<float> samples,
                TranscriptionOptions options,
                IProgress<int>? progress,
                [EnumeratorCancellation] CancellationToken ct)
            {
                if (owner.Throw is not null)
                    throw owner.Throw;
                await Task.Delay(owner.Delay, ct);
                progress?.Report(50);
                var index = owner.ChunkCount++;
                DetectedLanguage = options.Language;
                yield return new TranscriptSegment(TimeSpan.Zero, TimeSpan.FromSeconds(samples.Length / 16000.0), owner.TextOverride ?? $"chunk-{index}", 0.9f);
                progress?.Report(100);
            }

            public ValueTask DisposeAsync()
            {
                owner.Disposed = true;
                return ValueTask.CompletedTask;
            }
        }
    }
}
