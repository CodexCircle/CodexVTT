using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using NAudio.Wave;
using CodexVtt.Core.Audio;
using CodexVtt.Core.Errors;

namespace CodexVtt.Audio.Windows;

/// <summary>
/// Decodifica tramite Windows Media Foundation (ADR-0004): usa i codec del sistema operativo,
/// senza distribuire librerie di terzi. Il ricampionamento a 16 kHz mono lo esegue il resampler MF.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class MediaFoundationDecoder : IAudioDecoder
{
    /// <summary>Formati dichiarati in V1 (specifica #2). OGG/Opus dipendono dai codec installati.</summary>
    public static readonly IReadOnlySet<string> SupportedExtensions =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".wav", ".mp3", ".m4a", ".aac", ".wma", ".flac", ".ogg", ".opus" };

    // HRESULT di Media Foundation per formati o codec non disponibili.
    private const int MfEUnsupportedByteStreamType = unchecked((int)0xC00D36C4);
    private const int MfEInvalidMediaType = unchecked((int)0xC00D36B4);
    private const int MfETopoCodecNotFound = unchecked((int)0xC00D5212);

    public bool CanDecode(string path) => SupportedExtensions.Contains(Path.GetExtension(path));

    public AudioInfo Inspect(string path)
    {
        using var reader = Open(path);
        var format = reader.WaveFormat;
        return new AudioInfo(
            Path.GetFileName(path),
            Path.GetExtension(path).TrimStart('.').ToLowerInvariant(),
            reader.TotalTime,
            format.SampleRate,
            format.Channels,
            new FileInfo(path).Length);
    }

    public IPcmReader OpenPcm16kMono(string path)
    {
        var reader = Open(path);
        try
        {
            var resampler = new MediaFoundationResampler(reader, new WaveFormat(AudioConstants.WhisperSampleRate, 16, 1))
            {
                ResamplerQuality = 60,
            };
            return new MfPcmReader(reader, resampler);
        }
        catch (COMException ex)
        {
            reader.Dispose();
            throw Map(ex);
        }
    }

    private static MediaFoundationReader Open(string path)
    {
        if (!File.Exists(path))
            throw new CodexVttException(ErrorCodes.AudioNotFound, "File audio non trovato.");
        try
        {
            return new MediaFoundationReader(path, new MediaFoundationReader.MediaFoundationReaderSettings { RequestFloatOutput = false });
        }
        catch (COMException ex)
        {
            throw Map(ex);
        }
    }

    private static CodexVttException Map(COMException ex) => ex.HResult switch
    {
        MfETopoCodecNotFound => new CodexVttException(ErrorCodes.AudioCodecMissing,
            "Il codec per questo formato non è installato in Windows.", inner: ex),
        // Qui si arriva solo con un'estensione supportata (CanDecode): se Media Foundation non riconosce il
        // contenuto, il file è danneggiato o non è davvero di quel formato, non "di un formato non supportato".
        MfEUnsupportedByteStreamType or MfEInvalidMediaType => new CodexVttException(ErrorCodes.AudioCorrupted,
            "Il file audio è danneggiato o non è del formato indicato dall'estensione.", inner: ex),
        _ => new CodexVttException(ErrorCodes.AudioCorrupted, "Impossibile leggere il file audio.", inner: ex),
    };

    private sealed class MfPcmReader(MediaFoundationReader reader, MediaFoundationResampler resampler) : IPcmReader
    {
        private byte[] _bytes = [];

        public int Read(Span<float> buffer)
        {
            var needed = buffer.Length * 2;
            if (_bytes.Length < needed)
                _bytes = new byte[needed];

            int read;
            try
            {
                read = resampler.Read(_bytes.AsSpan(0, needed));
            }
            catch (COMException ex)
            {
                throw Map(ex);
            }

            var samples = read / 2;
            for (var i = 0; i < samples; i++)
                buffer[i] = BitConverter.ToInt16(_bytes, i * 2) / 32768f;
            return samples;
        }

        public void Dispose()
        {
            resampler.Dispose();
            reader.Dispose();
        }
    }
}
