using Microsoft.Extensions.Logging.Abstractions;
using CodexVtt.Core;
using CodexVtt.Core.Audio;
using CodexVtt.Core.Models;
using CodexVtt.Core.Transcription;
using CodexVtt.Transcription.Whisper;

namespace CodexVtt.Integration.Tests;

/// <summary>Esegue il test solo se è configurato un modello reale (CODEXVTT_TEST_MODEL).</summary>
public sealed class RequiresModelFactAttribute : FactAttribute
{
    public RequiresModelFactAttribute()
    {
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("CODEXVTT_TEST_MODEL")))
            Skip = "Impostare CODEXVTT_TEST_MODEL (file ggml) e CODEXVTT_TEST_AUDIO (WAV) per eseguire il test.";
    }
}

/// <summary>
/// Trascrizione reale e offline con whisper.cpp (#3, #9). In CI gira su Windows con il modello
/// "base" e il campione jfk.wav; in locale si attiva con le variabili d'ambiente.
/// </summary>
public sealed class WhisperIntegrationTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "codexvtt-it-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    [RequiresModelFact]
    public async Task Transcribes_reference_sample_offline()
    {
        var (service, audioId, modelId) = await SetUpAsync();

        var result = await service.RunToCompletionAsync(audioId, modelId, new TranscriptionOptions("auto"), CancellationToken.None);

        var expected = Environment.GetEnvironmentVariable("CODEXVTT_TEST_EXPECTED");
        Assert.NotEmpty(result.Segments);
        Assert.Equal(modelId, result.Model.ModelId);
        Assert.Contains(result.Model.Runtime, new[] { "Cpu", "CpuNoAvx" });
        if (!string.IsNullOrEmpty(expected))
        {
            foreach (var word in expected.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
                Assert.Contains(word, result.Text, StringComparison.OrdinalIgnoreCase);
        }

        Console.WriteLine($"[codexvtt] modello={result.Model.ModelId} runtime={result.Model.Runtime} lingua={result.DetectedLanguage} " +
                          $"audio={result.AudioDuration.TotalSeconds:0.0}s tempo={result.Elapsed.TotalSeconds:0.0}s testo=\"{result.Text}\"");
    }

    [RequiresModelFact]
    public async Task Cancellation_is_prompt()
    {
        var (service, audioId, modelId) = await SetUpAsync();
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(300));
        var watch = System.Diagnostics.Stopwatch.StartNew();

        try
        {
            await service.RunToCompletionAsync(audioId, modelId, new TranscriptionOptions("auto"), cts.Token);
        }
        catch (OperationCanceledException)
        {
        }

        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(30), $"Annullamento troppo lento: {watch.Elapsed}");
    }

    private async Task<(TranscriptionService Service, string AudioId, string ModelId)> SetUpAsync()
    {
        var modelPath = Environment.GetEnvironmentVariable("CODEXVTT_TEST_MODEL")!;
        var audioPath = Environment.GetEnvironmentVariable("CODEXVTT_TEST_AUDIO")!;
        var paths = new AppPaths(_root);
        paths.EnsureCreated();

        // Catalogo costruito sull'hash del file fornito: il percorso di import è lo stesso dell'app.
        var sha = await ModelStore.ComputeSha256Async(modelPath, null, CancellationToken.None);
        var embedded = ModelCatalog.LoadEmbedded();
        var template = embedded.Models.FirstOrDefault(m => string.Equals(m.File, Path.GetFileName(modelPath), StringComparison.OrdinalIgnoreCase))
            ?? embedded.Models[0];
        if (template.File == Path.GetFileName(modelPath))
            Assert.Equal(template.Sha256, sha);
        var catalog = new ModelCatalog(embedded.Source, embedded.Selection,
            [template with { File = Path.GetFileName(modelPath), Sha256 = sha, SizeBytes = new FileInfo(modelPath).Length }]);
        var store = new ModelStore(paths, catalog);
        await store.ImportAsync(modelPath);

        var audio = new AudioLibrary([new WavDecoder()], TimeSpan.FromHours(3));
        var service = new TranscriptionService(store, audio, new WhisperEngine(), NullLogger<TranscriptionService>.Instance);
        return (service, audio.Register(audioPath).AudioId, template.Id);
    }
}
