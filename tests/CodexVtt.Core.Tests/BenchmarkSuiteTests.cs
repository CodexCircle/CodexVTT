using CodexVtt.Core.Audio;
using CodexVtt.Core.Benchmark;
using CodexVtt.Core.Models;

namespace CodexVtt.Core.Tests;

public class BenchmarkClipsTests
{
    [Fact]
    public void Every_clip_has_a_unique_id_a_known_language_and_a_complete_pinned_file()
    {
        Assert.Equal(BenchmarkClips.All.Count, BenchmarkClips.All.Select(c => c.Id).Distinct().Count());
        foreach (var clip in BenchmarkClips.All)
        {
            Assert.Contains(clip.Language, BenchmarkClips.Languages);
            Assert.StartsWith("https://archive.org/download/", clip.Url, StringComparison.Ordinal);
            Assert.Equal(64, clip.Sha256.Length);
            Assert.True(clip.SizeBytes > 0);
            Assert.False(string.IsNullOrWhiteSpace(clip.ReferenceText));
            Assert.InRange(clip.ApproximateSeconds, 10, 600);
        }
    }

    [Fact]
    public void A_noisy_clip_reuses_the_file_and_the_text_of_a_clean_clip_of_the_same_language()
    {
        var noisy = BenchmarkClips.All.Where(c => c.IsDerived).ToList();
        Assert.NotEmpty(noisy);
        foreach (var clip in noisy)
        {
            var source = BenchmarkClips.SourceOf(clip);
            Assert.Equal(BenchmarkScenario.Noisy, clip.Scenario);
            Assert.Equal(BenchmarkScenario.Clean, source.Scenario);
            Assert.Equal(source.Language, clip.Language);
            Assert.Equal(source.Sha256, clip.Sha256);
            Assert.Equal(source.ReferenceText, clip.ReferenceText);
            Assert.True(clip.NoiseSnrDb > 0);
        }
    }

    [Fact]
    public void Downloads_lists_each_file_once_even_when_a_noisy_clip_shares_it()
    {
        Assert.Equal(BenchmarkClips.All.Count(c => !c.IsDerived), BenchmarkClips.Downloads().Count);
        Assert.All(BenchmarkClips.Downloads(), c => Assert.False(c.IsDerived));
    }

    [Fact]
    public void The_official_italian_clip_keeps_the_original_reference_file()
    {
        var clip = Assert.IsType<BenchmarkClip>(BenchmarkClips.Find("it-clean-1"));
        Assert.Equal(BenchmarkReference.Sha256, clip.Sha256);
        Assert.Equal(BenchmarkReference.FileName, clip.FileName);
        Assert.Equal(BenchmarkReferenceText.Text, clip.ReferenceText);
    }
}

public class BenchmarkReliabilityTests
{
    private static BenchmarkEntry E(string language, string scenario, double accuracy, string clip) =>
        new("m", "x", "Cpu", null, 60, 2, 4, DateTimeOffset.UtcNow, "2.0.0", accuracy, clip, language, scenario);

    [Fact]
    public void Each_scenario_is_averaged_and_the_two_scenarios_weigh_the_same()
    {
        var result = Assert.Single(BenchmarkReliability.ByLanguage([E("it", "clean", 98, "a"), E("it", "clean", 96, "b"), E("it", "noisy", 90, "c")]));

        Assert.Equal("it", result.Language);
        Assert.Equal(97, result.Clean);
        Assert.Equal(90, result.Noisy);
        Assert.Equal(93.5, result.Overall); // non (98 + 96 + 90) / 3: il pulito non pesa il doppio
    }

    [Fact]
    public void With_one_scenario_only_that_scenario_counts()
    {
        var result = Assert.Single(BenchmarkReliability.ByLanguage([E("en", "clean", 80, "a")]));

        Assert.Equal(80, result.Overall);
        Assert.Null(result.Noisy);
    }

    [Fact]
    public void The_score_is_for_the_chosen_language_or_the_average_of_the_measured_languages()
    {
        BenchmarkEntry[] entries = [E("it", "clean", 98, "a"), E("it", "clean", 96, "b"), E("it", "noisy", 90, "c"), E("en", "clean", 80, "d")];

        Assert.Equal(93.5, BenchmarkReliability.Score(entries, "it"));
        Assert.Equal(80, BenchmarkReliability.Score(entries, "en"));
        Assert.Equal(86.8, BenchmarkReliability.Score(entries, null));
        Assert.Null(BenchmarkReliability.Score(entries, "fr"));
    }

    [Fact]
    public void Measures_without_a_clip_or_without_accuracy_are_ignored()
    {
        BenchmarkEntry own = new("m", "x", "Cpu", null, 60, 2, 4, DateTimeOffset.UtcNow, "2.0.0");

        Assert.Empty(BenchmarkReliability.ByLanguage([own]));
        Assert.Null(BenchmarkReliability.Score([own], null));
    }

    [Fact]
    public void Combining_the_measures_of_a_model_sums_the_audio_and_keeps_the_newest_runtime()
    {
        var old = new BenchmarkEntry("m", "x", "Cpu", 4, 100, 4, 5, DateTimeOffset.UtcNow.AddHours(-1), "2.0.0");
        var a = new BenchmarkEntry("m", "x", "Vulkan (GPU)", 4, 100, 2, 10, DateTimeOffset.UtcNow, "2.0.0", 90, "a", "it", "clean");
        var b = new BenchmarkEntry("m", "x", "Vulkan (GPU)", 4, 300, 4, 30, DateTimeOffset.UtcNow, "2.0.0", 80, "b", "it", "noisy");

        var combined = Assert.IsType<BenchmarkEntry>(BenchmarkStore.Combine([old, a, b]));

        Assert.Equal("Vulkan (GPU)", combined.Runtime);
        Assert.Equal(400, combined.AudioSeconds);
        Assert.Equal(3, combined.LoadSeconds);
        Assert.Equal(400 / ((100 / 10.0) + (300 / 30.0)), combined.Speed, 6);
        Assert.Null(combined.Accuracy);
        Assert.Null(BenchmarkStore.Combine([]));
    }
}

public class BenchmarkPerLanguageRecommenderTests
{
    private static IEnumerable<(BenchmarkEntry Entry, ModelDescriptor Model)> Measured(string id, double speed, params (string Language, string Scenario, double Accuracy)[] scores)
    {
        var model = TestData.Catalog(models: [(id, TestData.Bytes(id, 100), 2, 2, 0)]).Models[0];
        return scores.Select((s, i) => (new BenchmarkEntry(id, "x", "Cpu", null, 60, 2, speed, DateTimeOffset.UtcNow, "2.0.0", s.Accuracy, $"{s.Language}-{s.Scenario}-{i}", s.Language, s.Scenario), model));
    }

    [Fact]
    public void The_best_model_depends_on_the_chosen_language_and_auto_uses_the_average()
    {
        var measured = Measured("alpha", 10, ("it", "clean", 99), ("it", "noisy", 99), ("en", "clean", 80), ("en", "noisy", 80))
            .Concat(Measured("beta", 10, ("it", "clean", 90), ("it", "noisy", 90), ("en", "clean", 95), ("en", "noisy", 95)))
            .ToList();

        Assert.Equal("alpha", BenchmarkRecommender.Pick(measured, "it")!.Model.Id);
        Assert.Equal("beta", BenchmarkRecommender.Pick(measured, "en")!.Model.Id);
        var auto = BenchmarkRecommender.Pick(measured, null)!;
        Assert.Equal("beta", auto.Model.Id); // media 92,5 contro 89,5
        Assert.True(auto.ByAccuracy);
        Assert.Equal(92.5, auto.Accuracy);
    }

    [Fact]
    public void The_noise_scenario_can_change_the_winner_even_when_the_clean_scenario_is_a_tie()
    {
        var measured = Measured("alpha", 10, ("it", "clean", 98), ("it", "noisy", 70))
            .Concat(Measured("beta", 10, ("it", "clean", 98), ("it", "noisy", 90)))
            .ToList();

        Assert.Equal("beta", BenchmarkRecommender.Pick(measured, "it")!.Model.Id);
    }

    [Fact]
    public void A_language_without_reliability_falls_back_to_the_quality_ranking()
    {
        var measured = Measured("alpha", 10, ("it", "clean", 99)).ToList();

        var picked = BenchmarkRecommender.Pick(measured, "fr")!;

        Assert.False(picked.ByAccuracy);
        Assert.Equal("alpha", picked.Model.Id);
    }
}

public class SyntheticNoiseTests
{
    private static float[] Tone(int samples) =>
        Enumerable.Range(0, samples).Select(i => (float)(0.2 * Math.Sin(2 * Math.PI * 220 * i / 16000.0))).ToArray();

    private static double Power(IEnumerable<double> values) => values.Average(v => v * v);

    [Fact]
    public void The_same_input_and_seed_always_give_the_same_noisy_audio()
    {
        var speech = Tone(16000);

        Assert.Equal(SyntheticNoise.Mix(speech, 10, 7), SyntheticNoise.Mix(speech, 10, 7));
        Assert.NotEqual(SyntheticNoise.Mix(speech, 10, 7), SyntheticNoise.Mix(speech, 10, 8));
    }

    [Fact]
    public void The_signal_to_noise_ratio_is_the_requested_one()
    {
        var speech = Tone(32000);

        var mixed = SyntheticNoise.Mix(speech, 10, 1);

        var noise = mixed.Zip(speech, (m, s) => (double)m - s);
        var snr = 10 * Math.Log10(Power(speech.Select(s => (double)s)) / Power(noise));
        Assert.Equal(10, snr, 0.3);
        Assert.True(mixed.Max(Math.Abs) <= 0.99f);
    }

    [Fact]
    public void Silence_and_empty_audio_come_back_unchanged()
    {
        Assert.Empty(SyntheticNoise.Mix([], 10, 1));
        Assert.All(SyntheticNoise.Mix(new float[100], 10, 1), s => Assert.Equal(0f, s));
    }

    [Fact]
    public void Loud_speech_is_scaled_down_instead_of_clipped()
    {
        var loud = Enumerable.Range(0, 16000).Select(i => (float)Math.Sin(2 * Math.PI * 220 * i / 16000.0)).ToArray();

        var mixed = SyntheticNoise.Mix(loud, 5, 1);

        Assert.InRange(mixed.Max(Math.Abs), 0.5f, 0.99f);
    }
}

public sealed class BenchmarkAudioPreparerTests : IDisposable
{
    private readonly TempDir _dir = new();
    private readonly AudioLibrary _audio = new([new WavDecoder()], TimeSpan.FromHours(3));

    public void Dispose() => _dir.Dispose();

    private BenchmarkClip Clip(string? noiseSource = null) =>
        new("t-1", "it", noiseSource is null ? BenchmarkScenario.Clean : BenchmarkScenario.Noisy, "t", "t.wav", "https://archive.org/download/x/t.wav",
            1, new string('a', 64), 3, "testo", noiseSource, noiseSource is null ? 0 : 10);

    private static float[] ReadAll(IPcmReader reader)
    {
        using (reader)
        {
            var all = new List<float>();
            var buffer = new float[4096];
            int read;
            while ((read = reader.Read(buffer)) > 0)
                all.AddRange(buffer.AsSpan(0, read));
            return [.. all];
        }
    }

    [Fact]
    public void A_clean_clip_is_registered_as_it_is()
    {
        var path = _dir.File("clean.wav", TestData.Wav(16000, 1, 3));
        var preparer = new BenchmarkAudioPreparer(_audio, Path.Combine(_dir.Path, "noise"));

        var handle = preparer.Prepare(Clip(), path);

        Assert.Equal(3, handle.Info.Duration.TotalSeconds, 0.01);
        Assert.False(Directory.Exists(Path.Combine(_dir.Path, "noise")));
    }

    [Fact]
    public void A_noisy_clip_has_the_same_length_but_different_samples_and_is_written_to_the_temp_folder()
    {
        var path = _dir.File("clean.wav", TestData.Wav(16000, 1, 3));
        var preparer = new BenchmarkAudioPreparer(_audio, Path.Combine(_dir.Path, "noise"));
        var clean = preparer.Prepare(Clip(), path);

        var noisy = preparer.Prepare(Clip("t-0"), path);

        Assert.Equal(clean.Info.Duration.TotalSeconds, noisy.Info.Duration.TotalSeconds, 0.01);
        Assert.True(File.Exists(Path.Combine(_dir.Path, "noise", "t-1.wav")));
        Assert.NotEqual(ReadAll(_audio.Open(clean.AudioId)), ReadAll(_audio.Open(noisy.AudioId)));
        // Ripetibile: lo stesso brano rumoroso è identico a ogni preparazione.
        var again = preparer.Prepare(Clip("t-0"), path);
        Assert.Equal(ReadAll(_audio.Open(noisy.AudioId)), ReadAll(_audio.Open(again.AudioId)));
    }
}

public sealed class WavWriterTests : IDisposable
{
    private readonly TempDir _dir = new();

    public void Dispose() => _dir.Dispose();

    [Fact]
    public void A_written_wav_reads_back_with_the_same_length_and_samples()
    {
        var samples = Enumerable.Range(0, 8000).Select(i => (float)(0.5 * Math.Sin(2 * Math.PI * 440 * i / 16000.0))).ToArray();
        var path = Path.Combine(_dir.Path, "out.wav");

        WavWriter.WritePcm16Mono(path, samples, 16000);

        var decoder = new WavDecoder();
        Assert.True(decoder.CanDecode(path));
        Assert.Equal(0.5, decoder.Inspect(path).Duration.TotalSeconds, 0.001);
        using var reader = decoder.OpenPcm16kMono(path);
        var read = new float[8000];
        Assert.Equal(8000, reader.Read(read));
        for (var i = 0; i < samples.Length; i++)
            Assert.Equal(samples[i], read[i], 1e-3f);
    }
}
