using CodexVtt.Audio.FFmpeg;
using CodexVtt.Core.Errors;

namespace CodexVtt.Server.Tests;

/// <summary>Si eseguono solo dove ffmpeg è installato (immagine Docker, CI Linux); altrove passano senza fare nulla.</summary>
public sealed class FfmpegDecoderTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("codexvtt-ffmpeg").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private string Wav(string name, int sampleRate, int seconds)
    {
        var samples = sampleRate * seconds;
        using var stream = new MemoryStream();
        using (var w = new BinaryWriter(stream, System.Text.Encoding.ASCII, leaveOpen: true))
        {
            w.Write("RIFF"u8);
            w.Write(36 + (samples * 2));
            w.Write("WAVEfmt "u8);
            w.Write(16);
            w.Write((short)1);
            w.Write((short)1);
            w.Write(sampleRate);
            w.Write(sampleRate * 2);
            w.Write((short)2);
            w.Write((short)16);
            w.Write("data"u8);
            w.Write(samples * 2);
            for (var i = 0; i < samples; i++)
                w.Write((short)(8000 * Math.Sin(2 * Math.PI * 440 * i / sampleRate)));
        }

        var path = Path.Combine(_dir, name);
        File.WriteAllBytes(path, stream.ToArray());
        return path;
    }

    [Fact]
    public void Inspects_and_resamples_to_16k_mono()
    {
        if (!FfmpegDecoder.IsAvailable())
            return;
        var decoder = new FfmpegDecoder();
        var path = Wav("tono.wav", 44100, 2);

        var info = decoder.Inspect(path);
        Assert.Equal(2, info.Duration.TotalSeconds, 1);
        Assert.Equal(44100, info.SampleRate);
        Assert.Equal(1, info.Channels);

        using var reader = decoder.OpenPcm16kMono(path);
        var buffer = new float[16000];
        var total = 0;
        float peak = 0;
        int read;
        while ((read = reader.Read(buffer)) > 0)
        {
            total += read;
            peak = Math.Max(peak, buffer.Take(read).Max(Math.Abs));
        }

        Assert.InRange(total, 31_000, 33_000);
        Assert.InRange(peak, 0.2f, 0.3f);
    }

    [Fact]
    public void Unreadable_files_are_reported_as_corrupted()
    {
        if (!FfmpegDecoder.IsAvailable())
            return;
        var path = Path.Combine(_dir, "rotto.mp3");
        File.WriteAllText(path, "questo non è un mp3");

        var error = Assert.Throws<CodexVttException>(() => new FfmpegDecoder().Inspect(path));

        Assert.Equal(ErrorCodes.AudioCorrupted, error.Code);
    }

    [Fact]
    public void Missing_program_is_reported_as_unavailable()
    {
        Assert.False(FfmpegDecoder.IsAvailable("codexvtt-ffmpeg-inesistente", "codexvtt-ffprobe-inesistente"));
        var path = Wav("x.wav", 16000, 1);

        var error = Assert.Throws<CodexVttException>(() => new FfmpegDecoder("codexvtt-ffmpeg-inesistente", "codexvtt-ffprobe-inesistente").Inspect(path));

        Assert.Equal(ErrorCodes.AudioCodecMissing, error.Code);
    }
}
