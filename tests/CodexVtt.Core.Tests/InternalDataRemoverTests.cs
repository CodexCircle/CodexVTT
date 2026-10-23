using CodexVtt.Core.Maintenance;

namespace CodexVtt.Core.Tests;

public class InternalDataRemoverTests : IDisposable
{
    private readonly TempDir _dir = new();

    public void Dispose()
    {
        _dir.Dispose();
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void Measure_sums_all_files_under_the_root()
    {
        var root = Path.Combine(_dir.Path, "CodexVTT.Data");
        Directory.CreateDirectory(Path.Combine(root, "models"));
        File.WriteAllBytes(Path.Combine(root, "models", "ggml-small.bin"), new byte[1000]);
        File.WriteAllBytes(Path.Combine(root, "settings.json"), new byte[24]);

        Assert.Equal(1024, InternalDataRemover.Measure(root));
    }

    [Fact]
    public void Remove_deletes_models_logs_and_settings_but_not_siblings()
    {
        var root = Path.Combine(_dir.Path, "CodexVTT.Data");
        var history = Path.Combine(_dir.Path, "Documents", "CodexVTT", "history");
        Directory.CreateDirectory(Path.Combine(root, "models"));
        Directory.CreateDirectory(Path.Combine(root, "logs"));
        Directory.CreateDirectory(history);
        File.WriteAllText(Path.Combine(root, "models", "ggml-small.bin"), "m");
        File.WriteAllText(Path.Combine(root, "logs", "codexvtt.log"), "l");
        File.WriteAllText(Path.Combine(root, "settings.json"), "{}");
        File.WriteAllText(Path.Combine(history, "trascrizione.txt"), "testo");

        var failures = InternalDataRemover.Remove(root);

        Assert.Empty(failures);
        Assert.False(Directory.Exists(root));
        Assert.True(File.Exists(Path.Combine(history, "trascrizione.txt")));
    }

    [Fact]
    public void Missing_root_is_a_no_op()
    {
        var root = Path.Combine(_dir.Path, "absent");

        Assert.Equal(0, InternalDataRemover.Measure(root));
        Assert.Empty(InternalDataRemover.Remove(root));
    }
}
