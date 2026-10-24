using System.Collections.Concurrent;
using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using CodexVtt.Core;
using CodexVtt.Core.Audio;
using CodexVtt.Core.Benchmark;
using CodexVtt.Core.Errors;
using CodexVtt.Core.Export;
using CodexVtt.Core.Hardware;
using CodexVtt.Core.History;
using CodexVtt.Core.Models;
using CodexVtt.Core.Settings;
using CodexVtt.Core.Transcription;

namespace CodexVtt.Bridge;

/// <summary>
/// Riceve i comandi JSON dalla UI, li esegue sui servizi del core e restituisce la risposta.
/// Gli aggiornamenti asincroni (progresso, completamento) arrivano alla UI come eventi.
/// </summary>
public sealed partial class BridgeDispatcher : IDisposable
{
    private readonly AppPaths _paths;
    private readonly SettingsStore _settings;
    private readonly ModelStore _models;
    private readonly IModelDownloader _downloader;
    private readonly IHardwareProbe _hardware;
    private readonly AudioLibrary _audio;
    private readonly TranscriptionService _transcription;
    private readonly IShellServices _shell;
    private readonly HistoryLocation _historyLocation;
    private readonly IUpdateService? _updates;
    private readonly IReferenceFileProvider? _referenceFile;
    private readonly ILogger<BridgeDispatcher> _logger;
    private readonly ConcurrentDictionary<string, CancellationTokenSource> _downloads = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Func<JsonElement?, CancellationToken, Task<object?>>> _handlers;

    public BridgeDispatcher(
        AppPaths paths,
        SettingsStore settings,
        ModelStore models,
        IModelDownloader downloader,
        IHardwareProbe hardware,
        AudioLibrary audio,
        TranscriptionService transcription,
        IShellServices shell,
        HistoryLocation historyLocation,
        ILogger<BridgeDispatcher> logger,
        IUpdateService? updates = null,
        CodexVtt.Core.Benchmark.IReferenceFileProvider? referenceFile = null)
    {
        _paths = paths;
        _settings = settings;
        _models = models;
        _downloader = downloader;
        _hardware = hardware;
        _audio = audio;
        _transcription = transcription;
        _shell = shell;
        _historyLocation = historyLocation;
        _logger = logger;
        _updates = updates;
        _referenceFile = referenceFile;

        InitBenchmark();
        _transcription.Progress += OnTranscriptionProgress;
        _transcription.Finished += OnTranscriptionFinished;

        _handlers = new(StringComparer.Ordinal)
        {
            ["system.info"] = (_, _) => Task.FromResult<object?>(SystemInfo()),
            ["models.list"] = (_, _) => Task.FromResult<object?>(ListModels()),
            ["models.recommend"] = (_, _) => Task.FromResult<object?>(Recommend()),
            ["models.download"] = (p, _) => Task.FromResult<object?>(StartDownload(Read<ModelRef>(p).ModelId)),
            ["models.cancelDownload"] = (p, _) => Task.FromResult<object?>(CancelDownload(Read<ModelRef>(p).ModelId)),
            ["models.verify"] = async (p, ct) => ToDto(await _models.VerifyAsync(RequireModel(Read<ModelRef>(p).ModelId), null, ct).ConfigureAwait(false)),
            ["models.import"] = ImportModelAsync,
            ["models.remove"] = (p, _) => Task.FromResult<object?>(RemoveModel(Read<ModelRef>(p).ModelId)),
            ["audio.pick"] = PickAudioAsync,
            ["audio.pickMany"] = PickAudioManyAsync,
            ["transcription.start"] = (p, _) => Task.FromResult<object?>(StartTranscription(Read<StartTranscriptionRequest>(p))),
            ["transcription.cancel"] = (p, _) => Task.FromResult<object?>(CancelTranscription(Read<JobRef>(p).JobId)),
            ["transcription.pause"] = (p, _) => Task.FromResult<object?>(PauseTranscription(Read<JobRef>(p).JobId)),
            ["transcription.resume"] = (p, _) => Task.FromResult<object?>(ResumeTranscription(Read<JobRef>(p).JobId)),
            ["transcription.get"] = (p, _) => Task.FromResult<object?>(GetTranscription(Read<JobRef>(p).JobId)),
            ["updates.status"] = (_, _) => Task.FromResult<object?>(UpdateStatusNow()),
            ["updates.check"] = async (_, ct) => await CheckUpdatesAsync(ct).ConfigureAwait(false),
            ["updates.apply"] = (_, ct) => Task.FromResult<object?>(ApplyUpdate(ct)),
            ["transcription.active"] = (_, _) => Task.FromResult<object?>(ActiveTranscription()),
            ["queue.get"] = (_, _) => Task.FromResult<object?>(QueueSnapshot()),
            ["queue.add"] = (p, _) => Task.FromResult<object?>(QueueAdd(Read<QueueAddRequest>(p))),
            ["queue.remove"] = (p, _) => Task.FromResult<object?>(QueueRemove(Read<QueueItemRef>(p).Key)),
            ["queue.clear"] = (_, _) => Task.FromResult<object?>(QueueClear()),
            ["queue.start"] = (p, _) => Task.FromResult<object?>(QueueStart(p is { ValueKind: JsonValueKind.Object } ? Read<QueueStartRequest>(p) : new QueueStartRequest(null, null))),
            ["queue.stop"] = (_, _) => Task.FromResult<object?>(QueueStop()),
            ["benchmark.get"] = (_, _) => Task.FromResult<object?>(BenchmarkSnapshot()),
            ["benchmark.start"] = BenchmarkStartAsync,
            ["benchmark.reference"] = (_, _) => Task.FromResult<object?>(BenchmarkReferenceInfo()),
            ["benchmark.cancel"] = (_, _) => Task.FromResult<object?>(BenchmarkCancel()),
            ["benchmark.estimate"] = (p, _) => Task.FromResult<object?>(BenchmarkEstimate(Read<BenchmarkEstimateRequest>(p))),
            ["export.save"] = ExportAsync,
            ["settings.get"] = (_, _) => Task.FromResult<object?>(_settings.Load()),
            ["settings.set"] = (p, _) => Task.FromResult<object?>(SaveSettings(Read<AppSettings>(p))),
            ["history.list"] = (_, _) => Task.FromResult<object?>(ListHistory()),
            ["history.get"] = (p, _) => Task.FromResult<object?>(History().Load(Read<HistoryRef>(p).Id)),
            ["history.delete"] = (p, _) => Task.FromResult<object?>(DeleteHistory(Read<HistoryRef>(p).Id)),
            ["history.openFolder"] = (_, _) => Task.FromResult<object?>(OpenHistoryFolder()),
            ["diagnostics.openLogs"] = (_, _) => Task.FromResult<object?>(OpenLogsFolder()),
            ["history.pickFolder"] = PickHistoryFolderAsync,
            ["history.testFolder"] = (p, _) => Task.FromResult<object?>(FolderProbe.Test(Read<FolderRef>(p).Directory)),
            ["history.setFolder"] = (p, _) => Task.FromResult<object?>(SetHistoryFolder(Read<FolderRef>(p).Directory)),
            ["history.update"] = (p, _) => Task.FromResult<object?>(UpdateHistory(p)),
            ["collections.save"] = (p, _) => Task.FromResult<object?>(SaveCollection(Read<CollectionSaveRequest>(p))),
            ["collections.delete"] = (p, _) => Task.FromResult<object?>(DeleteCollection(Read<HistoryRef>(p).Id)),
            ["collections.reorder"] = (p, _) => Task.FromResult<object?>(ReorderCollection(Read<CollectionReorderRequest>(p))),
        };
    }

    public IReadOnlyCollection<string> Commands => _handlers.Keys;

    /// <summary>Gestisce un messaggio della UI e restituisce la risposta serializzata.</summary>
    public async Task<string> HandleAsync(string json, CancellationToken ct = default)
    {
        BridgeRequest? request = null;
        var started = Stopwatch.GetTimestamp();
        try
        {
            request = JsonSerializer.Deserialize<BridgeRequest>(json, BridgeProtocol.JsonOptions);
            if (request is null || string.IsNullOrEmpty(request.Id) || string.IsNullOrEmpty(request.Type))
                throw new CodexVttException(ErrorCodes.InvalidRequest, "Richiesta non valida.");
            if (request.V != BridgeProtocol.Version)
                throw new CodexVttException(ErrorCodes.InvalidRequest, $"Versione del protocollo non supportata: {request.V}");
            if (!_handlers.TryGetValue(request.Type, out var handler))
                throw new CodexVttException(ErrorCodes.InvalidRequest, $"Comando sconosciuto: {request.Type}");

            LogCommandReceived(request.Type);
            var payload = await handler(request.Payload, ct).ConfigureAwait(false);
#pragma warning disable CA1873 // Solo una sottrazione di timestamp: più economica del controllo del livello.
            LogCommandCompleted(request.Type, Stopwatch.GetElapsedTime(started).TotalMilliseconds, "ok");
#pragma warning restore CA1873
            return Serialize(new BridgeResponse(BridgeProtocol.Version, request.Id, "result", true, payload, null));
        }
        catch (Exception ex)
        {
            var error = ToError(ex);
            if (error.Code == ErrorCodes.Internal)
                LogUnexpected(ex, request?.Type ?? "?");
            else
                LogRequestFailed(request?.Type ?? "?", error.Code);
#pragma warning disable CA1873
            LogCommandCompleted(request?.Type ?? "?", Stopwatch.GetElapsedTime(started).TotalMilliseconds, error.Code);
#pragma warning restore CA1873
            return Serialize(new BridgeResponse(BridgeProtocol.Version, request?.Id ?? "", "result", false, null, error));
        }
    }

    public void Dispose()
    {
        _transcription.Progress -= OnTranscriptionProgress;
        _transcription.Finished -= OnTranscriptionFinished;
        foreach (var cts in _downloads.Values)
            cts.Cancel();
    }

    public static BridgeError ToError(Exception ex) => ex switch
    {
        CodexVttException s => new BridgeError(s.Code, s.Message, s.Details.Count > 0 ? s.Details : null),
        OperationCanceledException => new BridgeError(ErrorCodes.Cancelled, "Operazione annullata."),
        JsonException => new BridgeError(ErrorCodes.InvalidRequest, "Payload non valido."),
        _ => new BridgeError(ErrorCodes.Internal, "Errore interno. I dettagli sono nel log locale."),
    };

    private UpdateStatus UpdateStatusNow() =>
        _updates?.Current ?? new UpdateStatus(UpdateState.Unsupported, AppVersion());

    private async Task<object?> CheckUpdatesAsync(CancellationToken ct)
    {
        if (_updates is null)
            return UpdateStatusNow();
        var status = await _updates.CheckAsync(ct).ConfigureAwait(false);
        Emit("updates.changed", status);
        return status;
    }

    /// <summary>Scarica e installa in background: l'app si riavvia da sola alla fine, quindi la risposta è subito.</summary>
    private object ApplyUpdate(CancellationToken ct)
    {
        var updates = _updates ?? throw new CodexVttException(ErrorCodes.UpdateUnavailable, "Aggiornamento non disponibile in questa versione.");
        if (updates.Current.State != UpdateState.Available)
            throw new CodexVttException(ErrorCodes.UpdateUnavailable, "Nessun aggiornamento da installare: controlla prima gli aggiornamenti.");
        var progress = new SyncProgress<int>(p => Emit("updates.progress", new { Percent = p }));
        _ = Task.Run(async () =>
        {
            try
            {
                await updates.ApplyAsync(progress, CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Emit("updates.failed", ToError(ex));
                Emit("updates.changed", updates.Current);
            }
        }, CancellationToken.None);
        return new { Started = true };
    }

    private static string AppVersion() =>
        typeof(BridgeDispatcher).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0] ?? "0.0.0";

    private object SystemInfo()
    {
        var hardware = _hardware.Probe(_paths.Models).WithGpuAcceleration(_settings.Load().UseGpu);
        return new
        {
            AppVersion = AppVersion(),
            ProtocolVersion = BridgeProtocol.Version,
            Processing = "local",
            _shell.Host,
            Hardware = hardware,
            hardware.SupportsStandardRuntime,
            SupportedAudioExtensions = new[] { ".wav", ".mp3", ".m4a", ".aac", ".wma", ".flac", ".ogg", ".opus" },
        };
    }

    private List<ModelDto> ListModels()
    {
        var hardware = _hardware.Probe(_paths.Models).WithGpuAcceleration(_settings.Load().UseGpu);
        var maxCost = ModelSelector.MaxCost(hardware, _models.Catalog.Selection);
        return _models.List().Select(s => ToDto(s, ModelSelector.Assess(
            s.Model, hardware, _models.Catalog.Selection, maxCost, s.State is not ModelState.NotInstalled))).ToList();
    }

    private object Recommend()
    {
        var hardware = _hardware.Probe(_paths.Models).WithGpuAcceleration(_settings.Load().UseGpu);
        var recommendation = ModelSelector.Recommend(hardware, _models.Catalog, _models.InstalledIds());
        return new
        {
            RecommendedId = recommendation.Recommended?.Id,
            recommendation.Reasons,
            Hardware = hardware,
        };
    }

    private object StartDownload(string modelId)
    {
        var model = RequireModel(modelId);
        if (_models.Catalog.DownloadUrl(model) is null)
            throw new CodexVttException(ErrorCodes.ModelNotVerified, $"Download non disponibile per {model.Id}: catalogo senza hash fissato.");

        var cts = new CancellationTokenSource();
        if (!_downloads.TryAdd(model.Id, cts))
        {
            cts.Dispose();
            throw new CodexVttException(ErrorCodes.DownloadInProgress, $"Download di {model.Id} già in corso.");
        }

        var progress = new SyncProgress<DownloadProgress>(p => Emit("models.downloadProgress", p));
        _ = Task.Run(async () =>
        {
            try
            {
                var status = await _downloader.DownloadAsync(model.Id, progress, cts.Token).ConfigureAwait(false);
                Emit("models.downloadCompleted", ToDto(status));
            }
            catch (Exception ex)
            {
                Emit("models.downloadFailed", new { ModelId = model.Id, Error = ToError(ex) });
            }
            finally
            {
                _downloads.TryRemove(model.Id, out _);
                cts.Dispose();
                Emit("models.changed", null);
            }
        });
        Emit("models.changed", null);
        return new { Started = true };
    }

    private object CancelDownload(string modelId)
    {
        if (_downloads.TryGetValue(modelId, out var cts))
            cts.Cancel();
        return new { Cancelled = true };
    }

    private async Task<object?> ImportModelAsync(JsonElement? payload, CancellationToken ct)
    {
        var path = await _shell.PickModelFileAsync(ct).ConfigureAwait(false);
        if (path is null)
            return null;
        var status = await _models.ImportAsync(path, null, ct).ConfigureAwait(false);
        Emit("models.changed", null);
        return ToDto(status);
    }

    private object RemoveModel(string modelId)
    {
        _models.Remove(modelId);
        Emit("models.changed", null);
        return new { Removed = true };
    }

    private async Task<object?> PickAudioAsync(JsonElement? payload, CancellationToken ct)
    {
        var path = await _shell.PickAudioFileAsync(ct).ConfigureAwait(false);
        if (path is null)
            return null;
        var handle = _audio.Register(path);
        return new { handle.AudioId, handle.Info };
    }

    /// <summary>
    /// Scelta di più file per il batch. I file illeggibili non fermano gli altri: finiscono in "Failed"
    /// con il loro errore, così la UI li mostra in coda come non riusciti.
    /// </summary>
    private async Task<object?> PickAudioManyAsync(JsonElement? payload, CancellationToken ct)
    {
        var paths = await _shell.PickAudioFilesAsync(ct).ConfigureAwait(false);
        return paths.Count == 0 ? null : RegisterAudioFiles(paths);
    }

    /// <summary>Registra file già presenti sul disco del core (dialog nativo o caricamento dal browser).</summary>
    public PickedBatch RegisterAudioFiles(IReadOnlyList<string> paths)
    {
        var picked = new List<PickedAudio>();
        var failed = new List<FailedAudio>();
        foreach (var path in paths)
        {
            try
            {
                var handle = _audio.Register(path);
                picked.Add(new PickedAudio(handle.AudioId, handle.Info));
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                failed.Add(new FailedAudio(Path.GetFileName(path), ToError(ex)));
            }
        }

        return new PickedBatch(picked, failed);
    }

    private object StartTranscription(StartTranscriptionRequest request)
    {
        if (_benchmark.Running)
            throw new CodexVttException(ErrorCodes.TranscriptionBusy, "È in corso il test bench dei modelli.");
        var modelId = request.ModelId ?? _settings.Load().SelectedModelId
            ?? throw new CodexVttException(ErrorCodes.ModelNotInstalled, "Nessun modello selezionato.");
        // V1: nessuna traduzione (issue #17), solo trascrizione nella lingua dell'audio.
        var job = _transcription.Start(request.AudioId, modelId,
            new TranscriptionOptions(request.Language ?? _settings.Load().Language, Translate: false, Threads: ThreadLimit()));
        return new { job.JobId, ModelId = modelId };
    }

    private object CancelTranscription(string jobId)
    {
        _transcription.Cancel(jobId);
        return new { Cancelling = true };
    }

    private object PauseTranscription(string jobId)
    {
        _transcription.Pause(jobId);
        return new { Pausing = true };
    }

    private object ResumeTranscription(string jobId)
    {
        _transcription.Resume(jobId);
        return new { Resumed = true };
    }

    /// <summary>Thread dei motori secondo il limite di CPU scelto (null = nessun limite).</summary>
    private int? ThreadLimit() => CpuLimit.Threads(_settings.Load().CpuLimitPercent, Environment.ProcessorCount);

    private object GetTranscription(string jobId)
    {
        var job = _transcription.Get(jobId);
        return new { job.JobId, job.Status, job.Result, Error = job.Error is null ? null : ToError(job.Error) };
    }

    private async Task<object?> ExportAsync(JsonElement? payload, CancellationToken ct)
    {
        var request = Read<ExportRequest>(payload);
        var result = ExportSource(request);
        var extension = TranscriptExporter.FileExtension(request.Format);
        var path = await _shell.PickSaveFileAsync(SuggestedExportName(result, request.Format), extension, ct).ConfigureAwait(false);
        if (path is null)
            return null;
        await WriteExportAsync(result, request, path, ct).ConfigureAwait(false);
        return new { Saved = true, FileName = Path.GetFileName(path) };
    }

    /// <summary>
    /// Export come file da scaricare, senza dialog "Salva con nome" (versione server): si scrive in temp,
    /// si legge e si cancella, così il contenuto è identico a quello dell'export su disco.
    /// </summary>
    public async Task<(string FileName, byte[] Content)> RenderExportAsync(ExportRequest request, CancellationToken ct)
    {
        var result = ExportSource(request);
        Directory.CreateDirectory(_paths.Temp);
        var path = Path.Combine(_paths.Temp, Guid.NewGuid().ToString("N") + TranscriptExporter.FileExtension(request.Format));
        try
        {
            await WriteExportAsync(result, request, path, ct).ConfigureAwait(false);
            return (SuggestedExportName(result, request.Format), await File.ReadAllBytesAsync(path, ct).ConfigureAwait(false));
        }
        finally
        {
            File.Delete(path);
        }
    }

    private TranscriptionResult ExportSource(ExportRequest request)
    {
        if (request.HistoryId is { } historyId)
            return History().Load(historyId);
        var job = _transcription.Get(request.JobId ?? throw new CodexVttException(ErrorCodes.InvalidRequest, "jobId o historyId richiesto."));
        return job.Result ?? throw new CodexVttException(ErrorCodes.TranscriptionNotFound, "La trascrizione non è completata.");
    }

    private static string SuggestedExportName(TranscriptionResult result, ExportFormat format) =>
        Path.GetFileNameWithoutExtension(result.AudioFileName) + TranscriptExporter.FileExtension(format);

    private async Task WriteExportAsync(TranscriptionResult result, ExportRequest request, string path, CancellationToken ct)
    {
        try
        {
            await TranscriptExporter.WriteAsync(result, request.Format, path,
                new ExportOptions(request.IncludeHeader ?? _settings.Load().ExportIncludeHeader, UiLanguage()), ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new CodexVttException(ErrorCodes.ExportFailed, "Impossibile salvare il file.", inner: ex);
        }
    }

    private AppSettings SaveSettings(AppSettings settings, bool trustTestedAt = false)
    {
        if (!trustTestedAt)
        {
            // Il «testata» lo decide solo il test: dal client resta quello già salvato, e sparisce se la cartella cambia.
            var current = _settings.Load();
            var same = string.Equals(current.HistoryDirectory, settings.HistoryDirectory, StringComparison.OrdinalIgnoreCase);
            settings = settings with { HistoryDirectoryTestedAt = same ? current.HistoryDirectoryTestedAt : null };
        }

        if (settings.SelectedModelId is { } id)
            RequireModel(id);
        if (!CpuLimit.IsValid(settings.CpuLimitPercent))
            throw new CodexVttException(ErrorCodes.InvalidRequest, $"Il limite di CPU deve essere tra {CpuLimit.MinPercent}% e {CpuLimit.MaxPercent}%.");
        if (settings.UiLanguage is not (null or "it" or "en"))
            throw new CodexVttException(ErrorCodes.InvalidRequest, "Lingua dell'interfaccia non supportata.");
        if (settings.Theme is not (null or "light" or "dark"))
            throw new CodexVttException(ErrorCodes.InvalidRequest, "Tema dell'interfaccia non supportato.");
        if (settings.Palette is not (null or "sabbia" or "natale" or "halloween" or "ciliegia"))
            throw new CodexVttException(ErrorCodes.InvalidRequest, "Tema di colore non supportato.");
        if (settings.Mascot is not (null or "tina" or "giulio"))
            throw new CodexVttException(ErrorCodes.InvalidRequest, "Mascotte non supportata.");
        if (settings.HistoryDirectory is { Length: > 0 } dir && !Path.IsPathFullyQualified(dir))
            throw new CodexVttException(ErrorCodes.InvalidRequest, "La cartella della cronologia deve essere un percorso assoluto.");
        _settings.Save(settings);
        return settings;
    }

    private TranscriptHistory History() => new(_historyLocation.Resolve(_settings.Load()));

    private string UiLanguage() => _settings.Load().UiLanguage ?? "it";

    private object ListHistory()
    {
        var history = History();
        var entries = history.List();
        var library = new HistoryLibrary(history.Directory).Read();
        var existing = entries.Select(e => e.Id).ToHashSet(StringComparer.Ordinal);
        return new
        {
            history.Directory,
            Entries = entries.Select(e => ToView(e, library)).ToList(),
            // Le raccolte mostrano solo le trascrizioni ancora presenti nella cartella.
            Collections = library.Collections.Select(c => c with { EntryIds = c.EntryIds.Where(existing.Contains).ToList() }).ToList(),
        };
    }

    private object DeleteHistory(string id)
    {
        var history = History();
        history.Delete(id);
        new HistoryLibrary(history.Directory).Forget(id);
        Emit("history.changed", null);
        return new { Deleted = true };
    }

    /// <summary>Cambia nome e/o raccolta: si aggiornano solo le proprietà presenti nel payload.</summary>
    private HistoryEntryView UpdateHistory(JsonElement? payload)
    {
        var request = Read<HistoryRef>(payload);
        var element = payload!.Value;
        var history = History();
        var entry = history.List().FirstOrDefault(e => e.Id == request.Id)
            ?? throw new CodexVttException(ErrorCodes.HistoryNotFound, "Trascrizione non trovata nella cronologia.");

        var library = new HistoryLibrary(history.Directory);
        if (TryGetProperty(element, "title", out var title))
            library.SetTitle(entry.Id, ReadOptionalString(title));
        if (TryGetProperty(element, "collectionId", out var collection))
            library.SetCollection(entry.Id, ReadOptionalString(collection));
        Emit("history.changed", null);
        return ToView(entry, library.Read());
    }

    private HistoryGroup SaveCollection(CollectionSaveRequest request)
    {
        var saved = Library().SaveCollection(request.Id, request.Name, request.Color);
        Emit("history.changed", null);
        return saved;
    }

    private object DeleteCollection(string id)
    {
        Library().DeleteCollection(id);
        Emit("history.changed", null);
        return new { Deleted = true };
    }

    private HistoryGroup ReorderCollection(CollectionReorderRequest request)
    {
        var saved = Library().Reorder(request.Id, request.EntryIds ?? []);
        Emit("history.changed", null);
        return saved;
    }

    private HistoryLibrary Library() => new(History().Directory);

    private static HistoryEntryView ToView(HistoryEntry e, HistoryLibrarySnapshot library) => new(
        e.Id, e.AudioFileName, e.CompletedAt, e.AudioDuration, e.ModelDisplayName, e.DetectedLanguage, e.Preview, e.TextFileName,
        library.Titles.GetValueOrDefault(e.Id), library.CollectionOf(e.Id));

    private static bool TryGetProperty(JsonElement element, string name, out JsonElement value)
    {
        foreach (var property in element.EnumerateObject())
        {
            if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                value = property.Value;
                return true;
            }
        }

        value = default;
        return false;
    }

    private static string? ReadOptionalString(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.Null => null,
        JsonValueKind.String => value.GetString(),
        _ => throw new CodexVttException(ErrorCodes.InvalidRequest, "Payload non valido."),
    };

    private object OpenLogsFolder()
    {
        Directory.CreateDirectory(_paths.Logs);
        _shell.OpenFolder(_paths.Logs);
        return new { Opened = true };
    }

    private object OpenHistoryFolder()
    {
        var directory = History().Directory;
        Directory.CreateDirectory(directory);
        _shell.OpenFolder(directory);
        return new { Opened = true };
    }

    /// <summary>Apre la selezione e restituisce la cartella scelta, senza salvarla: va prima testata (<c>history.testFolder</c>).</summary>
    private async Task<object?> PickHistoryFolderAsync(JsonElement? payload, CancellationToken ct)
    {
        var folder = await _shell.PickFolderAsync(ct).ConfigureAwait(false);
        return folder is null ? null : new FolderRef(folder);
    }

    /// <summary>Adotta la cartella solo se il test di lettura, scrittura e cancellazione riesce; altrimenti resta la precedente.</summary>
    private AppSettings SetHistoryFolder(string directory)
    {
        var test = FolderProbe.Test(directory);
        if (!test.Ok)
            throw new CodexVttException(ErrorCodes.FolderTestFailed, $"Cartella non utilizzabile: {test.Error}.",
                new Dictionary<string, string> { ["reason"] = test.Error ?? "" });
        var settings = SaveSettings(_settings.Load() with { HistoryDirectory = directory, HistoryDirectoryTestedAt = DateTimeOffset.UtcNow }, trustTestedAt: true);
        Emit("history.changed", null);
        return settings;
    }

    /// <summary>
    /// Con "Accesso alle cartelle controllato" di Defender la cartella della cronologia può risultare
    /// non scrivibile: la trascrizione si salva comunque nella cartella dati locale, così non si perde.
    /// </summary>
    private bool TryRecover(TranscriptionResult result)
    {
        if (_historyLocation.RecoveryDirectory is not { Length: > 0 } directory)
            return false;
        try
        {
            new TranscriptHistory(directory).Save(result, UiLanguage());
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            LogHistorySaveFailed(ex);
            return false;
        }
    }

    private void OnTranscriptionProgress(JobProgress progress)
    {
        if (_benchmark.Running)
            return; // i job del test bench non sono trascrizioni dell'utente
        _lastProgress = progress;
        Emit("transcription.progress", progress);
    }

    private void OnTranscriptionFinished(TranscriptionJob job)
    {
        if (_benchmark.Running)
            return; // il test bench non salva nulla in cronologia
        // I job della coda arrivano alla UI con queue.changed; gli eventi transcription.* restano per quelli singoli.
        var fromQueue = IsQueueJob(job.JobId);
        string? historyId = null;
        switch (job.Status)
        {
            case JobStatus.Completed:
                try
                {
                    historyId = History().Save(job.Result!, UiLanguage()).Id;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    LogHistorySaveFailed(ex);
                    // La cartella non vale più come testata: la spunta verde non deve restare su una cartella che non scrive.
                    if (_settings.Load() is { HistoryDirectoryTestedAt: not null } tested)
                        _settings.Save(tested with { HistoryDirectoryTestedAt = null });
                    var code = TryRecover(job.Result!) ? ErrorCodes.HistoryFolderBlocked : ErrorCodes.HistorySaveFailed;
                    Emit("history.saveFailed", new { job.JobId, Error = new BridgeError(code, "Impossibile salvare nella cronologia.") });
                }

                if (!fromQueue)
                    Emit("transcription.completed", new { job.JobId, job.Result, HistoryId = historyId });
                if (historyId is not null)
                    Emit("history.changed", null);
                break;
            case JobStatus.Cancelled:
                if (!fromQueue)
                    Emit("transcription.cancelled", new { job.JobId });
                break;
            default:
                // Un modello trovato danneggiato durante la verifica cambia stato: la scheda Modelli va aggiornata.
                if (job.Error?.Code == ErrorCodes.ModelHashMismatch)
                    Emit("models.changed", null);
                if (!fromQueue)
                    Emit("transcription.failed", new { job.JobId, Error = JobError(job) });
                break;
        }

        // Anche dopo un job singolo: la coda poteva essere in attesa che il core si liberasse.
        OnQueueJobFinished(job, historyId);
    }

    private static BridgeError JobError(TranscriptionJob job) =>
        ToError(job.Error ?? new CodexVttException(ErrorCodes.TranscriptionFailed, "Trascrizione non riuscita."));

    private void Emit(string name, object? payload) =>
        _shell.PostToUi(Serialize(new BridgeEvent(BridgeProtocol.Version, "event", name, payload)));

    private ModelDescriptor RequireModel(string modelId) =>
        _models.Catalog.Find(modelId) ?? throw new CodexVttException(ErrorCodes.ModelUnknown, $"Modello sconosciuto: {modelId}");

    private ModelDto ToDto(InstalledModelStatus status, ModelAssessment? assessment = null) => new(
        status.Model.Id,
        status.Model.DisplayName,
        status.Model.File,
        status.Model.Quantization,
        status.Model.SizeBytes,
        status.Model.RamRequiredBytes,
        status.Model.RelativeCost,
        status.Model.Multilingual,
        status.Model.License,
        _models.Catalog.DownloadUrl(status.Model) is not null,
        status.State,
        status.SizeOnDiskBytes,
        assessment?.Issues ?? [],
        status.Model.Engine,
        status.Model.Preview,
        status.Model.RequiresLanguage,
        status.Model.Attribution,
        status.Model.Accuracy is { } a ? new AccuracyDto(a.Language, a.Percent, a.Metric, a.Source, a.MeasuredByProject) : null);

    private static T Read<T>(JsonElement? payload)
    {
        if (payload is not { ValueKind: JsonValueKind.Object } element)
            throw new CodexVttException(ErrorCodes.InvalidRequest, "Payload mancante.");
        return element.Deserialize<T>(BridgeProtocol.JsonOptions)
            ?? throw new CodexVttException(ErrorCodes.InvalidRequest, "Payload non valido.");
    }

    private static string Serialize<T>(T value) => JsonSerializer.Serialize(value, BridgeProtocol.JsonOptions);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Salvataggio nella cronologia non riuscito")]
    private partial void LogHistorySaveFailed(Exception ex);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Comando {Command} rifiutato: {Code}")]
    private partial void LogRequestFailed(string command, string code);

    [LoggerMessage(Level = LogLevel.Error, Message = "Errore non gestito nel comando {Command}")]
    private partial void LogUnexpected(Exception ex, string command);

    // Solo nome del comando, durata ed esito: mai il payload (può contenere testo della trascrizione).
    [LoggerMessage(Level = LogLevel.Debug, Message = "Comando {Command} ricevuto")]
    private partial void LogCommandReceived(string command);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Comando {Command} terminato in {ElapsedMs:F1} ms: {Outcome}")]
    private partial void LogCommandCompleted(string command, double elapsedMs, string outcome);

    private sealed class SyncProgress<T>(Action<T> handler) : IProgress<T>
    {
        public void Report(T value) => handler(value);
    }
}

public sealed record ModelRef(string ModelId);

public sealed record JobRef(string JobId);

public sealed record StartTranscriptionRequest(string AudioId, string? ModelId, string? Language);

public sealed record ExportRequest(string? JobId, string? HistoryId, ExportFormat Format, bool? IncludeHeader);

public sealed record HistoryRef(string Id);

public sealed record FolderRef(string Directory);

public sealed record PickedAudio(string AudioId, AudioInfo Info);

public sealed record FailedAudio(string FileName, BridgeError Error);

/// <summary>Esito della scelta di più file: quelli registrati e quelli scartati con il loro errore.</summary>
public sealed record PickedBatch(IReadOnlyList<PickedAudio> Picked, IReadOnlyList<FailedAudio> Failed);

public sealed record CollectionSaveRequest(string? Id, string Name, string Color);

public sealed record CollectionReorderRequest(string Id, IReadOnlyList<string>? EntryIds);

/// <summary>Voce della cronologia con nome e raccolta scelti dall'utente.</summary>
public sealed record HistoryEntryView(
    string Id,
    string AudioFileName,
    DateTimeOffset CompletedAt,
    TimeSpan AudioDuration,
    string ModelDisplayName,
    string? DetectedLanguage,
    string Preview,
    string TextFileName,
    string? Title,
    string? CollectionId);

public sealed record ModelDto(
    string Id,
    string DisplayName,
    string File,
    string Quantization,
    long SizeBytes,
    long RamRequiredBytes,
    int RelativeCost,
    bool Multilingual,
    string License,
    bool Downloadable,
    ModelState State,
    long? SizeOnDiskBytes,
    IReadOnlyList<CompatibilityIssue> Issues,
    string Engine,
    bool Preview,
    bool RequiresLanguage,
    string? Attribution,
    AccuracyDto? Accuracy);

/// <summary>Affidabilità dichiarata (100 - WER) con la fonte; <c>MeasuredByProject</c> dice se l'ha misurata CodexVTT.</summary>
public sealed record AccuracyDto(string Language, double Percent, string Metric, string Source, bool MeasuredByProject);
