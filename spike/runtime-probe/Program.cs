// Spike per ADR-0002 (runtime Whisper locale).
// Verifica che Whisper.net (whisper.cpp) carichi il runtime nativo, apra un modello GGML
// locale, trascriva un WAV senza rete e riporti tempi, memoria, segmenti e modello usato.
// Non scarica nulla: il modello va fornito come file locale.

using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using Whisper.net;
using Whisper.net.LibraryLoader;

var options = ProbeOptions.Parse(args);
if (options is null)
{
    Console.Error.WriteLine(ProbeOptions.Usage);
    return 2;
}

if (options.NetworkCheck)
{
    // Usato in CI dopo aver bloccato in uscita questo eseguibile: deve fallire.
    try
    {
        using var tcp = new System.Net.Sockets.TcpClient();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await tcp.ConnectAsync("huggingface.co", 443, cts.Token);
        Console.Error.WriteLine("network-check: connessione RIUSCITA (la rete è raggiungibile)");
        return 3;
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"network-check: connessione bloccata ({ex.GetType().Name})");
    }
}

var report = new ProbeReport
{
    Os = System.Runtime.InteropServices.RuntimeInformation.OSDescription,
    Architecture = System.Runtime.InteropServices.RuntimeInformation.OSArchitecture.ToString(),
    ProcessorCount = Environment.ProcessorCount,
    TotalMemoryBytes = GC.GetGCMemoryInfo().TotalAvailableMemoryBytes,
    CpuFeatures = CpuFeatures.Describe(),
    ModelPath = options.ModelPath,
    AudioPath = options.AudioPath,
};

try
{
    if (!File.Exists(options.ModelPath))
        throw new ProbeException("model_missing", $"Modello non trovato: {options.ModelPath}");
    if (!File.Exists(options.AudioPath))
        throw new ProbeException("audio_missing", $"Audio non trovato: {options.AudioPath}");

    report.ModelSizeBytes = new FileInfo(options.ModelPath).Length;

    var hashWatch = Stopwatch.StartNew();
    report.ModelSha256 = await Sha256Async(options.ModelPath);
    report.HashMilliseconds = hashWatch.ElapsedMilliseconds;
    if (options.ExpectedSha256 is { } expected &&
        !string.Equals(expected, report.ModelSha256, StringComparison.OrdinalIgnoreCase))
    {
        throw new ProbeException("model_hash_mismatch",
            $"Hash del modello non valido. Atteso {expected}, trovato {report.ModelSha256}");
    }

    var loadWatch = Stopwatch.StartNew();
    using var factory = WhisperFactory.FromPath(options.ModelPath, new WhisperFactoryOptions { UseGpu = options.UseGpu });
    report.LoadMilliseconds = loadWatch.ElapsedMilliseconds;
    report.RuntimeInfo = WhisperFactory.GetRuntimeInfo();
    report.LoadedRuntime = RuntimeOptions.LoadedLibrary?.ToString();

    var builder = factory.CreateBuilder()
        .WithThreads(options.Threads)
        .WithProgressHandler(p =>
        {
            report.LastProgress = p;
            Console.Error.Write($"\rprogresso {p,3}%");
        });
    builder = options.Language == "auto" ? builder.WithLanguageDetection() : builder.WithLanguage(options.Language);

    using var cts = new CancellationTokenSource();
    if (options.CancelAfterSeconds is { } seconds)
        cts.CancelAfter(TimeSpan.FromSeconds(seconds));

    await using var processor = builder.Build();
    await using var audio = File.OpenRead(options.AudioPath);

    var transcribeWatch = Stopwatch.StartNew();
    try
    {
        await foreach (var segment in processor.ProcessAsync(audio, cts.Token))
        {
            report.Segments.Add(new ProbeSegment(segment.Start, segment.End, segment.Text.Trim(), segment.Language, segment.Probability));
        }
    }
    catch (OperationCanceledException)
    {
        report.Cancelled = true;
    }
    report.TranscribeMilliseconds = transcribeWatch.ElapsedMilliseconds;
    report.CancelLatencyMilliseconds = report.Cancelled && options.CancelAfterSeconds is { } c
        ? transcribeWatch.ElapsedMilliseconds - (long)(c * 1000)
        : null;
    Console.Error.WriteLine();

    report.AudioSeconds = report.Segments.Count > 0 ? report.Segments[^1].End.TotalSeconds : null;
    report.RealTimeFactor = report.AudioSeconds is > 0
        ? report.TranscribeMilliseconds / 1000.0 / report.AudioSeconds
        : null;
    report.Outcome = report.Cancelled ? "cancelled" : "ok";
}
catch (ProbeException ex)
{
    report.Outcome = ex.Code;
    report.Error = ex.Message;
}
catch (Exception ex)
{
    // Errori del runtime nativo (DLL mancanti, istruzioni CPU, modello corrotto) arrivano qui.
    report.Outcome = "runtime_error";
    report.Error = $"{ex.GetType().Name}: {ex.Message}";
}
finally
{
    report.PeakWorkingSetBytes = Process.GetCurrentProcess().PeakWorkingSet64;
}

var json = JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true });
Console.WriteLine(json);
if (options.JsonOutput is { } path)
    await File.WriteAllTextAsync(path, json);

return report.Outcome is "ok" or "cancelled" ? 0 : 1;

static async Task<string> Sha256Async(string path)
{
    await using var stream = File.OpenRead(path);
    return Convert.ToHexStringLower(await SHA256.HashDataAsync(stream));
}

sealed class ProbeException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}

sealed record ProbeSegment(TimeSpan Start, TimeSpan End, string Text, string? Language, float Probability);

sealed class ProbeReport
{
    public string Outcome { get; set; } = "not_started";
    public string? Error { get; set; }
    public string Os { get; set; } = "";
    public string Architecture { get; set; } = "";
    public int ProcessorCount { get; set; }
    public long TotalMemoryBytes { get; set; }
    public string CpuFeatures { get; set; } = "";
    public string? RuntimeInfo { get; set; }
    public string? LoadedRuntime { get; set; }
    public string ModelPath { get; set; } = "";
    public long ModelSizeBytes { get; set; }
    public string? ModelSha256 { get; set; }
    public string AudioPath { get; set; } = "";
    public double? AudioSeconds { get; set; }
    public long HashMilliseconds { get; set; }
    public long LoadMilliseconds { get; set; }
    public long TranscribeMilliseconds { get; set; }
    public double? RealTimeFactor { get; set; }
    public int LastProgress { get; set; }
    public bool Cancelled { get; set; }
    public long? CancelLatencyMilliseconds { get; set; }
    public long PeakWorkingSetBytes { get; set; }
    public List<ProbeSegment> Segments { get; } = [];
}

sealed record ProbeOptions(
    string ModelPath,
    string AudioPath,
    string? ExpectedSha256,
    string Language,
    int Threads,
    bool UseGpu,
    double? CancelAfterSeconds,
    string? JsonOutput,
    bool NetworkCheck)
{
    public const string Usage =
        "Uso: codexvtt-runtime-probe --model <ggml.bin> --audio <file.wav 16kHz mono> " +
        "[--sha256 <hex>] [--language auto|it|en|...] [--threads N] [--gpu] " +
        "[--cancel-after <secondi>] [--json <report.json>] [--network-check]";

    public static ProbeOptions? Parse(string[] args)
    {
        var map = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < args.Length; i++)
        {
            if (!args[i].StartsWith("--", StringComparison.Ordinal))
                return null;
            var hasValue = i + 1 < args.Length && !args[i + 1].StartsWith("--", StringComparison.Ordinal);
            map[args[i][2..]] = hasValue ? args[++i] : null;
        }

        if (!map.TryGetValue("model", out var model) || model is null ||
            !map.TryGetValue("audio", out var audio) || audio is null)
            return null;

        return new ProbeOptions(
            model,
            audio,
            map.GetValueOrDefault("sha256"),
            map.GetValueOrDefault("language") ?? "auto",
            int.TryParse(map.GetValueOrDefault("threads"), out var t) ? t : Math.Max(1, Environment.ProcessorCount - 1),
            map.ContainsKey("gpu"),
            double.TryParse(map.GetValueOrDefault("cancel-after"), System.Globalization.CultureInfo.InvariantCulture, out var c) ? c : null,
            map.GetValueOrDefault("json"),
            map.ContainsKey("network-check"));
    }
}

static class CpuFeatures
{
    public static string Describe()
    {
        if (!System.Runtime.Intrinsics.X86.X86Base.IsSupported)
            return System.Runtime.Intrinsics.Arm.AdvSimd.IsSupported ? "arm64 AdvSimd" : "n/d";

        var features = new List<string>();
        if (System.Runtime.Intrinsics.X86.Avx.IsSupported) features.Add("AVX");
        if (System.Runtime.Intrinsics.X86.Avx2.IsSupported) features.Add("AVX2");
        if (System.Runtime.Intrinsics.X86.Fma.IsSupported) features.Add("FMA");
        if (System.Runtime.Intrinsics.X86.Avx512F.IsSupported) features.Add("AVX512F");
        return features.Count == 0 ? "nessuna estensione AVX (serve Whisper.net.Runtime.NoAvx)" : string.Join(' ', features);
    }
}
