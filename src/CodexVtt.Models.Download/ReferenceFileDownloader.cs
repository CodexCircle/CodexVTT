using System.Security.Cryptography;
using CodexVtt.Core.Benchmark;
using CodexVtt.Core.Errors;

namespace CodexVtt.Models.Download;

/// <summary>
/// Scarica i file di riferimento del Test bench (ADR-0006, terza eccezione). Solo gli indirizzi fissati in
/// <see cref="BenchmarkClips"/>, host in allowlist (archive.org e suoi nodi), redirect controllati uno a uno,
/// installazione solo se dimensione e SHA-256 coincidono. La richiesta non contiene dati dell'utente.
/// </summary>
public sealed class ReferenceFileDownloader : IReferenceFileProvider
{
    private const int MaxRedirects = 5;
    private readonly HttpClient _http;
    private readonly HostAllowlist _allowlist;
    private readonly string _directory;

    public ReferenceFileDownloader(HttpClient http, string directory, HostAllowlist? allowlist = null)
    {
        _http = http;
        _directory = directory;
        _allowlist = allowlist ?? new HostAllowlist([BenchmarkReference.Host]);
    }

    private string TargetPath(BenchmarkClip clip) => Path.Combine(_directory, clip.FileName);

    public string? CachedPath(BenchmarkClip clip)
    {
        var path = TargetPath(clip);
        try
        {
            if (!File.Exists(path) || new FileInfo(path).Length != clip.SizeBytes)
                return null;
            using var stream = File.OpenRead(path);
            return string.Equals(Convert.ToHexStringLower(SHA256.HashData(stream)), clip.Sha256, StringComparison.OrdinalIgnoreCase) ? path : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    public async Task<string> EnsureAsync(BenchmarkClip clip, CancellationToken ct)
    {
        if (CachedPath(clip) is { } cached)
            return cached;

        Directory.CreateDirectory(_directory);
        var target = TargetPath(clip);
        var partial = target + ".part";
        try
        {
            using var response = await SendFollowingRedirectsAsync(new Uri(clip.Url), ct).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
                throw new CodexVttException(ErrorCodes.DownloadFailed, $"Download non riuscito ({(int)response.StatusCode}).");

            using var hasher = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            long total = 0;
            await using (var source = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false))
            await using (var output = new FileStream(partial, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 16, useAsync: true))
            {
                var buffer = new byte[1 << 16];
                int read;
                while ((read = await source.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0)
                {
                    total += read;
                    if (total > clip.SizeBytes)
                        break; // più grande del previsto: non è il file fissato
                    hasher.AppendData(buffer, 0, read);
                    await output.WriteAsync(buffer.AsMemory(0, read), ct).ConfigureAwait(false);
                }
            }

            if (total != clip.SizeBytes || !string.Equals(Convert.ToHexStringLower(hasher.GetHashAndReset()), clip.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new CodexVttException(ErrorCodes.ModelHashMismatch, "Il file scaricato non corrisponde a quello atteso: scartato.");

            File.Move(partial, target, overwrite: true);
            return target;
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

    private async Task<HttpResponseMessage> SendFollowingRedirectsAsync(Uri url, CancellationToken ct)
    {
        var current = url;
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
