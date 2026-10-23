#if WINDOWS
using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Web.WebView2.Core;

namespace CodexVtt.App.Shell;

/// <summary>
/// Log diagnostico della WebView (issue #38), attivo solo con la modalità debug: navigazioni, crash del
/// processo WebView, richieste bloccate e messaggi/eccezioni JavaScript (via protocollo DevTools).
/// Non registra query string né payload del bridge. I messaggi della console possono contenere testo
/// della UI: il pacchetto diagnostico va condiviso solo dopo averlo letto.
/// </summary>
internal sealed class WebViewDiagnostics(ILogger logger)
{
    private const int MaxTextLength = 500;

    public async Task AttachAsync(CoreWebView2 core)
    {
        core.NavigationStarting += (_, e) => logger.LogDebug("Navigazione verso {Target}", Target(e.Uri));
        core.NavigationCompleted += (_, e) =>
        {
            if (e.IsSuccess)
                logger.LogDebug("Navigazione completata: successo");
            else
                logger.LogWarning("Navigazione fallita: {Status}", e.WebErrorStatus);
        };
        core.DOMContentLoaded += (_, _) => logger.LogDebug("DOM caricato");
        core.ProcessFailed += (_, e) =>
            logger.LogError("Processo WebView fallito: {Kind}, exit {ExitCode}", e.ProcessFailedKind, e.ExitCode);

        try
        {
            core.GetDevToolsProtocolEventReceiver("Runtime.consoleAPICalled").DevToolsProtocolEventReceived +=
                (_, e) => LogConsole(e.ParameterObjectAsJson);
            core.GetDevToolsProtocolEventReceiver("Runtime.exceptionThrown").DevToolsProtocolEventReceived +=
                (_, e) => LogException(e.ParameterObjectAsJson);
            await core.CallDevToolsProtocolMethodAsync("Runtime.enable", "{}");
            logger.LogDebug("Console JavaScript collegata al log");
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or COMException)
        {
            logger.LogWarning(ex, "Console JavaScript non collegabile al log");
        }
    }

    /// <summary>Richiesta di rete bloccata dalla policy di isolamento (ADR-0006).</summary>
    public void LogBlockedRequest(string uri) =>
        logger.LogDebug("Richiesta bloccata verso {Target}", Target(uri));

    private void LogConsole(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        var type = root.TryGetProperty("type", out var t) ? t.GetString() : "log";
        var text = root.TryGetProperty("args", out var args) ? string.Join(' ', args.EnumerateArray().Select(ArgText)) : "";

        var level = type is "error" or "assert" or "warning" ? LogLevel.Warning : LogLevel.Debug;
        logger.Log(level, "Console JS [{Type}]: {Text}", type, Truncate(text));
    }

    private void LogException(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var details = doc.RootElement.GetProperty("exceptionDetails");
        var text = details.TryGetProperty("text", out var t) ? t.GetString() : "";
        var description = details.TryGetProperty("exception", out var ex) && ex.TryGetProperty("description", out var d)
            ? d.GetString()
            : "";
        var line = details.TryGetProperty("lineNumber", out var l) ? l.GetInt32() : -1;
        logger.LogWarning("Eccezione JS: {Text} {Description} (riga {Line})", Truncate(text ?? ""), Truncate(FirstLine(description)), line);
    }

    private static string ArgText(JsonElement arg)
    {
        if (arg.TryGetProperty("value", out var value))
            return value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : value.GetRawText();
        return arg.TryGetProperty("description", out var description) ? description.GetString() ?? "" : "";
    }

    private static string Target(string? uri)
    {
        if (!Uri.TryCreate(uri, UriKind.Absolute, out var parsed))
            return "?";
        // Solo schema, host e percorso: niente query string né fragment.
        return $"{parsed.Scheme}://{parsed.Host}{parsed.AbsolutePath}";
    }

    private static string FirstLine(string? text) => (text ?? "").Split('\n')[0];

    private static string Truncate(string text) =>
        text.Length <= MaxTextLength ? text : text[..MaxTextLength] + "…";
}
#endif
