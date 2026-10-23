using System.Reflection;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using Microsoft.Web.WebView2.Core;
using CodexVtt.Audio.Windows;
using CodexVtt.Core;
using CodexVtt.Core.Audio;
using CodexVtt.Core.Diagnostics;
using CodexVtt.Core.Export;
using CodexVtt.Core.History;
using CodexVtt.Core.Logging;
using CodexVtt.Core.Models;
using CodexVtt.Core.Settings;
using CodexVtt.Core.Transcription;
using CodexVtt.Transcription.Sherpa;
using CodexVtt.Transcription.Whisper;

namespace CodexVtt.App.Headless;

/// <summary>
/// Modalità senza GUI (issue #38), avviate dalla riga di comando:
/// <list type="bullet">
/// <item><c>--diagnose &lt;cartella&gt;</c>: scrive <c>diagnostic-report.json</c> (exit 0 se tutti i controlli passano, 2 altrimenti).</item>
/// <item><c>--headless-transcribe &lt;audio&gt; [--model id] [--format txt|srt|vtt] [--language auto] --out &lt;cartella&gt;</c>:
/// import, trascrizione ed export senza finestra, per verificare la pipeline core.</item>
/// </list>
/// Nessuna di queste modalità apre rete (ADR-0006): usano solo modelli già installati e verificati.
/// </summary>
internal static class HeadlessCommands
{
    private const string DiagnoseFlag = "--diagnose";
    private const string TranscribeFlag = "--headless-transcribe";

    public static bool IsHeadless(string[] args) =>
        args.Any(a => a is DiagnoseFlag or TranscribeFlag);

    /// <summary>Esegue la modalità richiesta e restituisce il codice di uscita.</summary>
    public static async Task<int> RunAsync(string[] args)
    {
        AttachToParentConsole();
        try
        {
            return args.Contains(DiagnoseFlag)
                ? await DiagnoseAsync(args)
                : await TranscribeAsync(args);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Errore: {ex.Message}");
            return 1;
        }
    }

    /// <summary>Gli stessi motori dell'app (ADR-0009); la modalità senza GUI usa sempre la CPU.</summary>
    private static EngineRegistry Engines()
    {
        var sherpa = new SherpaEngine();
        return new EngineRegistry(new Dictionary<string, ITranscriptionEngine>
        {
            ["whisper"] = new WhisperEngine(),
            [SherpaEngine.Transducer] = sherpa,
            [SherpaEngine.Canary] = sherpa,
            [SherpaEngine.Cohere] = sherpa,
        });
    }

    private static async Task<int> DiagnoseAsync(string[] args)
    {
        var outDir = ValueOf(args, DiagnoseFlag) ?? throw new ArgumentException("Specificare la cartella di output dopo --diagnose.");
        var services = Create(args);

        var report = await DiagnosticReporter.RunAsync(
            services.Paths,
            services.Models,
            AppVersion(),
            WebView2Version(),
            services.Settings.HistoryDirectory ?? TranscriptHistory.DefaultDirectory,
            logLines: 200);

        var file = Path.Combine(outDir, "diagnostic-report.json");
        await DiagnosticReporter.WriteAsync(report, file);

        foreach (var check in report.Checks)
            Console.WriteLine($"[{(check.Ok ? "OK" : "KO")}] {check.Name}: {check.Detail}");
        Console.WriteLine($"Report: {file}");
        return report.Passed ? 0 : 2;
    }

    private static async Task<int> TranscribeAsync(string[] args)
    {
        var audioPath = ValueOf(args, TranscribeFlag) ?? throw new ArgumentException("Specificare il file audio dopo --headless-transcribe.");
        var outDir = ValueOf(args, "--out") ?? throw new ArgumentException("Specificare la cartella di output con --out.");
        var modelId = ValueOf(args, "--model") ?? "small";
        var format = ParseFormat(ValueOf(args, "--format"));
        var language = ValueOf(args, "--language") ?? "auto";

        var services = Create(args);
        var audio = new AudioLibrary([new WavDecoder(), new MediaFoundationDecoder()], MauiProgram.MaxAudioDuration);
        using var transcription = new TranscriptionService(services.Models, audio, Engines(), new Logger<TranscriptionService>(services.Loggers));

        var handle = audio.Register(Path.GetFullPath(audioPath));
        var result = await transcription.RunToCompletionAsync(handle.AudioId, modelId, new TranscriptionOptions(language), CancellationToken.None);

        Directory.CreateDirectory(outDir);
        var output = Path.Combine(outDir, Path.GetFileNameWithoutExtension(audioPath) + TranscriptExporter.FileExtension(format));
        await TranscriptExporter.WriteAsync(result, format, output);

        // Solo metadati: il testo trascritto non va su console né nel log.
        Console.WriteLine($"Trascrizione completata: modello {result.Model.ModelId} ({result.Model.Runtime}), durata audio {result.AudioDuration}, tempo {result.Elapsed}");
        Console.WriteLine($"Output: {output}");
        return 0;
    }

    private static HeadlessServices Create(string[] args)
    {
        var root = AppPaths.DefaultRoot;
        var settingsStore = new SettingsStore(Path.Combine(root, "settings.json"));
        var settings = settingsStore.Load();
        var paths = new AppPaths(root, settings.ModelsDirectory);
        paths.EnsureCreated();

        var diagnostics = DiagnosticOptions.Resolve(args, Environment.GetEnvironmentVariable, settings.DiagnosticLogLevel);
        var loggers = new LoggerFactory([new FileLoggerProvider(paths.Logs, diagnostics.MinimumLevel)]);

        return new HeadlessServices(paths, settings, new ModelStore(paths, ModelCatalog.LoadEmbedded()), loggers);
    }

    private sealed record HeadlessServices(AppPaths Paths, AppSettings Settings, ModelStore Models, ILoggerFactory Loggers);

    private static ExportFormat ParseFormat(string? value) => value?.ToLowerInvariant() switch
    {
        null or "txt" => ExportFormat.Txt,
        "srt" => ExportFormat.Srt,
        "vtt" => ExportFormat.Vtt,
        _ => throw new ArgumentException($"Formato non supportato: {value}"),
    };

    private static string? ValueOf(string[] args, string flag)
    {
        var index = Array.IndexOf(args, flag);
        return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
    }

    private static string AppVersion() =>
        Assembly.GetEntryAssembly()?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "sconosciuta";

    private static string? WebView2Version()
    {
        try
        {
            return CoreWebView2Environment.GetAvailableBrowserVersionString();
        }
        catch (Exception)
        {
            return null;
        }
    }

    [DllImport("kernel32.dll")]
    private static extern bool AttachConsole(int processId);

    /// <summary>L'app è WinExe: senza questo, stdout non arriva al terminale da cui è stata lanciata.</summary>
    private static void AttachToParentConsole()
    {
        const int attachParentProcess = -1;
        if (AttachConsole(attachParentProcess))
        {
            Console.SetOut(new StreamWriter(Console.OpenStandardOutput()) { AutoFlush = true });
            Console.SetError(new StreamWriter(Console.OpenStandardError()) { AutoFlush = true });
        }
    }
}
