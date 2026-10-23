using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using CodexVtt.Core;
using CodexVtt.Core.Audio;
using CodexVtt.Core.Evaluation;
using CodexVtt.Core.Models;
using CodexVtt.Core.Transcription;
using CodexVtt.Models.Download;
using CodexVtt.Transcription.Sherpa;
using CodexVtt.Transcription.Whisper;

// Confronto tra modelli sullo stesso audio (ADR-0009): tempo, velocità rispetto al tempo reale e WER.
//
//   dotnet run -c Release --project tools/CodexVtt.Bench -- --audio cartella --models tiny,base --language it --download
//
// --audio    file .wav o cartella di .wav; accanto a x.wav un x.txt con il testo di riferimento calcola il WER
// --models   id del catalogo separati da virgola, oppure "all"
// --language codice lingua (default it); Canary e Cohere lo richiedono
// --data     cartella dati (modelli, temp); default: una cartella "bench-data" accanto al programma
// --models-dir  cartella con modelli già scaricati (per non riscaricarli)
// --download scarica i modelli mancanti con lo stesso downloader dell'app
// --gpu      prova il runtime GPU per Whisper
// --out      scrive i risultati in un file JSON
var options = Options.Parse(args);
if (options.Audio is null)
{
    Console.Error.WriteLine("Manca --audio. Vedi i commenti in tools/CodexVtt.Bench/Program.cs.");
    return 2;
}

Console.WriteLine($"Schede video Vulkan: {(CodexVtt.Core.Hardware.GpuProbe.Devices.Count == 0 ? "nessuna" : string.Join(", ", CodexVtt.Core.Hardware.GpuProbe.Devices.Select(g => $"{g.Name} ({g.Kind})")))}; GPU per Whisper: {(options.Gpu ? "richiesta" : "non richiesta (usa --gpu)")}");
var dataRoot = Path.GetFullPath(options.Data ?? Path.Combine(AppContext.BaseDirectory, "bench-data"));
var paths = new AppPaths(dataRoot, options.ModelsDir);
paths.EnsureCreated();
var catalog = ModelCatalog.LoadEmbedded();
var store = new ModelStore(paths, catalog);
var downloader = new ModelDownloader(ModelDownloader.CreateHttpClient(), store, HostAllowlist.ModelHosts, NullLogger<ModelDownloader>.Instance);
var audio = new AudioLibrary([new WavDecoder()], TimeSpan.FromHours(4));
var sherpa = new SherpaEngine();
var engines = new EngineRegistry(new Dictionary<string, ITranscriptionEngine>
{
    ["whisper"] = new WhisperEngine(options.Gpu),
    [SherpaEngine.Transducer] = sherpa,
    [SherpaEngine.Canary] = sherpa,
    [SherpaEngine.Cohere] = sherpa,
});
using var service = new TranscriptionService(store, audio, engines, NullLogger<TranscriptionService>.Instance);

var files = Directory.Exists(options.Audio)
    ? Directory.EnumerateFiles(options.Audio, "*.wav").Order().ToList()
    : [options.Audio];
if (files.Count == 0)
{
    Console.Error.WriteLine("Nessun file .wav trovato.");
    return 2;
}

var ids = options.Models is "all" ? catalog.Models.Select(m => m.Id).ToList() : options.Models.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
var results = new List<Row>();
foreach (var id in ids)
{
    var model = catalog.Find(id);
    if (model is null)
    {
        Console.WriteLine($"{id}: sconosciuto nel catalogo");
        continue;
    }

    if (store.Status(model).State is ModelState.NotInstalled)
    {
        if (!options.Download)
        {
            Console.WriteLine($"{id}: non installato (usa --download)");
            continue;
        }

        Console.Write($"{id}: scarico {model.SizeBytes / 1_000_000} MB… ");
        var clock = Stopwatch.StartNew();
        var lastPercent = -1;
        await downloader.DownloadAsync(id, new Progress<DownloadProgress>(p =>
        {
            var percent = (int)(p.ReceivedBytes * 100 / Math.Max(1, p.TotalBytes));
            if (percent / 10 != lastPercent / 10)
                Console.Write($"{percent}% ");
            lastPercent = percent;
        }), CancellationToken.None);
        Console.WriteLine($"fatto in {clock.Elapsed.TotalSeconds:F0} s");
    }

    foreach (var file in files)
    {
        var handle = audio.Register(file);
        var reference = File.Exists(Path.ChangeExtension(file, ".txt")) ? File.ReadAllText(Path.ChangeExtension(file, ".txt")) : null;
        var language = options.Language;
        var started = Stopwatch.StartNew();
        try
        {
            var result = await service.RunToCompletionAsync(handle.AudioId, id, new TranscriptionOptions(language, Translate: false), CancellationToken.None);
            started.Stop();
            var wer = reference is null ? (double?)null : WordErrorRate.Compute(reference, result.Text);
            var row = new Row(id, Path.GetFileName(file), result.Model.Runtime, handle.Info.Duration.TotalSeconds, started.Elapsed.TotalSeconds,
                started.Elapsed.TotalSeconds / handle.Info.Duration.TotalSeconds, wer, result.Text, null);
            results.Add(row);
            Console.WriteLine($"{id,-32} {row.File,-24} {row.Runtime,-20} {row.AudioSeconds,6:F1}s in {row.ElapsedSeconds,6:F1}s (x{row.RealTimeFactor:F2}){(wer is { } w ? $"  WER {w * 100:F1}%  affidabilità {100 - (w * 100):F1}%" : "")}");
            Console.WriteLine($"    {Trim(result.Text)}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"{id,-32} {Path.GetFileName(file),-24} ERRORE: {(ex as CodexVtt.Core.Errors.CodexVttException)?.Code ?? ex.GetType().Name}: {ex.Message}");
            if (ex.InnerException is not null)
                Console.WriteLine($"    {ex.InnerException.GetType().Name}: {ex.InnerException.Message}");
            results.Add(new Row(id, Path.GetFileName(file), "", handle.Info.Duration.TotalSeconds, started.Elapsed.TotalSeconds, 0, null, "", ex.Message));
        }
    }
}

if (options.Out is not null)
    File.WriteAllText(options.Out, JsonSerializer.Serialize(results, new JsonSerializerOptions { WriteIndented = true }));

Console.WriteLine();
foreach (var group in results.Where(r => r.Error is null).GroupBy(r => r.Model))
{
    var withWer = group.Where(r => r.Wer is not null).ToList();
    var averageWer = withWer.Count > 0 ? withWer.Average(r => r.Wer!.Value) * 100 : (double?)null;
    var rtf = group.Sum(r => r.ElapsedSeconds) / group.Sum(r => r.AudioSeconds);
    Console.WriteLine($"{group.Key,-32} velocità x{rtf:F2} del tempo reale{(averageWer is { } a ? $"   WER medio {a:F1}%  affidabilità {100 - a:F1}%" : "")}");
}

return results.Any(r => r.Error is not null) ? 1 : 0;

static string Trim(string text) => text.Length <= 220 ? text : text[..220] + "…";

internal sealed record Row(string Model, string File, string Runtime, double AudioSeconds, double ElapsedSeconds, double RealTimeFactor, double? Wer, string Text, string? Error);

internal sealed record Options(string? Audio, string Models, string Language, string? Data, string? ModelsDir, bool Download, bool Gpu, string? Out)
{
    public static Options Parse(string[] args)
    {
        string? Get(string name) => Array.IndexOf(args, name) is var i and >= 0 && i + 1 < args.Length ? args[i + 1] : null;
        return new Options(Get("--audio"), Get("--models") ?? "all", Get("--language") ?? "it", Get("--data"), Get("--models-dir"),
            args.Contains("--download"), args.Contains("--gpu"), Get("--out"));
    }
}
