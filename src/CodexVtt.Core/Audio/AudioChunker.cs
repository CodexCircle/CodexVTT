namespace CodexVtt.Core.Audio;

public sealed record PcmChunk(TimeSpan Offset, float[] Samples)
{
    public TimeSpan Duration => TimeSpan.FromSeconds((double)Samples.Length / AudioConstants.WhisperSampleRate);
}

/// <summary>
/// Divide il PCM in blocchi di durata limitata, tagliando nel punto più silenzioso vicino al
/// confine, così la memoria resta limitata anche per file di ore (ADR-0004) e si evita di
/// spezzare parole.
/// </summary>
public sealed class AudioChunker
{
    private const int Rate = AudioConstants.WhisperSampleRate;
    private readonly int _chunkSamples;
    private readonly int _searchSamples;
    private readonly int _frameSamples;

    public AudioChunker(TimeSpan chunkLength, TimeSpan silenceSearchWindow, TimeSpan? frameLength = null)
    {
        _chunkSamples = (int)(chunkLength.TotalSeconds * Rate);
        _searchSamples = Math.Min((int)(silenceSearchWindow.TotalSeconds * Rate), _chunkSamples / 2);
        _frameSamples = Math.Max(1, (int)((frameLength ?? TimeSpan.FromMilliseconds(20)).TotalSeconds * Rate));
        ArgumentOutOfRangeException.ThrowIfLessThan(_chunkSamples, Rate);
    }

    public static AudioChunker Default { get; } = new(TimeSpan.FromMinutes(10), TimeSpan.FromSeconds(5));

    public IEnumerable<PcmChunk> Split(IPcmReader reader)
    {
        var buffer = new float[_chunkSamples];
        var filled = 0;
        long consumed = 0;

        while (true)
        {
            while (filled < buffer.Length)
            {
                var read = reader.Read(buffer.AsSpan(filled));
                if (read == 0)
                    break;
                filled += read;
            }

            if (filled == 0)
                yield break;

            var cut = filled < buffer.Length ? filled : QuietestCut(buffer);
            yield return new PcmChunk(TimeSpan.FromSeconds((double)consumed / Rate), buffer.AsSpan(0, cut).ToArray());
            consumed += cut;

            var rest = filled - cut;
            buffer.AsSpan(cut, rest).CopyTo(buffer);
            filled = rest;
            if (filled == 0 && cut < buffer.Length)
                yield break;
        }
    }

    private int QuietestCut(float[] buffer)
    {
        var start = buffer.Length - _searchSamples;
        var bestIndex = buffer.Length;
        var bestEnergy = double.MaxValue;
        for (var frame = start; frame + _frameSamples <= buffer.Length; frame += _frameSamples)
        {
            double energy = 0;
            for (var i = frame; i < frame + _frameSamples; i++)
                energy += buffer[i] * buffer[i];
            if (energy < bestEnergy)
            {
                bestEnergy = energy;
                bestIndex = frame + (_frameSamples / 2);
            }
        }

        return bestIndex;
    }
}
