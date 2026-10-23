using CodexVtt.Core.Hardware;
using CodexVtt.Core.Models;

namespace CodexVtt.Core.Tests;

public class ModelSelectorTests
{
    private static readonly ModelCatalog Catalog = ModelCatalog.LoadEmbedded();

    private static ModelCatalog PinnedCatalog() => new(
        Catalog.Source with { Revision = "rev" },
        Catalog.Selection,
        Catalog.Models.Select(m => m with { Sha256 = new string('a', 64) }).ToList());

    [Fact]
    public void Embedded_catalog_is_valid()
    {
        Assert.NotEmpty(Catalog.Models);
        Assert.All(Catalog.Models, m =>
        {
            Assert.True(m.SizeBytes > 0);
            Assert.True(m.RamRequiredBytes > 0);
            Assert.Contains(m.License, new[] { "MIT", "CC-BY-4.0", "Apache-2.0" });
            if (m.Engine == "whisper")
            {
                Assert.StartsWith("ggml-", m.File, StringComparison.Ordinal);
                Assert.False(m.IsMultiFile);
                Assert.Equal("MIT", m.License);
            }
            else
            {
                // Motori nuovi: cartella con più file, mai raccomandati da soli, con l'attribuzione richiesta dalla licenza.
                Assert.True(m.IsMultiFile);
                Assert.True(m.Preview);
                Assert.False(string.IsNullOrWhiteSpace(m.Attribution));
                Assert.Equal(m.SizeBytes, m.Files!.Sum(f => f.SizeBytes));
            }
        });
        Assert.Equal(Catalog.Models.Count, Catalog.Models.Select(m => m.QualityRank).Distinct().Count());
        Assert.Contains(Catalog.Models, m => m.Engine == "sherpa-transducer");
        Assert.Contains(Catalog.Models, m => m.Engine == "sherpa-canary" && m.RequiresLanguage);
        Assert.Contains(Catalog.Models, m => m.Engine == "sherpa-cohere" && m.RequiresLanguage);
    }

    [Fact]
    public void Embedded_catalog_is_pinned_to_a_revision_with_sha256()
    {
        Assert.Matches("^[0-9a-f]{40}$", Catalog.Source.Revision);
        Assert.All(Catalog.Models, m =>
        {
            var downloads = Catalog.DownloadUrls(m)!;
            Assert.Equal(m.IsMultiFile ? m.Files!.Count : 1, downloads.Count);
            var revision = (m.Source ?? Catalog.Source).Revision!;
            Assert.Matches("^[0-9a-f]{40}$", revision);
            foreach (var (file, url) in downloads)
            {
                Assert.Matches("^[0-9a-f]{64}$", file.Sha256);
                Assert.Equal("https", url.Scheme);
                Assert.Equal("huggingface.co", url.Host);
                Assert.Contains(revision, url.AbsolutePath, StringComparison.Ordinal);
                Assert.EndsWith("/" + file.Path, url.AbsolutePath, StringComparison.Ordinal);
            }
        });
    }

    [Fact]
    public void Preview_models_are_never_recommended()
    {
        var hardware = TestData.Hardware() with { LogicalCores = 32, TotalMemoryBytes = 64L * 1024 * 1024 * 1024, FreeDiskBytes = 500L * 1024 * 1024 * 1024 };

        var recommendation = ModelSelector.Recommend(hardware, Catalog, new HashSet<string>());

        Assert.NotNull(recommendation.Recommended);
        Assert.False(recommendation.Recommended!.Preview);
        Assert.Equal("whisper", recommendation.Recommended.Engine);
    }

    [Fact]
    public void Unpinned_entries_have_no_download_url()
    {
        var model = Catalog.Models[0] with { Sha256 = null };
        Assert.Null(Catalog.DownloadUrl(model));
        Assert.Null(new ModelCatalog(Catalog.Source with { Revision = null }, Catalog.Selection, Catalog.Models).DownloadUrl(Catalog.Models[0]));
    }

    [Theory]
    [InlineData(2, 4, "base")]
    [InlineData(4, 8, "small")]
    [InlineData(8, 16, "large-v3-turbo-q5_0")]
    [InlineData(16, 32, "large-v3-turbo-q5_0")]
    public void Recommends_most_accurate_compatible_model(int cores, long ramGb, string expected)
    {
        var result = ModelSelector.Recommend(TestData.Hardware(cores, ramGb), PinnedCatalog(), new HashSet<string>());

        Assert.Equal(expected, result.Recommended?.Id);
        Assert.Equal(expected, result.Reasons.Single(r => r.Code == SelectionReason.Recommended).Args["modelId"]);
        Assert.Equal((ramGb * TestData.Gb / 2).ToString(System.Globalization.CultureInfo.InvariantCulture),
            result.Reasons.Single(r => r.Code == SelectionReason.RamLimit).Args["limitBytes"]);
    }

    [Fact]
    public void Low_memory_excludes_large_models()
    {
        var result = ModelSelector.Recommend(TestData.Hardware(cores: 16, ramGb: 2), PinnedCatalog(), new HashSet<string>());

        Assert.Equal("small", result.Recommended?.Id);
        var large = result.Assessments.Single(a => a.Model.Id == "large-v3");
        Assert.Contains(CompatibilityIssue.InsufficientMemory, large.Issues);
    }

    [Fact]
    public void Cpu_without_avx2_is_limited_to_cheap_models()
    {
        var hardware = TestData.Hardware(cores: 16, ramGb: 32, cpu: CpuFeatures.Avx);

        Assert.False(hardware.SupportsStandardRuntime);
        var result = ModelSelector.Recommend(hardware, PinnedCatalog(), new HashSet<string>());
        Assert.Equal("base", result.Recommended?.Id);
        Assert.Contains(result.Reasons, r => r.Code == SelectionReason.CompatibilityRuntime);
    }

    [Fact]
    public void Insufficient_disk_excludes_models_not_installed_but_not_installed_ones()
    {
        var hardware = TestData.Hardware(cores: 16, ramGb: 32, diskGb: 0);
        var catalog = PinnedCatalog();

        var result = ModelSelector.Recommend(hardware, catalog, new HashSet<string> { "small" });

        Assert.Equal("small", result.Recommended?.Id);
        Assert.Contains(CompatibilityIssue.InsufficientDisk, result.Assessments.Single(a => a.Model.Id == "medium").Issues);
    }

    [Fact]
    public void Unpinned_models_are_not_recommended_unless_installed()
    {
        var unpinned = new ModelCatalog(Catalog.Source, Catalog.Selection, Catalog.Models.Select(m => m with { Sha256 = null }).ToList());

        Assert.Null(ModelSelector.Recommend(TestData.Hardware(), unpinned, new HashSet<string>()).Recommended);
        Assert.Equal("base", ModelSelector.Recommend(TestData.Hardware(), unpinned, new HashSet<string> { "base" }).Recommended?.Id);
    }

    [Fact]
    public void Nothing_compatible_returns_null_with_reason()
    {
        var result = ModelSelector.Recommend(TestData.Hardware(cores: 1, ramGb: 0), PinnedCatalog(), new HashSet<string>());

        Assert.Null(result.Recommended);
        Assert.Contains(result.Reasons, r => r.Code == SelectionReason.NoneCompatible);
    }
}
