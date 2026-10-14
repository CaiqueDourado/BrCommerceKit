using System.Text;

namespace BrCommerce.Quality;

/// <summary>Thresholds for <see cref="CatalogQualityAnalyzer"/>.</summary>
public sealed class QualityOptions
{
    /// <summary>Flag when products sharing the value have at least this many different name first words. Default: 3.</summary>
    public int MinDistinctFirstWords { get; set; } = 3;

    /// <summary>Flag when products sharing the value have at least this many different brands. Default: 2.</summary>
    public int MinDistinctBrands { get; set; } = 2;

    /// <summary>Flag when at least this many products share the value, whatever they are. Default: 30.</summary>
    public int MinProducts { get; set; } = 30;

    /// <summary>Brands that mean "no brand" and are ignored when counting brands.</summary>
    public ISet<string> GenericBrands { get; set; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "brand", "marca", "importado", "importada", "produto importado", "original", "generico", "genérico", "generica", "genérica",
        "sem marca", "outros", "outras", "n/a", "na", "-", "0",
    };
}

/// <summary>
/// Finds catalog problems that only show up across many products:
/// identifiers reused on unrelated items, and images that are placeholders ("sem foto", a store banner).
/// </summary>
/// <remarks>
/// The idea: a real EAN or a real product photo belongs to one product. When the same value appears on products
/// whose names start with different words and whose brands differ, it is almost certainly generic.
/// </remarks>
public static class CatalogQualityAnalyzer
{
    /// <summary>GTINs that fail the check digit (or have an invalid length).</summary>
    /// <param name="items">Products.</param>
    public static IReadOnlyList<CatalogItem> FindInvalidGtins(IEnumerable<CatalogItem> items) =>
        items.Where(i => !string.IsNullOrWhiteSpace(i.Gtin) && !Gtin.IsValid(i.Gtin)).ToList();

    /// <summary>GTIN/EAN values shared by unrelated products.</summary>
    /// <param name="items">Products.</param>
    /// <param name="options">Thresholds; null = defaults.</param>
    public static IReadOnlyList<SuspiciousValue> FindSharedIdentifiers(IEnumerable<CatalogItem> items, QualityOptions? options = null) =>
        FindShared(items.Where(i => LooksLikeProductCode(i.Gtin)), i => Gtin.Clean(i.Gtin) ?? i.Gtin!.Trim(), options ?? new QualityOptions());

    /// <summary>Image hashes shared by unrelated products (placeholders, banners, logos).</summary>
    /// <param name="items">Products.</param>
    /// <param name="options">Thresholds; null = defaults.</param>
    public static IReadOnlyList<SuspiciousValue> FindPlaceholderImages(IEnumerable<CatalogItem> items, QualityOptions? options = null) =>
        FindShared(items.Where(i => !string.IsNullOrWhiteSpace(i.ImageHash)), i => i.ImageHash!.Trim(), options ?? new QualityOptions());

    /// <summary>
    /// True when <paramref name="value"/> could be a product code worth grouping: at least 5 characters when it has
    /// letters, or at least 7 digits with at least 6 significant digits (so <c>000000000001</c> is not considered).
    /// </summary>
    /// <param name="value">The identifier.</param>
    public static bool LooksLikeProductCode(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var trimmed = value.Trim();
        if (!trimmed.All(char.IsAsciiDigit))
        {
            return trimmed.Length >= 5;
        }

        return trimmed.Length >= 7 && trimmed.TrimStart('0').Length >= 6;
    }

    /// <summary>Normalised first word of a product name: lower case, no accents, letters and digits only.</summary>
    /// <param name="name">Product name.</param>
    public static string FirstWord(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return string.Empty;
        }

        var word = name.Trim().Split((char[]?)null, 2, StringSplitOptions.RemoveEmptyEntries)[0];
        var builder = new StringBuilder(word.Length);
        foreach (var original in word.ToLowerInvariant())
        {
            var c = Diacritics.Strip(original);
            if (char.IsLetterOrDigit(c))
            {
                builder.Append(c);
            }
        }

        return builder.ToString();
    }

    private static List<SuspiciousValue> FindShared(IEnumerable<CatalogItem> items, Func<CatalogItem, string> key, QualityOptions options)
    {
        var result = new List<SuspiciousValue>();
        foreach (var group in items.GroupBy(key, StringComparer.OrdinalIgnoreCase))
        {
            var list = group.ToList();
            if (list.Count < 2)
            {
                continue;
            }

            var firstWords = list.Select(i => FirstWord(i.Name)).Where(w => w.Length > 0).Distinct().Count();
            var brands = list
                .Select(i => i.Brand?.Trim())
                .Where(b => !string.IsNullOrEmpty(b) && !options.GenericBrands.Contains(b))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Count();
            var sources = list.Select(i => i.Source).Where(s => !string.IsNullOrEmpty(s)).Distinct(StringComparer.OrdinalIgnoreCase).Count();

            var reasons = new List<string>();
            if (firstWords >= options.MinDistinctFirstWords)
            {
                reasons.Add($"{firstWords} different kinds of product (first word of the name)");
            }

            if (brands >= options.MinDistinctBrands)
            {
                reasons.Add($"{brands} different brands");
            }

            if (list.Count >= options.MinProducts)
            {
                reasons.Add($"shared by {list.Count} products");
            }

            if (reasons.Count > 0)
            {
                result.Add(new SuspiciousValue(group.Key, list.Count, firstWords, brands, sources, reasons,
                    list.Take(20).Select(i => i.Id).ToList()));
            }
        }

        return result
            .OrderByDescending(r => r.Reasons.Count)
            .ThenByDescending(r => r.Products)
            .ToList();
    }
}
