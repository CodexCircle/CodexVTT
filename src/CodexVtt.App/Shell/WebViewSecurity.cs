using Microsoft.Extensions.Logging;
using CodexVtt.App.Shell;
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

    /// <param name="options">Opzioni diagnostiche: con <see cref="DiagnosticOptions.DebugMode"/> attivo si collega il log della WebView.</param>
    /// <param name="logger">Logger della WebView; usato solo in modalità debug.</param>
    public static void Configure(DiagnosticOptions options, ILogger logger)
    {
#if WINDOWS
        var diagnostics = options.DebugMode ? new WebViewDiagnostics(logger) : null;

        Microsoft.Maui.Handlers.HybridWebViewHandler.Mapper.AppendToMapping("CodexVttSecurity", (handler, mappedView) =>
        {
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
