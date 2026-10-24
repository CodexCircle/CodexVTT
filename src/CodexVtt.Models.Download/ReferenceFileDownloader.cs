using System.Security.Cryptography;
using CodexVtt.Core.Benchmark;
using CodexVtt.Core.Errors;

namespace CodexVtt.Models.Download;

/// <summary>
/// Scarica il file di riferimento del Test bench (ADR-0006, terza eccezione). Solo l'indirizzo fissato in
/// <see cref="BenchmarkReference"/>, host in allowlist (archive.org e suoi nodi), redirect controllati uno a uno,
/// installazione solo se dimensione e SHA-256 coincidono. La richiesta non contiene dati dell'utente.
/// </summary>
public sealed class ReferenceFileDownloader : IReferenceFileProvider
{
    private const int MaxRedirects = 5;
    private readonly HttpClient _http;
    private readonly HostAllowlist _allowlist;
    private readonly string _directory;
    private readonly Uri _url;
    private readonly string _sha256;
    private readonly long _size;

    public ReferenceFileDownloader(HttpClient http, string directory, HostAllowlist? allowlist = null, Uri? url = null, string? sha256 = null, long? size = null)
    {
        _http = http;
        _directory = directory;
        _allowlist = allowlist ?? new HostAllowlist([BenchmarkReference.Host]);
        _url = url ?? new Uri(BenchmarkReference.Url);
        _sha256 = (sha256 ?? BenchmarkReference.Sha256).ToLowerInvariant();
        _size = size ?? BenchmarkReference.SizeBytes;
    }

    private string TargetPath => Path.Combine(_directory, BenchmarkReference.FileName);

    public string? CachedPath()
    {
        var path = TargetPath;
        try
        {
            if (!File.Exists(path) || new FileInfo(path).Length != _size)
                return null;
            using var stream = File.OpenRead(path);
            return Convert.ToHexStringLower(SHA256.HashData(stream)) == _sha256 ? path : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    public async Task<string> EnsureAsync(CancellationToken ct)
    {
        if (CachedPath() is { } cached)
            return cached;

        Directory.CreateDirectory(_directory);
        var partial = TargetPath + ".part";
        try
        {
            using var response = await SendFollowingRedirectsAsync(ct).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
                throw new CodexVttException(ErrorCodes.DownloadFailed, $"Download non riuscito ({(int)response.StatusCode}).");

            using var hasher = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            long total = 0;
            await using (var source = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false))
            await using (var target = new FileStream(partial, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 16, useAsync: true))
            {
                var buffer = new byte[1 << 16];
                int read;
                while ((read = await source.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0)
                {
                    total += read;
                    if (total > _size)
                        break; // più grande del previsto: non è il file fissato
                    hasher.AppendData(buffer, 0, read);
                    await target.WriteAsync(buffer.AsMemory(0, read), ct).ConfigureAwait(false);
                }
            }

            if (total != _size || Convert.ToHexStringLower(hasher.GetHashAndReset()) != _sha256)
                throw new CodexVttException(ErrorCodes.ModelHashMismatch, "Il file scaricato non corrisponde a quello atteso: scartato.");

            File.Move(partial, TargetPath, overwrite: true);
            return TargetPath;
        }
        catch (HttpRequestException ex)
        {
            throw new CodexVttException(ErrorCodes.DownloadFailed, "Download non riuscito.", inner: ex);
        }
        finally
        {
            try
            {
                if (File.Exists(partial)) File.Delete(partial);
            }
            catch (IOException)
            {
                // Resto di un tentativo fallito: verrà sovrascritto al prossimo.
            }
        }
    }

    private async Task<HttpResponseMessage> SendFollowingRedirectsAsync(CancellationToken ct)
    {
        var current = _url;
        for (var hop = 0; hop <= MaxRedirects; hop++)
        {
            _allowlist.EnsureAllowed(current);
            using var request = new HttpRequestMessage(HttpMethod.Get, current);
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
}
