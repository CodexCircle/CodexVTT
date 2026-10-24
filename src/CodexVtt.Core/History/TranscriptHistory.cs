using System.Globalization;
using System.Text;
using System.Text.Json;
using CodexVtt.Core.Audio;
using CodexVtt.Core.Errors;
using CodexVtt.Core.Export;
using CodexVtt.Core.Transcription;

namespace CodexVtt.Core.History;

public sealed record HistoryEntry(
    string Id,
    string AudioFileName,
    DateTimeOffset CompletedAt,
    TimeSpan AudioDuration,
    string ModelDisplayName,
    string? DetectedLanguage,
    string Preview,
    string TextFileName);

/// <summary>
/// Cronologia delle trascrizioni in una cartella visibile scelta dall'utente (default
/// Documenti\CodexVTT\history). Ogni trascrizione è salvata in file propri con lo stesso nome base:
/// un .txt leggibile e un .json con segmenti e metadati, che l'app usa per riaprirla.
/// Tutto resta sul disco locale (ADR-0006).
/// </summary>
public sealed class TranscriptHistory
{
    private const string Format = "codexvtt-transcript";
    private const int FormatVersion = 1;
    private const int PreviewLength = 160;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private readonly Lock _gate = new();

    public TranscriptHistory(string directory)
    {
        Directory = Path.GetFullPath(directory);
    }

    public string Directory { get; }

    public static string DefaultDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "CodexVTT", "history");

    /// <summary>Salva una trascrizione completata e restituisce la voce creata.</summary>
    public HistoryEntry Save(TranscriptionResult result, string language = "it", string? audioPath = null)
    {
        lock (_gate)
        {
            System.IO.Directory.CreateDirectory(Directory);
            var id = UniqueBaseName(result);
            var document = new HistoryDocument(Format, FormatVersion, result, audioPath);
            WriteAtomic(Path.Combine(Directory, id + ".json"), JsonSerializer.Serialize(document, Json));
            WriteAtomic(Path.Combine(Directory, id + ".txt"),
                TranscriptExporter.Render(result, ExportFormat.Txt, new ExportOptions(IncludeHeader: true, Language: language)));
            return ToEntry(id, result);
        }
    }

    /// <summary>
    /// Sostituisce i segmenti di una trascrizione già salvata (testo corretto o riorganizzato dall'utente) e riscrive
    /// il .json e il .txt. Modello, durata e data restano quelli della trascrizione originale.
    /// </summary>
    public HistoryEntry UpdateSegments(string id, IReadOnlyList<TranscriptSegment> segments, string language = "it")
    {
        lock (_gate)
        {
            var current = Read(PathFor(id, ".json")) ?? throw NotFound();
            var result = current.Result! with { Segments = segments };
            var document = new HistoryDocument(Format, FormatVersion, result, current.AudioPath);
            WriteAtomic(PathFor(id, ".json"), JsonSerializer.Serialize(document, Json));
            WriteAtomic(PathFor(id, ".txt"),
                TranscriptExporter.Render(result, ExportFormat.Txt, new ExportOptions(IncludeHeader: true, Language: language)));
            return ToEntry(id, result);
        }
    }

    /// <summary>Elenco dalla più recente; i file non riconosciuti vengono ignorati.</summary>
    public IReadOnlyList<HistoryEntry> List()
    {
        if (!System.IO.Directory.Exists(Directory))
            return [];

        var entries = new List<HistoryEntry>();
        foreach (var file in System.IO.Directory.EnumerateFiles(Directory, "*.json"))
        {
            var id = Path.GetFileNameWithoutExtension(file);
            if (TryRead(file) is { } result)
                entries.Add(ToEntry(id, result));
        }

        return entries.OrderByDescending(e => e.CompletedAt).ThenByDescending(e => e.Id, StringComparer.Ordinal).ToList();
    }

    public TranscriptionResult Load(string id) => (Read(PathFor(id, ".json")) ?? throw NotFound()).Result!;

    /// <summary>Percorso dell'audio originale della trascrizione, se registrato, è un audio noto e il file esiste ancora.</summary>
    public string? FindAudio(string id)
    {
        var path = (Read(PathFor(id, ".json")) ?? throw NotFound()).AudioPath;
        return path is { Length: > 0 } && Path.IsPathFullyQualified(path) && AudioMedia.ContentType(path) is not null && File.Exists(path) ? path : null;
    }

    private static CodexVttException NotFound() => new(ErrorCodes.HistoryNotFound, "Trascrizione non trovata nella cronologia.");

    public void Delete(string id)
    {
        var json = PathFor(id, ".json");
        if (!File.Exists(json))
            throw new CodexVttException(ErrorCodes.HistoryNotFound, "Trascrizione non trovata nella cronologia.");
        File.Delete(json);
        var txt = PathFor(id, ".txt");
        if (File.Exists(txt))
            File.Delete(txt);
    }

    /// <summary>Nome base sicuro: data e nome dell'audio, senza caratteri non validi per Windows.</summary>
    internal static string BaseName(TranscriptionResult result)
    {
        var stamp = result.CompletedAt.ToLocalTime().ToString("yyyy-MM-dd HH.mm.ss", CultureInfo.InvariantCulture);
        // Niente Path.GetFileNameWithoutExtension: tratterebbe '/' o '\' nel nome come cartelle.
        var name = result.AudioFileName;
        var dot = name.LastIndexOf('.');
        var audio = Sanitize(dot > 0 ? name[..dot] : name);
        return audio.Length == 0 ? stamp : $"{stamp} - {audio}";
    }

    private string UniqueBaseName(TranscriptionResult result)
    {
        var name = BaseName(result);
        var candidate = name;
        for (var i = 2; File.Exists(Path.Combine(Directory, candidate + ".json")) || File.Exists(Path.Combine(Directory, candidate + ".txt")); i++)
            candidate = $"{name} ({i})";
        return candidate;
    }

    private static string Sanitize(string value)
    {
        var invalid = new HashSet<char>(Path.GetInvalidFileNameChars().Concat(['<', '>', ':', '"', '/', '\\', '|', '?', '*']));
        var sb = new StringBuilder();
        foreach (var c in value)
            sb.Append(invalid.Contains(c) || char.IsControl(c) ? '_' : c);
        var clean = sb.ToString().Trim().TrimEnd('.');
        return clean.Length > 60 ? clean[..60].TrimEnd() : clean;
    }

    /// <summary>Gli id arrivano dalla UI: si accettano solo nomi di file, mai percorsi.</summary>
    private string PathFor(string id, string extension)
    {
        if (string.IsNullOrWhiteSpace(id) || id != Path.GetFileName(id) || id.Contains("..", StringComparison.Ordinal) ||
            id.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            throw new CodexVttException(ErrorCodes.InvalidRequest, "Identificativo di cronologia non valido.");
        }

        return Path.Combine(Directory, id + extension);
    }

    private static TranscriptionResult? TryRead(string path) => Read(path)?.Result;

    private static HistoryDocument? Read(string path)
    {
        if (!File.Exists(path))
            return null;
        try
        {
            var document = JsonSerializer.Deserialize<HistoryDocument>(File.ReadAllText(path), Json);
            return document is { Format: Format, Version: <= FormatVersion, Result: not null } ? document : null;
        }
        catch (Exception ex) when (ex is JsonException or IOException or NotSupportedException)
        {
            return null;
        }
    }

    private static HistoryEntry ToEntry(string id, TranscriptionResult result)
    {
        var text = result.Text;
        return new HistoryEntry(
            id,
            result.AudioFileName,
            result.CompletedAt,
            result.AudioDuration,
            result.Model.DisplayName,
            result.DetectedLanguage,
            text.Length > PreviewLength ? text[..PreviewLength].TrimEnd() + "…" : text,
            id + ".txt");
    }

    private static void WriteAtomic(string path, string content)
    {
        var temp = path + ".tmp";
        File.WriteAllText(temp, content, new UTF8Encoding(false));
        File.Move(temp, path, overwrite: true);
    }

    private sealed record HistoryDocument(string Format, int Version, TranscriptionResult? Result, string? AudioPath = null);
}
