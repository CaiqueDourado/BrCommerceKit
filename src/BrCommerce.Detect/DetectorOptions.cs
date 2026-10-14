namespace BrCommerce.Detect;

/// <summary>Options for <see cref="PlatformDetector"/>.</summary>
public sealed class DetectorOptions
{
    /// <summary>
    /// User-Agent sent with every request. Identify yourself; please do not impersonate browsers.
    /// </summary>
    public string UserAgent { get; set; } = "BrCommerceKit/0.1 (+https://github.com/CaiqueDourado/BrCommerceKit)";

    /// <summary>Timeout for each request. Default: 15 seconds.</summary>
    public TimeSpan RequestTimeout { get; set; } = TimeSpan.FromSeconds(15);

    /// <summary>Pause between requests to the same store. Default: 250 ms.</summary>
    public TimeSpan DelayBetweenRequests { get; set; } = TimeSpan.FromMilliseconds(250);

    /// <summary>Maximum number of redirects to follow. Default: 5.</summary>
    public int MaxRedirects { get; set; } = 5;

    /// <summary>Maximum bytes read from the home page. Default: 2 MB.</summary>
    public int MaxPageBytes { get; set; } = 2 * 1024 * 1024;

    /// <summary>Bytes read from a feed to validate it (the feed itself is not downloaded). Default: 64 KB.</summary>
    public int MaxFeedProbeBytes { get; set; } = 64 * 1024;

    /// <summary>Whether to also read <c>/robots.txt</c>. Default: true.</summary>
    public bool CheckRobotsTxt { get; set; } = true;

    /// <summary>Which well-known feed paths to try. Default: <see cref="FeedProbeMode.All"/>.</summary>
    public FeedProbeMode FeedProbes { get; set; } = FeedProbeMode.All;

    /// <summary>When the https home page fails to connect, retry with http. Default: true.</summary>
    public bool FallbackToHttp { get; set; } = true;

    /// <summary>Minimum score for <see cref="DetectionResult.Platform"/>. Default: 0.5.</summary>
    public double PlatformThreshold { get; set; } = 0.5;

    /// <summary>The rules to use. Default: the rules embedded in the package.</summary>
    public PlatformRuleSet Rules { get; set; } = PlatformRuleSet.Default;
}

/// <summary>Which feed paths <see cref="PlatformDetector"/> tries.</summary>
public enum FeedProbeMode
{
    /// <summary>Do not request any feed.</summary>
    None,

    /// <summary>Only the feeds of platforms that already have evidence in the HTML or robots.txt.</summary>
    CandidatesOnly,

    /// <summary>Every known feed path (about a dozen requests per store).</summary>
    All,
}
