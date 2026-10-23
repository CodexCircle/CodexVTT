namespace CodexVtt.Core.Audio;

/// <summary>
/// Lettore su campioni già in memoria: serve a rispezzare un blocco in finestre più corte con
/// <see cref="AudioChunker"/> (i motori di Parakeet, Canary e Cohere accettano solo pochi secondi per volta).
/// </summary>
public sealed class MemoryPcmReader : IPcmReader
{
    private readonly ReadOnlyMemory<float> _samples;
    private int _position;

    public MemoryPcmReader(ReadOnlyMemory<float> samples) => _samples = samples;

    public int Read(Span<float> buffer)
    {
        var count = Math.Min(buffer.Length, _samples.Length - _position);
        _samples.Span.Slice(_position, count).CopyTo(buffer);
        _position += count;
        return count;
    }

    public void Dispose()
    {
    }
}
