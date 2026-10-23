using CodexVtt.Core.Errors;
using CodexVtt.Core.Models;

namespace CodexVtt.Core.Tests;

public sealed class ModelStoreTests : IDisposable
{
    private static readonly byte[] TinyContent = TestData.Bytes("tiny-model", 1000);
    private readonly TempDir _dir = new();
    private readonly ModelStore _store;

    public ModelStoreTests()
    {
        _store = new ModelStore(_dir.AppPaths(), TestData.Catalog(models: ("tiny", TinyContent, 1, 1, 0)));
    }

    public void Dispose() => _dir.Dispose();

    private ModelDescriptor Tiny => _store.Catalog.Find("tiny")!;

    [Fact]
    public void Missing_file_is_not_installed()
    {
        Assert.Equal(ModelState.NotInstalled, _store.Status(Tiny).State);
        Assert.Empty(_store.InstalledIds());
    }

    [Fact]
    public async Task File_without_marker_is_unverified_then_ready_after_verify()
    {
        await File.WriteAllBytesAsync(_store.PathFor(Tiny), TinyContent);
        Assert.Equal(ModelState.Unverified, _store.Status(Tiny).State);

        var status = await _store.VerifyAsync(Tiny);

        Assert.Equal(ModelState.Ready, status.State);
        Assert.Equal(_store.PathFor(Tiny), await _store.RequireReadyAsync("tiny"));
    }

    [Fact]
    public async Task Tampered_model_is_corrupted_and_never_used()
    {
        await File.WriteAllBytesAsync(_store.PathFor(Tiny), TestData.Bytes("evil", 10));

        var ex = await Assert.ThrowsAsync<CodexVttException>(() => _store.VerifyAsync(Tiny));

        Assert.Equal(ErrorCodes.ModelHashMismatch, ex.Code);
        Assert.Equal(ModelState.Corrupted, _store.Status(Tiny).State);
        var use = await Assert.ThrowsAsync<CodexVttException>(() => _store.RequireReadyAsync("tiny"));
        Assert.Equal(ErrorCodes.ModelHashMismatch, use.Code);
    }

    [Fact]
    public async Task File_modified_after_verification_needs_verification_again()
    {
        await File.WriteAllBytesAsync(_store.PathFor(Tiny), TinyContent);
        await _store.VerifyAsync(Tiny);

        await File.WriteAllBytesAsync(_store.PathFor(Tiny), TestData.Bytes("x", 5));

        Assert.Equal(ModelState.Unverified, _store.Status(Tiny).State);
        await Assert.ThrowsAsync<CodexVttException>(() => _store.RequireReadyAsync("tiny"));
    }

    [Fact]
    public async Task Import_accepts_only_catalog_hashes()
    {
        var good = _dir.File("downloaded.bin", TinyContent);
        var bad = _dir.File("other.bin", TestData.Bytes("other"));

        var status = await _store.ImportAsync(good);
        var ex = await Assert.ThrowsAsync<CodexVttException>(() => _store.ImportAsync(bad));

        Assert.Equal(ModelState.Ready, status.State);
        Assert.Equal(ErrorCodes.ModelImportUnknown, ex.Code);
        Assert.True(File.Exists(good));
    }

    [Fact]
    public async Task Remove_deletes_model_and_marker()
    {
        await _store.ImportAsync(_dir.File("m.bin", TinyContent));

        _store.Remove("tiny");

        Assert.Equal(ModelState.NotInstalled, _store.Status(Tiny).State);
        Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(_store.PathFor(Tiny))!));
    }

    [Fact]
    public async Task Unknown_or_missing_models_produce_typed_errors()
    {
        Assert.Equal(ErrorCodes.ModelUnknown, (await Assert.ThrowsAsync<CodexVttException>(() => _store.RequireReadyAsync("nope"))).Code);
        Assert.Equal(ErrorCodes.ModelNotInstalled, (await Assert.ThrowsAsync<CodexVttException>(() => _store.RequireReadyAsync("tiny"))).Code);
    }

    [Fact]
    public void Concurrent_operations_on_same_model_are_rejected()
    {
        using var first = _store.BeginTransient(Tiny, ModelState.Downloading);

        Assert.Equal(ModelState.Downloading, _store.Status(Tiny).State);
        var ex = Assert.Throws<CodexVttException>(() => _store.BeginTransient(Tiny, ModelState.Verifying));
        Assert.Equal(ErrorCodes.DownloadInProgress, ex.Code);
    }
}
