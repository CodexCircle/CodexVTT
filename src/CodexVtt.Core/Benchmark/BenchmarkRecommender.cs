using CodexVtt.Core.Models;

namespace CodexVtt.Core.Benchmark;

/// <summary>Modello consigliato dalle misure del Test bench, con i numeri su cui si basa.</summary>
public sealed record BenchmarkRecommendation(ModelDescriptor Model, BenchmarkEntry Entry, bool ByAccuracy);

/// <summary>
/// Consiglia un modello dalle misure del Test bench invece che dalle regole sull'hardware. Funzione pura.
/// Regole: contano solo i modelli provati e utilizzabili (velocità almeno pari al tempo reale); le anteprime non si
/// consigliano mai da sole. Se le misure hanno l'affidabilità vince la più alta, e a parità (entro mezzo punto) la
/// più veloce; senza affidabilità (file proprio) vince il modello di qualità più alta, e a parità il più veloce.
/// Se nessun modello regge il tempo reale si consiglia il più veloce.
/// </summary>
public static class BenchmarkRecommender
{
    /// <summary>Sotto questa velocità (audio al secondo) un modello è più lento dell'audio stesso.</summary>
    public const double MinUsableSpeed = 1.0;

    private const double AccuracyTolerance = 0.5;

    public static BenchmarkRecommendation? Pick(IEnumerable<(BenchmarkEntry Entry, ModelDescriptor Model)> measured)
    {
        var all = measured.Where(m => !m.Model.Preview && m.Entry.Speed > 0).ToList();
        if (all.Count == 0)
            return null;

        var usable = all.Where(m => m.Entry.Speed >= MinUsableSpeed).ToList();
        if (usable.Count == 0)
        {
            var fastest = all.OrderByDescending(m => m.Entry.Speed).First();
            return new BenchmarkRecommendation(fastest.Model, fastest.Entry, ByAccuracy: false);
        }

        var withAccuracy = usable.Where(m => m.Entry.Accuracy is not null).ToList();
        if (withAccuracy.Count > 0)
        {
            var top = withAccuracy.Max(m => m.Entry.Accuracy!.Value);
            var best = withAccuracy
                .Where(m => top - m.Entry.Accuracy!.Value <= AccuracyTolerance)
                .OrderByDescending(m => m.Entry.Speed)
                .First();
            return new BenchmarkRecommendation(best.Model, best.Entry, ByAccuracy: true);
        }

        var byQuality = usable.OrderByDescending(m => m.Model.QualityRank).ThenByDescending(m => m.Entry.Speed).First();
        return new BenchmarkRecommendation(byQuality.Model, byQuality.Entry, ByAccuracy: false);
    }
}
