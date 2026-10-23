using System.Text;
using System.Text.Json;
using CodexVtt.Core.Errors;

namespace CodexVtt.Core.History;

/// <summary>Raccolta di trascrizioni, con il loro ordine scelto dall'utente.</summary>
public sealed record HistoryGroup(string Id, string Name, string Color, IReadOnlyList<string> EntryIds);

/// <summary>Nomi e raccolte in un dato momento.</summary>
public sealed record HistoryLibrarySnapshot(IReadOnlyDictionary<string, string> Titles, IReadOnlyList<HistoryGroup> Collections)
{
    public string? CollectionOf(string entryId) =>
        Collections.FirstOrDefault(c => c.EntryIds.Contains(entryId, StringComparer.Ordinal))?.Id;
}

/// <summary>
/// Nomi delle trascrizioni e raccolte, salvati in library.json nella cartella della cronologia
/// accanto ai file delle trascrizioni (così seguono la cartella se l'utente la sposta).
/// Le trascrizioni restano nei loro file: qui ci sono solo i riferimenti per id.
/// </summary>
public sealed class HistoryLibrary
{
    public const string FileName = "library.json";
    private const string Format = "codexvtt-library";
    private const int FormatVersion = 1;
    private const int MaxNameLength = 80;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private static readonly Lock Gate = new();

    public static readonly IReadOnlyList<string> Colors = ["bordeaux", "green", "cream", "rose", "sage", "ochre"];

    private readonly string _path;

    public HistoryLibrary(string directory)
    {
        _path = Path.Combine(Path.GetFullPath(directory), FileName);
    }

    public HistoryLibrarySnapshot Read()
    {
        lock (Gate)
        {
            var doc = Load();
            return new HistoryLibrarySnapshot(
                new Dictionary<string, string>(doc.Titles, StringComparer.Ordinal),
                doc.Collections.Select(ToCollection).ToList());
        }
    }

    /// <summary>Nome scelto dall'utente; vuoto o null lo toglie.</summary>
    public void SetTitle(string entryId, string? title) => Update(doc =>
    {
        var clean = Clean(title);
        if (clean is null)
            doc.Titles.Remove(entryId);
        else
            doc.Titles[entryId] = clean;
    });

    /// <summary>Sposta la trascrizione in una raccolta (in fondo) o la toglie con null.</summary>
    public void SetCollection(string entryId, string? collectionId) => Update(doc =>
    {
        var target = collectionId is null ? null : Find(doc, collectionId);
        foreach (var c in doc.Collections)
        {
            if (!ReferenceEquals(c, target))
                c.EntryIds.Remove(entryId);
        }

        if (target is not null && !target.EntryIds.Contains(entryId, StringComparer.Ordinal))
            target.EntryIds.Add(entryId);
    });

    /// <summary>Crea (id null) o rinomina/ricolora una raccolta.</summary>
    public HistoryGroup SaveCollection(string? id, string name, string color)
    {
        var clean = Clean(name) ?? throw new CodexVttException(ErrorCodes.InvalidRequest, "Nome della raccolta mancante.");
        if (!Colors.Contains(color, StringComparer.Ordinal))
            throw new CodexVttException(ErrorCodes.InvalidRequest, "Colore della raccolta non supportato.");

        CollectionDocument? saved = null;
        Update(doc =>
        {
            if (id is null)
            {
                saved = new CollectionDocument("col-" + Guid.NewGuid().ToString("N")[..12], clean, color, []);
                doc.Collections.Add(saved);
            }
            else
            {
                var existing = Find(doc, id);
                saved = existing with { Name = clean, Color = color };
                doc.Collections[doc.Collections.IndexOf(existing)] = saved;
            }
        });
        return ToCollection(saved!);
    }

    /// <summary>Elimina la raccolta; le trascrizioni restano nella cronologia.</summary>
    public void DeleteCollection(string id) => Update(doc => doc.Collections.Remove(Find(doc, id)));

    /// <summary>Nuovo ordine: deve contenere esattamente le trascrizioni già nella raccolta.</summary>
    public HistoryGroup Reorder(string id, IReadOnlyList<string> entryIds)
    {
        CollectionDocument? collection = null;
        Update(doc =>
        {
            collection = Find(doc, id);
            var same = entryIds.Count == collection.EntryIds.Count &&
                       entryIds.Distinct(StringComparer.Ordinal).Count() == entryIds.Count &&
                       entryIds.All(e => collection.EntryIds.Contains(e, StringComparer.Ordinal));
            if (!same)
                throw new CodexVttException(ErrorCodes.InvalidRequest, "Ordine della raccolta non valido.");
            collection.EntryIds.Clear();
            collection.EntryIds.AddRange(entryIds);
        });
        return ToCollection(collection!);
    }

    /// <summary>Dimentica una trascrizione eliminata (nome e appartenenza alle raccolte).</summary>
    public void Forget(string entryId)
    {
        lock (Gate)
        {
            if (!File.Exists(_path))
                return;
        }

        Update(doc =>
        {
            doc.Titles.Remove(entryId);
            foreach (var c in doc.Collections)
                c.EntryIds.Remove(entryId);
        });
    }

    private void Update(Action<LibraryDocument> change)
    {
        lock (Gate)
        {
            var doc = Load();
            change(doc);
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            var temp = _path + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(doc, Json), new UTF8Encoding(false));
            File.Move(temp, _path, overwrite: true);
        }
    }

    private LibraryDocument Load()
    {
        if (File.Exists(_path))
        {
            try
            {
                var doc = JsonSerializer.Deserialize<LibraryDocument>(File.ReadAllText(_path), Json);
                if (doc is { Format: Format, Version: <= FormatVersion })
                {
                    return doc with
                    {
                        Titles = new Dictionary<string, string>(doc.Titles ?? new Dictionary<string, string>(), StringComparer.Ordinal),
                        Collections = (doc.Collections ?? []).Select(c => c with { EntryIds = [.. c.EntryIds ?? []] }).ToList(),
                    };
                }
            }
            catch (Exception ex) when (ex is JsonException or IOException or NotSupportedException)
            {
                // File illeggibile: si riparte vuoti, le trascrizioni non sono toccate.
            }
        }

        return new LibraryDocument(Format, FormatVersion, new Dictionary<string, string>(StringComparer.Ordinal), []);
    }

    private static CollectionDocument Find(LibraryDocument doc, string id) =>
        doc.Collections.FirstOrDefault(c => c.Id == id)
        ?? throw new CodexVttException(ErrorCodes.InvalidRequest, "Raccolta inesistente.");

    private static string? Clean(string? value)
    {
        var trimmed = value?.Trim();
        if (string.IsNullOrEmpty(trimmed))
            return null;
        return trimmed.Length > MaxNameLength ? trimmed[..MaxNameLength].TrimEnd() : trimmed;
    }

    private static HistoryGroup ToCollection(CollectionDocument c) => new(c.Id, c.Name, c.Color, [.. c.EntryIds]);

    private sealed record LibraryDocument(string Format, int Version, Dictionary<string, string> Titles, List<CollectionDocument> Collections);

    private sealed record CollectionDocument(string Id, string Name, string Color, List<string> EntryIds);
}
