namespace CodexVtt.Core.Benchmark;

/// <summary>
/// File di riferimento ufficiale del Test bench: sempre lo stesso, con indirizzo, dimensione e SHA-256 fissati qui.
/// È la lettura in italiano del capitolo 6 de «Le avventure di Pinocchio» di Carlo Collodi (voce di Simona), registrazione
/// LibriVox dedicata al pubblico dominio (CC0/Public Domain Mark; testo di Collodi, morto nel 1890). Ospitata su archive.org,
/// l'archivio che già distribuisce le registrazioni LibriVox. Non viene incluso nell'app né scaricato senza un'azione esplicita.
/// </summary>
public static class BenchmarkReference
{
    public const string Url = "https://archive.org/download/avventure_pinocchio_librivox/avventurepinocchio_06_collodi_64kb.mp3";

    public const string FileName = "avventurepinocchio_06_collodi_64kb.mp3";

    public const long SizeBytes = 1_764_750;

    public const string Sha256 = "4d0f7bfe60934ce8c7490c3525ab118a55e31f2fe26bcb8d11e91ec1bc25f148";

    public const string Title = "Le avventure di Pinocchio, capitolo 6 (LibriVox)";

    public const string License = "Pubblico dominio";

    /// <summary>Durata circa 3 min 40 s: dentro i limiti del Test bench.</summary>
    public const double ApproximateSeconds = 220.55;

    public const string Host = "archive.org";
}

/// <summary>Fornisce il file di riferimento ufficiale. L'implementazione che usa la rete sta in CodexVtt.Models.Download (ADR-0006).</summary>
public interface IReferenceFileProvider
{
    /// <summary>Percorso del file già scaricato e verificato, o null se non c'è.</summary>
    string? CachedPath();

    /// <summary>Scarica il file (se manca) dall'indirizzo fissato, ne verifica dimensione e SHA-256 e restituisce il percorso. Solo su azione esplicita.</summary>
    Task<string> EnsureAsync(CancellationToken ct);
}
