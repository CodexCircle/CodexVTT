using CodexVtt.Core.Errors;
using CodexVtt.Core.History;
using CodexVtt.Core.Transcription;

namespace CodexVtt.Core.Tests;

public sealed class TranscriptHistoryTests : IDisposable
{
    private readonly TempDir _dir = new();

    public void Dispose() => _dir.Dispose();

    private static TranscriptionResult Result(string audio = "lezione: storia/1.mp3", int minute = 30, string text = "Testo della lezione.") => new(
        audio,
        TimeSpan.FromMinutes(90),
        "it",
        new ModelUsage("small", "Small", "ggml-small.bin", "abc", "Cpu"),
        [new TranscriptSegment(TimeSpan.Zero, TimeSpan.FromSeconds(3), text, 0.9f)],
        TimeSpan.FromMinutes(12),
        new DateTimeOffset(2026, 10, 14, 18, minute, 5, TimeSpan.Zero));

    [Fact]
    public void Default_folder_is_documents_codexvtt_history()
    {
        var documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);

        Assert.Equal(Path.Combine(documents, "CodexVTT", "history"), TranscriptHistory.DefaultDirectory);
    }

    [Fact]
    public void Each_transcription_gets_its_own_readable_files()
    {
        var history = new TranscriptHistory(Path.Combine(_dir.Path, "CodexVTT"));

        var first = history.Save(Result());
        var second = history.Save(Result());

        Assert.NotEqual(first.Id, second.Id);
        Assert.EndsWith("(2)", second.Id, StringComparison.Ordinal);
        Assert.DoesNotContain(':', first.Id);
        Assert.DoesNotContain('/', first.Id);
        Assert.Contains("lezione_ storia_1", first.Id, StringComparison.Ordinal);
        var files = Directory.GetFiles(history.Directory).Select(Path.GetFileName).Order().ToList();
        Assert.Equal(4, files.Count);
        var txt = File.ReadAllText(Path.Combine(history.Directory, first.TextFileName));
        Assert.Contains("Modello: Small", txt, StringComparison.Ordinal);
        Assert.EndsWith("Testo della lezione.\n", txt, StringComparison.Ordinal);
    }

    [Fact]
    public void Edited_segments_replace_the_saved_ones_in_json_and_txt()
    {
        var history = new TranscriptHistory(_dir.Path);
        var saved = history.Save(Result());

        history.UpdateSegments(saved.Id, [new TranscriptSegment(TimeSpan.Zero, TimeSpan.FromSeconds(5), "Testo corretto.", null)]);

        var loaded = history.Load(saved.Id);
        Assert.Equal("Testo corretto.", Assert.Single(loaded.Segments).Text);
        Assert.Equal("ggml-small.bin", loaded.Model.File);
        Assert.EndsWith("Testo corretto.\n", File.ReadAllText(Path.Combine(history.Directory, saved.TextFileName)), StringComparison.Ordinal);
        Assert.Equal("Testo corretto.", Assert.Single(history.List()).Preview);
        Assert.Throws<CodexVttException>(() => history.UpdateSegments("assente", loaded.Segments));
    }

    [Fact]
    public void List_is_newest_first_and_round_trips_results()
    {
        var history = new TranscriptHistory(_dir.Path);
        history.Save(Result(audio: "a.wav", minute: 10));
        var newest = history.Save(Result(audio: "b.wav", minute: 50, text: new string('x', 400)));
        File.WriteAllText(Path.Combine(_dir.Path, "estraneo.json"), "{\"altro\":1}");
        File.WriteAllText(Path.Combine(_dir.Path, "rotto.json"), "{");

        var entries = history.List();
        var loaded = history.Load(newest.Id);

        Assert.Equal(["b.wav", "a.wav"], entries.Select(e => e.AudioFileName));
        Assert.EndsWith("…", entries[0].Preview, StringComparison.Ordinal);
        Assert.Equal(TimeSpan.FromMinutes(90), loaded.AudioDuration);
        Assert.Equal("ggml-small.bin", loaded.Model.File);
        Assert.Equal(TimeSpan.FromSeconds(3), loaded.Segments[0].End);
        Assert.Equal(new string('x', 400), loaded.Text);
    }

    [Fact]
    public void Delete_removes_both_files_and_missing_folder_is_empty()
    {
        var history = new TranscriptHistory(Path.Combine(_dir.Path, "nuova"));
        Assert.Empty(history.List());

        var entry = history.Save(Result(), language: "en");
        Assert.Contains("Model: Small", File.ReadAllText(Path.Combine(history.Directory, entry.TextFileName)), StringComparison.Ordinal);
        history.Delete(entry.Id);

        Assert.Empty(Directory.GetFiles(history.Directory));
        Assert.Equal(ErrorCodes.HistoryNotFound, Assert.Throws<CodexVttException>(() => history.Load(entry.Id)).Code);
        Assert.Equal(ErrorCodes.InvalidRequest, Assert.Throws<CodexVttException>(() => history.Load("../x")).Code);
    }
}
