using Microsoft.Extensions.Logging.Abstractions;
using CodexVtt.Bridge;
using Velopack.Locators;
using Velopack.Logging;
using Velopack.Sources;

namespace CodexVtt.Updates.Tests;

/// <summary>
/// Prova con un feed prodotto davvero da <c>vpk pack</c> (due versioni, con delta). Si esegue solo se
/// CODEXVTT_TEST_FEED punta alla cartella dell'output di vpk: <c>scripts/test-locale.ps1 -Feed</c> lo prepara.
/// </summary>
public sealed class RealFeedTests : IDisposable
{
    private readonly string _packages = Directory.CreateTempSubdirectory("codexvtt-packages").FullName;

    public void Dispose() => Directory.Delete(_packages, recursive: true);

    [Fact]
    public async Task The_service_finds_the_newer_release_in_a_real_velopack_feed_and_downloads_it()
    {
        var feed = Environment.GetEnvironmentVariable("CODEXVTT_TEST_FEED");
        if (string.IsNullOrEmpty(feed) || !Directory.Exists(feed))
            return;

        var service = new VelopackUpdateService("https://github.com/esempio/CodexVTT", NullLogger<VelopackUpdateService>.Instance,
            new SimpleFileSource(new DirectoryInfo(feed)), new TestVelopackLocator("CodexVTT", "0.1.0", _packages, NullVelopackLogger.Instance));

        var status = await service.CheckAsync(CancellationToken.None);

        Assert.Equal(UpdateState.Available, status.State);
        Assert.Equal("0.2.0", status.LatestVersion);

        // Scarica (con il delta) e verifica il pacchetto; l'installazione vera avviene solo in un'app installata.
        var percent = 0;
        await Record.ExceptionAsync(() => service.ApplyAsync(new Progress<int>(p => percent = p), CancellationToken.None));
        Assert.True(File.Exists(Path.Combine(_packages, "CodexVTT-0.2.0-full.nupkg")), "Il pacchetto 0.2.0 non è stato ricostruito dal delta.");
    }
}
