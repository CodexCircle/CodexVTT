using CodexVtt.Bridge;
using CodexVtt.Core;
using Microsoft.Extensions.Logging.Abstractions;
using Velopack.Locators;
using Velopack.Logging;

namespace CodexVtt.Updates.Tests;

/// <summary>
/// Controllo contro GitHub vero, da fare dopo ogni release: il servizio deve vedere l'ultima release pubblicata di
/// <see cref="ProjectInfo.RepositoryUrl"/>. Si esegue solo con CODEXVTT_TEST_GITHUB=1 (usa la rete);
/// con CODEXVTT_TEST_GITHUB=download scarica anche il pacchetto e ne verifica il checksum.
/// </summary>
public sealed class GithubReleaseTests : IDisposable
{
    private readonly string _packages = Directory.CreateTempSubdirectory("codexvtt-github").FullName;

    public void Dispose() => Directory.Delete(_packages, recursive: true);

    private VelopackUpdateService Service(string installedVersion) =>
        new(ProjectInfo.RepositoryUrl, NullLogger<VelopackUpdateService>.Instance, source: null,
            new TestVelopackLocator("CodexVTT", installedVersion, _packages, NullVelopackLogger.Instance));

    [Fact]
    public async Task The_latest_github_release_is_offered_to_an_older_install_and_not_to_an_up_to_date_one()
    {
        var mode = Environment.GetEnvironmentVariable("CODEXVTT_TEST_GITHUB");
        if (string.IsNullOrEmpty(mode))
            return;

        var older = Service("0.0.1");
        var offered = await older.CheckAsync(CancellationToken.None);
        Assert.Equal(UpdateState.Available, offered.State);
        Assert.False(string.IsNullOrEmpty(offered.LatestVersion));

        var current = await Service("99.0.0").CheckAsync(CancellationToken.None);
        Assert.Equal(UpdateState.UpToDate, current.State);

        if (mode == "download")
        {
            // Scarica e verifica il pacchetto; l'installazione vera (e il riavvio) avviene solo in un'app installata.
            await Record.ExceptionAsync(() => older.ApplyAsync(new Progress<int>(), CancellationToken.None));
            var downloaded = Directory.GetFiles(_packages, "*-full.nupkg");
            Assert.NotEmpty(downloaded);
            Assert.True(new FileInfo(downloaded[0]).Length > 1_000_000, "Il pacchetto scaricato è troppo piccolo.");
        }
    }
}
