namespace CodexVtt.Core.Benchmark;

/// <summary>
/// Affidabilità di un modello in una lingua: la media sui brani puliti, la media sui brani rumorosi e la media delle due.
/// I due scenari pesano uguale, qualunque sia il numero di brani di ciascuno (altrimenti il pulito peserebbe il doppio).
/// Con un solo scenario misurato vale quello. Tutti i valori sono 100 meno il tasso di errore sulle parole, in percentuale.
/// </summary>
public sealed record LanguageReliability(string Language, double? Clean, double? Noisy, double? Overall);

/// <summary>Calcolo dell'affidabilità dalle misure del Test bench. Funzioni pure.</summary>
public static class BenchmarkReliability
{
    /// <summary>L'affidabilità del modello per ogni lingua misurata, in ordine di lingua.</summary>
    public static IReadOnlyList<LanguageReliability> ByLanguage(IEnumerable<BenchmarkEntry> modelEntries) =>
        modelEntries
            .Where(e => e.Accuracy is not null && e.Language is not null && e.Scenario is not null)
            .GroupBy(e => e.Language!)
            .OrderBy(g => g.Key, StringComparer.Ordinal)
            .Select(g =>
            {
                var clean = Mean(g.Where(e => e.Scenario == "clean"));
                var noisy = Mean(g.Where(e => e.Scenario == "noisy"));
                return new LanguageReliability(g.Key, clean, noisy, Mean(new[] { clean, noisy }.Where(v => v is not null).Select(v => v!.Value)));
            })
            .ToList();

    /// <summary>
    /// L'affidabilità da usare per consigliare un modello: quella della lingua indicata, o con lingua automatica (null) la media
    /// delle lingue misurate. Null se per quella lingua non ci sono misure di affidabilità.
    /// </summary>
    public static double? Score(IEnumerable<BenchmarkEntry> modelEntries, string? language)
    {
        var languages = ByLanguage(modelEntries);
        if (language is not null)
            return languages.FirstOrDefault(l => l.Language == language)?.Overall;
        return Mean(languages.Where(l => l.Overall is not null).Select(l => l.Overall!.Value));
    }

    private static double? Mean(IEnumerable<BenchmarkEntry> entries) => Mean(entries.Select(e => e.Accuracy!.Value));

    private static double? Mean(IEnumerable<double> values)
    {
        var list = values.ToList();
        return list.Count == 0 ? null : Math.Round(list.Average(), 1);
    }
}
