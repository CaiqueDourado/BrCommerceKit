using System.IO.Compression;

namespace BrCommerce.Feeds;

/// <summary>Opens a feed from a local path or an http(s) URL, transparently handling gzip.</summary>
public static class FeedSource
{
    /// <summary>Opens <paramref name="pathOrUrl"/> for reading.</summary>
    /// <param name="pathOrUrl">Local file path or http(s) URL.</param>
    /// <param name="httpClient">Client used for URLs. Null = a shared default client.</param>
    /// <param name="userAgent">User-Agent for URLs.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A readable stream; dispose it when done.</returns>
    public static async Task<Stream> OpenAsync(
        string pathOrUrl,
        HttpClient? httpClient = null,
        string userAgent = "BrCommerceKit/0.1 (+https://github.com/CaiqueDourado/BrCommerceKit)",
        CancellationToken cancellationToken = default)
    {
        Stream stream;
        if (Uri.TryCreate(pathOrUrl, UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
        {
            var http = httpClient ?? SharedClient.Value;
            var request = new HttpRequestMessage(HttpMethod.Get, uri);
            request.Headers.TryAddWithoutValidation("User-Agent", userAgent);
            var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                var status = (int)response.StatusCode;
                response.Dispose();
                request.Dispose();
                throw new HttpRequestException($"GET {uri} returned HTTP {status}.");
            }
            stream = new ResponseStream(await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false), response, request);
        }
        else
        {
            stream = File.OpenRead(pathOrUrl);
        }

        // Peek the first two bytes to detect gzip, then replay them.
        var header = new byte[2];
        var read = 0;
        while (read < header.Length)
        {
            var n = await stream.ReadAsync(header.AsMemory(read), cancellationToken).ConfigureAwait(false);
            if (n == 0)
            {
                break;
            }

            read += n;
        }

        Stream replay = new PrefixStream(header[..read], stream);
        var isGzip = read == 2 && header[0] == 0x1f && header[1] == 0x8b;
        return new BufferedStream(isGzip ? new GZipStream(replay, CompressionMode.Decompress) : replay, 64 * 1024);
    }

    private static readonly Lazy<HttpClient> SharedClient = new(() => new HttpClient(new HttpClientHandler
    {
        AutomaticDecompression = System.Net.DecompressionMethods.All,
    })
    {
        Timeout = TimeSpan.FromMinutes(5),
    });

    private sealed class PrefixStream(byte[] prefix, Stream inner) : Stream
    {
        private int _position;

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }

        public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

        public override int Read(Span<byte> buffer)
        {
            if (_position < prefix.Length)
            {
                var n = Math.Min(buffer.Length, prefix.Length - _position);
                prefix.AsSpan(_position, n).CopyTo(buffer);
                _position += n;
                return n;
            }

            return inner.Read(buffer);
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                inner.Dispose();
            }

            base.Dispose(disposing);
        }
    }

    private sealed class ResponseStream(Stream inner, HttpResponseMessage response, HttpRequestMessage request) : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => inner.Read(buffer, offset, count);
        public override int Read(Span<byte> buffer) => inner.Read(buffer);
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) => inner.ReadAsync(buffer, cancellationToken);
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                inner.Dispose();
                response.Dispose();
                request.Dispose();
            }

            base.Dispose(disposing);
        }
    }
}
