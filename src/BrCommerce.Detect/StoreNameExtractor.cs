using System.Net;
using System.Text.RegularExpressions;

namespace BrCommerce.Detect;

/// <summary>
/// Extracts a store name from a home page, trying in order: <c>og:site_name</c>, <c>application-name</c>,
/// <c>DC.title</c>, the <c>&lt;title&gt;</c> (with heuristics for common Brazilian platforms),
/// a "Razão Social:" line, and finally <c>author</c> / <c>copyright</c> (often the agency, so last).
/// </summary>
public static class StoreNameExtractor
{
    private static readonly RegexOptions Options = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(1);

    private static readonly Regex MetaTag = new("<meta\\b[^>]*>", Options, Timeout);
    private static readonly Regex Attribute = new("([a-zA-Z_:][-a-zA-Z0-9_:.]*)\\s*=\\s*(?:\"([^\"]*)\"|'([^']*)'|([^\\s\"'>]+))", Options, Timeout);
    private static readonly Regex Title = new("<title[^>]*>(.*?)</title>", Options | RegexOptions.Singleline, Timeout);
    private static readonly Regex RazaoSocial = new("raz[aã]o\\s+social\\s*:?\\s*(?:</[^>]+>)?\\s*([^<\\r\\n]{3,120})", Options, Timeout);
    private static readonly Regex Spaces = new("\\s+", RegexOptions.CultureInvariant, Timeout);

    private static readonly string[] TitleSeparators = [" | ", " - ", " – ", " — "];

    private static readonly HashSet<string> GenericSegments = new(StringComparer.OrdinalIgnoreCase)
    {
        "home", "página inicial", "pagina inicial", "início", "inicio", "loja online", "loja virtual", "bem-vindo", "bem vindo",
    };

    /// <summary>Returns the best store name found in <paramref name="html"/>, or null.</summary>
    /// <param name="html">The home page HTML.</param>
    public static StoreName? Extract(string html)
    {
        if (string.IsNullOrEmpty(html))
        {
            return null;
        }

        var metas = ReadMetas(html);

        foreach (var (key, source) in new[] { ("og:site_name", "og:site_name"), ("application-name", "application-name"), ("dc.title", "DC.title") })
        {
            if (metas.TryGetValue(key, out var value) && Clean(value) is { Length: > 0 } clean)
            {
                return new StoreName(clean, source);
            }
        }

        var title = Title.Match(html);
        if (title.Success && FromTitle(title.Groups[1].Value) is { Length: > 0 } fromTitle)
        {
            return new StoreName(fromTitle, "title");
        }

        var razao = RazaoSocial.Match(html);
        if (razao.Success && Clean(razao.Groups[1].Value) is { Length: > 0 } company)
        {
            return new StoreName(company, "razao-social");
        }

        foreach (var key in new[] { "author", "copyright" })
        {
            if (metas.TryGetValue(key, out var value) && Clean(value) is { Length: > 0 } clean)
            {
                return new StoreName(clean, key);
            }
        }

        return null;
    }

    /// <summary>Applies the title heuristics to a raw <c>&lt;title&gt;</c> text.</summary>
    /// <param name="rawTitle">Title text (HTML entities allowed).</param>
    /// <returns>The store name, or an empty string.</returns>
    public static string FromTitle(string rawTitle)
    {
        var title = Clean(rawTitle);
        if (title.Length == 0)
        {
            return string.Empty;
        }

        // Xtech: "Home | Página inicial | Store"
        const string xtech = "| Página inicial |";
        var xtechAt = title.IndexOf(xtech, StringComparison.OrdinalIgnoreCase);
        if (xtechAt >= 0)
        {
            return title[(xtechAt + xtech.Length)..].Trim();
        }

        foreach (var separator in TitleSeparators)
        {
            if (!title.Contains(separator, StringComparison.Ordinal))
            {
                continue;
            }

            var parts = title.Split(separator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            // JET and others: "Home | Store" -> skip generic leading segments.
            var meaningful = parts.FirstOrDefault(p => !GenericSegments.Contains(p));
            return meaningful ?? parts[^1];
        }

        return GenericSegments.Contains(title) ? string.Empty : title;
    }

    private static Dictionary<string, string> ReadMetas(string html)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match tag in MetaTag.Matches(html))
        {
            string? key = null;
            string? content = null;
            foreach (Match attr in Attribute.Matches(tag.Value))
            {
                var name = attr.Groups[1].Value.ToLowerInvariant();
                var value = attr.Groups[2].Success ? attr.Groups[2].Value
                    : attr.Groups[3].Success ? attr.Groups[3].Value
                    : attr.Groups[4].Value;

                if (name is "name" or "property")
                {
                    key = value.ToLowerInvariant();
                }
                else if (name == "content")
                {
                    content = value;
                }
            }

            if (key is not null && content is not null)
            {
                result.TryAdd(key, content);
            }
        }

        return result;
    }

    private static string Clean(string value) =>
        Spaces.Replace(WebUtility.HtmlDecode(value), " ").Trim();
}
