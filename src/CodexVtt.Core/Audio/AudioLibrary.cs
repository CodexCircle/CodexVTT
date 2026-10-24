using System.Collections.Concurrent;
using CodexVtt.Core.Errors;

namespace CodexVtt.Core.Audio;

public sealed record AudioHandle(string AudioId, AudioInfo Info);

/// <summary>
/// Registro dei file audio scelti dall'utente. La UI riceve solo un <c>audioId</c> opaco, mai un
/// percorso libero (ADR-0001), e i file vengono validati prima di essere accettati.
/// </summary>
public sealed class AudioLibrary
{
    private readonly IReadOnlyList<IAudioDecoder> _decoders;
    private readonly TimeSpan _maxDuration;
    private readonly ConcurrentDictionary<string, (string Path, AudioInfo Info, IAudioDecoder Decoder)> _items = new();

    public AudioLibrary(IEnumerable<IAudioDecoder> decoders, TimeSpan maxDuration)
    {
        _decoders = decoders.ToList();
        _maxDuration = maxDuration;
    }

    public AudioHandle Register(string path)
    {
        if (!File.Exists(path))
            throw new CodexVttException(ErrorCodes.AudioNotFound, "File audio non trovato.");
        if (new FileInfo(path).Length == 0)
            throw new CodexVttException(ErrorCodes.AudioEmpty, "Il file audio è vuoto.");

        var decoder = _decoders.FirstOrDefault(d => d.CanDecode(path))
            ?? throw new CodexVttException(ErrorCodes.AudioUnsupported, "Formato audio non supportato.",
                new Dictionary<string, string> { ["extension"] = Path.GetExtension(path) });

        var info = decoder.Inspect(path);
        if (info.Duration <= TimeSpan.Zero)
            throw new CodexVttException(ErrorCodes.AudioEmpty, "Il file audio non contiene campioni.");
        if (info.Duration > _maxDuration)
        {
            throw new CodexVttException(ErrorCodes.AudioTooLong, "Il file audio supera la durata massima.",
                new Dictionary<string, string> { ["maxSeconds"] = ((int)_maxDuration.TotalSeconds).ToString(System.Globalization.CultureInfo.InvariantCulture) });
        }

        var id = Guid.NewGuid().ToString("N");
        _items[id] = (Path.GetFullPath(path), info, decoder);
        return new AudioHandle(id, info);
    }

    public AudioInfo Info(string audioId) => Get(audioId).Info;

    public IPcmReader Open(string audioId)
    {
        var item = Get(audioId);
        return item.Decoder.OpenPcm16kMono(item.Path);
    }

    /// <summary>Percorso del file scelto: serve alla cronologia per riascoltarlo, non arriva mai alla UI.</summary>
    public string PathOf(string audioId) => Get(audioId).Path;

    public string SuggestedExportName(string audioId) => Path.GetFileNameWithoutExtension(Get(audioId).Path);

    private (string Path, AudioInfo Info, IAudioDecoder Decoder) Get(string audioId) =>
        _items.TryGetValue(audioId, out var item)
            ? item
            : throw new CodexVttException(ErrorCodes.AudioNotFound, "Audio non registrato.");
}
