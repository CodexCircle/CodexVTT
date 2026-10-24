using Microsoft.Extensions.Logging;
using CodexVtt.App.Shell;
using CodexVtt.Core.Audio;
using CodexVtt.Core.Logging;

namespace CodexVtt.App;

/// <summary>
/// Isolamento della WebView (ADR-0006): solo l'origine locale di HybridWebView, nessuna
/// navigazione o richiesta verso l'esterno, nessuna nuova finestra, DevTools disattivati in Release
/// (a meno di modalità debug esplicita, issue #38).
/// </summary>
public static class WebViewSecurity
{
    /// <summary>Origine locale con cui HybridWebView serve gli asset su Windows.</summary>
    public const string LocalOrigin = "https://0.0.0.1";

    public static bool IsLocal(string? uri) =>
        uri is not null &&
        (uri.StartsWith(LocalOrigin + "/", StringComparison.OrdinalIgnoreCase) ||
         string.Equals(uri, LocalOrigin, StringComparison.OrdinalIgnoreCase) ||
         uri.StartsWith("data:", StringComparison.OrdinalIgnoreCase) ||
         uri.StartsWith("about:blank", StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Serve <c>/audio/ID</c> (l'audio originale di una trascrizione) dall'origine locale: il file è risolto solo dal
    /// bridge a partire dall'ID in cronologia, mai da un percorso scelto dalla UI. Supporta Range (206) perché
    /// l'elemento audio di Chromium cerca con richieste a intervalli. Tutto il resto resta com'è (ADR-0006).
    /// </summary>
    /// <returns>null se l'indirizzo non è <c>/audio/ID</c> dell'origine locale: la richiesta prosegue come prima.</returns>
    public static AudioResponse? TryServeAudio(Uri uri, string method, string? rangeHeader, Func<string, (string Path, string ContentType)?> open)
    {
        if (!IsLocal(uri.GetLeftPart(UriPartial.Authority) + "/") || !uri.AbsolutePath.StartsWith("/audio/", StringComparison.Ordinal))
            return null;

        var id = Uri.UnescapeDataString(uri.AbsolutePath["/audio/".Length..]);
        return id.Length == 0 || id.Contains('/', StringComparison.Ordinal) || open(id) is not { } audio
            ? AudioResponse.NotFound
            : AudioResponse.Create(audio.Path, audio.ContentType, method, rangeHeader);
    }

    /// <param name="options">Opzioni diagnostiche: con <see cref="DiagnosticOptions.DebugMode"/> attivo si collega il log della WebView.</param>
    /// <param name="logger">Logger della WebView; usato solo in modalità debug.</param>
    public static void Configure(DiagnosticOptions options, ILogger logger)
    {
#if WINDOWS
        var diagnostics = options.DebugMode ? new WebViewDiagnostics(logger) : null;

        Microsoft.Maui.Handlers.HybridWebViewHandler.Mapper.AppendToMapping("CodexVttSecurity", (handler, mappedView) =>
        {
            // L'evento di HybridWebView passa prima di quello che serve gli asset: se la richiesta è gestita qui,
            // HybridWebView non risponde con il suo 404.
            if (mappedView is Microsoft.Maui.Controls.HybridWebView hybrid && handler.MauiContext?.Services.GetService(typeof(CodexVtt.Bridge.BridgeDispatcher)) is CodexVtt.Bridge.BridgeDispatcher dispatcher)
            {
                hybrid.WebResourceRequested += (_, e) =>
                {
                    e.Headers.TryGetValue("Range", out var range);
                    if (TryServeAudio(e.Uri, e.Method, range, dispatcher.OpenHistoryAudio) is not { } response)
                        return;
                    e.Handled = true;
                    e.SetResponse(response.Status, response.Reason, response.Headers, response.Body ?? Stream.Null);
                };
            }

            var webView = handler.PlatformView;
            webView.CoreWebView2Initialized += (sender, initArgs) =>
            {
                var core = sender.CoreWebView2;
                if (core is null)
                    return;
#if !DEBUG
                // DevTools solo con modalità debug esplicita (variabile d'ambiente o argomento --debug).
                core.Settings.AreDevToolsEnabled = options.DebugMode;
                core.Settings.AreDefaultContextMenusEnabled = false;
#endif
                // Nessun controllo di reputazione verso SmartScreen: l'app naviga solo sull'origine locale (IsLocal).
                core.Settings.IsReputationCheckingRequired = false;
                core.Settings.IsStatusBarEnabled = false;
                core.Settings.IsGeneralAutofillEnabled = false;
                core.Settings.IsPasswordAutosaveEnabled = false;

                core.NavigationStarting += (_, e) =>
                {
                    if (!IsLocal(e.Uri))
                        e.Cancel = true;
                };
                core.NewWindowRequested += (_, e) => e.Handled = true;

                // Qualunque richiesta non locale (script, immagini, fetch) viene bloccata.
                core.AddWebResourceRequestedFilter("*", Microsoft.Web.WebView2.Core.CoreWebView2WebResourceContext.All);
                core.WebResourceRequested += (_, e) =>
                {
                    if (!IsLocal(e.Request.Uri))
                    {
                        diagnostics?.LogBlockedRequest(e.Request.Uri);
                        e.Response = core.Environment.CreateWebResourceResponse(null, 403, "Blocked by CodexVTT", "");
                    }
                };

                if (diagnostics is not null)
                    _ = diagnostics.AttachAsync(core);
            };
        });
#endif
    }
}

/// <summary>Espone solo <c>count</c> byte di un file a partire da <c>start</c> (corpo di una risposta 206).</summary>
internal sealed class SliceStream : Stream
{
    private readonly Stream _inner;
    private long _remaining;

    public SliceStream(Stream inner, long start, long count)
    {
        _inner = inner;
        _remaining = count;
        _inner.Seek(start, SeekOrigin.Begin);
    }

    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

    public override int Read(byte[] buffer, int offset, int count)
    {
        if (_remaining <= 0)
            return 0;
        var read = _inner.Read(buffer, offset, (int)Math.Min(count, _remaining));
        _remaining -= read;
        return read;
    }

    public override void Flush() { }
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            _inner.Dispose();
        base.Dispose(disposing);
    }
}
