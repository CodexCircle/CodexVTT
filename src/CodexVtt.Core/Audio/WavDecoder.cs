using System.Buffers.Binary;
using CodexVtt.Core.Errors;

namespace CodexVtt.Core.Audio;

/// <summary>
/// Decoder WAV gestito (PCM 8/16/24/32 bit e IEEE float), indipendente dalla piattaforma.
/// Valida l'header RIFF invece di fidarsi dell'estensione.
/// </summary>
public sealed class WavDecoder : IAudioDecoder
{
    private const ushort FormatPcm = 1;
    private const ushort FormatFloat = 3;
    private const ushort FormatExtensible = 0xFFFE;

    public bool CanDecode(string path)
    {
        try
        {
            using var stream = File.OpenRead(path);
            Span<byte> header = stackalloc byte[12];
            return stream.Read(header) == 12 &&
                   header[..4].SequenceEqual("RIFF"u8) &&
                   header[8..12].SequenceEqual("WAVE"u8);
        }
        catch (IOException)
        {
            return false;
        }
    }

    public AudioInfo Inspect(string path)
    {
        using var stream = OpenValidated(path, out var format, out _, out var dataLength);
        var frames = dataLength / format.BlockAlign;
        return new AudioInfo(
            Path.GetFileName(path),
            "wav",
            TimeSpan.FromSeconds((double)frames / format.SampleRate),
            format.SampleRate,
            format.Channels,
            new FileInfo(path).Length);
    }

    public IPcmReader OpenPcm16kMono(string path)
    {
        var stream = OpenValidated(path, out var format, out var dataOffset, out var dataLength);
        stream.Position = dataOffset;
        return new WavPcmReader(stream, format, dataLength);
    }

    private static FileStream OpenValidated(string path, out WavFormat format, out long dataOffset, out long dataLength)
    {
        if (!File.Exists(path))
            throw new CodexVttException(ErrorCodes.AudioNotFound, "File audio non trovato.");

        var stream = File.OpenRead(path);
        try
        {
            using var reader = new BinaryReader(stream, System.Text.Encoding.ASCII, leaveOpen: true);
            if (stream.Length < 12 || new string(reader.ReadChars(4)) != "RIFF")
                throw Unsupported();
            reader.ReadUInt32();
            if (new string(reader.ReadChars(4)) != "WAVE")
                throw Unsupported();

            WavFormat? fmt = null;
            while (stream.Position + 8 <= stream.Length)
            {
                var id = new string(reader.ReadChars(4));
                long size = reader.ReadUInt32();
                var next = stream.Position + size + (size & 1);
                if (id == "fmt ")
                {
                    fmt = ReadFormat(reader, size);
                }
                else if (id == "data")
                {
                    if (fmt is null)
                        throw Corrupted("Chunk data prima del chunk fmt.");
                    format = fmt;
                    dataOffset = stream.Position;
                    dataLength = Math.Min(size, stream.Length - stream.Position);
                    dataLength -= dataLength % format.BlockAlign;
                    if (dataLength <= 0)
                        throw new CodexVttException(ErrorCodes.AudioEmpty, "Il file audio non contiene campioni.");
                    return stream;
                }

                stream.Position = next;
            }

            throw Corrupted("Chunk data mancante.");
        }
        catch (EndOfStreamException)
        {
            stream.Dispose();
            throw Corrupted("Header WAV troncato.");
        }
        catch
        {
            stream.Dispose();
            throw;
        }
    }

    private static WavFormat ReadFormat(BinaryReader reader, long size)
    {
        if (size < 16)
            throw Corrupted("Chunk fmt troppo corto.");
        var tag = reader.ReadUInt16();
        var channels = reader.ReadUInt16();
        var sampleRate = reader.ReadInt32();
        reader.ReadInt32();
        var blockAlign = reader.ReadUInt16();
        var bits = reader.ReadUInt16();
        var consumed = 16L;
        if (tag == FormatExtensible && size >= 40)
        {
            reader.ReadUInt16();
            reader.ReadUInt16();
            reader.ReadUInt32();
            var subFormat = reader.ReadBytes(16);
            tag = BinaryPrimitives.ReadUInt16LittleEndian(subFormat);
            consumed = 40;
        }

        reader.BaseStream.Position += size - consumed;

        var supported = (tag == FormatPcm && bits is 8 or 16 or 24 or 32) || (tag == FormatFloat && bits == 32);
        if (!supported || channels == 0 || sampleRate <= 0 || blockAlign != channels * (bits / 8))
            throw Unsupported();
        return new WavFormat(tag, channels, sampleRate, blockAlign, bits);
    }

    private static CodexVttException Unsupported() =>
        new(ErrorCodes.AudioUnsupported, "Formato WAV non supportato.");

    private static CodexVttException Corrupted(string detail) =>
        new(ErrorCodes.AudioCorrupted, $"File WAV non valido: {detail}");

    private sealed record WavFormat(ushort Tag, int Channels, int SampleRate, int BlockAlign, int Bits);

    private sealed class WavPcmReader : IPcmReader
    {
        private readonly FileStream _stream;
        private readonly WavFormat _format;
        private readonly StreamingResampler _resampler;
        private readonly byte[] _raw;
        private readonly float[] _interleaved;
        private readonly List<float> _pending = [];
        private long _remaining;
        private int _pendingOffset;

        public WavPcmReader(FileStream stream, WavFormat format, long dataLength)
        {
            _stream = stream;
            _format = format;
            _remaining = dataLength;
            _resampler = new StreamingResampler(format.SampleRate, format.Channels);
            var frames = 8192;
            _raw = new byte[frames * format.BlockAlign];
            _interleaved = new float[frames * format.Channels];
        }

        public int Read(Span<float> buffer)
        {
            var written = 0;
            while (written < buffer.Length)
            {
                if (_pendingOffset >= _pending.Count)
                {
                    _pending.Clear();
                    _pendingOffset = 0;
                    if (!Fill())
                        break;
                    continue;
                }

                var take = Math.Min(buffer.Length - written, _pending.Count - _pendingOffset);
                System.Runtime.InteropServices.CollectionsMarshal.AsSpan(_pending).Slice(_pendingOffset, take).CopyTo(buffer[written..]);
                _pendingOffset += take;
                written += take;
            }

            return written;
        }

        private bool Fill()
        {
            if (_remaining <= 0)
                return false;
            var toRead = (int)Math.Min(_raw.Length, _remaining);
            var read = _stream.ReadAtLeast(_raw.AsSpan(0, toRead), toRead, throwOnEndOfStream: false);
            read -= read % _format.BlockAlign;
            if (read <= 0)
            {
                _remaining = 0;
                return false;
            }

            _remaining -= read;
            var samples = read / (_format.Bits / 8);
            Convert(_raw.AsSpan(0, read), _interleaved.AsSpan(0, samples));
            _resampler.Process(_interleaved.AsSpan(0, samples), _pending);
            return true;
        }

        private void Convert(ReadOnlySpan<byte> raw, Span<float> output)
        {
            switch (_format.Bits)
            {
                case 8:
                    for (var i = 0; i < output.Length; i++)
                        output[i] = (raw[i] - 128) / 128f;
                    break;
                case 16:
                    for (var i = 0; i < output.Length; i++)
                        output[i] = BinaryPrimitives.ReadInt16LittleEndian(raw[(i * 2)..]) / 32768f;
                    break;
                case 24:
                    for (var i = 0; i < output.Length; i++)
                    {
                        var o = i * 3;
                        var value = (raw[o] | (raw[o + 1] << 8) | (raw[o + 2] << 16)) << 8 >> 8;
                        output[i] = value / 8388608f;
                    }

                    break;
                case 32 when _format.Tag == FormatFloat:
                    for (var i = 0; i < output.Length; i++)
                        output[i] = BinaryPrimitives.ReadSingleLittleEndian(raw[(i * 4)..]);
                    break;
                default:
                    for (var i = 0; i < output.Length; i++)
                        output[i] = BinaryPrimitives.ReadInt32LittleEndian(raw[(i * 4)..]) / 2147483648f;
                    break;
            }
        }

        public void Dispose() => _stream.Dispose();
    }
}
