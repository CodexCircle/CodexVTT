using System.Text.Json;
using System.Text.Json.Serialization;

namespace CodexVtt.Core.Models;

/// <summary>Un file di un modello a più file (cartella): percorso relativo, dimensione e SHA-256.</summary>
public sealed record ModelFile(string Path, long SizeBytes, string Sha256);

/// <summary>
/// Affidabilità dichiarata: 100 meno il WER (in percentuale) sull'italiano, con la fonte del dato.
/// <c>MeasuredByProject</c> è false quando il valore viene dalla scheda del modello e non da un test di CodexVTT.
/// </summary>
public sealed record ModelAccuracy(string Language, double Percent, string Metric, string Source, bool MeasuredByProject);

/// <summary>Voce del catalogo dei modelli (ADR-0003, ADR-0009).</summary>
/// <param name="File">File del modello (un solo file) oppure nome della cartella (modello a più file).</param>
/// <param name="Engine">Motore che lo esegue: "whisper", "sherpa-transducer" o "sherpa-canary".</param>
/// <param name="Files">Elenco dei file di un modello a più file; assente per i modelli a file singolo.</param>
/// <param name="Source">Origine propria del modello; se assente vale quella del catalogo.</param>
/// <param name="Attribution">Attribuzione richiesta dalla licenza (es. CC-BY-4.0), mostrata nell'app.</param>
/// <param name="Preview">Anteprima: non viene mai raccomandato in automatico.</param>
/// <param name="RequiresLanguage">Il modello non rileva la lingua: va indicata.</param>
public sealed record ModelDescriptor(
    string Id,
    string DisplayName,
    string File,
    string Quantization,
    long SizeBytes,
    string? Sha256,
    long RamRequiredBytes,
    int RelativeCost,
    int QualityRank,
    bool Multilingual,
    string License,
    string Engine = "whisper",
    IReadOnlyList<ModelFile>? Files = null,
    CatalogSource? Source = null,
    ModelAccuracy? Accuracy = null,
    string? Attribution = null,
    bool Preview = false,
    bool RequiresLanguage = false)
{
    /// <summary>Modello fatto di più file, installato in una cartella.</summary>
    [JsonIgnore]
    public bool IsMultiFile => Files is { Count: > 0 };

    /// <summary>Senza hash fissato il modello non può essere né scaricato né verificato.</summary>
    [JsonIgnore]
    public bool IsPinned => IsMultiFile
        ? Files!.All(f => !string.IsNullOrWhiteSpace(f.Sha256))
        : !string.IsNullOrWhiteSpace(Sha256);

    /// <summary>
    /// Impronta del modello registrata nel risultato: l'SHA-256 del file, oppure per i modelli a più file
    /// l'SHA-256 dell'elenco «percorso=hash» ordinato.
    /// </summary>
    [JsonIgnore]
    public string Fingerprint
    {
        get
        {
            if (!IsMultiFile)
                return Sha256 ?? "";
            var text = string.Join('\n', Files!.OrderBy(f => f.Path, StringComparer.Ordinal).Select(f => $"{f.Path}={f.Sha256}"));
            return Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(text)));
        }
    }
}

public sealed record CatalogSource(string Repository, string? Revision, string UrlTemplate);

public sealed record CostBudget(int MinCores, int MaxCost);

public sealed record SelectionRules(
    double MaxRamFraction,
    double DiskHeadroomFraction,
    IReadOnlyList<CostBudget> CostBudgetByCores,
    int NoAvxMaxCost);

/// <summary>
/// Catalogo dei modelli incluso nell'app. È l'unica fonte di verità per URL e hash:
/// non viene mai scaricato dalla rete.
/// </summary>
public sealed class ModelCatalog
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    public ModelCatalog(CatalogSource source, SelectionRules selection, IReadOnlyList<ModelDescriptor> models)
    {
        Source = source;
        Selection = selection;
        Models = models;
    }

    public CatalogSource Source { get; }

    public SelectionRules Selection { get; }

    public IReadOnlyList<ModelDescriptor> Models { get; }

    public ModelDescriptor? Find(string id) =>
        Models.FirstOrDefault(m => string.Equals(m.Id, id, StringComparison.OrdinalIgnoreCase));

    /// <summary>Solo i modelli a file singolo si possono importare da un file: gli altri sono cartelle.</summary>
    public ModelDescriptor? FindBySha256(string sha256) =>
        Models.FirstOrDefault(m => !m.IsMultiFile && m.IsPinned && string.Equals(m.Sha256, sha256, StringComparison.OrdinalIgnoreCase));

    /// <summary>URL di download di un modello a file singolo, disponibile solo se revisione e hash sono fissati.</summary>
    public Uri? DownloadUrl(ModelDescriptor model) => DownloadUrls(model) is { Count: > 0 } urls ? urls[0].Url : null;

    /// <summary>
    /// Tutti i file da scaricare con il loro indirizzo (uno solo per i modelli a file singolo), oppure null se
    /// revisione e hash non sono fissati. L'indirizzo è sempre costruito dal catalogo, mai da dati esterni.
    /// </summary>
    public IReadOnlyList<(ModelFile File, Uri Url)>? DownloadUrls(ModelDescriptor model)
    {
        var source = model.Source ?? Source;
        if (!model.IsPinned || string.IsNullOrWhiteSpace(source.Revision))
            return null;

        Uri Build(string file) => new(source.UrlTemplate
            .Replace("{revision}", source.Revision, StringComparison.Ordinal)
            .Replace("{file}", file, StringComparison.Ordinal));

        return model.IsMultiFile
            ? model.Files!.Select(f => (f, Build(f.Path))).ToList()
            : [(new ModelFile(model.File, model.SizeBytes, model.Sha256!), Build(model.File))];
    }

    public static ModelCatalog LoadEmbedded()
    {
        using var stream = typeof(ModelCatalog).Assembly.GetManifestResourceStream("CodexVtt.Core.models-catalog.json")
            ?? throw new InvalidOperationException("Catalogo modelli non incluso nell'assembly.");
        return Load(stream);
    }

    public static ModelCatalog Load(Stream json)
    {
        var dto = JsonSerializer.Deserialize<CatalogDto>(json, JsonOptions)
            ?? throw new InvalidDataException("Catalogo modelli vuoto.");
        if (dto.Models.Count == 0)
            throw new InvalidDataException("Catalogo modelli senza voci.");
        if (dto.Models.Select(m => m.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count() != dto.Models.Count)
            throw new InvalidDataException("Id duplicati nel catalogo modelli.");
        foreach (var model in dto.Models.Where(m => m.IsMultiFile))
        {
            if (model.Files!.Sum(f => f.SizeBytes) != model.SizeBytes)
                throw new InvalidDataException($"La dimensione del modello {model.Id} non è la somma dei suoi file.");
            if (model.Files!.Any(f => f.Path.Contains("..", StringComparison.Ordinal) || System.IO.Path.IsPathRooted(f.Path)))
                throw new InvalidDataException($"Percorso non ammesso nei file del modello {model.Id}.");
        }
        return new ModelCatalog(dto.Source, dto.Selection, dto.Models);
    }

    private sealed record CatalogDto(int Version, CatalogSource Source, SelectionRules Selection, List<ModelDescriptor> Models);
}
