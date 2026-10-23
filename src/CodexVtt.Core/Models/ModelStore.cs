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
/// Gestione locale dei modelli: stato, verifica SHA-256, import da file e rimozione (ADR-0003, ADR-0009).
/// Un modello è un file (Whisper) oppure una cartella <c>models\&lt;id&gt;</c> con più file (Parakeet, Canary),
/// installata in modo atomico solo quando tutti i file sono verificati.
/// Nessun accesso alla rete: il download vive in CodexVtt.Models.Download.
/// </summary>
public sealed class ModelStore
{
    private const string MarkerSuffix = ".verified.json";
    private const string FolderMarker = ".verified.json";
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

    /// <summary>File del modello, oppure la sua cartella per i modelli a più file.</summary>
    public string PathFor(ModelDescriptor model) => Path.Combine(_paths.Models, model.IsMultiFile ? model.Id : model.File);

    /// <summary>Dove il downloader tiene i file in arrivo: cartella per modello, così non si mescolano.</summary>
    public string PartialFolderFor(ModelDescriptor model) =>
        model.IsMultiFile ? Path.Combine(_paths.PartialModels, model.Id) : _paths.PartialModels;

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
                return new InstalledModelStatus(model, transient, path, SizeOnDisk(model, path));
        }

        return model.IsMultiFile ? FolderStatus(model, path) : FileStatus(model, path);
    }

    private static InstalledModelStatus FileStatus(ModelDescriptor model, string path)
    {
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

    private static InstalledModelStatus FolderStatus(ModelDescriptor model, string folder)
    {
        if (!Directory.Exists(folder))
            return new InstalledModelStatus(model, ModelState.NotInstalled, folder, null);

        var files = model.Files!;
        var infos = files.Select(f => new FileInfo(Path.Combine(folder, f.Path))).ToList();
        var size = infos.Where(i => i.Exists).Sum(i => i.Length);
        // Un'installazione interrotta non lascia cartelle a metà (rinomina atomica): se manca qualcosa, va rifatta.
        if (infos.Any(i => !i.Exists) || files.Zip(infos).Any(p => p.Second.Length != p.First.SizeBytes))
            return new InstalledModelStatus(model, ModelState.Corrupted, folder, size);

        var marker = ReadFolderMarker(folder);
        if (marker is null)
            return new InstalledModelStatus(model, ModelState.Unverified, folder, size);

        var state = ModelState.Ready;
        foreach (var (file, info) in files.Zip(infos))
        {
            var entry = marker.FirstOrDefault(m => string.Equals(m.Path, file.Path, StringComparison.Ordinal));
            if (entry is null)
            {
                state = ModelState.Unverified;
            }
            else if (!string.Equals(entry.Sha256, file.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                return new InstalledModelStatus(model, ModelState.Corrupted, folder, size);
            }
            else if (entry.SizeBytes != info.Length || entry.LastWriteUtc != info.LastWriteTimeUtc)
            {
                state = ModelState.Unverified;
            }
        }

        return new InstalledModelStatus(model, state, folder, size);
    }

    private static long? SizeOnDisk(ModelDescriptor model, string path)
    {
        if (!model.IsMultiFile)
            return File.Exists(path) ? new FileInfo(path).Length : null;
        return Directory.Exists(path) ? Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories).Sum(f => new FileInfo(f).Length) : null;
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
        if (model.IsMultiFile ? !Directory.Exists(path) : !File.Exists(path))
            throw new CodexVttException(ErrorCodes.ModelNotInstalled, $"Il modello {model.Id} non è installato.");
        if (!model.IsPinned)
            throw new CodexVttException(ErrorCodes.ModelNotVerified, $"Il catalogo non contiene l'hash del modello {model.Id}.");

        using (BeginTransient(model, ModelState.Verifying))
        {
            if (model.IsMultiFile)
                await VerifyFolderAsync(model, path, progress, ct).ConfigureAwait(false);
            else
                await VerifyFileAsync(model, path, progress, ct).ConfigureAwait(false);
        }

        return Status(model);
    }

    private static async Task VerifyFileAsync(ModelDescriptor model, string path, IProgress<double>? progress, CancellationToken ct)
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

    private static async Task VerifyFolderAsync(ModelDescriptor model, string folder, IProgress<double>? progress, CancellationToken ct)
    {
        var entries = new List<FileMarker>();
        string? mismatch = null;
        long done = 0;
        foreach (var file in model.Files!)
        {
            var full = Path.Combine(folder, file.Path);
            if (!File.Exists(full))
            {
                throw new CodexVttException(ErrorCodes.ModelHashMismatch, $"Nel modello {model.Id} manca il file {file.Path}.",
                    new Dictionary<string, string> { ["file"] = file.Path });
            }

            var before = done;
            var part = progress is null ? null : new Progress<double>(p => progress.Report((before + (p * file.SizeBytes)) / model.SizeBytes));
            var hash = await ComputeSha256Async(full, part, ct).ConfigureAwait(false);
            done += file.SizeBytes;
            var info = new FileInfo(full);
            entries.Add(new FileMarker(file.Path, hash, info.Length, info.LastWriteTimeUtc));
            if (!string.Equals(hash, file.Sha256, StringComparison.OrdinalIgnoreCase))
                mismatch ??= file.Path;
        }

        // Anche con un file sbagliato si scrive il marker: lo stato diventa «danneggiato» e non si riverifica ogni volta.
        await WriteFolderMarkerAsync(folder, entries, ct).ConfigureAwait(false);
        if (mismatch is not null)
        {
            throw new CodexVttException(ErrorCodes.ModelHashMismatch, $"Hash del modello {model.Id} non valido.",
                new Dictionary<string, string> { ["file"] = mismatch });
        }
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

    /// <summary>
    /// Installa tutti i file di un modello a più file, già scaricati e verificati: li sposta in una cartella
    /// provvisoria e la rinomina in un colpo solo, così non esiste mai un modello a metà.
    /// </summary>
    /// <param name="verifiedTempPaths">Percorso relativo del file nel modello → file scaricato e verificato.</param>
    public async Task InstallVerifiedFolderAsync(ModelDescriptor model, IReadOnlyDictionary<string, string> verifiedTempPaths, CancellationToken ct = default)
    {
        var target = PathFor(model);
        var staging = target + ".installing";
        if (Directory.Exists(staging))
            Directory.Delete(staging, recursive: true);
        Directory.CreateDirectory(staging);

        var entries = new List<FileMarker>();
        foreach (var file in model.Files!)
        {
            var destination = Path.Combine(staging, file.Path);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Move(verifiedTempPaths[file.Path], destination, overwrite: true);
            var info = new FileInfo(destination);
            entries.Add(new FileMarker(file.Path, file.Sha256, info.Length, info.LastWriteTimeUtc));
        }

        await WriteFolderMarkerAsync(staging, entries, ct).ConfigureAwait(false);
        if (Directory.Exists(target))
            Directory.Delete(target, recursive: true);
        Directory.Move(staging, target);
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
        if (model.IsMultiFile)
        {
            if (Directory.Exists(path))
                Directory.Delete(path, recursive: true);
            return;
        }

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

    private static List<FileMarker>? ReadFolderMarker(string folder)
    {
        var markerPath = Path.Combine(folder, FolderMarker);
        if (!File.Exists(markerPath))
            return null;
        try
        {
            return JsonSerializer.Deserialize<List<FileMarker>>(File.ReadAllText(markerPath));
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

    private static Task WriteFolderMarkerAsync(string folder, List<FileMarker> entries, CancellationToken ct) =>
        File.WriteAllTextAsync(Path.Combine(folder, FolderMarker), JsonSerializer.Serialize(entries), ct);

    private static void DeleteMarker(string modelPath)
    {
        if (File.Exists(modelPath + MarkerSuffix))
            File.Delete(modelPath + MarkerSuffix);
    }

    private sealed record VerificationMarker(string Sha256, long SizeBytes, DateTime LastWriteUtc);

    private sealed record FileMarker(string Path, string Sha256, long SizeBytes, DateTime LastWriteUtc);

    private sealed class Release(Action action) : IDisposable
    {
        private Action? _action = action;

        public void Dispose() => Interlocked.Exchange(ref _action, null)?.Invoke();
    }
}
