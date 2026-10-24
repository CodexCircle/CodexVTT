namespace CodexVtt.Core.History;

/// <summary>Esito del test di una cartella di lavoro. <c>Error</c> è un codice stabile che la UI traduce.</summary>
public sealed record FolderTestResult(bool Ok, string? Error = null)
{
    public const string NotFound = "not_found";
    public const string CannotWrite = "cannot_write";
    public const string CannotRead = "cannot_read";
    public const string CannotDelete = "cannot_delete";
}

/// <summary>
/// Prova una cartella prima di adottarla: crea un file di prova, lo rilegge e lo cancella.
/// Nessuna rete e nessun dato dell'utente: il file contiene solo un testo fisso.
/// </summary>
public static class FolderProbe
{
    private const string Content = "CodexVTT folder test";

    public static FolderTestResult Test(string directory)
    {
        if (string.IsNullOrWhiteSpace(directory) || !Path.IsPathFullyQualified(directory))
            return new FolderTestResult(false, FolderTestResult.NotFound);

        var created = false;
        try
        {
            if (!Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
                created = true;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return new FolderTestResult(false, FolderTestResult.NotFound);
        }

        var path = Path.Combine(directory, $".codexvtt-test-{Guid.NewGuid():N}.tmp");
        try
        {
            try
            {
                File.WriteAllText(path, Content);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return new FolderTestResult(false, FolderTestResult.CannotWrite);
            }

            try
            {
                if (File.ReadAllText(path) != Content)
                    return new FolderTestResult(false, FolderTestResult.CannotRead);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return new FolderTestResult(false, FolderTestResult.CannotRead);
            }

            try
            {
                File.Delete(path);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return new FolderTestResult(false, FolderTestResult.CannotDelete);
            }

            if (File.Exists(path))
                return new FolderTestResult(false, FolderTestResult.CannotDelete);
            return new FolderTestResult(true);
        }
        finally
        {
            // Se il test è fallito a metà, non lascia file di prova né una cartella creata solo per provarla.
            try
            {
                if (File.Exists(path)) File.Delete(path);
                if (created && Directory.Exists(directory) && !Directory.EnumerateFileSystemEntries(directory).Any())
                    Directory.Delete(directory);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Pulizia best-effort: l'esito del test è già deciso.
            }
        }
    }
}
