using CodexVtt.Core.Audio;

namespace CodexVtt.Core.Tests;

public sealed class ByteRangeTests
{
    [Fact]
    public void Missing_or_unsupported_header_serves_the_whole_file()
    {
        Assert.True(ByteRange.TryParse(null, 100, out var none));
        Assert.Null(none);
        Assert.True(ByteRange.TryParse("bytes=0-1,5-6", 100, out var multi));
        Assert.Null(multi);
        Assert.True(ByteRange.TryParse("items=1-2", 100, out var other));
        Assert.Null(other);
    }

    [Theory]
    [InlineData("bytes=0-9", 0, 9)]
    [InlineData("bytes=10-", 10, 99)]
    [InlineData("bytes=90-500", 90, 99)]
    [InlineData("bytes=-10", 90, 99)]
    [InlineData("bytes=-500", 0, 99)]
    public void Parses_single_ranges(string header, long start, long end)
    {
        Assert.True(ByteRange.TryParse(header, 100, out var range));
        Assert.Equal(new ByteRange(start, end), range);
    }

    [Theory]
    [InlineData("bytes=100-")]
    [InlineData("bytes=200-300")]
    [InlineData("bytes=-0")]
    public void Unsatisfiable_ranges_are_rejected(string header) => Assert.False(ByteRange.TryParse(header, 100, out _));

    [Fact]
    public void Length_counts_both_ends() => Assert.Equal(10, new ByteRange(0, 9).Length);
}

public sealed class AudioResponseTests : IDisposable
{
    private readonly TempDir _dir = new();

    public void Dispose() => _dir.Dispose();

    private string Sample()
    {
        var path = Path.Combine(_dir.Path, "a.mp3");
        File.WriteAllBytes(path, Enumerable.Range(0, 100).Select(i => (byte)i).ToArray());
        return path;
    }

    private static byte[] Read(AudioResponse response)
    {
        using var body = response.Body!;
        using var copy = new MemoryStream();
        body.CopyTo(copy);
        return copy.ToArray();
    }

    [Fact]
    public void Range_request_returns_206_with_exact_bytes()
    {
        var response = AudioResponse.Create(Sample(), "audio/mpeg", "GET", "bytes=10-19");
        Assert.Equal(206, response.Status);
        Assert.Equal("bytes 10-19/100", response.Headers["Content-Range"]);
        Assert.Equal("10", response.Headers["Content-Length"]);
        Assert.Equal("bytes", response.Headers["Accept-Ranges"]);
        Assert.Equal(Enumerable.Range(10, 10).Select(i => (byte)i), Read(response));
    }

    [Fact]
    public void Open_ended_range_runs_to_the_end()
    {
        var response = AudioResponse.Create(Sample(), "audio/mpeg", "GET", "bytes=95-");
        Assert.Equal(206, response.Status);
        Assert.Equal("bytes 95-99/100", response.Headers["Content-Range"]);
        Assert.Equal(5, Read(response).Length);
    }

    [Fact]
    public void Without_range_returns_whole_file_with_200()
    {
        var response = AudioResponse.Create(Sample(), "audio/mpeg", "GET", null);
        Assert.Equal(200, response.Status);
        Assert.Equal("audio/mpeg", response.Headers["Content-Type"]);
        Assert.Equal(100, Read(response).Length);
    }

    [Fact]
    public void Head_has_no_body()
    {
        var response = AudioResponse.Create(Sample(), "audio/mpeg", "HEAD", "bytes=0-9");
        Assert.Equal(206, response.Status);
        Assert.Null(response.Body);
    }

    [Fact]
    public void Range_past_the_end_returns_416()
    {
        var response = AudioResponse.Create(Sample(), "audio/mpeg", "GET", "bytes=500-");
        Assert.Equal(416, response.Status);
        Assert.Equal("bytes */100", response.Headers["Content-Range"]);
    }

    [Fact]
    public void Missing_file_returns_404() =>
        Assert.Equal(404, AudioResponse.Create(Path.Combine(_dir.Path, "no.mp3"), "audio/mpeg", "GET", null).Status);
}
