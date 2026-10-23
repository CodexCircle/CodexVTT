using System.Runtime.Versioning;
using NAudio.Wave;
using CodexVtt.Audio.Windows;
using CodexVtt.Core.Audio;
using CodexVtt.Core.Errors;

namespace CodexVtt.Integration.Tests;

public sealed class WindowsFactAttribute : FactAttribute
{
    public WindowsFactAttribute()
    {
        if (!OperatingSystem.IsWindows())
            Skip = "Media Foundation è disponibile solo su Windows.";
    }
}

/// <summary>Decodifica reale con Media Foundation (#5): i formati compressi sono generati con gli encoder di Windows.</summary>
[SupportedOSPlatform("windows")]
public sealed class MediaFoundationDecoderTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "codexvtt-mf-" + Guid.NewGuid().ToString("N"));

    public MediaFoundationDecoderTests() => Directory.CreateDirectory(_dir);

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private string Tone(string name, Action<IWaveProvider, string> encode)
    {
        var path = Path.Combine(_dir, name);
        var provider = new SineProvider(new WaveFormat(44100, 16, 2), seconds: 3);
        encode(provider, path);
        return path;
    }

    [WindowsFact]
    public void Decodes_wav_mp3_aac_and_flac_to_16k_mono()
    {
        var files = new[]
        {
            Tone("tone.wav", (p, f) => WaveFileWriter.CreateWaveFile(f, p)),
            Tone("tone.mp3", (p, f) => MediaFoundationEncoder.EncodeToMp3(p, f, 128_000)),
            Tone("tone.m4a", (p, f) => MediaFoundationEncoder.EncodeToAac(p, f, 128_000)),
            Tone("tone.flac", (p, f) => MediaFoundationEncoder.EncodeToFlac(p, f)),
        };
        var decoder = new MediaFoundationDecoder();

        foreach (var file in files)
        {
            var info = decoder.Inspect(file);
            using var reader = decoder.OpenPcm16kMono(file);
            var total = 0;
            var peak = 0f;
            var buffer = new float[4096];
            int read;
            while ((read = reader.Read(buffer)) > 0)
            {
                total += read;
                for (var i = 0; i < read; i++)
                    peak = Math.Max(peak, Math.Abs(buffer[i]));
            }

            Console.WriteLine($"[codexvtt] {Path.GetFileName(file)}: durata {info.Duration.TotalSeconds:0.00}s, campioni 16k {total}, picco {peak:0.00}");
            Assert.InRange(info.Duration.TotalSeconds, 2.9, 3.2);
            Assert.InRange(total, 46_000, 51_500);
            Assert.InRange(peak, 0.3f, 0.7f);
        }
    }

    [WindowsFact]
    public void Garbage_with_audio_extension_is_reported()
    {
        var path = Path.Combine(_dir, "fake.mp3");
        File.WriteAllBytes(path, new byte[4096]);

        var ex = Assert.Throws<CodexVttException>(() => new MediaFoundationDecoder().Inspect(path));

        Assert.Contains(ex.Code, new[] { ErrorCodes.AudioUnsupported, ErrorCodes.AudioCorrupted });
    }

    private sealed class SineProvider(WaveFormat format, double seconds) : IWaveProvider
    {
        private readonly long _totalFrames = (long)(format.SampleRate * seconds);
        private long _frame;

        public WaveFormat WaveFormat { get; } = format;

        public int Read(Span<byte> buffer)
        {
            var frameBytes = WaveFormat.BlockAlign;
            var frames = (int)Math.Min(buffer.Length / frameBytes, _totalFrames - _frame);
            for (var i = 0; i < frames; i++, _frame++)
            {
                var sample = (short)(Math.Sin(2 * Math.PI * 440 * _frame / WaveFormat.SampleRate) * 0.5 * short.MaxValue);
                for (var c = 0; c < WaveFormat.Channels; c++)
                    BitConverter.TryWriteBytes(buffer[((i * frameBytes) + (c * 2))..], sample);
            }

            return frames * frameBytes;
        }
    }
}
