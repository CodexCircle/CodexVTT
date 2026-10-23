namespace CodexVtt.Core.Maintenance;

/// <summary>
/// Rimozione dei dati interni di CodexVTT (modelli Whisper, log, impostazioni, file temporanei) durante la
/// disinstallazione. La cronologia in Documenti non viene mai toccata: è un file dell'utente.
/// </summary>
public static class InternalDataRemover
{
    /// <summary>Dimensione totale in byte dei file sotto la cartella, 0 se non esiste.</summary>
    public static long Measure(string root)
    {
        if (!Directory.Exists(root))
            return 0;
        return new DirectoryInfo(root)
            .EnumerateFiles("*", SearchOption.AllDirectories)
            .Sum(f => f.Length);
    }

    /// <summary>
    /// Elimina la cartella e restituisce i percorsi che non è stato possibile rimuovere (file in uso, permessi).
    /// Non lancia eccezioni: la disinstallazione deve completarsi comunque.
    /// </summary>
    public static IReadOnlyList<string> Remove(string root)
    {
        var failures = new List<string>();
        if (!Directory.Exists(root))
            return failures;

        foreach (var file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
        {
            try
            {
                File.SetAttributes(file, FileAttributes.Normal);
                File.Delete(file);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                failures.Add(file);
            }
        }

        // Le cartelle vuote si rimuovono dal fondo; quelle con file rimasti restano.
        foreach (var dir in Directory.EnumerateDirectories(root, "*", SearchOption.AllDirectories)
                     .OrderByDescending(d => d.Length))
        {
            try { Directory.Delete(dir); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
        try { Directory.Delete(root); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }

        return failures;
    }
}
