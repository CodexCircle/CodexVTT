using System.Globalization;

namespace CodexVtt.Core.Audio;

/// <summary>
/// Risposta HTTP per l'audio originale di una trascrizione, con supporto a <c>Range</c>: l'elemento audio di Chromium
/// cerca nel file con richieste a intervalli e si aspetta 206 con <c>Content-Range</c>. Il corpo va chiuso da chi lo usa.
/// </summary>
public sealed record AudioResponse(int Status, string Reason, IReadOnlyDictionary<string, string> Headers, Stream? Body)
{
    private static readonly Dictionary<string, string> NoHeaders = [];

    public static AudioResponse NotFound { get; } = new(404, "Not Found", NoHeaders, null);

    public static AudioResponse Create(string path, string contentType, string method, string? rangeHeader)
    {
        long length;
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists)
                return NotFound;
            length = info.Length;
        }
        catch (IOException)
        {
            return NotFound;
        }

        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Content-Type"] = contentType,
            ["Accept-Ranges"] = "bytes",
        };
        if (!ByteRange.TryParse(rangeHeader, length, out var range))
        {
            headers["Content-Range"] = $"bytes */{length}";
            return new AudioResponse(416, "Range Not Satisfiable", headers, null);
        }

        var start = range?.Start ?? 0;
        var count = range?.Length ?? length;
        headers["Content-Length"] = count.ToString(CultureInfo.InvariantCulture);
        if (range is { } r)
            headers["Content-Range"] = $"bytes {r.Start}-{r.End}/{length}";
        var status = range is null ? 200 : 206;
        var reason = range is null ? "OK" : "Partial Content";
        if (string.Equals(method, "HEAD", StringComparison.OrdinalIgnoreCase))
            return new AudioResponse(status, reason, headers, null);

        try
        {
            var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            return new AudioResponse(status, reason, headers, new SliceStream(file, start, count));
        }
        catch (IOException)
        {
            return NotFound;
        }
    }

    /// <summary>Espone solo <c>count</c> byte di un file a partire da <c>start</c>.</summary>
    private sealed class SliceStream : Stream
    {
        private readonly Stream _inner;
        private long _remaining;

        public SliceStream(Stream inner, long start, long count)
        {
            _inner = inner;
            _remaining = count;
            _inner.Seek(start, SeekOrigin.Begin);
        }

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

        public override int Read(byte[] buffer, int offset, int count)
        {
            if (_remaining <= 0)
                return 0;
            var read = _inner.Read(buffer, offset, (int)Math.Min(count, _remaining));
            _remaining -= read;
            return read;
        }

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing)
                _inner.Dispose();
            base.Dispose(disposing);
        }
    }
}
