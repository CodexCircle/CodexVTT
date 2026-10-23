using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Options;
using CodexVtt.Audio.FFmpeg;
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
using CodexVtt.Server;
using CodexVtt.Transcription.Sherpa;
using CodexVtt.Transcription.Whisper;

var builder = WebApplication.CreateBuilder(new WebApplicationOptions
{
    Args = args,
    // Come servizio di Windows la cartella corrente è System32: wwwroot e appsettings.json stanno accanto all'eseguibile.
    ContentRootPath = AppContext.BaseDirectory,
});
builder.Services.AddWindowsService(o => o.ServiceName = "CodexVTT");
builder.Services.Configure<ServerOptions>(builder.Configuration.GetSection(ServerOptions.Section));
var options = builder.Configuration.GetSection(ServerOptions.Section).Get<ServerOptions>() ?? new ServerOptions();

var dataRoot = string.IsNullOrWhiteSpace(options.DataRoot) ? AppPaths.DefaultRoot : options.DataRoot;
var settingsStore = new SettingsStore(Path.Combine(dataRoot, "settings.json"));
var paths = new AppPaths(dataRoot, settingsStore.Load().ModelsDirectory);
paths.EnsureCreated();
paths.CleanTemp();
CleanUploads(paths);

var diagnostics = DiagnosticOptions.Resolve(args, Environment.GetEnvironmentVariable, settingsStore.Load().DiagnosticLogLevel);
builder.Logging.AddProvider(new FileLoggerProvider(paths.Logs, diagnostics.MinimumLevel));

builder.WebHost.ConfigureKestrel(k => k.Limits.MaxRequestBodySize = options.MaxUploadBytes);
builder.Services.Configure<FormOptions>(f => f.MultipartBodyLengthLimit = options.MaxUploadBytes);

var services = builder.Services;
services.AddSingleton(paths);
services.AddSingleton(settingsStore);
services.AddSingleton(diagnostics);
services.AddSingleton(_ => ModelCatalog.LoadEmbedded());
services.AddSingleton<ModelStore>();
services.AddSingleton<IHardwareProbe, HardwareProbe>();

// Download dei modelli: resta l'unico accesso del core a Internet (ADR-0006).
services.AddSingleton(HostAllowlist.ModelHosts);
services.AddSingleton<IModelDownloader>(sp => new ModelDownloader(
    ModelDownloader.CreateHttpClient(),
    sp.GetRequiredService<ModelStore>(),
    sp.GetRequiredService<HostAllowlist>(),
    sp.GetRequiredService<ILogger<ModelDownloader>>()));

// Decodifica: Media Foundation su Windows (ADR-0004); su Linux (Docker) ffmpeg/ffprobe del sistema (ADR-0007).
// Il WAV si legge sempre con il decoder gestito, quindi il server parte anche senza ffmpeg (solo WAV).
services.AddSingleton(sp =>
{
    var decoders = new List<IAudioDecoder> { new WavDecoder() };
    if (OperatingSystem.IsWindows())
        decoders.Add(new MediaFoundationDecoder());
    else if (FfmpegDecoder.IsAvailable())
        decoders.Add(new FfmpegDecoder());
    else
        sp.GetRequiredService<ILoggerFactory>().CreateLogger("CodexVtt.Server").LogFfmpegMissing();
    return new AudioLibrary(decoders, ServerDefaults.MaxAudioDuration);
});
// Motori (ADR-0009): Whisper (con GPU se possibile) e sherpa-onnx per Parakeet, Canary e Cohere.
var sherpa = new SherpaEngine();
services.AddSingleton<ITranscriptionEngine>(_ => new EngineRegistry(new Dictionary<string, ITranscriptionEngine>
{
    ["whisper"] = new WhisperEngine(settingsStore.Load().UseGpu),
    [SherpaEngine.Transducer] = sherpa,
    [SherpaEngine.Canary] = sherpa,
    [SherpaEngine.Cohere] = sherpa,
}));
services.AddSingleton(sp => new TranscriptionService(
    sp.GetRequiredService<ModelStore>(),
    sp.GetRequiredService<AudioLibrary>(),
    sp.GetRequiredService<ITranscriptionEngine>(),
    sp.GetRequiredService<ILogger<TranscriptionService>>()));
services.AddSingleton(new HistoryLocation(
    string.IsNullOrWhiteSpace(options.HistoryDirectory) ? TranscriptHistory.DefaultDirectory : options.HistoryDirectory,
    Path.Combine(dataRoot, "history-recovery")));

services.AddSingleton<ClientHub>();
services.AddSingleton<IShellServices, ServerShellServices>();
services.AddSingleton<BridgeDispatcher>();

var app = builder.Build();

app.Use(async (context, next) =>
{
    var headers = context.Response.Headers;
    headers.ContentSecurityPolicy = ServerDefaults.ContentSecurityPolicy;
    headers.XContentTypeOptions = "nosniff";
    headers["Referrer-Policy"] = "no-referrer";
    await next(context).ConfigureAwait(false);
});
// UseWebSockets prima del controllo d'accesso: AccessGate deve poter riconoscere le richieste WebSocket per verificarne l'origine.
app.UseWebSockets(new WebSocketOptions { KeepAliveInterval = TimeSpan.FromSeconds(30) });
app.UseMiddleware<AccessGate>();
app.UseDefaultFiles();
app.UseStaticFiles(new StaticFileOptions
{
    // index.html va sempre riletto: dopo un aggiornamento punta ai nuovi file con hash.
    OnPrepareResponse = c =>
    {
        if (c.File.Name.Equals("index.html", StringComparison.OrdinalIgnoreCase))
            c.Context.Response.Headers.CacheControl = "no-cache";
    },
});
app.MapBridge();
// Controllo di stato per Docker: nessun dato, risponde anche senza token (vedi AccessGate).
app.MapGet(AccessGate.HealthPath, () => Results.Text("ok"));
if (!File.Exists(Path.Combine(app.Environment.WebRootPath ?? "", "index.html")))
{
    app.MapGet("/", () => Results.Text(
        "UI non compilata: eseguire 'npm ci && npm run build:server' nella cartella ui/ e riavviare il server.",
        "text/plain; charset=utf-8"));
}

// Il dispatcher si crea subito: la coda deve esistere anche prima che una pagina si colleghi.
app.Services.GetRequiredService<BridgeDispatcher>();
var localOnly = string.IsNullOrEmpty(options.AccessToken);
app.Logger.LogServerStarted(localOnly);
app.Run();

/// <summary>I file caricati servono solo finché il processo li tiene registrati: si ripuliscono a ogni avvio.</summary>
static void CleanUploads(AppPaths paths)
{
    var uploads = BridgeEndpoints.UploadsFolder(paths);
    if (!Directory.Exists(uploads))
        return;
    foreach (var folder in Directory.EnumerateDirectories(uploads))
    {
        try
        {
            Directory.Delete(folder, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // File ancora in uso: verrà rimosso al prossimo avvio.
        }
    }
}

namespace CodexVtt.Server
{
    internal static class ServerDefaults
    {
        /// <summary>Durata massima dell'audio, come nell'app (specifica #2, REQ-012).</summary>
        public static readonly TimeSpan MaxAudioDuration = TimeSpan.FromHours(4);

        /// <summary>La CSP dell'app (ui/src/security/csp.ts) senza lo script di HybridWebView; connect-src 'self' copre il WebSocket.</summary>
        public const string ContentSecurityPolicy =
            "default-src 'self'; script-src 'self'; style-src 'self'; img-src 'self' data:; font-src 'self'; " +
            "connect-src 'self'; object-src 'none'; base-uri 'self'; form-action 'none'; frame-src 'none'; frame-ancestors 'none'";
    }

    internal static partial class ServerLog
    {
        [LoggerMessage(Level = LogLevel.Information, Message = "Server CodexVTT avviato (solo localhost: {LocalOnly})")]
        public static partial void LogServerStarted(this ILogger logger, bool localOnly);

        [LoggerMessage(Level = LogLevel.Warning, Message = "ffmpeg non trovato: sono accettati solo file WAV")]
        public static partial void LogFfmpegMissing(this ILogger logger);
    }
}
