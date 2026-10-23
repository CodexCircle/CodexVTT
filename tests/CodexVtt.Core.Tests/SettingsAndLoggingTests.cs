using Microsoft.Extensions.Logging;
using CodexVtt.Core.Logging;
using CodexVtt.Core.Settings;

namespace CodexVtt.Core.Tests;

public sealed class SettingsAndLoggingTests : IDisposable
{
    private readonly TempDir _dir = new();

    public void Dispose() => _dir.Dispose();

    [Fact]
    public void Settings_round_trip_and_survive_corruption()
    {
        var path = Path.Combine(_dir.Path, "settings.json");
        new SettingsStore(path).Save(new AppSettings { SelectedModelId = "small", Language = "it", ShowMascot = false });

        var loaded = new SettingsStore(path).Load();
        File.WriteAllText(path, "{ broken");
        var recovered = new SettingsStore(path).Load();

        Assert.Equal("small", loaded.SelectedModelId);
        Assert.False(loaded.ShowMascot);
        Assert.Equal(new AppSettings(), recovered);
    }

    [Fact]
    public void Logs_rotate_and_respect_minimum_level()
    {
        var logs = Path.Combine(_dir.Path, "logs");
        using var provider = new FileLoggerProvider(logs, LogLevel.Information, maxBytes: 200, maxFiles: 3);
        var logger = provider.CreateLogger("Test");

        logger.LogDebug("non deve comparire");
        for (var i = 0; i < 20; i++)
            logger.LogInformation("messaggio numero {Index} con un po' di testo", i);

        var files = Directory.GetFiles(logs).Select(Path.GetFileName).Order().ToList();
        Assert.Equal(["codexvtt.1.log", "codexvtt.2.log", "codexvtt.log"], files);
        Assert.DoesNotContain("non deve comparire", string.Concat(files.Select(f => File.ReadAllText(Path.Combine(logs, f!)))), StringComparison.Ordinal);
    }

    [Fact]
    public void App_paths_clean_temp_files()
    {
        var paths = _dir.AppPaths();
        File.WriteAllText(Path.Combine(paths.Temp, "job.wav"), "x");

        paths.CleanTemp();

        Assert.Empty(Directory.GetFiles(paths.Temp));
        Assert.True(Directory.Exists(paths.PartialModels));
    }
}
