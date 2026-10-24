using System.Security.Cryptography;
using System.Text;

namespace CodexVtt.Core.Benchmark;

/// <summary>
/// Versione del Test bench: una misura salvata vale finché questa versione non cambia. Non segue la versione dell'app, così un
/// aggiornamento qualsiasi non obbliga a rifare il test. Cambia da sola quando cambiano i brani (file, testi, rumore);
/// <see cref="Revision"/> si alza a mano quando cambia qualcosa che cambia i numeri senza toccare i brani.
/// </summary>
public static class BenchmarkVersion
{
    /// <summary>
    /// Da alzare di 1 quando cambia il modo di misurare: calcolo dell'affidabilità (<see cref="Evaluation.WordErrorRate"/>) o della velocità,
    /// generazione del rumore, preparazione dell'audio, motore di trascrizione o sua versione. Non va alzata per modifiche
    /// all'interfaccia, ai testi o ad altre parti dell'app che non incidono sulle misure.
    /// </summary>
    public const int Revision = 1;

    /// <summary>
    /// Versioni dei motori di trascrizione (Whisper.net e sherpa-onnx) di cui la <see cref="Revision"/> attuale tiene conto.
    /// Un test confronta questa riga con Directory.Packages.props: se aggiorni un motore il test fallisce, e va alzata la
    /// revisione (le velocità cambiano) e riscritta questa riga.
    /// </summary>
    public const string CoveredEngines = "Whisper.net=1.9.1;sherpa-onnx=2.0.0";

    /// <summary>Revisione e impronta dei brani, per esempio <c>1-3fa9c2d1</c>.</summary>
    public static string Current { get; } = Compute();

    private static string Compute()
    {
        var builder = new StringBuilder();
        builder.Append(Revision).Append('|').Append(BenchmarkClips.NoiseSeed).Append('|').Append(BenchmarkClips.NoiseSnrDb);
        foreach (var clip in BenchmarkClips.All.OrderBy(c => c.Id, StringComparer.Ordinal))
            builder.Append('|').Append(clip.Id).Append(':').Append(clip.Sha256).Append(':').Append(clip.NoiseSourceId).Append(':').Append(clip.ReferenceText);
        var hash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString())));
        return $"{Revision}-{hash[..8]}";
    }
}
