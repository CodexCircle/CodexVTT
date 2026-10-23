using Microsoft.Extensions.Logging;
using CodexVtt.Bridge;
using CodexVtt.Core.Errors;
using Velopack;
using Velopack.Sources;

namespace CodexVtt.Updates;

/// <summary>
/// Aggiornamenti dell'app con Velopack (ADR-0005, ADR-0008). Legge le release del repository del progetto
/// su GitHub (pacchetti completi e delta, con controllo SHA-256 fatto da Velopack) e riavvia l'app per installarle.
/// Una build non installata (sviluppo, server) risulta <see cref="UpdateState.Unsupported"/> e non fa richieste.
/// </summary>
public sealed partial class VelopackUpdateService : IUpdateService
{
    /// <summary>Variabile d'ambiente per provare gli aggiornamenti senza GitHub: cartella o indirizzo di un feed Velopack.</summary>
    public const string SourceVariable = "CODEXVTT_UPDATE_SOURCE";

    private readonly UpdateManager? _manager;
    private readonly ILogger<VelopackUpdateService> _logger;
    private readonly string _releasesUrl;
    private readonly Lock _gate = new();
    private UpdateInfo? _pending;
    private UpdateStatus _current;

    public VelopackUpdateService(string repositoryUrl, ILogger<VelopackUpdateService> logger, IUpdateSource? source = null, Velopack.Locators.IVelopackLocator? locator = null)
    {
        _logger = logger;
        _releasesUrl = repositoryUrl.TrimEnd('/') + "/releases";
        var overrideSource = Environment.GetEnvironmentVariable(SourceVariable);
        // Le versioni di anteprima (1.0.0-beta.1) ricevono anche le anteprime; le stabili solo le stabili.
        var prerelease = (typeof(VelopackUpdateService).Assembly
            .GetCustomAttributes(typeof(System.Reflection.AssemblyInformationalVersionAttribute), false)
            .OfType<System.Reflection.AssemblyInformationalVersionAttribute>().FirstOrDefault()?.InformationalVersion ?? "").Split('+')[0].Contains('-');
        source ??= string.IsNullOrWhiteSpace(overrideSource)
            ? new GithubSource(repositoryUrl, accessToken: null, prerelease)
            : Directory.Exists(overrideSource) ? new SimpleFileSource(new DirectoryInfo(overrideSource)) : new SimpleWebSource(overrideSource);
        try
        {
            _manager = new UpdateManager(source, locator: locator);
        }
        catch (InvalidOperationException)
        {
            // Nessun VelopackLocator (VelopackApp.Run() non eseguito): come una build non installata.
            _manager = null;
        }

        _current = new UpdateStatus(
            _manager?.IsInstalled == true ? UpdateState.UpToDate : UpdateState.Unsupported,
            _manager?.CurrentVersion?.ToString() ?? "0.0.0",
            ReleasesUrl: _releasesUrl);
    }

    public UpdateStatus Current
    {
        get
        {
            lock (_gate)
                return _current;
        }
    }

    public async Task<UpdateStatus> CheckAsync(CancellationToken ct)
    {
        if (_manager is not { IsInstalled: true } manager)
            return Current;

        UpdateInfo? info;
        try
        {
            info = await manager.CheckForUpdatesAsync().WaitAsync(ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogCheckFailed(ex);
            throw new CodexVttException(ErrorCodes.UpdateFailed, "Impossibile controllare gli aggiornamenti.", inner: ex);
        }

        lock (_gate)
        {
            _pending = info;
            var version = manager.CurrentVersion?.ToString() ?? "0.0.0";
            _current = info is null
                ? new UpdateStatus(UpdateState.UpToDate, version, ReleasesUrl: _releasesUrl)
                : new UpdateStatus(UpdateState.Available, version, info.TargetFullRelease.Version.ToString(), info.TargetFullRelease.NotesMarkdown, _releasesUrl);
            return _current;
        }
    }

    public async Task ApplyAsync(IProgress<int> progress, CancellationToken ct)
    {
        var manager = _manager ?? throw new CodexVttException(ErrorCodes.UpdateUnavailable, "Aggiornamento non disponibile in questa versione.");
        UpdateInfo info;
        lock (_gate)
        {
            info = _pending ?? throw new CodexVttException(ErrorCodes.UpdateUnavailable, "Controlla prima gli aggiornamenti.");
            _current = _current with { State = UpdateState.Downloading };
        }

        try
        {
            await manager.DownloadUpdatesAsync(info, progress.Report, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            lock (_gate)
                _current = _current with { State = UpdateState.Available };
            LogDownloadFailed(ex);
            throw new CodexVttException(ErrorCodes.UpdateFailed, "Download dell'aggiornamento non riuscito.", inner: ex);
        }

        // Riavvia l'app: i file della nuova versione sostituiscono quelli attuali, i dati stanno altrove (AppPaths).
        manager.ApplyUpdatesAndRestart(info.TargetFullRelease);
    }

    // Nei log solo l'esito: niente indirizzi con token né percorsi (ADR-0006).
    [LoggerMessage(Level = LogLevel.Warning, Message = "Controllo aggiornamenti non riuscito")]
    private partial void LogCheckFailed(Exception ex);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Download dell'aggiornamento non riuscito")]
    private partial void LogDownloadFailed(Exception ex);
}
