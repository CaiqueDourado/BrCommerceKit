using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.RegularExpressions;

namespace BrCommerce.Detect.Http;

/// <summary>Result of fetching a URL with manual redirect handling.</summary>
/// <param name="FinalUrl">URL after redirects.</param>
/// <param name="StatusCode">Final HTTP status.</param>
/// <param name="Body">Decoded body (possibly truncated to the byte limit).</param>
/// <param name="ContentType">Media type returned by the server.</param>
/// <param name="Redirects">Locations that were followed.</param>
public sealed record FetchResult(Uri FinalUrl, int StatusCode, string Body, string? ContentType, IReadOnlyList<string> Redirects);

/// <summary>
/// Fetches pages following redirects manually (so relative <c>Location</c> headers and the chain are kept),
/// reads at most a given number of bytes and decodes using the charset from the header or the page.
/// </summary>
public sealed class PageFetcher
{
    private static readonly Regex MetaCharset = new(
        "<meta[^>]+charset\\s*=\\s*['\"]?([A-Za-z0-9_-]+)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

    static PageFetcher() => Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

    private readonly HttpClient _http;
    private readonly string _userAgent;
    private readonly int _maxRedirects;

    /// <summary>Creates a fetcher.</summary>
    /// <param name="http">An <see cref="HttpClient"/> whose handler does NOT follow redirects automatically.</param>
    /// <param name="userAgent">User-Agent header.</param>
    /// <param name="maxRedirects">Maximum redirects to follow.</param>
    public PageFetcher(HttpClient http, string userAgent, int maxRedirects)
    {
        _http = http;
        _userAgent = userAgent;
        _maxRedirects = maxRedirects;
    }

    /// <summary>Creates an <see cref="HttpClient"/> suitable for this fetcher (no auto-redirect, automatic decompression).</summary>
    /// <param name="timeout">Request timeout.</param>
    public static HttpClient CreateDefaultClient(TimeSpan timeout) => new(new HttpClientHandler
    {
        AllowAutoRedirect = false,
        AutomaticDecompression = DecompressionMethods.All,
    })
    {
        Timeout = timeout,
    };

    /// <summary>Fetches <paramref name="url"/>, following redirects, and reads up to <paramref name="maxBytes"/>.</summary>
    /// <param name="url">The URL.</param>
    /// <param name="maxBytes">Maximum bytes to read from the body.</param>
    /// <param name="accept">Optional Accept header.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<FetchResult> FetchAsync(Uri url, int maxBytes, string? accept = null, CancellationToken cancellationToken = default)
    {
        var redirects = new List<string>();
        var current = url;

        for (var hop = 0; ; hop++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, current);
            request.Headers.TryAddWithoutValidation("User-Agent", _userAgent);
            if (accept is not null)
            {
                request.Headers.TryAddWithoutValidation("Accept", accept);
            }

            using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                .ConfigureAwait(false);

            var status = (int)response.StatusCode;
            if (status is 301 or 302 or 303 or 307 or 308 && response.Headers.Location is { } location && hop < _maxRedirects)
            {
                var next = location.IsAbsoluteUri ? location : new Uri(current, location);
                redirects.Add(next.ToString());
                current = next;
                continue;
            }

            var bytes = await ReadLimitedAsync(response.Content, maxBytes, cancellationToken).ConfigureAwait(false);
            var body = Decode(bytes, response.Content.Headers.ContentType);
            return new FetchResult(current, status, body, response.Content.Headers.ContentType?.MediaType, redirects);
        }
    }

    private static async Task<byte[]> ReadLimitedAsync(HttpContent content, int maxBytes, CancellationToken cancellationToken)
    {
        await using var stream = await content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        var buffer = new byte[Math.Min(maxBytes, 81920)];
        using var output = new MemoryStream();
        while (output.Length < maxBytes)
        {
            var toRead = (int)Math.Min(buffer.Length, maxBytes - output.Length);
            var read = await stream.ReadAsync(buffer.AsMemory(0, toRead), cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                break;
            }

            output.Write(buffer, 0, read);
        }

        return output.ToArray();
    }

    /// <summary>Decodes bytes using the header charset, then a <c>&lt;meta charset&gt;</c>, then UTF-8.</summary>
    /// <param name="bytes">Raw body.</param>
    /// <param name="contentType">Content-Type header, if any.</param>
    public static string Decode(byte[] bytes, MediaTypeHeaderValue? contentType)
    {
        var encoding = TryGetEncoding(contentType?.CharSet);
        if (encoding is null)
        {
            var head = Encoding.ASCII.GetString(bytes, 0, Math.Min(bytes.Length, 4096));
            var match = MetaCharset.Match(head);
            if (match.Success)
            {
                encoding = TryGetEncoding(match.Groups[1].Value);
            }
        }

        return (encoding ?? Encoding.UTF8).GetString(bytes);
    }

    private static Encoding? TryGetEncoding(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        try
        {
            return Encoding.GetEncoding(name.Trim('"', '\'', ' '));
        }
        catch (ArgumentException)
        {
            return null;
        }
    }
}
