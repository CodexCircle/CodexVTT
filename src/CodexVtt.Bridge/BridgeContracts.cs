using System.Text.Json;
using System.Text.Json.Serialization;

namespace CodexVtt.Bridge;

/// <summary>Versione del contratto core ↔ UI (ADR-0001). Va incrementata a ogni modifica incompatibile.</summary>
public static class BridgeProtocol
{
    public const int Version = 1;

    public static JsonSerializerOptions JsonOptions { get; } = CreateOptions();

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        };
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        options.Converters.Add(new TimeSpanSecondsConverter());
        return options;
    }

    /// <summary>I TimeSpan viaggiano come secondi (double) per semplicità lato JavaScript.</summary>
    private sealed class TimeSpanSecondsConverter : JsonConverter<TimeSpan>
    {
        public override TimeSpan Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
            TimeSpan.FromSeconds(reader.GetDouble());

        public override void Write(Utf8JsonWriter writer, TimeSpan value, JsonSerializerOptions options) =>
            writer.WriteNumberValue(Math.Round(value.TotalSeconds, 3));
    }
}

public sealed record BridgeRequest(int V, string Id, string Type, JsonElement? Payload);

public sealed record BridgeError(string Code, string Message, IReadOnlyDictionary<string, string>? Details = null);

public sealed record BridgeResponse(int V, string Id, string Type, bool Ok, object? Payload, BridgeError? Error);

public sealed record BridgeEvent(int V, string Type, string Event, object? Payload);

/// <summary>Servizi forniti dalla shell (dialog nativi e canale degli eventi verso la UI).</summary>
public interface IShellServices
{
    /// <summary>"desktop" (app con dialog nativi) o "server" (UI nel browser: file caricati e scaricati via HTTP).</summary>
    string Host => "desktop";

    /// <summary>Dialog "Apri" per file audio. Restituisce null se l'utente annulla.</summary>
    Task<string?> PickAudioFileAsync(CancellationToken ct);

    /// <summary>Dialog "Apri" con selezione multipla di file audio (batch). Lista vuota se l'utente annulla.</summary>
    Task<IReadOnlyList<string>> PickAudioFilesAsync(CancellationToken ct);

    /// <summary>Dialog "Apri" per un file modello GGML da importare.</summary>
    Task<string?> PickModelFileAsync(CancellationToken ct);

    /// <summary>Dialog "Salva con nome". Restituisce null se l'utente annulla.</summary>
    Task<string?> PickSaveFileAsync(string suggestedName, string extension, CancellationToken ct);

    /// <summary>Dialog di scelta cartella (cronologia). Restituisce null se l'utente annulla.</summary>
    Task<string?> PickFolderAsync(CancellationToken ct);

    /// <summary>Apre la cartella in Esplora file.</summary>
    void OpenFolder(string path);

    /// <summary>Invia un messaggio JSON alla UI.</summary>
    void PostToUi(string json);
}

public enum UpdateState
{
    /// <summary>Versione non installata (sviluppo, server, container): l'aggiornamento non si applica.</summary>
    Unsupported,
    UpToDate,
    Available,
    Downloading,
    Failed,
}

/// <summary>Esito del controllo degli aggiornamenti. <c>LatestVersion</c> e <c>Notes</c> solo se c'è una versione nuova.</summary>
public sealed record UpdateStatus(UpdateState State, string CurrentVersion, string? LatestVersion = null, string? Notes = null, string? ReleasesUrl = null);

/// <summary>
/// Aggiornamento dell'app (ADR-0008). L'unica implementazione contatta GitHub (le release del repository del
/// progetto) e solo su richiesta dell'utente o, se l'ha attivato, all'avvio: non invia audio, testo né dati personali.
/// </summary>
public interface IUpdateService
{
    UpdateStatus Current { get; }

    /// <summary>Cerca una versione più recente.</summary>
    Task<UpdateStatus> CheckAsync(CancellationToken ct);

    /// <summary>Scarica la versione trovata da <see cref="CheckAsync"/> e riavvia l'app per installarla.</summary>
    Task ApplyAsync(IProgress<int> progress, CancellationToken ct);
}

/// <summary>
/// Cartella predefinita della cronologia (Documenti\CodexVTT\history nell'app) e cartella locale in cui
/// si mette al sicuro una trascrizione quando la cronologia non è scrivibile (#20).
/// </summary>
public sealed record HistoryLocation(string DefaultDirectory, string? RecoveryDirectory = null)
{
    public string Resolve(CodexVtt.Core.Settings.AppSettings settings) =>
        string.IsNullOrWhiteSpace(settings.HistoryDirectory) ? DefaultDirectory : settings.HistoryDirectory;
}
