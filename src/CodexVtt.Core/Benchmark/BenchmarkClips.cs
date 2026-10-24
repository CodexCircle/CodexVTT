namespace CodexVtt.Core.Benchmark;

/// <summary>Condizione di ascolto del brano: lettura pulita o la stessa lettura con rumore sintetico aggiunto.</summary>
public enum BenchmarkScenario
{
    Clean,
    Noisy,
}

/// <summary>
/// Brano del Test bench ufficiale. Il file è sempre lo stesso (indirizzo, dimensione e SHA-256 fissati nel codice) e il testo
/// parlato è trascritto a mano, così l'affidabilità di ogni modello si misura contro un riferimento esatto. Un brano
/// «rumoroso» non ha un file suo: usa quello del brano <paramref name="NoiseSourceId"/> con rumore rosa aggiunto dall'app
/// a un rapporto segnale/rumore fisso (<paramref name="NoiseSnrDb"/>), quindi il testo è lo stesso e non servono diritti nuovi.
/// </summary>
public sealed record BenchmarkClip(
    string Id,
    string Language,
    BenchmarkScenario Scenario,
    string Title,
    string FileName,
    string Url,
    long SizeBytes,
    string Sha256,
    double ApproximateSeconds,
    string ReferenceText,
    string? NoiseSourceId = null,
    double NoiseSnrDb = 0)
{
    public bool IsDerived => NoiseSourceId is not null;

    /// <summary>Il nome dello scenario nelle misure salvate e nella UI: clean o noisy.</summary>
    public string ScenarioKey => Scenario == BenchmarkScenario.Noisy ? "noisy" : "clean";
}

/// <summary>Elenco dei brani ufficiali. Aggiungerne uno è solo un dato: serve il file di pubblico dominio e il suo testo.</summary>
public static class BenchmarkClips
{
    /// <summary>Rapporto segnale/rumore dei brani rumorosi: il parlato è circa 3 volte più forte del rumore.</summary>
    public const double NoiseSnrDb = 10;

    /// <summary>Seme fisso: lo stesso brano rumoroso è identico a ogni test e su ogni PC.</summary>
    public const int NoiseSeed = 20241024;

    public static IReadOnlyList<string> Languages { get; } = ["it", "en"];

    public static IReadOnlyList<BenchmarkClip> All { get; } =
    [
        new("it-clean-1", "it", BenchmarkScenario.Clean, BenchmarkReference.Title, BenchmarkReference.FileName, BenchmarkReference.Url,
            BenchmarkReference.SizeBytes, BenchmarkReference.Sha256, BenchmarkReference.ApproximateSeconds, BenchmarkReferenceText.Text),
        new("it-noisy-1", "it", BenchmarkScenario.Noisy, "Le avventure di Pinocchio, capitolo 6, con rumore", BenchmarkReference.FileName, BenchmarkReference.Url,
            BenchmarkReference.SizeBytes, BenchmarkReference.Sha256, BenchmarkReference.ApproximateSeconds, BenchmarkReferenceText.Text,
            NoiseSourceId: "it-clean-1", NoiseSnrDb: NoiseSnrDb),
    ];

    public static BenchmarkClip? Find(string id) => All.FirstOrDefault(c => c.Id == id);

    /// <summary>Il brano che fornisce il file: il brano stesso, o quello da cui deriva il rumoroso.</summary>
    public static BenchmarkClip SourceOf(BenchmarkClip clip) =>
        clip.NoiseSourceId is { } id ? Find(id) ?? throw new InvalidOperationException($"Brano di origine mancante: {id}") : clip;

    /// <summary>I file da scaricare, senza doppioni (un brano rumoroso condivide il file del suo originale).</summary>
    public static IReadOnlyList<BenchmarkClip> Downloads() => All.Select(SourceOf).DistinctBy(c => c.Id).ToList();
}
