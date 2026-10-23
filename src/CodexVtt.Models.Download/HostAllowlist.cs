using CodexVtt.Core.Errors;

namespace CodexVtt.Models.Download;

/// <summary>
/// Host ammessi per le uniche richieste di rete di CodexVTT (ADR-0006). Un host è ammesso se
/// coincide con una voce o ne è un sottodominio. Solo HTTPS, salvo loopback esplicitamente
/// abilitato per i test.
/// </summary>
public sealed class HostAllowlist
{
    private readonly IReadOnlyList<string> _domains;
    private readonly bool _allowLoopbackHttp;

    public HostAllowlist(IEnumerable<string> domains, bool allowLoopbackHttp = false)
    {
        _domains = domains.Select(d => d.Trim().TrimStart('.').ToLowerInvariant()).Where(d => d.Length > 0).ToList();
        _allowLoopbackHttp = allowLoopbackHttp;
    }

    /// <summary>Hugging Face e i suoi CDN (redirect verso cdn-lfs*.huggingface.co e *.hf.co).</summary>
    public static HostAllowlist ModelHosts { get; } = new(["huggingface.co", "hf.co"]);

    public bool IsAllowed(Uri uri)
    {
        if (uri.IsLoopback && _allowLoopbackHttp)
            return true;
        if (uri.Scheme != Uri.UriSchemeHttps)
            return false;
        var host = uri.IdnHost.ToLowerInvariant();
        return _domains.Any(d => host == d || host.EndsWith("." + d, StringComparison.Ordinal));
    }

    public void EnsureAllowed(Uri uri)
    {
        if (!IsAllowed(uri))
        {
            throw new CodexVttException(ErrorCodes.DownloadHostNotAllowed, $"Host non ammesso: {uri.Host}",
                new Dictionary<string, string> { ["host"] = uri.Host });
        }
    }
}
