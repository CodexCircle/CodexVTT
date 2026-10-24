using System.Net;
using System.Security.Cryptography;
using CodexVtt.Core.Benchmark;
using CodexVtt.Core.Errors;
using CodexVtt.Models.Download;

namespace CodexVtt.Core.Tests;

public sealed class ReferenceFileDownloaderTests : IDisposable
{
    private static readonly byte[] Content = TestData.Bytes("riferimento", 500);
    private static readonly string Hash = Convert.ToHexStringLower(SHA256.HashData(Content));
    private readonly TempDir _dir = new();

    public void Dispose() => _dir.Dispose();

    private ReferenceFileDownloader Create(Func<HttpRequestMessage, HttpResponseMessage> handler, string url = "https://archive.org/download/x/y.mp3", byte[]? expected = null)
    {
        var bytes = expected ?? Content;
        return new ReferenceFileDownloader(new HttpClient(new FakeHandler(handler)), _dir.Path, new HostAllowlist(["archive.org"]), new Uri(url),
            Convert.ToHexStringLower(SHA256.HashData(bytes)), bytes.Length);
    }

    private static HttpResponseMessage Ok(byte[] bytes) => new(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) };

    [Fact]
    public async Task Downloads_follows_redirects_inside_the_allowlist_and_keeps_the_verified_file()
    {
        var hosts = new List<string>();
        var downloader = Create(request =>
        {
            hosts.Add(request.RequestUri!.Host);
            return request.RequestUri.Host == "archive.org"
                ? new HttpResponseMessage(HttpStatusCode.Found) { Headers = { Location = new Uri("https://dn1.ca.archive.org/0/items/x/y.mp3") } }
                : Ok(Content);
        });
        Assert.Null(downloader.CachedPath());

        var path = await downloader.EnsureAsync(CancellationToken.None);

        Assert.Equal(["archive.org", "dn1.ca.archive.org"], hosts);
        Assert.Equal(Content, await File.ReadAllBytesAsync(path));
        Assert.Equal(path, downloader.CachedPath());
        Assert.Empty(Directory.GetFiles(_dir.Path, "*.part"));
    }

    [Fact]
    public async Task A_cached_verified_file_needs_no_network()
    {
        var calls = 0;
        var downloader = Create(_ => { calls++; return Ok(Content); });
        await downloader.EnsureAsync(CancellationToken.None);

        await downloader.EnsureAsync(CancellationToken.None);

        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task A_file_with_the_wrong_hash_is_discarded()
    {
        var downloader = Create(_ => Ok(TestData.Bytes("altro-contenuto", 500)), expected: Content);

        var error = await Assert.ThrowsAsync<CodexVttException>(() => downloader.EnsureAsync(CancellationToken.None));

        Assert.Equal(ErrorCodes.ModelHashMismatch, error.Code);
        Assert.Null(downloader.CachedPath());
        Assert.Empty(Directory.GetFileSystemEntries(_dir.Path));
    }

    [Fact]
    public async Task A_longer_file_than_expected_is_discarded()
    {
        var downloader = Create(_ => Ok([.. Content, .. Content]));

        var error = await Assert.ThrowsAsync<CodexVttException>(() => downloader.EnsureAsync(CancellationToken.None));

        Assert.Equal(ErrorCodes.ModelHashMismatch, error.Code);
    }

    [Fact]
    public async Task Hosts_outside_the_allowlist_are_refused_even_after_a_redirect()
    {
        var downloader = Create(_ => new HttpResponseMessage(HttpStatusCode.Found) { Headers = { Location = new Uri("https://evil.example/y.mp3") } });

        var error = await Assert.ThrowsAsync<CodexVttException>(() => downloader.EnsureAsync(CancellationToken.None));

        Assert.Equal(ErrorCodes.DownloadHostNotAllowed, error.Code);
    }

    [Fact]
    public async Task A_server_error_is_reported_as_a_failed_download()
    {
        var downloader = Create(_ => new HttpResponseMessage(HttpStatusCode.NotFound));

        var error = await Assert.ThrowsAsync<CodexVttException>(() => downloader.EnsureAsync(CancellationToken.None));

        Assert.Equal(ErrorCodes.DownloadFailed, error.Code);
    }

    [Fact]
    public void The_pinned_reference_points_at_archive_org_over_https_with_a_complete_hash()
    {
        Assert.StartsWith("https://archive.org/download/", BenchmarkReference.Url, StringComparison.Ordinal);
        Assert.Equal(64, BenchmarkReference.Sha256.Length);
        Assert.True(BenchmarkReference.SizeBytes > 0);
        Assert.InRange(BenchmarkReference.ApproximateSeconds, 10, 600);
    }

    private sealed class FakeHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(respond(request));
    }
}
