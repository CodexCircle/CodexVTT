namespace CodexVtt.Core.Benchmark;

/// <summary>Stima del tempo di una coda dalla misura di un modello: <c>fisso + durata / velocità</c>. Funzione pura.</summary>
public static class BenchmarkEstimator
{
    /// <summary>Secondi stimati per trascrivere file di queste durate (in secondi) in una sola coda, con il modello caricato una volta.</summary>
    public static double EstimateSeconds(BenchmarkEntry entry, IEnumerable<double> durationsSeconds)
    {
        var total = 0.0;
        var any = false;
        foreach (var d in durationsSeconds)
        {
            if (d <= 0 || double.IsNaN(d) || double.IsInfinity(d))
                continue;
            total += d;
            any = true;
        }

        if (!any || entry.Speed <= 0 || double.IsNaN(entry.Speed) || double.IsInfinity(entry.Speed))
            return 0;
        return Math.Max(0, entry.LoadSeconds) + (total / entry.Speed);
    }

    /// <summary>
    /// Ricava velocità e tempo fisso da una trascrizione misurata. <paramref name="loadSeconds"/> è il tempo fino al primo
    /// avanzamento reale; il resto del tempo è lavoro sull'audio. Null se la misura non è utilizzabile.
    /// </summary>
    public static (double LoadSeconds, double Speed)? Measure(double audioSeconds, double elapsedSeconds, double loadSeconds)
    {
        if (audioSeconds <= 0 || elapsedSeconds <= 0)
            return null;
        var load = Math.Clamp(loadSeconds, 0, elapsedSeconds);
        var work = elapsedSeconds - load;
        // Un lavoro troppo breve per essere misurato non dà una velocità attendibile: si usa il tempo totale.
        if (work < 0.05 * elapsedSeconds || work < 0.05)
        {
            load = 0;
            work = elapsedSeconds;
        }

        return (load, audioSeconds / work);
    }
}
