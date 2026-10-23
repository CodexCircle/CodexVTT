using System.Text.Json;

namespace CodexVtt.Core.Settings;

public sealed record AppSettings
{
    public string? SelectedModelId { get; init; }

    public string Language { get; init; } = "auto";

    public bool ShowMascot { get; init; } = true;

    public bool ExportIncludeHeader { get; init; }

    /// <summary>Directory dei modelli personalizzata; null = %LOCALAPPDATA%\CodexVTT.Data\models.</summary>
    public string? ModelsDirectory { get; init; }

    /// <summary>Lingua dell'interfaccia: "it", "en" o null (lingua di sistema).</summary>
    public string? UiLanguage { get; init; }

    /// <summary>Tema dell'interfaccia: "light", "dark" o null (come il sistema).</summary>
    public string? Theme { get; init; }

    /// <summary>Tema di colore: "sabbia", "natale", "halloween", "ciliegia" o null (Sabbia).</summary>
    public string? Palette { get; init; }

    /// <summary>Mascotte: "tina", "giulio" o null (Tina).</summary>
    public string? Mascot { get; init; }

    /// <summary>Cartella visibile della cronologia; null = Documenti\CodexVTT\history.</summary>
    public string? HistoryDirectory { get; init; }

    /// <summary>
    /// Controlla da solo, all'avvio, se su GitHub c'è una versione più recente (ADR-0008). Di default no:
    /// senza questa scelta l'app non contatta mai GitHub, se non quando si preme "Controlla aggiornamenti".
    /// </summary>
    public bool AutoCheckUpdates { get; init; }

    /// <summary>
    /// Quanta CPU possono usare i motori, in percentuale dei core logici (10-100). Si traduce nel numero di thread:
    /// con il 50% su 8 core si usano al più 4 thread. 100 = nessun limite. Vale dalla prossima trascrizione.
    /// </summary>
    public int CpuLimitPercent { get; init; } = 100;

    /// <summary>
    /// Usa la scheda video (Vulkan) per Whisper quando il PC la supporta. Di default sì, con ripiego automatico sulla CPU;
    /// si applica al prossimo avvio. Parakeet e Canary girano sempre sulla CPU.
    /// </summary>
    public bool UseGpu { get; init; } = true;

    /// <summary>Livello minimo del log diagnostico ("Trace".."Error"); null = Information. Vedi issue #38.</summary>
    public string? DiagnosticLogLevel { get; init; }
}

/// <summary>Impostazioni locali in settings.json (scrittura atomica).</summary>
public sealed class SettingsStore
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };
    private readonly string _path;
    private readonly Lock _gate = new();
    private AppSettings? _cached;

    public SettingsStore(string path) => _path = path;

    public AppSettings Load()
    {
        lock (_gate)
        {
            if (_cached is not null)
                return _cached;
            try
            {
                _cached = File.Exists(_path)
                    ? JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(_path), Options) ?? new AppSettings()
                    : new AppSettings();
            }
            catch (JsonException)
            {
                // File illeggibile: si riparte dai default senza perdere l'avvio dell'app.
                _cached = new AppSettings();
            }

            return _cached;
        }
    }

    public void Save(AppSettings settings)
    {
        lock (_gate)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            var temp = _path + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(settings, Options));
            File.Move(temp, _path, overwrite: true);
            _cached = settings;
        }
    }
}
