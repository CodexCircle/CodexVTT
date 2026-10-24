using CodexVtt.Core.Models;

namespace CodexVtt.Core.Benchmark;

/// <summary>Modello consigliato dalle misure del Test bench, con i numeri su cui si basa.</summary>
public sealed record BenchmarkRecommendation(ModelDescriptor Model, BenchmarkEntry Entry, bool ByAccuracy, double? Accuracy = null);

/// <summary>
/// Consiglia un modello dalle misure del Test bench invece che dalle regole sull'hardware. Funzione pura.
/// Regole: contano solo i modelli provati e utilizzabili (velocità almeno pari al tempo reale); le anteprime non si
/// consigliano mai da sole. Se le misure hanno l'affidabilità vince quella più alta per la lingua scelta (con lingua
/// automatica, la media delle lingue misurate), e a parità (entro mezzo punto) il più veloce; senza affidabilità (file
/// proprio) vince il modello di qualità più alta, e a parità il più veloce. Se nessun modello regge il tempo reale si
/// consiglia il più veloce.
/// </summary>
public static class BenchmarkRecommender
{
    /// <summary>Sotto questa velocità (audio al secondo) un modello è più lento dell'audio stesso.</summary>
    public const double MinUsableSpeed = 1.0;

    private const double AccuracyTolerance = 0.5;

    /// <param name="measured">Le misure valide, una per modello e brano.</param>
    /// <param name="language">La lingua scelta (it, en), o null con lingua automatica.</param>
    public static BenchmarkRecommendation? Pick(IEnumerable<(BenchmarkEntry Entry, ModelDescriptor Model)> measured, string? language = null)
    {
        var all = measured
            .Where(m => !m.Model.Preview && m.Entry.Speed > 0)
            .GroupBy(m => m.Model.Id)
            .Select(g => (Model: g.First().Model, Entries: g.Select(m => m.Entry).ToList()))
            .Select(g => (g.Model, Entry: BenchmarkStore.Combine(g.Entries)!, Accuracy: BenchmarkReliability.Score(g.Entries, language)))
            .ToList();
        if (all.Count == 0)
            return null;

        var usable = all.Where(m => m.Entry.Speed >= MinUsableSpeed).ToList();
        if (usable.Count == 0)
        {
            var fastest = all.OrderByDescending(m => m.Entry.Speed).First();
            return new BenchmarkRecommendation(fastest.Model, fastest.Entry, ByAccuracy: false);
        }

        var withAccuracy = usable.Where(m => m.Accuracy is not null).ToList();
        if (withAccuracy.Count > 0)
        {
            var top = withAccuracy.Max(m => m.Accuracy!.Value);
            var best = withAccuracy
                .Where(m => top - m.Accuracy!.Value <= AccuracyTolerance)
                .OrderByDescending(m => m.Entry.Speed)
                .First();
            return new BenchmarkRecommendation(best.Model, best.Entry, ByAccuracy: true, best.Accuracy);
        }

        var byQuality = usable.OrderByDescending(m => m.Model.QualityRank).ThenByDescending(m => m.Entry.Speed).First();
        return new BenchmarkRecommendation(byQuality.Model, byQuality.Entry, ByAccuracy: false);
    }
}
