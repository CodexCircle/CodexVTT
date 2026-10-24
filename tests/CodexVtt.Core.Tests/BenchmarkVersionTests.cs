using CodexVtt.Core.Benchmark;

namespace CodexVtt.Core.Tests;

public class BenchmarkVersionTests
{
    [Fact]
    public void The_version_is_the_revision_plus_a_short_fingerprint_of_the_clips()
    {
        var parts = BenchmarkVersion.Current.Split('-');
        Assert.Equal(2, parts.Length);
        Assert.Equal(BenchmarkVersion.Revision.ToString(System.Globalization.CultureInfo.InvariantCulture), parts[0]);
        Assert.Matches("^[0-9a-f]{8}$", parts[1]);
    }

    [Fact]
    public void The_version_does_not_depend_on_the_app_version_so_an_update_alone_never_invalidates_measurements()
    {
        var store = new BenchmarkStore(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "benchmark.json"));
        var entry = new BenchmarkEntry("base", "sha-base", "Cpu", null, 60, 2, 4, DateTimeOffset.UtcNow, BenchmarkVersion.Current);
        store.Record("pc-1", entry);

        // La stessa versione del test resta valida qualunque sia la versione dell'app che la legge.
        Assert.Equal(BenchmarkStaleReason.None, store.Views("pc-1", _ => "sha-base", BenchmarkVersion.Current).Single().Stale);
        Assert.Equal(BenchmarkStaleReason.TestChanged, store.Views("pc-1", _ => "sha-base", "0-00000000").Single().Stale);
    }
}
