namespace CodexVtt.Core.Audio;

/// <summary>Intervallo di byte di una richiesta HTTP <c>Range: bytes=…</c>, con estremi inclusi.</summary>
public readonly record struct ByteRange(long Start, long End)
{
    public long Length => End - Start + 1;

    /// <summary>
    /// Interpreta l'header Range (un solo intervallo) rispetto a un file di <paramref name="length"/> byte.
    /// Senza header (o con un formato non supportato) restituisce true e null: si serve tutto il file.
    /// Se l'intervallo non è soddisfacibile restituisce false (risposta 416).
    /// </summary>
    public static bool TryParse(string? header, long length, out ByteRange? range)
    {
        range = null;
        if (string.IsNullOrWhiteSpace(header) || !header.StartsWith("bytes=", StringComparison.OrdinalIgnoreCase) || header.Contains(','))
            return true;

        var spec = header["bytes=".Length..].Trim();
        var dash = spec.IndexOf('-', StringComparison.Ordinal);
        if (dash < 0)
            return true;

        var first = spec[..dash].Trim();
        var last = spec[(dash + 1)..].Trim();
        if (first.Length == 0)
        {
            // "-N": gli ultimi N byte.
            if (!long.TryParse(last, out var suffix) || suffix <= 0)
                return false;
            if (length == 0)
                return false;
            range = new ByteRange(Math.Max(0, length - suffix), length - 1);
            return true;
        }

        if (!long.TryParse(first, out var start) || start < 0)
            return true;
        if (start >= length)
            return false;

        long end = length - 1;
        if (last.Length > 0)
        {
            if (!long.TryParse(last, out var parsed) || parsed < start)
                return true;
            end = Math.Min(parsed, length - 1);
        }

        range = new ByteRange(start, end);
        return true;
    }
}
