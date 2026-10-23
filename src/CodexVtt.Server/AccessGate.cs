using System.Net;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;

namespace CodexVtt.Server;

/// <summary>
/// Chi può usare il server (ADR-0007). Senza token: solo richieste dallo stesso PC. Con il token: chi apre
/// una volta <c>/?token=…</c> riceve un cookie HttpOnly e da lì in poi passa. Il WebSocket accetta solo
/// pagine della stessa origine, così un altro sito aperto nel browser non può pilotare il core.
/// </summary>
public sealed partial class AccessGate(RequestDelegate next, IOptions<ServerOptions> options, ILogger<AccessGate> logger)
{
    public const string CookieName = "codexvtt_access";
    public const string HealthPath = "/healthz";
    private const string TokenQuery = "token";

    public async Task InvokeAsync(HttpContext context)
    {
        var token = options.Value.AccessToken;
        if (context.Request.Path == HealthPath && HttpMethods.IsGet(context.Request.Method))
        {
            await next(context).ConfigureAwait(false);
            return;
        }

        if (string.IsNullOrEmpty(token))
        {
            if (!IsLoopback(context.Connection.RemoteIpAddress))
            {
                LogRejected(context.Connection.RemoteIpAddress?.ToString() ?? "?", "nessun token configurato");
                await Deny(context, StatusCodes.Status403Forbidden,
                    "Accesso da altri PC disattivato: imposta CodexVtt:AccessToken nella configurazione del server.").ConfigureAwait(false);
                return;
            }
        }
        else if (context.Request.Query.TryGetValue(TokenQuery, out var offered) && Matches(offered.ToString(), token))
        {
            context.Response.Cookies.Append(CookieName, token, new CookieOptions
            {
                HttpOnly = true,
                SameSite = SameSiteMode.Strict,
                Secure = context.Request.IsHttps,
                MaxAge = TimeSpan.FromDays(365),
                Path = "/",
            });
            // Il token non resta nella barra degli indirizzi né nella cronologia del browser.
            context.Response.Redirect(context.Request.PathBase + context.Request.Path);
            return;
        }
        else if (!context.Request.Cookies.TryGetValue(CookieName, out var cookie) || !Matches(cookie, token))
        {
            LogRejected(context.Connection.RemoteIpAddress?.ToString() ?? "?", "token mancante o errato");
            await Deny(context, StatusCodes.Status401Unauthorized,
                "Serve il token d'accesso: apri l'indirizzo del server aggiungendo ?token=… una volta.").ConfigureAwait(false);
            return;
        }

        if (context.WebSockets.IsWebSocketRequest && !SameOrigin(context.Request))
        {
            LogRejected(context.Connection.RemoteIpAddress?.ToString() ?? "?", "origine diversa");
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            return;
        }

        await next(context).ConfigureAwait(false);
    }

    public static bool IsLoopback(IPAddress? address) => address is not null && IPAddress.IsLoopback(address);

    /// <summary>
    /// L'header Origin deve indicare lo stesso host:porta della richiesta. I browser lo mandano sempre con i
    /// WebSocket; senza Origin la richiesta non viene da una pagina web (script locale con il token o da localhost).
    /// </summary>
    public static bool SameOrigin(HttpRequest request)
    {
        var origin = request.Headers.Origin.ToString();
        if (string.IsNullOrEmpty(origin))
            return true;
        return Uri.TryCreate(origin, UriKind.Absolute, out var uri)
               && string.Equals(uri.Authority, request.Host.Value, StringComparison.OrdinalIgnoreCase);
    }

    private static bool Matches(string offered, string expected) =>
        CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(offered), Encoding.UTF8.GetBytes(expected));

    private static Task Deny(HttpContext context, int status, string message)
    {
        context.Response.StatusCode = status;
        context.Response.ContentType = "text/plain; charset=utf-8";
        return context.Response.WriteAsync(message);
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Richiesta da {Address} rifiutata: {Reason}")]
    private partial void LogRejected(string address, string reason);
}
