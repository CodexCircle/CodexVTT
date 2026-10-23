using System.Text;

namespace CodexVtt.Core.Evaluation;

/// <summary>
/// Word error rate per confrontare i modelli sullo stesso testo di riferimento (ADR-0009). Il testo si normalizza
/// allo stesso modo per riferimento e ipotesi: minuscolo, senza punteggiatura, apostrofi come spazi.
/// L'«affidabilità» mostrata nell'app è 100 meno il WER in percentuale.
/// </summary>
public static class WordErrorRate
{
    public static string Normalize(string text)
    {
        var builder = new StringBuilder(text.Length);
        var lastSpace = true;
        foreach (var c in text.Normalize(NormalizationForm.FormC).ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(c))
            {
                builder.Append(c);
                lastSpace = false;
            }
            else if (!lastSpace)
            {
                builder.Append(' ');
                lastSpace = true;
            }
        }

        return builder.ToString().Trim();
    }

    public static string[] Words(string text)
    {
        var normalized = Normalize(text);
        return normalized.Length == 0 ? [] : normalized.Split(' ');
    }

    /// <summary>Distanza di Levenshtein tra le parole divisa per le parole del riferimento (0 = identico).</summary>
    public static double Compute(string reference, string hypothesis)
    {
        var r = Words(reference);
        var h = Words(hypothesis);
        if (r.Length == 0)
            return h.Length == 0 ? 0 : 1;

        var previous = new int[h.Length + 1];
        var current = new int[h.Length + 1];
        for (var j = 0; j <= h.Length; j++)
            previous[j] = j;

        for (var i = 1; i <= r.Length; i++)
        {
            current[0] = i;
            for (var j = 1; j <= h.Length; j++)
            {
                var substitution = previous[j - 1] + (string.Equals(r[i - 1], h[j - 1], StringComparison.Ordinal) ? 0 : 1);
                current[j] = Math.Min(substitution, Math.Min(previous[j] + 1, current[j - 1] + 1));
            }

            (previous, current) = (current, previous);
        }

        return (double)previous[h.Length] / r.Length;
    }
}
