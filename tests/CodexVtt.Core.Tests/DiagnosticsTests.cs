using Microsoft.Extensions.Logging;
using CodexVtt.Core.Diagnostics;
using CodexVtt.Core.Logging;
using CodexVtt.Core.Models;

namespace CodexVtt.Core.Tests;

public class DiagnosticOptionsTests
{
    private static Func<string, string?> Env(params (string Key, string Value)[] values) =>
        key => values.FirstOrDefault(v => v.Key == key).Value;

    [Fact]
    public void Default_is_information_without_debug_mode()
    {
        var options = DiagnosticOptions.Resolve([], Env());

        Assert.Equal(LogLevel.Information, options.MinimumLevel);
        Assert.False(options.DebugMode);
    }

    [Fact]
    public void Debug_argument_enables_debug_mode_and_debug_level()
    {
        var options = DiagnosticOptions.Resolve(["--debug"], Env());

        Assert.True(options.DebugMode);
        Assert.Equal(LogLevel.Debug, options.MinimumLevel);
    }

    [Fact]
    public void Debug_environment_variable_is_equivalent_to_argument()
    {
        Assert.True(DiagnosticOptions.Resolve([], Env(("CODEXVTT_DEBUG", "1"))).DebugMode);
        Assert.True(DiagnosticOptions.Resolve([], Env(("CODEXVTT_DEBUG", "true"))).DebugMode);
        Assert.False(DiagnosticOptions.Resolve([], Env(("CODEXVTT_DEBUG", "0"))).DebugMode);
    }

    [Fact]
    public void Log_level_environment_variable_overrides_settings()
    {
        var options = DiagnosticOptions.Resolve([], Env(("CODEXVTT_LOG_LEVEL", "Warning")), settingsLevel: "Trace");

        Assert.Equal(LogLevel.Warning, options.MinimumLevel);
    }

    [Fact]
    public void Settings_level_is_used_when_no_environment_variable()
    {
        var options = DiagnosticOptions.Resolve([], Env(), settingsLevel: "error");

        Assert.Equal(LogLevel.Error, options.MinimumLevel);
    }

    [Fact]
    public void Debug_level_in_settings_enables_debug_mode()
    {
        var options = DiagnosticOptions.Resolve([], Env(), settingsLevel: "Debug");

        Assert.True(options.DebugMode);
        Assert.Equal(LogLevel.Debug, options.MinimumLevel);
    }

    [Fact]
    public void Invalid_or_none_level_falls_back_to_default()
    {
        Assert.Equal(LogLevel.Information, DiagnosticOptions.Resolve([], Env(("CODEXVTT_LOG_LEVEL", "verbose"))).MinimumLevel);
        Assert.Equal(LogLevel.Information, DiagnosticOptions.Resolve([], Env(("CODEXVTT_LOG_LEVEL", "None"))).MinimumLevel);
    }

    [Fact]
    public void Explicit_level_wins_over_debug_flag()
    {
        var options = DiagnosticOptions.Resolve(["--debug"], Env(("CODEXVTT_LOG_LEVEL", "Error")));

        Assert.Equal(LogLevel.Error, options.MinimumLevel);
        Assert.True(options.DebugMode);
    }
}

public class DiagnosticReporterTests
{
    [Fact]
    public async Task Report_marks_missing_webview2_and_reports_writable_directories()
    {
        using var dir = new TempDir();
        var paths = dir.AppPaths();
        paths.EnsureCreated();
        var models = new ModelStore(paths, TestData.Catalog());

        var report = await DiagnosticReporter.RunAsync(paths, models, "0.2.8-test", webView2Version: null, historyDirectory: null);

        Assert.False(report.Passed);
        Assert.Contains(report.Checks, c => c.Name == "webview2.runtime" && !c.Ok);
        Assert.Contains(report.Checks, c => c.Name == "dir.models" && c.Ok);
        Assert.Empty(report.LogTail);
    }

    [Fact]
    public async Task Report_includes_last_log_lines_and_passes_without_models()
    {
        using var dir = new TempDir();
        var paths = dir.AppPaths();
        paths.EnsureCreated();
        File.WriteAllLines(Path.Combine(paths.Logs, DiagnosticReporter.LogFileName), ["riga 1", "riga 2", "riga 3"]);
        var models = new ModelStore(paths, TestData.Catalog());

        var report = await DiagnosticReporter.RunAsync(paths, models, "0.2.8-test", "120.0.0.0", null, logLines: 2);

        Assert.True(report.Passed);
        Assert.Equal(["riga 2", "riga 3"], report.LogTail);
    }

    [Fact]
    public async Task Report_round_trips_to_json_file()
    {
        using var dir = new TempDir();
        var paths = dir.AppPaths();
        paths.EnsureCreated();
        var models = new ModelStore(paths, TestData.Catalog());
        var report = await DiagnosticReporter.RunAsync(paths, models, "0.2.8-test", "120.0.0.0", null);
        var file = Path.Combine(dir.Path, "out", "diagnostic-report.json");

        await DiagnosticReporter.WriteAsync(report, file);

        Assert.True(File.Exists(file));
        Assert.Contains("\"AppVersion\": \"0.2.8-test\"", await File.ReadAllTextAsync(file));
    }
}
