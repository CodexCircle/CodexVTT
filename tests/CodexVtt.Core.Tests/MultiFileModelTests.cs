using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using CodexVtt.Core.Errors;
using CodexVtt.Core.Models;
using CodexVtt.Models.Download;

namespace CodexVtt.Core.Tests;

/// <summary>Modelli fatti di più file (Parakeet, Canary, Cohere): installazione in cartella, verifica e download.</summary>
public sealed class MultiFileModelTests : IDisposable
{
    private readonly TempDir _dir = new();
    private readonly HttpListener _listener = new();
    private readonly int _port;
    private readonly Dictionary<string, byte[]> _served = new(StringComparer.Ordinal);
    private readonly List<string?> _ranges = [];
    private readonly Dictionary<string, byte[]> _files = new()
    {
        ["encoder.int8.onnx"] = TestData.Bytes("encoder-pesi", 40_000),
        ["decoder.int8.onnx"] = TestData.Bytes("decoder-pesi", 10_000),
        ["tokens.txt"] = TestData.Bytes("<blk> 0\n", 100),
    };

    public MultiFileModelTests()
    {
        using (var probe = new TcpListener(IPAddress.Loopback, 0))
        {
            probe.Start();
            _port = ((IPEndPoint)probe.LocalEndpoint).Port;
        }

        _listener.Prefixes.Add($"http://127.0.0.1:{_port}/");
        _listener.Start();
        foreach (var (name, content) in _files)
            _served[$"/m/rev9/{name}"] = content;
        _ = Task.Run(ServeAsync);
    }

    public void Dispose()
    {
        _listener.Close();
        _dir.Dispose();
    }

    private ModelDescriptor Descriptor(string? wrongHashFor = null)
    {
        var files = _files.Select(f => new ModelFile(f.Key, f.Value.Length, f.Key == wrongHashFor ? new string('0', 64) : TestData.Sha256(f.Value))).ToList();
        return new ModelDescriptor(
            "multi", "Multi", "multi", "int8", files.Sum(f => f.SizeBytes), null, 1_000_000_000, 1, 9, true, "CC-BY-4.0",
            Engine: "sherpa-transducer", Files: files,
            Source: new CatalogSource("x/y", "rev9", $"http://127.0.0.1:{_port}/m/{{revision}}/{{file}}"),
            Attribution: "Esempio by NVIDIA, CC-BY-4.0", Preview: true);
    }

    private (ModelStore Store, ModelDownloader Downloader, ModelDescriptor Model) Create(string? wrongHashFor = null)
    {
        var model = Descriptor(wrongHashFor);
        var catalog = new ModelCatalog(new CatalogSource("t/t", "rev1", "https://huggingface.co/{revision}/{file}"), TestData.Rules, [model]);
        var store = new ModelStore(_dir.AppPaths(), catalog);
        var downloader = new ModelDownloader(ModelDownloader.CreateHttpClient(), store, new HostAllowlist([], allowLoopbackHttp: true), NullLogger<ModelDownloader>.Instance);
        return (store, downloader, model);
    }

    [Fact]
    public async Task Downloads_every_file_verifies_each_and_installs_the_folder_in_one_step()
    {
        var (store, downloader, model) = Create();
        var reports = new List<DownloadProgress>();

        var status = await downloader.DownloadAsync("multi", new Progress<DownloadProgress>(reports.Add), CancellationToken.None);

        Assert.Equal(ModelState.Ready, status.State);
        Assert.Equal(Path.Combine(_dir.Path, "models", "multi"), status.Path);
        foreach (var (name, content) in _files)
            Assert.Equal(content, await File.ReadAllBytesAsync(Path.Combine(status.Path, name)));
        // Il progresso copre l'intero modello, non il singolo file.
        Assert.Equal(model.SizeBytes, reports[^1].TotalBytes);
        Assert.Equal(model.SizeBytes, reports[^1].ReceivedBytes);
        Assert.False(Directory.Exists(status.Path + ".installing"));
        Assert.False(Directory.Exists(store.PartialFolderFor(model)));
        Assert.Equal(model.SizeBytes, status.SizeOnDiskBytes);
    }

    [Fact]
    public async Task A_wrong_hash_on_any_file_installs_nothing()
    {
        var (store, downloader, model) = Create(wrongHashFor: "decoder.int8.onnx");

        var error = await Assert.ThrowsAsync<CodexVttException>(() => downloader.DownloadAsync("multi", null, CancellationToken.None));

        Assert.Equal(ErrorCodes.ModelHashMismatch, error.Code);
        Assert.Equal("decoder.int8.onnx", error.Details["file"]);
        Assert.Equal(ModelState.NotInstalled, store.Status(model).State);
        Assert.False(Directory.Exists(store.PathFor(model)));
    }

    [Fact]
    public async Task A_partial_file_is_resumed_with_a_range_request()
    {
        var (store, downloader, model) = Create();
        Directory.CreateDirectory(store.PartialFolderFor(model));
        var first = _files["encoder.int8.onnx"];
        await File.WriteAllBytesAsync(Path.Combine(store.PartialFolderFor(model), "encoder.int8.onnx.part"), first[..(first.Length / 2)]);

        var status = await downloader.DownloadAsync("multi", null, CancellationToken.None);

        Assert.Equal(ModelState.Ready, status.State);
        Assert.Contains($"bytes={first.Length / 2}-", _ranges);
        Assert.Equal(first, await File.ReadAllBytesAsync(Path.Combine(status.Path, "encoder.int8.onnx")));
    }

    [Fact]
    public async Task Altering_a_file_is_found_by_verification_and_the_model_is_not_used()
    {
        var (store, downloader, model) = Create();
        await downloader.DownloadAsync("multi", null, CancellationToken.None);
        var target = Path.Combine(store.PathFor(model), "decoder.int8.onnx");
        var altered = (byte[])_files["decoder.int8.onnx"].Clone();
        altered[0] ^= 0xFF;
        await File.WriteAllBytesAsync(target, altered);

        // Stessa dimensione ma data diversa: prima va riverificato, poi risulta danneggiato.
        Assert.Equal(ModelState.Unverified, store.Status(model).State);
        var error = await Assert.ThrowsAsync<CodexVttException>(() => store.VerifyAsync(model));
        Assert.Equal(ErrorCodes.ModelHashMismatch, error.Code);
        Assert.Equal(ModelState.Corrupted, store.Status(model).State);
        await Assert.ThrowsAsync<CodexVttException>(() => store.RequireReadyAsync("multi"));
    }

    [Fact]
    public async Task A_missing_file_makes_the_model_corrupted_and_removal_deletes_the_folder()
    {
        var (store, downloader, model) = Create();
        await downloader.DownloadAsync("multi", null, CancellationToken.None);
        File.Delete(Path.Combine(store.PathFor(model), "tokens.txt"));

        Assert.Equal(ModelState.Corrupted, store.Status(model).State);
        store.Remove("multi");

        Assert.False(Directory.Exists(store.PathFor(model)));
        Assert.Equal(ModelState.NotInstalled, store.Status(model).State);
    }

    [Fact]
    public async Task A_folder_without_the_verification_marker_is_unverified_then_ready_after_verification()
    {
        var (store, downloader, model) = Create();
        await downloader.DownloadAsync("multi", null, CancellationToken.None);
        File.Delete(Path.Combine(store.PathFor(model), ".verified.json"));

        Assert.Equal(ModelState.Unverified, store.Status(model).State);
        Assert.Equal(store.PathFor(model), await store.RequireReadyAsync("multi"));
        Assert.Equal(ModelState.Ready, store.Status(model).State);
    }

    [Fact]
    public void Download_urls_use_the_source_of_the_model_not_the_catalog_one()
    {
        var (store, _, model) = Create();

        var urls = store.Catalog.DownloadUrls(model)!;

        Assert.Equal(3, urls.Count);
        Assert.All(urls, u => Assert.Equal($"http://127.0.0.1:{_port}/m/rev9/{u.File.Path}", u.Url.ToString()));
    }

    [Fact]
    public void Files_cannot_escape_the_model_folder_and_sizes_must_add_up()
    {
        string Catalog(string path, long total) => JsonSerializer.Serialize(new
        {
            version = 2,
            source = new { repository = "a/b", revision = "r", urlTemplate = "https://huggingface.co/{revision}/{file}" },
            selection = new { maxRamFraction = 0.5, diskHeadroomFraction = 0.1, costBudgetByCores = new[] { new { minCores = 1, maxCost = 2 } }, noAvxMaxCost = 2 },
            models = new[]
            {
                new
                {
                    id = "m", displayName = "M", file = "m", quantization = "int8", sizeBytes = total, ramRequiredBytes = 1, relativeCost = 1, qualityRank = 1,
                    multilingual = true, license = "MIT", engine = "sherpa-canary",
                    files = new[] { new { path, sizeBytes = 10L, sha256 = new string('a', 64) } },
                },
            },
        });

        static ModelCatalog Load(string json) => ModelCatalog.Load(new MemoryStream(Encoding.UTF8.GetBytes(json)));

        Assert.Equal("m", Load(Catalog("encoder.onnx", 10)).Models[0].Id);
        Assert.Throws<InvalidDataException>(() => Load(Catalog("encoder.onnx", 11)));
        Assert.Throws<InvalidDataException>(() => Load(Catalog("../escape.bin", 10)));
        Assert.Throws<InvalidDataException>(() => Load(Catalog("/etc/passwd", 10)));
    }

    [Fact]
    public void The_fingerprint_identifies_the_set_of_files()
    {
        var a = Descriptor();
        var b = Descriptor(wrongHashFor: "tokens.txt");

        Assert.Matches("^[0-9a-f]{64}$", a.Fingerprint);
        Assert.NotEqual(a.Fingerprint, b.Fingerprint);
        Assert.Equal(a.Fingerprint, Descriptor().Fingerprint);
    }

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
                if (!_served.TryGetValue(ctx.Request.Url!.AbsolutePath, out var content))
                {
                    ctx.Response.StatusCode = 404;
                    ctx.Response.Close();
                    continue;
                }

                var range = ctx.Request.Headers["Range"];
                lock (_ranges)
                    _ranges.Add(range);
                var start = 0;
                if (range is not null && range.StartsWith("bytes=", StringComparison.Ordinal))
                {
                    start = int.Parse(range[6..].TrimEnd('-'), System.Globalization.CultureInfo.InvariantCulture);
                    ctx.Response.StatusCode = 206;
                    ctx.Response.Headers["Content-Range"] = $"bytes {start}-{content.Length - 1}/{content.Length}";
                }

                ctx.Response.ContentLength64 = content.Length - start;
                await ctx.Response.OutputStream.WriteAsync(content.AsMemory(start));
                ctx.Response.Close();
            }
            catch (Exception)
            {
                // Il client può chiudere la connessione: irrilevante per i test.
            }
        }
    }
}
