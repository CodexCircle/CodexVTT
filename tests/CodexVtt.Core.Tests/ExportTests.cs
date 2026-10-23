using CodexVtt.Core.Export;
using CodexVtt.Core.Transcription;

namespace CodexVtt.Core.Tests;

public sealed class ExportTests : IDisposable
{
    private readonly TempDir _dir = new();

    public void Dispose() => _dir.Dispose();

    private static TranscriptionResult Result() => new(
        "riunione.mp3",
        TimeSpan.FromSeconds(3725),
        "it",
        new ModelUsage("small", "Small", "ggml-small.bin", "abc123", "Cpu"),
        [
            new TranscriptSegment(TimeSpan.Zero, TimeSpan.FromSeconds(2.5), " Buongiorno a tutti.", 0.9f),
            new TranscriptSegment(TimeSpan.FromSeconds(2.5), TimeSpan.FromSeconds(2.5), "Iniziamo.", 0.8f),
            new TranscriptSegment(TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(4), "   ", 0.1f),
            new TranscriptSegment(TimeSpan.FromSeconds(3723.25), TimeSpan.FromSeconds(3725.004), "Fine --> ora.", 0.9f),
        ],
        TimeSpan.FromSeconds(60),
        new DateTimeOffset(2026, 10, 14, 10, 30, 0, TimeSpan.Zero));

    [Fact]
    public void Txt_joins_segments_without_blank_ones()
    {
        var txt = TranscriptExporter.Render(Result(), ExportFormat.Txt);

        Assert.Equal("Buongiorno a tutti. Iniziamo. Fine --> ora.\n", txt);
    }

    [Fact]
    public void Txt_header_names_the_model_used()
    {
        var txt = TranscriptExporter.Render(Result(), ExportFormat.Txt, new ExportOptions(IncludeHeader: true));

        Assert.Contains("Modello: Small (ggml-small.bin, sha256 abc123)", txt, StringComparison.Ordinal);
        Assert.Contains("Lingua: it", txt, StringComparison.Ordinal);
    }

    [Fact]
    public void Header_can_be_in_english()
    {
        var txt = TranscriptExporter.Render(Result(), ExportFormat.Txt, new ExportOptions(IncludeHeader: true, Language: "en"));

        Assert.Contains("Model: Small (ggml-small.bin, sha256 abc123)", txt, StringComparison.Ordinal);
        Assert.Contains("Language: it", txt, StringComparison.Ordinal);
        Assert.Contains("Transcribed locally with CodexVTT.", txt, StringComparison.Ordinal);
    }

    [Fact]
    public void Srt_uses_comma_milliseconds_and_sequential_indexes()
    {
        var srt = TranscriptExporter.Render(Result(), ExportFormat.Srt);

        const string expected =
            "1\n00:00:00,000 --> 00:00:02,500\nBuongiorno a tutti.\n\n" +
            "2\n00:00:02,500 --> 00:00:03,000\nIniziamo.\n\n" +
            "3\n01:02:03,250 --> 01:02:05,004\nFine --> ora.\n\n";
        Assert.Equal(expected, srt);
    }

    [Fact]
    public void Vtt_has_header_and_dot_milliseconds()
    {
        var vtt = TranscriptExporter.Render(Result(), ExportFormat.Vtt, new ExportOptions(IncludeHeader: true));

        Assert.StartsWith("WEBVTT\n\nNOTE\n", vtt, StringComparison.Ordinal);
        Assert.Contains("01:02:03.250 --> 01:02:05.004\nFine --> ora.", vtt, StringComparison.Ordinal);
        var note = vtt.Split("\n\n")[1];
        Assert.DoesNotContain("-->", note, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Written_file_is_utf8_without_bom()
    {
        var path = Path.Combine(_dir.Path, "out.txt");

        await TranscriptExporter.WriteAsync(Result() with { Segments = [new(TimeSpan.Zero, TimeSpan.FromSeconds(1), "perché è così", null)] }, ExportFormat.Txt, path);

        var bytes = await File.ReadAllBytesAsync(path);
        Assert.NotEqual(0xEF, bytes[0]);
        Assert.Equal("perché è così\n", System.Text.Encoding.UTF8.GetString(bytes));
        Assert.False(File.Exists(path + ".tmp"));
    }
}
