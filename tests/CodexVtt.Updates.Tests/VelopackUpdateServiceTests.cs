using Microsoft.Extensions.Logging.Abstractions;
using CodexVtt.Bridge;
using CodexVtt.Core.Errors;
using CodexVtt.Updates;
using Velopack;
using Velopack.Locators;
using Velopack.Logging;
using Velopack.Sources;

namespace CodexVtt.Updates.Tests;

public sealed class VelopackUpdateServiceTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("codexvtt-updates").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private VelopackUpdateService Service(FakeSource source, string installed = "0.1.0") =>
        new("https://github.com/esempio/CodexVTT", NullLogger<VelopackUpdateService>.Instance, source,
            new TestVelopackLocator("CodexVtt", installed, _dir, NullVelopackLogger.Instance));

    [Fact]
    public async Task A_build_that_is_not_installed_never_asks_the_network()
    {
        var source = new FakeSource();
        var service = new VelopackUpdateService("https://github.com/esempio/CodexVTT", NullLogger<VelopackUpdateService>.Instance, source);

        var status = await service.CheckAsync(CancellationToken.None);

        Assert.Equal(UpdateState.Unsupported, status.State);
        Assert.Equal(0, source.FeedRequests);
    }

    [Fact]
    public async Task A_newer_release_is_reported_with_its_notes()
    {
        var source = new FakeSource(new VelopackAsset { PackageId = "CodexVtt", Version = SemanticVersion.Parse("0.2.0"), Type = VelopackAssetType.Full, FileName = "CodexVtt-0.2.0-full.nupkg", NotesMarkdown = "Novità" });
        var service = Service(source);

        var status = await service.CheckAsync(CancellationToken.None);

        Assert.Equal(UpdateState.Available, status.State);
        Assert.Equal("0.1.0", status.CurrentVersion);
        Assert.Equal("0.2.0", status.LatestVersion);
        Assert.Equal("Novità", status.Notes);
        Assert.Equal("https://github.com/esempio/CodexVTT/releases", status.ReleasesUrl);
        Assert.Equal(UpdateState.Available, service.Current.State);
    }

    [Fact]
    public async Task The_same_or_an_older_release_means_up_to_date()
    {
        var source = new FakeSource(new VelopackAsset { PackageId = "CodexVtt", Version = SemanticVersion.Parse("0.1.0"), Type = VelopackAssetType.Full, FileName = "CodexVtt-0.1.0-full.nupkg" });

        var status = await Service(source).CheckAsync(CancellationToken.None);

        Assert.Equal(UpdateState.UpToDate, status.State);
        Assert.Null(status.LatestVersion);
    }

    [Fact]
    public async Task A_failing_source_becomes_a_typed_error_without_leaking_details()
    {
        var source = new FakeSource { Failure = new HttpRequestException("403 rate limit https://api.github.com/repos/x?token=segreto") };

        var error = await Assert.ThrowsAsync<CodexVttException>(() => Service(source).CheckAsync(CancellationToken.None));

        Assert.Equal(ErrorCodes.UpdateFailed, error.Code);
        Assert.DoesNotContain("segreto", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Applying_needs_a_check_first()
    {
        var error = await Assert.ThrowsAsync<CodexVttException>(() => Service(new FakeSource()).ApplyAsync(new Progress<int>(), CancellationToken.None));

        Assert.Equal(ErrorCodes.UpdateUnavailable, error.Code);
    }

    private sealed class FakeSource(params VelopackAsset[] assets) : IUpdateSource
    {
        public int FeedRequests { get; private set; }

        public Exception? Failure { get; init; }

        public Task<VelopackAssetFeed> GetReleaseFeed(IVelopackLogger logger, string? appId, string channel, Guid? stagingId = null, VelopackAsset? latestLocalRelease = null)
        {
            FeedRequests++;
            if (Failure is not null)
                throw Failure;
            return Task.FromResult(new VelopackAssetFeed { Assets = assets });
        }

        public Task DownloadReleaseEntry(IVelopackLogger logger, VelopackAsset releaseEntry, string localFile, Action<int> progress, CancellationToken cancelToken) =>
            throw new NotSupportedException("Nessun download nei test.");
    }
}
