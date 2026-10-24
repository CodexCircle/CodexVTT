namespace CodexVtt.Core.Audio;

/// <summary>Tipi MIME con cui l'audio originale viene riprodotto nell'editor; solo formati noti, mai file qualsiasi.</summary>
public static class AudioMedia
{
    private static readonly Dictionary<string, string> Types = new(StringComparer.OrdinalIgnoreCase)
    {
        [".mp3"] = "audio/mpeg",
        [".wav"] = "audio/wav",
        [".m4a"] = "audio/mp4",
        [".aac"] = "audio/aac",
        [".mp4"] = "audio/mp4",
        [".ogg"] = "audio/ogg",
        [".oga"] = "audio/ogg",
        [".opus"] = "audio/ogg",
        [".flac"] = "audio/flac",
        [".webm"] = "audio/webm",
        [".wma"] = "audio/x-ms-wma",
    };

    /// <summary>Tipo MIME dell'estensione del file, o null se non è un audio da riprodurre.</summary>
    public static string? ContentType(string path) => Types.GetValueOrDefault(Path.GetExtension(path));
}
