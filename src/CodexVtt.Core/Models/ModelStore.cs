using System.Security.Cryptography;
using System.Text.Json;
using CodexVtt.Core.Errors;

namespace CodexVtt.Core.Models;

public enum ModelState
{
    NotInstalled,
    Downloading,
    Verifying,
    Unverified,
    Ready,
    Corrupted,
}

public sealed record InstalledModelStatus(ModelDescriptor Model, ModelState State, string Path, long? SizeOnDiskBytes);

/// <summary>
/// Gestione locale dei modelli: stato, verifica SHA-256, import da file e rimozione (ADR-0003).
/// Nessun accesso alla rete: il download vive in CodexVtt.Models.Download.
/// </summary>
public sealed class ModelStore
{
    private const string MarkerSuffix = ".verified.json";
    private readonly AppPaths _paths;
    private readonly HashSet<string> _busy = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, ModelState> _transient = new(StringComparer.OrdinalIgnoreCase);
    private readonly Lock _gate = new();

    public ModelStore(AppPaths paths, ModelCatalog catalog)
    {
        _paths = paths;
        Catalog = catalog;
    }

    public ModelCatalog Catalog { get; }

    public string PathFor(ModelDescriptor model) => Path.Combine(_paths.Models, model.File);

    public IReadOnlyList<InstalledModelStatus> List() => Catalog.Models.Select(Status).ToList();

    public IReadOnlySet<string> InstalledIds() =>
        List().Where(s => s.State is not ModelState.NotInstalled and not ModelState.Downloading)
            .Select(s => s.Model.Id)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

    public InstalledModelStatus Status(ModelDescriptor model)
    {
        var path = PathFor(model);
        lock (_gate)
        {
            if (_transient.TryGetValue(model.Id, out var transient))
                return new InstalledModelStatus(model, transient, path, FileLength(path));
        }

        var file = new FileInfo(path);
        if (!file.Exists)
            return new InstalledModelStatus(model, ModelState.NotInstalled, path, null);

        var marker = ReadMarker(path);
        var state = marker switch
        {
            null => ModelState.Unverified,
            _ when !string.Equals(marker.Sha256, model.Sha256, StringComparison.OrdinalIgnoreCase) => ModelState.Corrupted,
            _ when marker.SizeBytes != file.Length || marker.LastWriteUtc != file.LastWriteTimeUtc => ModelState.Unverified,
            _ => ModelState.Ready,
        };
        return new InstalledModelStatus(model, state, path, file.Length);
    }

    /// <summary>Marca un modello come in corso di download/verifica (stato mostrato alla UI).</summary>
    public IDisposable BeginTransient(ModelDescriptor model, ModelState state)
    {
        lock (_gate)
        {
            if (!_busy.Add(model.Id))
                throw new CodexVttException(ErrorCodes.DownloadInProgress, $"Operazione già in corso sul modello {model.Id}.");
            _transient[model.Id] = state;
        }

        return new Release(() =>
        {
            lock (_gate)
            {
                _busy.Remove(model.Id);
                _transient.Remove(model.Id);
            }
        });
    }

    public void SetTransientState(ModelDescriptor model, ModelState state)
    {
        lock (_gate)
        {
            if (_busy.Contains(model.Id))
                _transient[model.Id] = state;
        }
    }

    /// <summary>Ricalcola l'hash completo e aggiorna il marker di verifica.</summary>
    public async Task<InstalledModelStatus> VerifyAsync(ModelDescriptor model, IProgress<double>? progress = null, CancellationToken ct = default)
    {
        var path = PathFor(model);
        if (!File.Exists(path))
            throw new CodexVttException(ErrorCodes.ModelNotInstalled, $"Il modello {model.Id} non è installato.");
        if (!model.IsPinned)
            throw new CodexVttException(ErrorCodes.ModelNotVerified, $"Il catalogo non contiene l'hash del modello {model.Id}.");

        using (BeginTransient(model, ModelState.Verifying))
        {
            var hash = await ComputeSha256Async(path, progress, ct).ConfigureAwait(false);
            if (!string.Equals(hash, model.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                DeleteMarker(path);
                await WriteMarkerAsync(path, hash, ct).ConfigureAwait(false);
                throw new CodexVttException(ErrorCodes.ModelHashMismatch, $"Hash del modello {model.Id} non valido.",
                    new Dictionary<string, string> { ["expected"] = model.Sha256!, ["actual"] = hash });
            }

            await WriteMarkerAsync(path, hash, ct).ConfigureAwait(false);
        }

        return Status(model);
    }

    /// <summary>
    /// Restituisce il percorso di un modello pronto all'uso, verificandolo se necessario.
    /// Un modello corrotto non viene mai usato.
    /// </summary>
    public async Task<string> RequireReadyAsync(string modelId, CancellationToken ct = default)
    {
        var model = Catalog.Find(modelId) ?? throw new CodexVttException(ErrorCodes.ModelUnknown, $"Modello sconosciuto: {modelId}");
        var status = Status(model);
        switch (status.State)
        {
            case ModelState.Ready:
                return status.Path;
            case ModelState.Unverified:
                await VerifyAsync(model, null, ct).ConfigureAwait(false);
                return status.Path;
            case ModelState.Corrupted:
                throw new CodexVttException(ErrorCodes.ModelHashMismatch, $"Il modello {model.Id} è corrotto: va riscaricato o reimportato.");
            case ModelState.Downloading or ModelState.Verifying:
                throw new CodexVttException(ErrorCodes.DownloadInProgress, $"Il modello {model.Id} non è ancora pronto.");
            default:
                throw new CodexVttException(ErrorCodes.ModelNotInstalled, $"Il modello {model.Id} non è installato.");
        }
    }

    /// <summary>Importa un file GGML già presente sul disco, accettato solo se l'hash è nel catalogo.</summary>
    public async Task<InstalledModelStatus> ImportAsync(string sourcePath, IProgress<double>? progress = null, CancellationToken ct = default)
    {
        if (!File.Exists(sourcePath))
            throw new CodexVttException(ErrorCodes.ModelNotInstalled, "File modello non trovato.");

        var hash = await ComputeSha256Async(sourcePath, progress, ct).ConfigureAwait(false);
        var model = Catalog.FindBySha256(hash)
            ?? throw new CodexVttException(ErrorCodes.ModelImportUnknown, "Il file non corrisponde a nessun modello del catalogo.",
                new Dictionary<string, string> { ["sha256"] = hash });

        var size = new FileInfo(sourcePath).Length;
        EnsureDiskSpace(size);

        using (BeginTransient(model, ModelState.Verifying))
        {
            var target = PathFor(model);
            var temp = Path.Combine(_paths.PartialModels, model.File + ".import");
            Directory.CreateDirectory(_paths.PartialModels);
            File.Copy(sourcePath, temp, overwrite: true);
            File.Move(temp, target, overwrite: true);
            await WriteMarkerAsync(target, hash, ct).ConfigureAwait(false);
        }

        return Status(model);
    }

    /// <summary>Installa un file scaricato e già verificato (spostamento atomico).</summary>
    public async Task InstallVerifiedAsync(ModelDescriptor model, string verifiedTempPath, string sha256, CancellationToken ct = default)
    {
        var target = PathFor(model);
        File.Move(verifiedTempPath, target, overwrite: true);
        await WriteMarkerAsync(target, sha256, ct).ConfigureAwait(false);
    }

    public void Remove(string modelId)
    {
        var model = Catalog.Find(modelId) ?? throw new CodexVttException(ErrorCodes.ModelUnknown, $"Modello sconosciuto: {modelId}");
        lock (_gate)
        {
            if (_busy.Contains(model.Id))
                throw new CodexVttException(ErrorCodes.DownloadInProgress, $"Operazione in corso sul modello {model.Id}.");
        }

        var path = PathFor(model);
        DeleteMarker(path);
        if (File.Exists(path))
            File.Delete(path);
    }

    public void EnsureDiskSpace(long requiredBytes)
    {
        var root = Path.GetPathRoot(_paths.Models);
        if (root is null)
            return;
        var free = new DriveInfo(root).AvailableFreeSpace;
        var needed = (long)(requiredBytes * (1 + Catalog.Selection.DiskHeadroomFraction));
        if (free < needed)
        {
            throw new CodexVttException(ErrorCodes.InsufficientDisk, "Spazio su disco insufficiente.",
                new Dictionary<string, string> { ["requiredBytes"] = needed.ToString(System.Globalization.CultureInfo.InvariantCulture), ["freeBytes"] = free.ToString(System.Globalization.CultureInfo.InvariantCulture) });
        }
    }

    public static async Task<string> ComputeSha256Async(string path, IProgress<double>? progress, CancellationToken ct)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 20, useAsync: true);
        using var hasher = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[1 << 20];
        long done = 0;
        int read;
        while ((read = await stream.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0)
        {
            hasher.AppendData(buffer, 0, read);
            done += read;
            progress?.Report(stream.Length == 0 ? 1 : (double)done / stream.Length);
        }

        return Convert.ToHexStringLower(hasher.GetHashAndReset());
    }

    private static long? FileLength(string path) => File.Exists(path) ? new FileInfo(path).Length : null;

    private static VerificationMarker? ReadMarker(string modelPath)
    {
        var markerPath = modelPath + MarkerSuffix;
        if (!File.Exists(markerPath))
            return null;
        try
        {
            return JsonSerializer.Deserialize<VerificationMarker>(File.ReadAllText(markerPath));
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static async Task WriteMarkerAsync(string modelPath, string sha256, CancellationToken ct)
    {
        var info = new FileInfo(modelPath);
        var marker = new VerificationMarker(sha256, info.Length, info.LastWriteTimeUtc);
        await File.WriteAllTextAsync(modelPath + MarkerSuffix, JsonSerializer.Serialize(marker), ct).ConfigureAwait(false);
    }

    private static void DeleteMarker(string modelPath)
    {
        if (File.Exists(modelPath + MarkerSuffix))
            File.Delete(modelPath + MarkerSuffix);
    }

    private sealed record VerificationMarker(string Sha256, long SizeBytes, DateTime LastWriteUtc);

    private sealed class Release(Action action) : IDisposable
    {
        private Action? _action = action;

        public void Dispose() => Interlocked.Exchange(ref _action, null)?.Invoke();
    }
}
