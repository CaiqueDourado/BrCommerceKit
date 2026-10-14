using BrCommerce.Detect.Http;

namespace BrCommerce.Detect;

/// <summary>
/// Detects the e-commerce platform of a store, its public product feeds and its name.
/// </summary>
/// <example>
/// <code>
/// using var detector = new PlatformDetector();
/// var result = await detector.DetectAsync("minhaloja.com.br");
/// Console.WriteLine($"{result.StoreName?.Value}: {result.Platform?.Name} ({result.Platform?.Score:P0})");
/// foreach (var feed in result.Feeds) Console.WriteLine(feed.Url);
/// </code>
/// </example>
public sealed class PlatformDetector : IDisposable
{
    private readonly HttpClient _http;
    private readonly bool _ownsClient;
    private readonly DetectorOptions _options;
    private readonly PageFetcher _fetcher;

    /// <summary>Creates a detector with its own <see cref="HttpClient"/>.</summary>
    /// <param name="options">Options; null = defaults.</param>
    public PlatformDetector(DetectorOptions? options = null)
        : this(null, options)
    {
    }

    /// <summary>Creates a detector using an existing <see cref="HttpClient"/>.</summary>
    /// <param name="httpClient">
    /// Client to use. Its handler must NOT follow redirects (<c>AllowAutoRedirect = false</c>);
    /// the detector follows them itself to record the chain. Null = create one.
    /// </param>
    /// <param name="options">Options; null = defaults.</param>
    public PlatformDetector(HttpClient? httpClient, DetectorOptions? options = null)
    {
        _options = options ?? new DetectorOptions();
        _ownsClient = httpClient is null;
        _http = httpClient ?? PageFetcher.CreateDefaultClient(_options.RequestTimeout);
        _fetcher = new PageFetcher(_http, _options.UserAgent, _options.MaxRedirects);
    }

    /// <summary>Analyses a store.</summary>
    /// <param name="urlOrDomain">A URL (<c>https://loja.com.br/</c>) or a bare domain (<c>loja.com.br</c>).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<DetectionResult> DetectAsync(string urlOrDomain, CancellationToken cancellationToken = default)
    {
        var requested = NormalizeUrl(urlOrDomain);
        var rules = _options.Rules;

        FetchResult? home = null;
        string? error = null;
        try
        {
            home = await _fetcher.FetchAsync(requested, _options.MaxPageBytes, "text/html,*/*;q=0.8", cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
        {
            error = ex.Message;
            if (_options.FallbackToHttp && requested.Scheme == Uri.UriSchemeHttps)
            {
                var http = new UriBuilder(requested) { Scheme = Uri.UriSchemeHttp, Port = requested.IsDefaultPort ? -1 : requested.Port }.Uri;
                try
                {
                    home = await _fetcher.FetchAsync(http, _options.MaxPageBytes, "text/html,*/*;q=0.8", cancellationToken).ConfigureAwait(false);
                    error = null;
                }
                catch (Exception ex2) when (ex2 is HttpRequestException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
                {
                    error = ex2.Message;
                }
            }
        }

        if (home is null)
        {
            return new DetectionResult { RequestedUrl = requested.ToString(), Error = error };
        }

        var root = new Uri(home.FinalUrl.GetLeftPart(UriPartial.Authority) + "/");
        var found = new List<PlatformMatch>();
        var flags = new List<PlatformMatch>();

        found.AddRange(SignatureMatcher.Match(home.Body, rules.Platforms, "html"));
        flags.AddRange(SignatureMatcher.Match(home.Body, rules.Flags, "html"));
        flags.AddRange(SignatureMatcher.Match(home.FinalUrl.ToString(), rules.Flags, "url"));

        if (_options.CheckRobotsTxt)
        {
            var robots = await TryFetchAsync(new Uri(root, "robots.txt"), _options.MaxFeedProbeBytes, "text/plain", cancellationToken).ConfigureAwait(false);
            if (robots is { StatusCode: 200 } && !LooksLikeHtml(robots.Body))
            {
                found.AddRange(SignatureMatcher.Match(robots.Body, rules.Platforms, "robots.txt"));
            }
        }

        var feeds = await ProbeFeedsAsync(root, home.Body, found, cancellationToken).ConfigureAwait(false);
        foreach (var feed in feeds)
        {
            var rule = rules.Platforms.First(p => p.Id == feed.PlatformId);
            found.Add(new PlatformMatch(rule.Id, rule.Name, 1.0, [$"feed: {feed.Url}"]));
        }

        var candidates = SignatureMatcher.Merge(found);
        var best = candidates.FirstOrDefault(c => c.Score >= _options.PlatformThreshold);

        return new DetectionResult
        {
            RequestedUrl = requested.ToString(),
            FinalUrl = home.FinalUrl.ToString(),
            StatusCode = home.StatusCode,
            Redirects = home.Redirects,
            StoreName = StoreNameExtractor.Extract(home.Body),
            Platform = best,
            Candidates = candidates,
            Flags = SignatureMatcher.Merge(flags),
            Feeds = feeds,
        };
    }

    /// <summary>Normalises a URL or bare domain to an absolute http(s) URL.</summary>
    /// <param name="urlOrDomain">Input such as <c>loja.com.br</c> or <c>https://loja.com.br/x</c>.</param>
    /// <exception cref="ArgumentException">The input is empty or not a valid host.</exception>
    public static Uri NormalizeUrl(string urlOrDomain)
    {
        if (string.IsNullOrWhiteSpace(urlOrDomain))
        {
            throw new ArgumentException("URL or domain is required.", nameof(urlOrDomain));
        }

        var value = urlOrDomain.Trim();
        if (!value.Contains("://", StringComparison.Ordinal))
        {
            value = "https://" + value;
        }

        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            throw new ArgumentException($"Not a valid http(s) URL or domain: '{urlOrDomain}'.", nameof(urlOrDomain));
        }

        return uri.AbsolutePath.Length == 0 ? new Uri(uri, "/") : uri;
    }

    private async Task<List<DiscoveredFeed>> ProbeFeedsAsync(Uri root, string homeHtml, List<PlatformMatch> evidence, CancellationToken cancellationToken)
    {
        var feeds = new List<DiscoveredFeed>();
        if (_options.FeedProbes == FeedProbeMode.None)
        {
            return feeds;
        }

        var withEvidence = evidence.Select(e => e.Id).ToHashSet(StringComparer.Ordinal);
        var rules = _options.Rules.Platforms
            .Where(r => _options.FeedProbes == FeedProbeMode.All || withEvidence.Contains(r.Id))
            .OrderByDescending(r => withEvidence.Contains(r.Id));

        foreach (var rule in rules)
        {
            foreach (var probe in rule.Feeds)
            {
                var feed = await ProbeAsync(rule.Id, new Uri(root, probe.Path), probe.MustContain, cancellationToken).ConfigureAwait(false);
                if (feed is not null)
                {
                    feeds.Add(feed);
                }
            }

            foreach (var template in rule.FeedTemplates)
            {
                var match = template.Extract.Match(homeHtml);
                if (!match.Success || match.Groups.Count < 2)
                {
                    continue;
                }

                var url = string.Format(System.Globalization.CultureInfo.InvariantCulture, template.UrlFormat, Uri.EscapeDataString(match.Groups[1].Value));
                var feed = await ProbeAsync(rule.Id, new Uri(url), template.MustContain, cancellationToken).ConfigureAwait(false);
                if (feed is not null)
                {
                    feeds.Add(feed);
                }
            }
        }

        return feeds;
    }

    private async Task<DiscoveredFeed?> ProbeAsync(string platformId, Uri url, string? mustContain, CancellationToken cancellationToken)
    {
        var result = await TryFetchAsync(url, _options.MaxFeedProbeBytes, "application/xml,text/xml,application/json,*/*;q=0.5", cancellationToken).ConfigureAwait(false);
        if (result is not { StatusCode: 200 })
        {
            return null;
        }

        if (mustContain is not null && !result.Body.Contains(mustContain, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        // A soft-404 home page often answers 200 for any path.
        if (LooksLikeHtml(result.Body))
        {
            return null;
        }

        return new DiscoveredFeed(platformId, result.FinalUrl.ToString(), result.ContentType);
    }

    private async Task<FetchResult?> TryFetchAsync(Uri url, int maxBytes, string accept, CancellationToken cancellationToken)
    {
        if (_options.DelayBetweenRequests > TimeSpan.Zero)
        {
            await Task.Delay(_options.DelayBetweenRequests, cancellationToken).ConfigureAwait(false);
        }

        try
        {
            return await _fetcher.FetchAsync(url, maxBytes, accept, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or UriFormatException && !cancellationToken.IsCancellationRequested)
        {
            return null;
        }
    }

    private static bool LooksLikeHtml(string body)
    {
        var start = body.AsSpan().TrimStart();
        return start.StartsWith("<!doctype html", StringComparison.OrdinalIgnoreCase)
            || start.StartsWith("<html", StringComparison.OrdinalIgnoreCase);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_ownsClient)
        {
            _http.Dispose();
        }
    }
}
