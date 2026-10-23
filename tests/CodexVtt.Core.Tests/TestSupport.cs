using System.Security.Cryptography;
using System.Text;
using CodexVtt.Core;
using CodexVtt.Core.Hardware;
using CodexVtt.Core.Models;

namespace CodexVtt.Core.Tests;

internal sealed class TempDir : IDisposable
{
    public TempDir()
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "codexvtt-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path);
    }

    public string Path { get; }

    public AppPaths AppPaths()
    {
        var paths = new AppPaths(Path);
        paths.EnsureCreated();
        return paths;
    }

    public string File(string name, byte[] content)
    {
        var path = System.IO.Path.Combine(Path, name);
        System.IO.File.WriteAllBytes(path, content);
        return path;
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(Path, recursive: true);
        }
        catch (IOException)
        {
        }
    }
}

internal sealed class FakeHardware(HardwareProfile profile) : IHardwareProbe
{
    public HardwareProfile Profile { get; set; } = profile;

    public HardwareProfile Probe(string modelsDirectory) => Profile;
}

internal static class TestData
{
    public const long Gb = 1_000_000_000;

    public static HardwareProfile Hardware(int cores = 8, long ramGb = 16, long diskGb = 100, CpuFeatures? cpu = null) => new(
        "test-os", "X64", cores, ramGb * Gb, ramGb * Gb / 2,
        cpu ?? (CpuFeatures.Avx | CpuFeatures.Avx2 | CpuFeatures.Fma | CpuFeatures.F16C),
        diskGb * Gb);

    public static string Sha256(byte[] content) => Convert.ToHexStringLower(SHA256.HashData(content));

    public static SelectionRules Rules { get; } = new(0.5, 0.1,
        [new CostBudget(12, 16), new CostBudget(8, 10), new CostBudget(4, 6), new CostBudget(1, 2)], 2);

    /// <summary>Catalogo di prova con file piccoli e hash reali.</summary>
    public static ModelCatalog Catalog(string urlTemplate = "https://huggingface.co/x/{revision}/{file}", string? revision = "rev1", params (string Id, byte[] Content, int Cost, int Quality, long RamGb)[] models)
    {
        var descriptors = models.Select(m => new ModelDescriptor(
            m.Id, m.Id.ToUpperInvariant(), $"ggml-{m.Id}.bin", "f16", m.Content.Length, Sha256(m.Content),
            m.RamGb * Gb, m.Cost, m.Quality, true, "MIT")).ToList();
        return new ModelCatalog(new CatalogSource("test/repo", revision, urlTemplate), Rules, descriptors);
    }

    public static byte[] Bytes(string text, int repeat = 1) =>
        Encoding.UTF8.GetBytes(string.Concat(Enumerable.Repeat(text, repeat)));

    /// <summary>WAV PCM 16 bit con un'onda sinusoidale.</summary>
    public static byte[] Wav(int sampleRate, int channels, double seconds, double frequency = 440, int bits = 16)
    {
        var frames = (int)(sampleRate * seconds);
        var bytesPerSample = bits / 8;
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        var dataLength = frames * channels * bytesPerSample;
        w.Write("RIFF"u8);
        w.Write(36 + dataLength);
        w.Write("WAVE"u8);
        w.Write("fmt "u8);
        w.Write(16);
        w.Write((short)(bits == 32 ? 3 : 1));
        w.Write((short)channels);
        w.Write(sampleRate);
        w.Write(sampleRate * channels * bytesPerSample);
        w.Write((short)(channels * bytesPerSample));
        w.Write((short)bits);
        w.Write("data"u8);
        w.Write(dataLength);
        for (var i = 0; i < frames; i++)
        {
            var value = Math.Sin(2 * Math.PI * frequency * i / sampleRate) * 0.5;
            for (var c = 0; c < channels; c++)
            {
                if (bits == 32)
                    w.Write((float)value);
                else
                    w.Write((short)(value * short.MaxValue));
            }
        }

        w.Flush();
        return ms.ToArray();
    }
}
