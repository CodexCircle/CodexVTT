using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using CodexVtt.Bridge;
using CodexVtt.Core;
using CodexVtt.Core.Errors;
using CodexVtt.Core.Export;

namespace CodexVtt.Server;

/// <summary>
/// Canali tra il browser e il core: il WebSocket porta gli stessi messaggi JSON di HybridWebView,
/// caricamento e download sostituiscono i dialog "Apri" e "Salva con nome" dell'app desktop.
/// </summary>
public static partial class BridgeEndpoints
{
    /// <summary>Messaggio più grande accettato dalla UI (le impostazioni e le richieste sono piccole).</summary>
    private const int MaxMessageBytes = 1024 * 1024;

    public static void MapBridge(this WebApplication app)
    {
        app.Map("/bridge", HandleSocketAsync);
        app.MapPost("/api/audio", UploadAsync).DisableAntiforgery();
        app.MapGet("/api/export", ExportAsync);
    }

    private static async Task HandleSocketAsync(HttpContext context, BridgeDispatcher dispatcher, ClientHub hub, ILoggerFactory loggers)
    {
        if (!context.WebSockets.IsWebSocketRequest)
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            return;
        }

        var logger = loggers.CreateLogger("CodexVtt.Server.Bridge");
        using var socket = await context.WebSockets.AcceptWebSocketAsync().ConfigureAwait(false);
        var connection = hub.Add(socket);
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted);
        var pump = connection.PumpAsync(stop.Token);
        try
        {
            while (await ReceiveAsync(socket, stop.Token).ConfigureAwait(false) is { } message)
            {
                // Come nell'app: ogni comando va per conto suo, così uno lento non ferma gli altri.
                _ = Task.Run(async () => connection.Post(await dispatcher.HandleAsync(message, stop.Token).ConfigureAwait(false)), stop.Token);
            }
        }
        catch (Exception ex) when (ex is WebSocketException or OperationCanceledException)
        {
            LogSocketClosed(logger, ex.GetType().Name);
        }
        finally
        {
            hub.Remove(connection);
            connection.Complete();
            await stop.CancelAsync().ConfigureAwait(false);
            try
            {
                await pump.ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is WebSocketException or OperationCanceledException)
            {
                // Pagina chiusa mentre si inviava: niente da fare, il core continua.
            }
        }
    }

    private static async Task<string?> ReceiveAsync(WebSocket socket, CancellationToken ct)
    {
        var buffer = new byte[16 * 1024];
        using var message = new MemoryStream();
        while (true)
        {
            var result = await socket.ReceiveAsync(buffer, ct).ConfigureAwait(false);
            if (result.MessageType == WebSocketMessageType.Close)
            {
                await socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, null, ct).ConfigureAwait(false);
                return null;
            }

            message.Write(buffer, 0, result.Count);
            if (message.Length > MaxMessageBytes)
            {
                await socket.CloseOutputAsync(WebSocketCloseStatus.MessageTooBig, null, ct).ConfigureAwait(false);
                return null;
            }

            if (result.EndOfMessage)
                return Encoding.UTF8.GetString(message.GetBuffer(), 0, (int)message.Length);
        }
    }

    /// <summary>
    /// Riceve i file scelti nel browser, li salva nella cartella dati del server e li registra come farebbe il
    /// dialog nativo. Risponde con lo stesso formato di audio.pickMany.
    /// </summary>
    private static async Task<IResult> UploadAsync(HttpRequest request, BridgeDispatcher dispatcher, AppPaths paths, ILoggerFactory loggers)
    {
        if (!request.HasFormContentType)
            return Error(StatusCodes.Status400BadRequest, new BridgeError(ErrorCodes.InvalidRequest, "Caricamento non valido."));

        var form = await request.ReadFormAsync(request.HttpContext.RequestAborted).ConfigureAwait(false);
        var saved = new List<string>();
        foreach (var file in form.Files)
        {
            var folder = Path.Combine(UploadsFolder(paths), Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
            var path = Path.Combine(folder, SafeFileName(file.FileName));
            var output = File.Create(path);
            await using (output.ConfigureAwait(false))
                await file.CopyToAsync(output, request.HttpContext.RequestAborted).ConfigureAwait(false);
            saved.Add(path);
        }

        var count = saved.Count;
        LogUploaded(loggers.CreateLogger("CodexVtt.Server.Upload"), count);
        return Results.Json(dispatcher.RegisterAudioFiles(saved), BridgeProtocol.JsonOptions);
    }

    private static async Task<IResult> ExportAsync(
        string? jobId, string? historyId, string? format, bool? includeHeader, BridgeDispatcher dispatcher, CancellationToken ct)
    {
        if (!Enum.TryParse<ExportFormat>(format, ignoreCase: true, out var parsed))
            return Error(StatusCodes.Status400BadRequest, new BridgeError(ErrorCodes.InvalidRequest, "Formato non valido."));
        try
        {
            var (fileName, content) = await dispatcher.RenderExportAsync(new ExportRequest(jobId, historyId, parsed, includeHeader), ct).ConfigureAwait(false);
            var type = parsed switch
            {
                ExportFormat.Vtt => "text/vtt",
                ExportFormat.Srt => "application/x-subrip",
                _ => "text/plain",
            };
            return Results.File(content, type + "; charset=utf-8", fileName);
        }
        catch (Exception ex) when (ex is CodexVttException or JsonException)
        {
            var error = BridgeDispatcher.ToError(ex);
            return Error(error.Code == ErrorCodes.InvalidRequest ? StatusCodes.Status400BadRequest : StatusCodes.Status404NotFound, error);
        }
    }

    public static string UploadsFolder(AppPaths paths) => Path.Combine(paths.Root, "uploads");

    /// <summary>Solo il nome, senza cartelle né caratteri vietati da Windows: il nome resta quello mostrato nella cronologia.</summary>
    public static string SafeFileName(string name)
    {
        var safe = Path.GetFileName(name.Replace('\\', '/'));
        // Stesso risultato su Windows e Linux: i nomi finiscono nella cronologia e possono essere copiati tra i due.
        safe = string.Concat(safe.Select(c => c < ' ' || InvalidNameChars.Contains(c) ? '_' : c));
        return string.IsNullOrWhiteSpace(safe) || safe is "." or ".." ? "audio" : safe;
    }

    private const string InvalidNameChars = "<>:\"/\\|?*";

    private static IResult Error(int status, BridgeError error) =>
        Results.Json(error, BridgeProtocol.JsonOptions, statusCode: status);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Canale WebSocket chiuso ({Reason})")]
    private static partial void LogSocketClosed(ILogger logger, string reason);

    // Solo il numero dei file: i nomi possono essere dati personali (ADR-0006).
    [LoggerMessage(Level = LogLevel.Information, Message = "Caricati {Count} file audio")]
    private static partial void LogUploaded(ILogger logger, int count);
}
