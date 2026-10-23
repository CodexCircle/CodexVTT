namespace CodexVtt.Core;

/// <summary>
/// Directory dati per utente (ADR-0001): %LOCALAPPDATA%\CodexVTT.Data con models, temp, logs e settings.json.
/// Sta fuori da %LOCALAPPDATA%\CodexVTT, la cartella gestita da Velopack: aggiornare o disinstallare l'app
/// ricrea quella cartella e cancellerebbe i dati.
/// </summary>
public sealed class AppPaths
{
    public AppPaths(string root, string? modelsDirectory = null)
    {
        Root = Path.GetFullPath(root);
        Models = Path.GetFullPath(modelsDirectory ?? Path.Combine(Root, "models"));
        Temp = Path.Combine(Root, "temp");
        Logs = Path.Combine(Root, "logs");
        SettingsFile = Path.Combine(Root, "settings.json");
    }

    public string Root { get; }

    public string Models { get; }

    public string PartialModels => Path.Combine(Models, ".partial");

    public string Temp { get; }

    public string Logs { get; }

    public string SettingsFile { get; }

    public static string DefaultRoot =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CodexVTT.Data");

    /// <summary>Cartella di installazione gestita dall'installer (packId CodexVTT): i dati non devono stare lì.</summary>
    public static string InstallerRoot =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CodexVTT");

    public void EnsureCreated()
    {
        Directory.CreateDirectory(Root);
        Directory.CreateDirectory(Models);
        Directory.CreateDirectory(PartialModels);
        Directory.CreateDirectory(Temp);
        Directory.CreateDirectory(Logs);
    }

    /// <summary>Rimuove i file temporanei lasciati da job interrotti (crash o chiusura forzata).</summary>
    public void CleanTemp()
    {
        if (!Directory.Exists(Temp))
            return;

        foreach (var file in Directory.EnumerateFiles(Temp))
        {
            try
            {
                File.Delete(file);
            }
            catch (IOException)
            {
                // File ancora in uso: verrà rimosso al prossimo avvio.
            }
        }
    }
}
