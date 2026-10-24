namespace CodexVtt.Core.History;

/// <summary>
/// Esito del test di una cartella di lavoro. <c>Error</c> è un codice stabile che la UI traduce.
/// <c>ControlledFolderAccess</c> (<c>on</c>, <c>audit</c>, <c>off</c>, <c>unknown</c>) e <c>AppPath</c> compaiono solo con
/// <c>blocked_by_windows</c>, per dire all'utente quale app autorizzare. <c>Detail</c> è l'eccezione grezza (tipo e HRESULT),
/// mostrata nell'avviso per capire cosa ha restituito davvero il sistema.
/// </summary>
public sealed record FolderTestResult(bool Ok, string? Error = null, string? ControlledFolderAccess = null, string? AppPath = null, string? Detail = null)
{
    public const string NotFound = "not_found";
    /// <summary>Scrittura negata dal sistema su Windows: causa probabile «Accesso alle cartelle controllato» (o i permessi della cartella).</summary>
    public const string BlockedByWindows = "blocked_by_windows";
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
                return Failure(ex, FolderTestResult.CannotWrite);
            }

            try
            {
                if (File.ReadAllText(path) != Content)
                    return new FolderTestResult(false, FolderTestResult.CannotRead);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return Failure(ex, FolderTestResult.CannotRead);
            }

            try
            {
                File.Delete(path);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return Failure(ex, FolderTestResult.CannotDelete);
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

    private const int ErrorHandleDiskFull = unchecked((int)0x80070027);
    private const int ErrorDiskFull = unchecked((int)0x80070070);
    private const int ErrorFilenameExcedRange = unchecked((int)0x800700CE);

    /// <summary>
    /// Su Windows un rifiuto del sistema (accesso negato, ma anche file «infetto» o altro errore di I/O che non sia disco
    /// pieno o percorso troppo lungo) viene da «Accesso alle cartelle controllato» o dai permessi: Defender non sempre
    /// risponde con <see cref="UnauthorizedAccessException"/>. Il dettaglio grezzo resta nell'esito.
    /// </summary>
    private static FolderTestResult Failure(Exception ex, string fallback)
    {
        var detail = $"{ex.GetType().Name} 0x{ex.HResult:X8}";
        if (OperatingSystem.IsWindows() && ex.HResult is not (ErrorHandleDiskFull or ErrorDiskFull or ErrorFilenameExcedRange))
            return new FolderTestResult(false, FolderTestResult.BlockedByWindows, ControlledFolderAccessState(), Environment.ProcessPath, detail);
        return new FolderTestResult(false, fallback, Detail: detail);
    }

    /// <summary>
    /// Stato di «Accesso alle cartelle controllato» letto dal registro (best effort, sola lettura):
    /// <c>on</c>, <c>audit</c>, <c>off</c>; <c>unknown</c> se la chiave non è leggibile (criteri di gruppo, permessi, altro antivirus).
    /// </summary>
    internal static string ControlledFolderAccessState()
    {
        if (!OperatingSystem.IsWindows())
            return "unknown";
        try
        {
            using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(
                @"SOFTWARE\Microsoft\Windows Defender\Windows Defender Exploit Guard\Controlled Folder Access");
            return key?.GetValue("EnableControlledFolderAccess") switch
            {
                1 => "on",
                2 => "audit",
                0 => "off",
                _ => "unknown",
            };
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            return "unknown";
        }
    }
}
