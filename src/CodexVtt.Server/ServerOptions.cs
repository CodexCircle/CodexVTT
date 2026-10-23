namespace CodexVtt.Server;

/// <summary>Impostazioni della versione server (sezione "CodexVtt" di appsettings.json, variabili CodexVtt__* o argomenti --CodexVtt:*).</summary>
public sealed class ServerOptions
{
    public const string Section = "CodexVTT";

    /// <summary>Cartella dati (modelli, log, settings.json). Vuota = %LOCALAPPDATA%\CodexVTT.Data dell'account del processo.</summary>
    public string? DataRoot { get; set; }

    /// <summary>Cartella predefinita della cronologia. Vuota = Documenti\CodexVTT\history dell'account del processo.</summary>
    public string? HistoryDirectory { get; set; }

    /// <summary>
    /// Token d'accesso. Senza token il server risponde solo a richieste dallo stesso PC (localhost);
    /// con il token si apre <c>http://indirizzo/?token=…</c> una volta e il browser lo ricorda in un cookie.
    /// </summary>
    public string? AccessToken { get; set; }

    /// <summary>Dimensione massima di un caricamento (tutti i file insieme).</summary>
    public long MaxUploadBytes { get; set; } = 4L * 1024 * 1024 * 1024;
}
