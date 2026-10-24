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
        new("it-clean-2", "it", BenchmarkScenario.Clean, "Il giornalino di Gian Burrasca, 7 novembre (LibriVox)", "gianburrasca_024_vamba_64kb.mp3",
            "https://archive.org/download/il_giornalino_di_gian_burrasca_0910_librivox1/gianburrasca_024_vamba_64kb.mp3",
            1_611_825, "0e1e58fb0824fdb9974c57f24bfe939ae797fe8762071a741035b110f4d6ca60", 201, BenchmarkClipTexts.GianBurrasca7Novembre),
        new("it-clean-3", "it", BenchmarkScenario.Clean, "Il delitto di Lord Savile, capitolo 6 (LibriVox)", "fantasmacantervilledelittolordsavile_13_wilde_64kb.mp3",
            "https://archive.org/download/fantasmadelitto_2102_librivox/fantasmacantervilledelittolordsavile_13_wilde_64kb.mp3",
            1_788_397, "9196bf6bc5e46fa7dc2ab1382975d66ed18be9ed3f59fc1aeee82e84c1496c55", 223, BenchmarkClipTexts.DelittoLordSavileCapitolo6),
        new("en-clean-1", "en", BenchmarkScenario.Clean, "Unbeaten Tracks in Japan, Letter XXXIV (LibriVox)", "unbeatentracksjapan_41_bird_64kb.mp3",
            "https://archive.org/download/unbeaten_tracks_japan_ava_librivox/unbeatentracksjapan_41_bird_64kb.mp3",
            1_801_728, "c45d96a6c69278a2c6c8f2c836f337d5e52228caff25f8aa45c5d436727f3a8f", 225, BenchmarkClipTexts.UnbeatenTracksLetter34),
        new("en-clean-2", "en", BenchmarkScenario.Clean, "The Wonderful Wizard of Oz, chapter 21 (LibriVox)", "wizardofoz_21_baum_64kb.mp3",
            "https://archive.org/download/wizard_of_oz/wizardofoz_21_baum_64kb.mp3",
            2_673_121, "99e4729c9b77706aeb1b00488d7700c39b882900ea938a5fbb34ac7108919437", 334, BenchmarkClipTexts.WizardOfOzChapter21),
        new("en-noisy-1", "en", BenchmarkScenario.Noisy, "Unbeaten Tracks in Japan, Letter XXXIV, with noise", "unbeatentracksjapan_41_bird_64kb.mp3",
            "https://archive.org/download/unbeaten_tracks_japan_ava_librivox/unbeatentracksjapan_41_bird_64kb.mp3",
            1_801_728, "c45d96a6c69278a2c6c8f2c836f337d5e52228caff25f8aa45c5d436727f3a8f", 225, BenchmarkClipTexts.UnbeatenTracksLetter34,
            NoiseSourceId: "en-clean-1", NoiseSnrDb: NoiseSnrDb),
    ];

    public static BenchmarkClip? Find(string id) => All.FirstOrDefault(c => c.Id == id);

    /// <summary>Il brano che fornisce il file: il brano stesso, o quello da cui deriva il rumoroso.</summary>
    public static BenchmarkClip SourceOf(BenchmarkClip clip) =>
        clip.NoiseSourceId is { } id ? Find(id) ?? throw new InvalidOperationException($"Brano di origine mancante: {id}") : clip;

    /// <summary>I file da scaricare, senza doppioni (un brano rumoroso condivide il file del suo originale).</summary>
    public static IReadOnlyList<BenchmarkClip> Downloads() => All.Select(SourceOf).DistinctBy(c => c.Id).ToList();
}
