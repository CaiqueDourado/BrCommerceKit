namespace BrCommerce.Detect;

/// <summary>Everything learned about a store.</summary>
public sealed class DetectionResult
{
    /// <summary>The URL that was requested.</summary>
    public required string RequestedUrl { get; init; }

    /// <summary>The URL after following redirects, or null when the site did not answer.</summary>
    public string? FinalUrl { get; init; }

    /// <summary>HTTP status of the home page after redirects, or null when the site did not answer.</summary>
    public int? StatusCode { get; init; }

    /// <summary>True when the home page answered with a 2xx status.</summary>
    public bool Online => StatusCode is >= 200 and < 300;

    /// <summary>Redirect chain, in order (each item is the <c>Location</c> that was followed).</summary>
    public IReadOnlyList<string> Redirects { get; init; } = [];

    /// <summary>Error message when the home page could not be fetched.</summary>
    public string? Error { get; init; }

    /// <summary>Best guess for the store name, or null.</summary>
    public StoreName? StoreName { get; init; }

    /// <summary>The most likely platform, or null when no candidate reached the threshold.</summary>
    public PlatformMatch? Platform { get; init; }

    /// <summary>All platforms with any evidence, highest score first.</summary>
    public IReadOnlyList<PlatformMatch> Candidates { get; init; } = [];

    /// <summary>Things that are not platforms: suspended store, WordPress, payment seals.</summary>
    public IReadOnlyList<PlatformMatch> Flags { get; init; } = [];

    /// <summary>Product feeds that answered and passed validation.</summary>
    public IReadOnlyList<DiscoveredFeed> Feeds { get; init; } = [];
}

/// <summary>A platform (or flag) with its score and the evidence behind it.</summary>
/// <param name="Id">Rule identifier, e.g. <c>vtex</c>.</param>
/// <param name="Name">Display name.</param>
/// <param name="Score">0 to 1. A verified feed counts as 1.</param>
/// <param name="Evidence">Human-readable reasons, e.g. <c>html: vteximg.com.br</c>.</param>
public sealed record PlatformMatch(string Id, string Name, double Score, IReadOnlyList<string> Evidence);

/// <summary>A product feed found on the store.</summary>
/// <param name="PlatformId">The platform whose rule found the feed.</param>
/// <param name="Url">Final URL of the feed, after redirects.</param>
/// <param name="ContentType">Content-Type returned by the server, if any.</param>
public sealed record DiscoveredFeed(string PlatformId, string Url, string? ContentType);

/// <summary>A store name and where it came from.</summary>
/// <param name="Value">The name.</param>
/// <param name="Source">Where it was found: <c>og:site_name</c>, <c>application-name</c>, <c>DC.title</c>, <c>title</c>, <c>razao-social</c>, <c>author</c> or <c>copyright</c>.</param>
public sealed record StoreName(string Value, string Source);
