using Microsoft.Extensions.Logging;
using CodexVtt.Audio.Windows;
using CodexVtt.Bridge;
using CodexVtt.Core;
using CodexVtt.Core.Audio;
using CodexVtt.Core.Hardware;
using CodexVtt.Core.History;
using CodexVtt.Core.Logging;
using CodexVtt.Core.Models;
using CodexVtt.Core.Settings;
using CodexVtt.Core.Transcription;
using CodexVtt.Models.Download;
using CodexVtt.Transcription.Whisper;

namespace CodexVtt.App;

public static class MauiProgram
{
    /// <summary>Durata massima dell'audio in V1 (specifica #2, REQ-012).</summary>
    public static readonly TimeSpan MaxAudioDuration = TimeSpan.FromHours(4);

    public static MauiApp CreateMauiApp()
    {
        var settingsStore = new SettingsStore(Path.Combine(AppPaths.DefaultRoot, "settings.json"));
        var paths = new AppPaths(AppPaths.DefaultRoot, settingsStore.Load().ModelsDirectory);
        paths.EnsureCreated();
        paths.CleanTemp();

        // Livello del log: argomenti (--debug), poi CODEXVTT_LOG_LEVEL / CODEXVTT_DEBUG, poi settings.json (issue #38).
        var diagnostics = DiagnosticOptions.Resolve(Environment.GetCommandLineArgs(), Environment.GetEnvironmentVariable, settingsStore.Load().DiagnosticLogLevel);
        var fileLogs = new FileLoggerProvider(paths.Logs, diagnostics.MinimumLevel);

        var builder = MauiApp.CreateBuilder();
        builder.UseMauiApp<App>();

        builder.Logging.ClearProviders();
        builder.Logging.AddProvider(fileLogs);
#if DEBUG
        builder.Logging.AddDebug();
#endif

        var services = builder.Services;
        services.AddSingleton(paths);
        services.AddSingleton(settingsStore);
        services.AddSingleton(diagnostics);
        services.AddSingleton(_ => ModelCatalog.LoadEmbedded());
        services.AddSingleton<ModelStore>();
        services.AddSingleton<IHardwareProbe, HardwareProbe>();

        // Unico punto di accesso alla rete (ADR-0006).
        services.AddSingleton(HostAllowlist.ModelHosts);
        services.AddSingleton<IModelDownloader>(sp => new ModelDownloader(
            ModelDownloader.CreateHttpClient(),
            sp.GetRequiredService<ModelStore>(),
            sp.GetRequiredService<HostAllowlist>(),
            sp.GetRequiredService<ILogger<ModelDownloader>>()));

        services.AddSingleton(_ => new AudioLibrary([new WavDecoder(), new MediaFoundationDecoder()], MaxAudioDuration));
        services.AddSingleton<ITranscriptionEngine, WhisperEngine>();
        services.AddSingleton(sp => new TranscriptionService(
            sp.GetRequiredService<ModelStore>(),
            sp.GetRequiredService<AudioLibrary>(),
            sp.GetRequiredService<ITranscriptionEngine>(),
            sp.GetRequiredService<ILogger<TranscriptionService>>()));

        // Cronologia in una cartella visibile: Documenti\CodexVTT\history, modificabile nelle impostazioni.
        services.AddSingleton(new HistoryLocation(TranscriptHistory.DefaultDirectory, Path.Combine(AppPaths.DefaultRoot, "history-recovery")));

        services.AddSingleton<WindowsShellServices>();
        services.AddSingleton<IShellServices>(sp => sp.GetRequiredService<WindowsShellServices>());
        // Aggiornamenti da GitHub (ADR-0008): solo su richiesta o, se l'utente lo attiva, all'avvio.
        services.AddSingleton<IUpdateService>(sp => new CodexVtt.Updates.VelopackUpdateService(
            ProjectInfo.RepositoryUrl, sp.GetRequiredService<ILogger<CodexVtt.Updates.VelopackUpdateService>>()));
        services.AddSingleton<BridgeDispatcher>();
        services.AddSingleton<MainPage>();

        WebViewSecurity.Configure(diagnostics, new LoggerFactory([fileLogs]).CreateLogger("CodexVtt.WebView"));
        return builder.Build();
    }
}
