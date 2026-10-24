using CodexVtt.Core.Audio;

namespace CodexVtt.Core.Benchmark;

/// <summary>
/// Prepara l'audio di un brano ufficiale per il Test bench: il brano pulito è il file scaricato così com'è; quello rumoroso
/// è lo stesso file letto, mescolato con rumore sintetico e salvato come WAV temporaneo (il registro audio accetta solo file).
/// </summary>
public sealed class BenchmarkAudioPreparer(AudioLibrary audio, string tempDirectory)
{
    /// <param name="clip">Il brano da preparare.</param>
    /// <param name="sourcePath">Il file scaricato e verificato del brano di origine (<see cref="BenchmarkClips.SourceOf"/>).</param>
    public AudioHandle Prepare(BenchmarkClip clip, string sourcePath)
    {
        var source = audio.Register(sourcePath);
        if (!clip.IsDerived)
            return source;

        var samples = new List<float>((int)Math.Min(int.MaxValue, (long)(source.Info.Duration.TotalSeconds * AudioConstants.WhisperSampleRate) + 1));
        using (var reader = audio.Open(source.AudioId))
        {
            var buffer = new float[1 << 16];
            int read;
            while ((read = reader.Read(buffer)) > 0)
                samples.AddRange(buffer.AsSpan(0, read));
        }

        var mixed = SyntheticNoise.Mix(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(samples), clip.NoiseSnrDb, BenchmarkClips.NoiseSeed);
        Directory.CreateDirectory(tempDirectory);
        var path = Path.Combine(tempDirectory, clip.Id + ".wav");
        WavWriter.WritePcm16Mono(path, mixed, AudioConstants.WhisperSampleRate);
        return audio.Register(path);
    }
}
