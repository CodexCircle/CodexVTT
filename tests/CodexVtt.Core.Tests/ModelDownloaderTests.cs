using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Logging.Abstractions;
using CodexVtt.Core.Errors;
using CodexVtt.Core.Models;
using CodexVtt.Models.Download;

namespace CodexVtt.Core.Tests;

public sealed class ModelDownloaderTests : IDisposable
{
    private static readonly byte[] Content = TestData.Bytes("0123456789abcdef", 64 * 1024);
    private readonly TempDir _dir = new();
    private readonly HttpListener _listener = new();
    private readonly int _port;
    private readonly List<string?> _rangeHeaders = [];
    private Func<HttpListenerContext, bool>? _override;

    public ModelDownloaderTests()
    {
        _port = FreePort();
        _listener.Prefixes.Add($"http://127.0.0.1:{_port}/");
        _listener.Start();
        _ = Task.Run(ServeAsync);
    }

    public void Dispose()
    {
        _listener.Close();
        _dir.Dispose();
    }

    private (ModelStore Store, ModelDownloader Downloader) Create(string path = "{revision}/{file}", HostAllowlist? allowlist = null, byte[]? catalogContent = null)
    {
        var catalog = TestData.Catalog($"http://127.0.0.1:{_port}/{path}", "rev1", ("tiny", catalogContent ?? Content, 1, 1, 0));
        var store = new ModelStore(_dir.AppPaths(), catalog);
        var downloader = new ModelDownloader(ModelDownloader.CreateHttpClient(), store,
            allowlist ?? new HostAllowlist([], allowLoopbackHttp: true), NullLogger<ModelDownloader>.Instance);
        return (store, downloader);
    }

    [Fact]
    public async Task Downloads_verifies_and_installs_model()
    {
        var (store, downloader) = Create();
        var reports = new List<DownloadProgress>();

        var status = await downloader.DownloadAsync("tiny", new Progress(reports), CancellationToken.None);

        Assert.Equal(ModelState.Ready, status.State);
        Assert.Equal(Content, await File.ReadAllBytesAsync(status.Path));
        Assert.Equal(Content.Length, reports[^1].ReceivedBytes);
        Assert.Equal("127.0.0.1", reports[^1].Host);
        Assert.Empty(Directory.GetFiles(Path.Combine(_dir.Path, "models", ".partial")));
    }

    [Fact]
    public async Task Resumes_partial_download_with_range_request()
    {
        var (store, downloader) = Create();
        var partial = Path.Combine(_dir.Path, "models", ".partial", "ggml-tiny.bin.part");
        await File.WriteAllBytesAsync(partial, Content[..(Content.Length / 2)]);

        var status = await downloader.DownloadAsync("tiny", null, CancellationToken.None);

        Assert.Equal(ModelState.Ready, status.State);
        Assert.Contains($"bytes={Content.Length / 2}-", _rangeHeaders);
        Assert.Equal(Content, await File.ReadAllBytesAsync(store.PathFor(store.Catalog.Find("tiny")!)));
    }

    [Fact]
    public async Task Hash_mismatch_discards_file()
    {
        var (store, downloader) = Create(catalogContent: TestData.Bytes("expected-other-content"));

        var ex = await Assert.ThrowsAsync<CodexVttException>(() => downloader.DownloadAsync("tiny", null, CancellationToken.None));

        Assert.Equal(ErrorCodes.ModelHashMismatch, ex.Code);
        Assert.Equal(ModelState.NotInstalled, store.Status(store.Catalog.Find("tiny")!).State);
        Assert.Empty(Directory.GetFiles(Path.Combine(_dir.Path, "models", ".partial")));
    }

    [Fact]
    public async Task Redirect_to_host_outside_allowlist_is_blocked()
    {
        _override = ctx =>
        {
            ctx.Response.StatusCode = 302;
            ctx.Response.RedirectLocation = "https://evil.example.com/ggml-tiny.bin";
            ctx.Response.Close();
            return true;
        };
        var (_, downloader) = Create();

        var ex = await Assert.ThrowsAsync<CodexVttException>(() => downloader.DownloadAsync("tiny", null, CancellationToken.None));

        Assert.Equal(ErrorCodes.DownloadHostNotAllowed, ex.Code);
        Assert.Equal("evil.example.com", ex.Details["host"]);
    }

    [Fact]
    public async Task Initial_host_must_be_allowed()
    {
        var (_, downloader) = Create(allowlist: HostAllowlist.ModelHosts);

        var ex = await Assert.ThrowsAsync<CodexVttException>(() => downloader.DownloadAsync("tiny", null, CancellationToken.None));

        Assert.Equal(ErrorCodes.DownloadHostNotAllowed, ex.Code);
        Assert.Empty(_rangeHeaders);
    }

    [Fact]
    public async Task Http_errors_are_reported_as_download_failed()
    {
        _override = ctx =>
        {
            ctx.Response.StatusCode = 503;
            ctx.Response.Close();
            return true;
        };
        var (_, downloader) = Create();

        var ex = await Assert.ThrowsAsync<CodexVttException>(() => downloader.DownloadAsync("tiny", null, CancellationToken.None));

        Assert.Equal(ErrorCodes.DownloadFailed, ex.Code);
    }

    [Theory]
    [InlineData("https://huggingface.co/ggerganov/whisper.cpp/resolve/abc/ggml-base.bin", true)]
    [InlineData("https://cdn-lfs.huggingface.co/repos/x", true)]
    [InlineData("https://cas-bridge.xethub.hf.co/x", true)]
    [InlineData("http://huggingface.co/x", false)]
    [InlineData("https://huggingface.co.evil.com/x", false)]
    [InlineData("https://evilhuggingface.co/x", false)]
    [InlineData("http://127.0.0.1/x", false)]
    public void Model_host_allowlist(string url, bool allowed) =>
        Assert.Equal(allowed, HostAllowlist.ModelHosts.IsAllowed(new Uri(url)));

    private async Task ServeAsync()
    {
        while (_listener.IsListening)
        {
            HttpListenerContext ctx;
            try
            {
                ctx = await _listener.GetContextAsync();
            }
            catch (Exception)
            {
                return;
            }

            try
            {
                if (_override?.Invoke(ctx) == true)
                    continue;
                var range = ctx.Request.Headers["Range"];
                lock (_rangeHeaders)
                    _rangeHeaders.Add(range);
                var start = 0;
                if (range is not null && range.StartsWith("bytes=", StringComparison.Ordinal))
                {
                    start = int.Parse(range[6..].TrimEnd('-'), System.Globalization.CultureInfo.InvariantCulture);
                    ctx.Response.StatusCode = 206;
                    ctx.Response.Headers["Content-Range"] = $"bytes {start}-{Content.Length - 1}/{Content.Length}";
                }

                ctx.Response.ContentLength64 = Content.Length - start;
                await ctx.Response.OutputStream.WriteAsync(Content.AsMemory(start));
                ctx.Response.Close();
            }
            catch (Exception)
            {
                // Il client può chiudere la connessione: irrilevante per i test.
            }
        }
    }

    private static int FreePort()
    {
        using var socket = new TcpListener(IPAddress.Loopback, 0);
        socket.Start();
        return ((IPEndPoint)socket.LocalEndpoint).Port;
    }

    private sealed class Progress(List<DownloadProgress> reports) : IProgress<DownloadProgress>
    {
        public void Report(DownloadProgress value)
        {
            lock (reports)
                reports.Add(value);
        }
    }
}
