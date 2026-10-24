namespace CodexVtt.Core.Benchmark;

/// <summary>
/// Misura di un modello sul file di riferimento. <c>LoadSeconds</c> è il tempo fisso (caricamento del modello e prima finestra),
/// <c>Speed</c> i secondi di audio per secondo reale dopo il caricamento (4 = quattro volte il tempo reale).
/// <c>Accuracy</c> è l'affidabilità in percentuale (100 meno il tasso di errore sulle parole contro il testo del file ufficiale);
/// null con un file scelto dall'utente, di cui non c'è un testo di riferimento.
/// </summary>
public sealed record BenchmarkEntry(
    string ModelId,
    string ModelIdentity,
    string Runtime,
    int? Threads,
    double AudioSeconds,
    double LoadSeconds,
    double Speed,
    DateTimeOffset MeasuredAt,
    string AppVersion,
    double? Accuracy = null);

/// <summary>Contenuto di benchmark.json: le misure valgono per l'impronta dell'hardware con cui sono state fatte.</summary>
public sealed record BenchmarkFile(int Version, string Fingerprint, IReadOnlyList<BenchmarkEntry> Entries)
{
    public const int CurrentVersion = 1;

    public static BenchmarkFile Empty { get; } = new(CurrentVersion, "", []);
}

/// <summary>Perché una misura non vale più.</summary>
public enum BenchmarkStaleReason
{
    None,
    HardwareChanged,
    ModelChanged,
    RuntimeChanged,
}

/// <summary>Misura con il suo stato di validità, per mostrarla nella UI come «da rifare».</summary>
public sealed record BenchmarkView(BenchmarkEntry Entry, BenchmarkStaleReason Stale)
{
    public bool IsValid => Stale == BenchmarkStaleReason.None;
}
