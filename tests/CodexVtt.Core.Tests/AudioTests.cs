using CodexVtt.Core.Audio;
using CodexVtt.Core.Errors;

namespace CodexVtt.Core.Tests;

public sealed class AudioTests : IDisposable
{
    private readonly TempDir _dir = new();

    public void Dispose() => _dir.Dispose();

    private static float[] ReadAll(IPcmReader reader)
    {
        var all = new List<float>();
        var buffer = new float[1000];
        int read;
        while ((read = reader.Read(buffer)) > 0)
            all.AddRange(buffer.AsSpan(0, read).ToArray());
        return [.. all];
    }

    [Theory]
    [InlineData(16000, 1, 16)]
    [InlineData(44100, 2, 16)]
    [InlineData(48000, 2, 32)]
    [InlineData(8000, 1, 16)]
    public void Wav_is_normalized_to_16k_mono(int rate, int channels, int bits)
    {
        var path = _dir.File("a.wav", TestData.Wav(rate, channels, 2.0, bits: bits));
        var decoder = new WavDecoder();

        var info = decoder.Inspect(path);
        using var reader = decoder.OpenPcm16kMono(path);
        var samples = ReadAll(reader);

        Assert.True(decoder.CanDecode(path));
        Assert.Equal(rate, info.SampleRate);
        Assert.Equal(channels, info.Channels);
        Assert.Equal(2.0, info.Duration.TotalSeconds, 2);
        Assert.InRange(samples.Length, 31_900, 32_100);
        Assert.InRange(samples.Max(), 0.4f, 0.55f);
    }

    [Fact]
    public void Resampled_tone_keeps_its_frequency()
    {
        var path = _dir.File("tone.wav", TestData.Wav(44100, 1, 1.0, frequency: 440));
        using var reader = new WavDecoder().OpenPcm16kMono(path);
        var samples = ReadAll(reader);

        var crossings = 0;
        for (var i = 1; i < samples.Length; i++)
        {
            if (samples[i - 1] < 0 && samples[i] >= 0)
                crossings++;
        }

        Assert.InRange(crossings, 435, 445);
    }

    [Fact]
    public void Non_wav_content_with_wav_extension_is_rejected()
    {
        var path = _dir.File("fake.wav", TestData.Bytes("ID3 not really a wav file"));

        Assert.False(new WavDecoder().CanDecode(path));
        var library = new AudioLibrary([new WavDecoder()], TimeSpan.FromHours(1));
        Assert.Equal(ErrorCodes.AudioUnsupported, Assert.Throws<CodexVttException>(() => library.Register(path)).Code);
    }

    [Fact]
    public void Truncated_and_empty_files_produce_typed_errors()
    {
        var wav = TestData.Wav(16000, 1, 1);
        var truncated = _dir.File("t.wav", wav[..20]);
        var empty = _dir.File("e.wav", []);
        var library = new AudioLibrary([new WavDecoder()], TimeSpan.FromHours(1));

        Assert.Equal(ErrorCodes.AudioCorrupted, Assert.Throws<CodexVttException>(() => library.Register(truncated)).Code);
        Assert.Equal(ErrorCodes.AudioEmpty, Assert.Throws<CodexVttException>(() => library.Register(empty)).Code);
        Assert.Equal(ErrorCodes.AudioNotFound, Assert.Throws<CodexVttException>(() => library.Register(Path.Combine(_dir.Path, "none.wav"))).Code);
    }

    [Fact]
    public void Too_long_audio_is_rejected()
    {
        var path = _dir.File("long.wav", TestData.Wav(16000, 1, 3));
        var library = new AudioLibrary([new WavDecoder()], TimeSpan.FromSeconds(2));

        Assert.Equal(ErrorCodes.AudioTooLong, Assert.Throws<CodexVttException>(() => library.Register(path)).Code);
    }

    [Fact]
    public void Library_hands_out_opaque_ids()
    {
        var path = _dir.File("speech.wav", TestData.Wav(16000, 1, 1));
        var library = new AudioLibrary([new WavDecoder()], TimeSpan.FromHours(1));

        var handle = library.Register(path);

        Assert.DoesNotContain(_dir.Path, handle.AudioId, StringComparison.Ordinal);
        Assert.Equal("speech.wav", handle.Info.FileName);
        Assert.Equal("speech", library.SuggestedExportName(handle.AudioId));
        Assert.Equal(ErrorCodes.AudioNotFound, Assert.Throws<CodexVttException>(() => library.Info("unknown")).Code);
    }

    [Fact]
    public void Chunker_preserves_all_samples_and_cuts_in_silence()
    {
        // 3,5 s di tono con un silenzio a 1,8 s; blocchi da 2 s con ricerca negli ultimi 0,5 s.
        var samples = new float[56_000];
        for (var i = 0; i < samples.Length; i++)
            samples[i] = i is >= 28_600 and < 29_000 ? 0 : 0.5f;
        var chunker = new AudioChunker(TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(0.5));

        var chunks = chunker.Split(new ArrayPcm(samples)).ToList();

        Assert.Equal(samples.Length, chunks.Sum(c => c.Samples.Length));
        Assert.Equal(2, chunks.Count);
        Assert.InRange(chunks[0].Samples.Length, 28_600, 29_000);
        Assert.Equal(chunks[0].Duration, chunks[1].Offset);
    }

    [Fact]
    public void Chunker_handles_exact_and_empty_inputs()
    {
        var chunker = new AudioChunker(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(0.2));

        Assert.Empty(chunker.Split(new ArrayPcm([])));
        var exact = chunker.Split(new ArrayPcm(new float[16_000])).ToList();
        Assert.Equal(16_000, exact.Sum(c => c.Samples.Length));
    }

    private sealed class ArrayPcm(float[] data) : IPcmReader
    {
        private int _position;

        public int Read(Span<float> buffer)
        {
            var count = Math.Min(buffer.Length, data.Length - _position);
            data.AsSpan(_position, count).CopyTo(buffer);
            _position += count;
            return count;
        }

        public void Dispose()
        {
        }
    }
}
