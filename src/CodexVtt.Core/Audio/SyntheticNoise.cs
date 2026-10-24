namespace CodexVtt.Core.Audio;

/// <summary>
/// Aggiunge rumore rosa (energia più forte sui toni bassi, come un ambiente con ventole e traffico) a un parlato, a un rapporto
/// segnale/rumore fissato in dB. Deterministico: stesso parlato, stesso rapporto e stesso seme danno sempre lo stesso risultato.
/// Funzione pura, usata dal Test bench per misurare i modelli con audio non perfetto senza file protetti da diritti.
/// </summary>
public static class SyntheticNoise
{
    public static float[] Mix(ReadOnlySpan<float> speech, double snrDb, int seed)
    {
        var mixed = speech.ToArray();
        if (mixed.Length == 0)
            return mixed;

        var rng = new XorShift((ulong)seed | 1UL);
        var noise = new double[mixed.Length];
        // Filtro di Paul Kellet per il rumore rosa a partire da rumore bianco gaussiano.
        double b0 = 0, b1 = 0, b2 = 0, b3 = 0, b4 = 0, b5 = 0, b6 = 0;
        for (var i = 0; i < noise.Length; i++)
        {
            var white = Gaussian(rng);
            b0 = (0.99886 * b0) + (white * 0.0555179);
            b1 = (0.99332 * b1) + (white * 0.0750759);
            b2 = (0.96900 * b2) + (white * 0.1538520);
            b3 = (0.86650 * b3) + (white * 0.3104856);
            b4 = (0.55000 * b4) + (white * 0.5329522);
            b5 = (-0.7616 * b5) - (white * 0.0168980);
            noise[i] = b0 + b1 + b2 + b3 + b4 + b5 + b6 + (white * 0.5362);
            b6 = white * 0.115926;
        }

        var speechPower = Power(mixed);
        var noisePower = Power(noise);
        if (speechPower <= 0 || noisePower <= 0)
            return mixed;

        var gain = Math.Sqrt(speechPower / (noisePower * Math.Pow(10, snrDb / 10)));
        var peak = 0.0;
        for (var i = 0; i < mixed.Length; i++)
        {
            var sample = mixed[i] + (noise[i] * gain);
            mixed[i] = (float)sample;
            peak = Math.Max(peak, Math.Abs(sample));
        }

        // Senza tagli: se la somma supera il fondo scala si abbassa tutto, il rapporto non cambia.
        if (peak > 0.99)
        {
            var scale = (float)(0.99 / peak);
            for (var i = 0; i < mixed.Length; i++)
                mixed[i] *= scale;
        }

        return mixed;
    }

    private static double Gaussian(XorShift rng)
    {
        // Box-Muller; 1 - NextDouble() evita il logaritmo di zero.
        var u1 = 1.0 - rng.NextDouble();
        var u2 = rng.NextDouble();
        return Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2);
    }

    /// <summary>Generatore minimo e fisso (xorshift64*): lo stesso seme dà gli stessi numeri su ogni piattaforma e versione di .NET.</summary>
    private sealed class XorShift(ulong state)
    {
        private ulong _state = state;

        public double NextDouble()
        {
            _state ^= _state >> 12;
            _state ^= _state << 25;
            _state ^= _state >> 27;
            var value = _state * 0x2545F4914F6CDD1DUL;
            return (value >> 11) * (1.0 / (1UL << 53));
        }
    }

    private static double Power(ReadOnlySpan<float> samples)
    {
        double sum = 0;
        foreach (var s in samples)
            sum += (double)s * s;
        return sum / samples.Length;
    }

    private static double Power(double[] samples)
    {
        double sum = 0;
        foreach (var s in samples)
            sum += s * s;
        return sum / samples.Length;
    }
}
