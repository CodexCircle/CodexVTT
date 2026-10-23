using System.Text.Json;
using CodexVtt.Core.Models;

namespace CodexVtt.Core.Diagnostics;

public sealed record DiagnosticCheck(string Name, bool Ok, string Detail);

/// <summary>Esito di <c>CodexVtt.App --diagnose</c> (issue #38). Non contiene audio né testo trascritto.</summary>
public sealed record DiagnosticReport(
    string AppVersion,
    string OsDescription,
    string? WebView2Version,
    DateTimeOffset CreatedAt,
    IReadOnlyList<DiagnosticCheck> Checks,
    IReadOnlyList<string> LogTail)
{
    public bool Passed => Checks.All(c => c.Ok);
}

/// <summary>
/// Verifica l'ambiente senza GUI: cartelle scrivibili, modelli installati con hash, runtime WebView2 e
/// ultime righe del log. Non modifica i modelli (l'hash è ricalcolato senza scrivere marker).
/// </summary>
public static class DiagnosticReporter
{
    public const string LogFileName = "codexvtt.log";

    private static readonly JsonSerializerOptions WriteOptions = new() { WriteIndented = true };

    public static async Task<DiagnosticReport> RunAsync(
        AppPaths paths,
        ModelStore models,
        string appVersion,
        string? webView2Version,
        string? historyDirectory,
        int logLines = 200,
        CancellationToken ct = default)
    {
        var checks = new List<DiagnosticCheck>
        {
            new("webview2.runtime", webView2Version is not null,
                webView2Version ?? "Runtime WebView2 non trovato"),
            WritableCheck("dir.root", paths.Root),
            WritableCheck("dir.models", paths.Models),
            WritableCheck("dir.logs", paths.Logs),
            WritableCheck("dir.temp", paths.Temp),
        };

        if (!string.IsNullOrWhiteSpace(historyDirectory))
            checks.Add(WritableCheck("dir.history", historyDirectory));

        foreach (var model in models.Catalog.Models)
            checks.Add(await ModelCheckAsync(models, model, ct).ConfigureAwait(false));

        return new DiagnosticReport(
            appVersion,
            System.Runtime.InteropServices.RuntimeInformation.OSDescription,
            webView2Version,
            DateTimeOffset.UtcNow,
            checks,
            ReadLogTail(paths.Logs, logLines));
    }

    public static async Task WriteAsync(DiagnosticReport report, string path, CancellationToken ct = default)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        await using var stream = File.Create(path);
        await JsonSerializer.SerializeAsync(stream, report, WriteOptions, ct).ConfigureAwait(false);
    }

    /// <summary>Sostituisce il profilo utente con "~" per non esporre il nome dell'utente nel report.</summary>
    public static string Redact(string path)
    {
        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return !string.IsNullOrEmpty(profile) && path.StartsWith(profile, StringComparison.OrdinalIgnoreCase)
            ? "~" + path[profile.Length..]
            : path;
    }

    private static DiagnosticCheck WritableCheck(string name, string directory)
    {
        var shown = Redact(directory);
        try
        {
            Directory.CreateDirectory(directory);
            var probe = Path.Combine(directory, $".diag-{Guid.NewGuid():N}");
            File.WriteAllText(probe, "probe");
            File.Delete(probe);
            return new DiagnosticCheck(name, true, shown);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new DiagnosticCheck(name, false, $"{shown}: {ex.GetType().Name}");
        }
    }

    private static async Task<DiagnosticCheck> ModelCheckAsync(ModelStore models, ModelDescriptor model, CancellationToken ct)
    {
        var name = $"model.{model.Id}";
        var status = models.Status(model);
        if (status.State == ModelState.NotInstalled)
            return new DiagnosticCheck(name, true, "non installato");

        if (string.IsNullOrEmpty(model.Sha256))
            return new DiagnosticCheck(name, false, "il catalogo non contiene l'hash");

        var actual = await ModelStore.ComputeSha256Async(status.Path, null, ct).ConfigureAwait(false);
        var ok = string.Equals(actual, model.Sha256, StringComparison.OrdinalIgnoreCase);
        return new DiagnosticCheck(name, ok, ok ? $"hash OK, stato {status.State}" : $"hash diverso (atteso {model.Sha256[..12]}…, letto {actual[..12]}…)");
    }

    private static List<string> ReadLogTail(string logDirectory, int lines)
    {
        var file = Path.Combine(logDirectory, LogFileName);
        if (!File.Exists(file))
            return [];

        // Il log è già limitato dalla rotazione (2 MB): leggerlo intero è accettabile per un report.
        var all = File.ReadAllLines(file);
        return all.Skip(Math.Max(0, all.Length - lines)).ToList();
    }
}
