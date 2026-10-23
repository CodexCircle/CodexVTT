namespace CodexVtt.Core.Transcription;

/// <summary>
/// Limite di utilizzo della CPU scelto dall'utente. Si traduce nel numero di thread dei motori: con N% su C core
/// si usano al più floor(C × N / 100) thread (almeno 1), quindi l'uso resta sotto la percentuale scelta.
/// Il 100% non limita nulla e lascia il comportamento predefinito (un core libero per il sistema).
/// </summary>
public static class CpuLimit
{
    public const int MinPercent = 10;

    public const int MaxPercent = 100;

    public static bool IsValid(int percent) => percent is >= MinPercent and <= MaxPercent;

    /// <summary>Numero di thread da usare, oppure null se non c'è nessun limite (100%).</summary>
    public static int? Threads(int percent, int logicalCores)
    {
        if (percent >= MaxPercent)
            return null;
        return Math.Max(1, (int)Math.Floor(logicalCores * Math.Max(percent, MinPercent) / 100.0));
    }
}
