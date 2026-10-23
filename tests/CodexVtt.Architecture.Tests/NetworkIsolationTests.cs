using System.Reflection;

namespace CodexVtt.Architecture.Tests;

/// <summary>
/// Vincoli di ADR-0006: solo CodexVtt.Models.Download può usare la rete; nessun SDK di telemetria.
/// </summary>
public class NetworkIsolationTests
{
    private static readonly string[] NetworkAssemblies =
    [
        "System.Net.Http",
        "System.Net.Sockets",
        "System.Net.WebSockets",
        "System.Net.WebSockets.Client",
        "System.Net.Requests",
        "System.Net.WebClient",
        "System.Net.Mail",
        "System.Net.HttpListener",
    ];

    private static readonly string[] TelemetryMarkers =
    [
        "ApplicationInsights",
        "Sentry",
        "AppCenter",
        "OpenTelemetry",
        "Bugsnag",
        "Raygun",
        "Mixpanel",
        "Segment",
    ];

    public static TheoryData<string> OfflineAssemblies => new()
    {
        "CodexVtt.Core",
        "CodexVtt.Transcription.Whisper",
        "CodexVtt.Audio.Windows",
        "CodexVtt.Audio.FFmpeg",
        "CodexVtt.Bridge",
    };

    [Theory]
    [MemberData(nameof(OfflineAssemblies))]
    public void Offline_assemblies_do_not_reference_network_apis(string assemblyName)
    {
        var assembly = Assembly.Load(assemblyName);

        var references = assembly.GetReferencedAssemblies().Select(a => a.Name!).ToList();

        Assert.Empty(references.Intersect(NetworkAssemblies, StringComparer.OrdinalIgnoreCase));
    }

    /// <summary>ADR-0008: l'aggiornamento dell'app è l'unico altro punto che parla con GitHub, tramite Velopack.</summary>
    [Fact]
    public void Only_the_update_assembly_references_velopack()
    {
        var offline = new[] { "CodexVtt.Core", "CodexVtt.Transcription.Whisper", "CodexVtt.Audio.Windows", "CodexVtt.Audio.FFmpeg", "CodexVtt.Bridge", "CodexVtt.Models.Download" };
        foreach (var name in offline)
            Assert.DoesNotContain(Assembly.Load(name).GetReferencedAssemblies(), a => a.Name == "Velopack");

        Assert.Contains(Assembly.Load("CodexVtt.Updates").GetReferencedAssemblies(), a => a.Name == "Velopack");
    }

    [Fact]
    public void Only_download_assembly_uses_http()
    {
        var references = Assembly.Load("CodexVtt.Models.Download").GetReferencedAssemblies().Select(a => a.Name);

        Assert.Contains("System.Net.Http", references);
    }

    [Fact]
    public void No_telemetry_packages_in_dependency_graph()
    {
        var deps = Directory.GetFiles(AppContext.BaseDirectory, "*.deps.json").Select(File.ReadAllText);
        var dlls = Directory.GetFiles(AppContext.BaseDirectory, "*.dll").Select(Path.GetFileName);

        foreach (var marker in TelemetryMarkers)
        {
            Assert.DoesNotContain(dlls, d => d!.Contains(marker, StringComparison.OrdinalIgnoreCase));
            Assert.All(deps, d => Assert.DoesNotContain($"\"{marker}", d, StringComparison.OrdinalIgnoreCase));
        }
    }
}
