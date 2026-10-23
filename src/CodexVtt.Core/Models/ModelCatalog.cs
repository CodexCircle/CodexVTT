using System.Text.Json;
using System.Text.Json.Serialization;

namespace CodexVtt.Core.Models;

/// <summary>Voce del catalogo dei modelli (ADR-0003).</summary>
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
    string License)
{
    /// <summary>Senza hash fissato il modello non può essere né scaricato né verificato.</summary>
    [JsonIgnore]
    public bool IsPinned => !string.IsNullOrWhiteSpace(Sha256);
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

    public ModelDescriptor? FindBySha256(string sha256) =>
        Models.FirstOrDefault(m => m.IsPinned && string.Equals(m.Sha256, sha256, StringComparison.OrdinalIgnoreCase));

    /// <summary>URL di download, disponibile solo se revisione e hash sono fissati.</summary>
    public Uri? DownloadUrl(ModelDescriptor model)
    {
        if (!model.IsPinned || string.IsNullOrWhiteSpace(Source.Revision))
            return null;
        return new Uri(Source.UrlTemplate
            .Replace("{revision}", Source.Revision, StringComparison.Ordinal)
            .Replace("{file}", model.File, StringComparison.Ordinal));
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
        return new ModelCatalog(dto.Source, dto.Selection, dto.Models);
    }

    private sealed record CatalogDto(int Version, CatalogSource Source, SelectionRules Selection, List<ModelDescriptor> Models);
}
