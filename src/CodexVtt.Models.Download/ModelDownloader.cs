using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using Microsoft.Extensions.Logging;
using CodexVtt.Core.Errors;
using CodexVtt.Core.Models;

namespace CodexVtt.Models.Download;

/// <summary>
/// Unico componente di CodexVTT che usa la rete per i modelli (ADR-0003, ADR-0006, ADR-0009).
/// Scarica solo URL del catalogo, segue i redirect manualmente controllando ogni host contro
/// l'allowlist, riprende i download interrotti e installa ogni file solo se l'hash SHA-256 coincide.
/// Un modello a più file viene installato solo quando tutti i file sono scaricati e verificati.
/// Le richieste non contengono dati dell'utente né delle trascrizioni.
/// </summary>
public sealed partial class ModelDownloader : IModelDownloader
{
    private const int MaxRedirects = 5;
    private readonly HttpClient _http;
    private readonly ModelStore _store;
    private readonly HostAllowlist _allowlist;
    private readonly ILogger<ModelDownloader> _logger;

    public ModelDownloader(HttpClient http, ModelStore store, HostAllowlist allowlist, ILogger<ModelDownloader> logger)
    {
        _http = http;
        _store = store;
        _allowlist = allowlist;
        _logger = logger;
    }

    /// <summary>HttpClient senza redirect automatici, senza cookie e senza credenziali.</summary>
    public static HttpClient CreateHttpClient() => new(new SocketsHttpHandler
    {
        AllowAutoRedirect = false,
        UseCookies = false,
        UseProxy = true,
        Credentials = null,
        AutomaticDecompression = DecompressionMethods.None,
        ConnectTimeout = TimeSpan.FromSeconds(30),
    })
    {
        Timeout = Timeout.InfiniteTimeSpan,
        DefaultRequestHeaders = { UserAgent = { new ProductInfoHeaderValue("CodexVTT", "1.0") } },
    };

    public async Task<InstalledModelStatus> DownloadAsync(string modelId, IProgress<DownloadProgress>? progress, CancellationToken ct)
    {
        var model = _store.Catalog.Find(modelId)
            ?? throw new CodexVttException(ErrorCodes.ModelUnknown, $"Modello sconosciuto: {modelId}");
        var files = _store.Catalog.DownloadUrls(model)
            ?? throw new CodexVttException(ErrorCodes.ModelNotVerified,
                $"Il catalogo non ha revisione e hash fissati per {model.Id}: download disabilitato.");

        using (_store.BeginTransient(model, ModelState.Downloading))
        {
            await DownloadAndInstallAsync(model, files, progress, ct).ConfigureAwait(false);
        }

        return _store.Status(model);
    }

    private async Task DownloadAndInstallAsync(
        ModelDescriptor model,
        IReadOnlyList<(ModelFile File, Uri Url)> files,
        IProgress<DownloadProgress>? progress,
        CancellationToken ct)
    {
        var folder = _store.PartialFolderFor(model);
        Directory.CreateDirectory(folder);

        // Nome del file parziale: i sottopercorsi del modello diventano parte del nome, così restano nella stessa cartella.
        string PartialOf(ModelFile file) => Path.Combine(folder, file.Path.Replace('/', '_').Replace('\\', '_') + ".part");

        long alreadyThere = 0;
        foreach (var (file, _) in files)
        {
            var partial = PartialOf(file);
            if (!File.Exists(partial))
                continue;
            var length = new FileInfo(partial).Length;
            if (length > file.SizeBytes)
                File.Delete(partial);
            else
                alreadyThere += length;
        }

        _store.EnsureDiskSpace(model.SizeBytes - alreadyThere);

        try
        {
            var verified = new Dictionary<string, string>();
            long finishedBytes = 0;
            foreach (var (file, url) in files)
            {
                var partial = PartialOf(file);
                var existing = File.Exists(partial) ? new FileInfo(partial).Length : 0;
                LogStarting(model.Id, url.Host, existing);

                var hash = await DownloadToFileAsync(model, file, url, partial, existing, finishedBytes, progress, ct).ConfigureAwait(false);
                if (!string.Equals(hash, file.Sha256, StringComparison.OrdinalIgnoreCase))
                {
                    File.Delete(partial);
                    throw new CodexVttException(ErrorCodes.ModelHashMismatch, $"Il file scaricato per {model.Id} non corrisponde all'hash atteso.",
                        new Dictionary<string, string> { ["expected"] = file.Sha256, ["actual"] = hash, ["file"] = file.Path });
                }

                verified[file.Path] = partial;
                finishedBytes += file.SizeBytes;
            }

            _store.SetTransientState(model, ModelState.Verifying);
            if (model.IsMultiFile)
            {
                await _store.InstallVerifiedFolderAsync(model, verified, ct).ConfigureAwait(false);
                // Cartella dei parziali ormai vuota.
                if (Directory.Exists(folder) && !Directory.EnumerateFileSystemEntries(folder).Any())
                    Directory.Delete(folder);
            }
            else
            {
                var only = files[0].File;
                await _store.InstallVerifiedAsync(model, verified[only.Path], only.Sha256, ct).ConfigureAwait(false);
            }

            LogCompleted(model.Id);
        }
        catch (HttpRequestException ex)
        {
            throw new CodexVttException(ErrorCodes.DownloadFailed, "Download del modello non riuscito. Il download può essere ripreso.", inner: ex);
        }
        catch (IOException ex) when (ex is not FileNotFoundException)
        {
            throw new CodexVttException(ErrorCodes.DownloadFailed, "Errore di scrittura durante il download.", inner: ex);
        }
    }

    /// <param name="finishedBytes">Byte dei file precedenti, già completi: il progresso è su tutto il modello.</param>
    private async Task<string> DownloadToFileAsync(
        ModelDescriptor model,
        ModelFile file,
        Uri url,
        string partial,
        long existing,
        long finishedBytes,
        IProgress<DownloadProgress>? progress,
        CancellationToken ct)
    {
        using var hasher = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        if (existing > 0)
            await HashExistingAsync(partial, hasher, ct).ConfigureAwait(false);

        using var response = await SendFollowingRedirectsAsync(url, existing, ct).ConfigureAwait(false);
        var resumed = existing > 0 && response.StatusCode == HttpStatusCode.PartialContent;
        if (!resumed && existing > 0)
        {
            // Il server non supporta la ripresa: si riparte da zero.
            existing = 0;
            hasher.GetHashAndReset();
        }

        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"Risposta HTTP {(int)response.StatusCode}", null, response.StatusCode);

        var host = response.RequestMessage?.RequestUri?.Host ?? url.Host;

        await using var source = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        await using var target = new FileStream(partial, resumed ? FileMode.Append : FileMode.Create, FileAccess.Write, FileShare.None, 1 << 20, useAsync: true);
        var buffer = new byte[1 << 20];
        var received = existing;
        var lastReport = 0L;
        int read;
        while ((read = await source.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0)
        {
            await target.WriteAsync(buffer.AsMemory(0, read), ct).ConfigureAwait(false);
            hasher.AppendData(buffer, 0, read);
            received += read;
            if (received - lastReport >= 4 << 20 || received == file.SizeBytes)
            {
                lastReport = received;
                progress?.Report(new DownloadProgress(model.Id, finishedBytes + received, model.SizeBytes, host));
            }
        }

        progress?.Report(new DownloadProgress(model.Id, finishedBytes + received, model.SizeBytes, host));
        return Convert.ToHexStringLower(hasher.GetHashAndReset());
    }

    private async Task<HttpResponseMessage> SendFollowingRedirectsAsync(Uri url, long rangeStart, CancellationToken ct)
    {
        var current = url;
        for (var hop = 0; hop <= MaxRedirects; hop++)
        {
            _allowlist.EnsureAllowed(current);
            using var request = new HttpRequestMessage(HttpMethod.Get, current);
            if (rangeStart > 0)
                request.Headers.Range = new RangeHeaderValue(rangeStart, null);

            var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
            if ((int)response.StatusCode is >= 300 and < 400 && response.Headers.Location is { } location)
            {
                current = location.IsAbsoluteUri ? location : new Uri(current, location);
                response.Dispose();
                continue;
            }

            return response;
        }

        throw new HttpRequestException("Troppi redirect.");
    }

    private static async Task HashExistingAsync(string path, IncrementalHash hasher, CancellationToken ct)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 20, useAsync: true);
        var buffer = new byte[1 << 20];
        int read;
        while ((read = await stream.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0)
            hasher.AppendData(buffer, 0, read);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Download modello {ModelId} da {Host} (ripresa da {Offset} byte)")]
    private partial void LogStarting(string modelId, string host, long offset);

    [LoggerMessage(Level = LogLevel.Information, Message = "Modello {ModelId} scaricato e verificato")]
    private partial void LogCompleted(string modelId);
}
