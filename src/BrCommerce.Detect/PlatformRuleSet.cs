using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace BrCommerce.Detect;

/// <summary>
/// The set of rules used to recognise platforms and flags. The default set ships embedded in the
/// package (<c>Rules/rules.json</c>); you can load your own with <see cref="FromJson"/>.
/// </summary>
public sealed class PlatformRuleSet
{
    private static readonly Lazy<PlatformRuleSet> DefaultSet = new(LoadEmbedded);

    /// <summary>Creates a rule set from already-built rules.</summary>
    /// <param name="platforms">Platform rules.</param>
    /// <param name="flags">Flag rules (things that are not platforms, such as "store suspended").</param>
    public PlatformRuleSet(IReadOnlyList<PlatformRule> platforms, IReadOnlyList<PlatformRule> flags)
    {
        Platforms = platforms;
        Flags = flags;
    }

    /// <summary>Platform rules, in file order.</summary>
    public IReadOnlyList<PlatformRule> Platforms { get; }

    /// <summary>Flag rules (not platforms): suspended store, WordPress, payment seals.</summary>
    public IReadOnlyList<PlatformRule> Flags { get; }

    /// <summary>The rules embedded in the package.</summary>
    public static PlatformRuleSet Default => DefaultSet.Value;

    /// <summary>Parses a rule set from JSON in the same format as the embedded <c>rules.json</c>.</summary>
    /// <param name="json">The JSON text.</param>
    /// <exception cref="FormatException">The JSON is not a valid rule file.</exception>
    public static PlatformRuleSet FromJson(string json)
    {
        RuleFile? file;
        try
        {
            file = JsonSerializer.Deserialize<RuleFile>(json, JsonOptions);
        }
        catch (JsonException ex)
        {
            throw new FormatException("Invalid rules JSON: " + ex.Message, ex);
        }

        if (file is null)
        {
            throw new FormatException("Invalid rules JSON: empty document.");
        }

        return new PlatformRuleSet(
            (file.Platforms ?? []).Select(ToRule).ToList(),
            (file.Flags ?? []).Select(ToRule).ToList());
    }

    private static PlatformRuleSet LoadEmbedded()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("BrCommerce.Detect.rules.json")
            ?? throw new InvalidOperationException("Embedded rules.json not found.");
        using var reader = new StreamReader(stream);
        return FromJson(reader.ReadToEnd());
    }

    private static PlatformRule ToRule(RuleDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.Id))
        {
            throw new FormatException("Every rule needs an 'id'.");
        }

        var signatures = (dto.Signatures ?? [])
            .Select(s => new Signature(
                new Regex(s.Pattern ?? throw new FormatException($"Rule '{dto.Id}' has a signature without 'pattern'."),
                    RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled,
                    TimeSpan.FromSeconds(1)),
                s.Weight is > 0 and <= 1 ? s.Weight : 0.5))
            .ToList();

        var feeds = (dto.Feeds ?? [])
            .Select(f => new FeedProbe(
                f.Path ?? throw new FormatException($"Rule '{dto.Id}' has a feed without 'path'."),
                f.MustContain))
            .ToList();

        var templates = (dto.FeedTemplates ?? [])
            .Select(t => new FeedTemplate(
                new Regex(t.Extract ?? throw new FormatException($"Rule '{dto.Id}' has a feed template without 'extract'."),
                    RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1)),
                t.Url ?? throw new FormatException($"Rule '{dto.Id}' has a feed template without 'url'."),
                t.MustContain))
            .ToList();

        return new PlatformRule(dto.Id, dto.Name ?? dto.Id, signatures, feeds, templates);
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    private sealed class RuleFile
    {
        [JsonPropertyName("platforms")] public List<RuleDto>? Platforms { get; set; }
        [JsonPropertyName("flags")] public List<RuleDto>? Flags { get; set; }
    }

    private sealed class RuleDto
    {
        public string? Id { get; set; }
        public string? Name { get; set; }
        public List<SignatureDto>? Signatures { get; set; }
        public List<FeedDto>? Feeds { get; set; }
        public List<TemplateDto>? FeedTemplates { get; set; }
    }

    private sealed class SignatureDto
    {
        public string? Pattern { get; set; }
        public double Weight { get; set; }
    }

    private sealed class FeedDto
    {
        public string? Path { get; set; }
        public string? MustContain { get; set; }
    }

    private sealed class TemplateDto
    {
        public string? Extract { get; set; }
        public string? Url { get; set; }
        public string? MustContain { get; set; }
    }
}

/// <summary>A platform (or flag) and how to recognise it.</summary>
/// <param name="Id">Stable identifier, e.g. <c>vtex</c>.</param>
/// <param name="Name">Display name, e.g. <c>VTEX</c>.</param>
/// <param name="Signatures">Patterns searched in the home page HTML, in <c>robots.txt</c> and in the final URL.</param>
/// <param name="Feeds">Well-known feed paths, relative to the store root.</param>
/// <param name="FeedTemplates">Feeds whose URL depends on a value extracted from the home page.</param>
public sealed record PlatformRule(
    string Id,
    string Name,
    IReadOnlyList<Signature> Signatures,
    IReadOnlyList<FeedProbe> Feeds,
    IReadOnlyList<FeedTemplate> FeedTemplates);

/// <summary>A text pattern and how much it says about the platform (0 to 1).</summary>
/// <param name="Pattern">Case-insensitive regular expression.</param>
/// <param name="Weight">1.0 = conclusive (e.g. a platform CDN host); 0.3 = weak (a short, common word).</param>
public sealed record Signature(Regex Pattern, double Weight);

/// <summary>A feed path to try on the store and the text that proves it is a real feed.</summary>
/// <param name="Path">Path relative to the store root, e.g. <c>XMLData/googleshopping.xml</c>.</param>
/// <param name="MustContain">Text that must appear in the first bytes of the response (case-insensitive). Null = any 200 response.</param>
public sealed record FeedProbe(string Path, string? MustContain);

/// <summary>A feed whose URL is built from a value found in the home page.</summary>
/// <param name="Extract">Regex with one capture group applied to the home page HTML.</param>
/// <param name="UrlFormat">URL with <c>{0}</c> where the captured value goes.</param>
/// <param name="MustContain">Text that must appear in the response.</param>
public sealed record FeedTemplate(Regex Extract, string UrlFormat, string? MustContain);
