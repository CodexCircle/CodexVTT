using CodexVtt.Core.Evaluation;
using CodexVtt.Core.Models;
using CodexVtt.Core.Transcription;

namespace CodexVtt.Core.Tests;

public class WordErrorRateTests
{
    [Fact]
    public void Identical_text_has_no_errors_whatever_the_punctuation_and_case()
    {
        Assert.Equal(0, WordErrorRate.Compute("Buongiorno, a tutti! L'anno è finito.", "buongiorno a tutti l anno è finito"));
    }

    [Fact]
    public void Counts_substitutions_deletions_and_insertions_over_the_reference_words()
    {
        // 4 parole di riferimento: «è» diventa «sarà» (sostituzione) e «oggi» è in più (inserzione): 2 errori su 4.
        Assert.Equal(0.5, WordErrorRate.Compute("la riunione è fissata", "la riunione sarà fissata oggi"), 3);
        // Una parola cancellata su tre.
        Assert.Equal(1.0 / 3, WordErrorRate.Compute("uno due tre", "uno tre"), 3);
    }

    [Fact]
    public void Empty_reference_is_total_error_only_when_there_is_a_hypothesis()
    {
        Assert.Equal(0, WordErrorRate.Compute("", " "));
        Assert.Equal(1, WordErrorRate.Compute("", "qualcosa"));
        Assert.Equal(1, WordErrorRate.Compute("uno due", ""));
    }
}

public class SegmenterTests
{
    private static readonly TimeSpan Offset = TimeSpan.FromSeconds(100);

    [Fact]
    public void Without_token_times_the_window_is_one_segment()
    {
        var segments = global::CodexVtt.Transcription.Sherpa.Segmenter.Build("Ciao a tutti.", null, null, null, Offset, TimeSpan.FromSeconds(5));

        var segment = Assert.Single(segments);
        Assert.Equal("Ciao a tutti.", segment.Text);
        Assert.Equal(Offset, segment.Start);
        Assert.Equal(Offset + TimeSpan.FromSeconds(5), segment.End);
    }

    [Fact]
    public void Token_times_split_at_sentence_ends_and_long_pauses()
    {
        string[] tokens = ["▁Buon", "giorno", ".", "▁Come", "▁stai", "?", "▁Bene", "▁grazie"];
        float[] times = [0.0f, 0.3f, 0.7f, 1.0f, 1.3f, 1.6f, 5.0f, 5.4f];

        var segments = global::CodexVtt.Transcription.Sherpa.Segmenter.Build("Buongiorno. Come stai? Bene grazie", tokens, times, null, Offset, TimeSpan.FromSeconds(8));

        Assert.Equal(["Buongiorno.", "Come stai?", "Bene grazie"], segments.Select(s => s.Text).ToArray());
        Assert.Equal(Offset, segments[0].Start);
        Assert.Equal(Offset + TimeSpan.FromSeconds(1.0), segments[1].Start);
        Assert.Equal(Offset + TimeSpan.FromSeconds(5.0), segments[2].Start);
        Assert.All(segments, s => Assert.True(s.End > s.Start));
    }

    [Fact]
    public void Empty_results_produce_no_segments()
    {
        Assert.Empty(global::CodexVtt.Transcription.Sherpa.Segmenter.Build("  ", null, null, null, Offset, TimeSpan.FromSeconds(3)));
    }
}

public class EngineRegistryTests
{
    [Fact]
    public async Task Picks_the_engine_named_by_the_model_and_refuses_unknown_ones()
    {
        var whisper = new MarkerEngine("whisper");
        var other = new MarkerEngine("other");
        var registry = new EngineRegistry(new Dictionary<string, ITranscriptionEngine> { ["whisper"] = whisper, ["sherpa-canary"] = other });
        var catalog = TestData.Catalog(models: ("base", TestData.Bytes("a"), 1, 1, 0));
        var model = catalog.Models[0];

        Assert.Equal("whisper", (await registry.LoadAsync(model, "x", default)).Runtime);
        Assert.Equal("other", (await registry.LoadAsync(model with { Engine = "sherpa-canary" }, "x", default)).Runtime);
        var error = await Assert.ThrowsAsync<CodexVtt.Core.Errors.CodexVttException>(() => registry.LoadAsync(model with { Engine = "boh" }, "x", default));
        Assert.Equal(CodexVtt.Core.Errors.ErrorCodes.EngineUnavailable, error.Code);
    }

    private sealed class MarkerEngine(string runtime) : ITranscriptionEngine
    {
        public Task<ITranscriptionSession> LoadAsync(ModelDescriptor model, string modelPath, CancellationToken ct) =>
            Task.FromResult<ITranscriptionSession>(new Session(runtime));

        private sealed class Session(string runtime) : ITranscriptionSession
        {
            public string Runtime { get; } = runtime;

            public string? DetectedLanguage => null;

            public async IAsyncEnumerable<TranscriptSegment> TranscribeAsync(
                ReadOnlyMemory<float> samples, TranscriptionOptions options, IProgress<int>? progress,
                [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
            {
                await Task.CompletedTask;
                yield break;
            }

            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }
}

public class GpuTests
{
    [Fact]
    public void The_runtime_label_claims_a_gpu_only_when_whisper_cpp_chose_one()
    {
        Assert.Equal("Cpu", global::CodexVtt.Transcription.Whisper.WhisperEngine.RuntimeLabel("Cpu", false, []));
        Assert.Equal("CpuNoAvx", global::CodexVtt.Transcription.Whisper.WhisperEngine.RuntimeLabel("CpuNoAvx", false, []));
        Assert.Equal("Cpu", global::CodexVtt.Transcription.Whisper.WhisperEngine.RuntimeLabel("Vulkan", true, ["whisper_backend_init_gpu: device 0: CPU (type: 0)", "whisper_backend_init_gpu: no GPU found"]));
        Assert.Equal("Vulkan (GPU)", global::CodexVtt.Transcription.Whisper.WhisperEngine.RuntimeLabel("Vulkan", true, ["whisper_backend_init_gpu: using Vulkan0 backend"]));
        Assert.Equal("Cpu", global::CodexVtt.Transcription.Whisper.WhisperEngine.RuntimeLabel("Vulkan", false, ["whisper_backend_init_gpu: using Vulkan0 backend"]));
        // Scheda richiesta e nessun messaggio contrario: è la scheda, anche se il log non ha la riga attesa (era l'errore «Cpu» su tutti i modelli).
        Assert.Equal("Vulkan (GPU)", global::CodexVtt.Transcription.Whisper.WhisperEngine.RuntimeLabel("Vulkan", true, []));
        Assert.Equal("Vulkan (GPU)", global::CodexVtt.Transcription.Whisper.WhisperEngine.RuntimeLabel("Vulkan", true, ["ggml_vulkan: Found 1 Vulkan devices:", "ggml_vulkan: 0 = NVIDIA GeForce RTX 3060"]));
    }

    [Fact]
    public void Probing_for_gpus_never_throws_and_lists_only_real_devices()
    {
        var devices = global::CodexVtt.Core.Hardware.GpuProbe.Devices;

        Assert.NotNull(devices);
        Assert.All(devices, d => Assert.False(string.IsNullOrWhiteSpace(d.Name)));
        Assert.Equal(devices.Count > 0, global::CodexVtt.Transcription.Whisper.WhisperEngine.IsVulkanGpuPresent());
    }
}
public class ProgressProtocolTests
{
    [Fact]
    public void Remaining_time_and_speed_travel_to_the_ui_in_seconds_and_are_left_out_when_unknown()
    {
        var known = System.Text.Json.JsonSerializer.Serialize(
            new JobProgress("j1", JobStatus.Transcribing, 50, TimeSpan.FromSeconds(4), TimeSpan.FromSeconds(8), TimeSpan.FromSeconds(4.5), 1.25),
            CodexVtt.Bridge.BridgeProtocol.JsonOptions);
        var unknown = System.Text.Json.JsonSerializer.Serialize(
            new JobProgress("j1", JobStatus.Transcribing, 10, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(8)),
            CodexVtt.Bridge.BridgeProtocol.JsonOptions);

        Assert.Contains("\"remaining\":4.5", known, StringComparison.Ordinal);
        Assert.Contains("\"speed\":1.25", known, StringComparison.Ordinal);
        Assert.DoesNotContain("remaining", unknown, StringComparison.Ordinal);
        Assert.DoesNotContain("speed", unknown, StringComparison.Ordinal);
    }
}

public class CpuLimitTests
{
    [Theory]
    [InlineData(100, 8, null)]
    [InlineData(50, 8, 4)]
    [InlineData(25, 8, 2)]
    [InlineData(10, 8, 1)]
    [InlineData(25, 6, 1)]
    [InlineData(75, 6, 4)]
    [InlineData(50, 1, 1)]
    [InlineData(50, 32, 16)]
    [InlineData(5, 8, 1)]
    public void Threads_follow_the_percentage_of_logical_cores_and_never_exceed_it(int percent, int cores, int? expected)
    {
        var threads = CodexVtt.Core.Transcription.CpuLimit.Threads(percent, cores);

        Assert.Equal(expected, threads);
        if (threads is { } t && cores > 1)
            Assert.True(t * 100.0 / cores <= Math.Max(percent, 100.0 / cores) + 1e-9, "L'uso non deve superare il limite (salvo il minimo di un thread).");
    }

    [Theory]
    [InlineData(9, false)]
    [InlineData(10, true)]
    [InlineData(100, true)]
    [InlineData(101, false)]
    public void Only_values_between_10_and_100_are_valid(int percent, bool valid) =>
        Assert.Equal(valid, CodexVtt.Core.Transcription.CpuLimit.IsValid(percent));
}
