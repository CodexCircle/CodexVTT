using System.Buffers.Binary;

namespace CodexVtt.Core.Audio;

/// <summary>Scrive un WAV PCM 16 bit mono. Serve al Test bench per salvare il brano con il rumore aggiunto.</summary>
public static class WavWriter
{
    public static void WritePcm16Mono(string path, ReadOnlySpan<float> samples, int sampleRate)
    {
        var dataLength = samples.Length * 2;
        var bytes = new byte[44 + dataLength];
        "RIFF"u8.CopyTo(bytes);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(4), 36 + dataLength);
        "WAVE"u8.CopyTo(bytes.AsSpan(8));
        "fmt "u8.CopyTo(bytes.AsSpan(12));
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(16), 16);
        BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(20), 1); // PCM
        BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(22), 1); // mono
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(24), sampleRate);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(28), sampleRate * 2);
        BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(32), 2);
        BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(34), 16);
        "data"u8.CopyTo(bytes.AsSpan(36));
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(40), dataLength);
        for (var i = 0; i < samples.Length; i++)
        {
            var value = (short)Math.Round(Math.Clamp(samples[i], -1f, 1f) * short.MaxValue);
            BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(44 + (i * 2)), value);
        }

        File.WriteAllBytes(path, bytes);
    }
}
