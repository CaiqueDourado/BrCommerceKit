using System.Net;
using System.Text.RegularExpressions;

namespace BrCommerce.Feeds;

/// <summary>
/// Reads product feeds (Google Merchant RSS/Atom, Facebook catalog, Brazilian XML dialects) into
/// <see cref="Product"/> objects, one item at a time.
/// </summary>
/// <example>
/// <code>
/// await using var stream = File.OpenRead("googleshopping.xml");
/// foreach (var p in ProductFeedReader.Read(stream))
///     Console.WriteLine($"{p.Title} - {p.EffectivePrice} - {p.Availability}");
/// </code>
/// </example>
public static class ProductFeedReader
{
    private static readonly Regex Spaces = new("\\s+", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

    /// <summary>
    /// Field aliases, in priority order. The first name with a non-empty value wins.
    /// Names are matched without namespace prefix and also with a <c>google_</c> prefix (as some exporters write).
    /// </summary>
    public static IReadOnlyDictionary<string, string[]> Aliases { get; } = new Dictionary<string, string[]>
    {
        ["id"] = ["id", "id_produto", "idproduto", "codigo", "codigo_produto"],
        ["title"] = ["title", "name", "nome_produto", "nome", "titulo"],
        ["description"] = ["description", "descricao", "summary"],
        ["link"] = ["link", "url", "link_produto", "url_produto"],
        ["image_link"] = ["image_link", "image_1", "link_imagem", "imagem", "image", "url_imagem"],
        ["additional_image_link"] = ["additional_image_link"],
        ["price"] = ["price", "preco_por", "preco_promocao", "preco", "valor"],
        ["sale_price"] = ["sale_price"],
        ["oldprice"] = ["oldprice", "old_price", "preco_de", "compare_at_price", "preco_original"],
        ["availability"] = ["availability", "disponibilidade", "in_stock", "estoque_disponivel"],
        ["brand"] = ["brand", "branding", "marca", "fabricante"],
        ["gtin"] = ["gtin", "ean", "isbn", "barcode", "codigo_barras", "upc"],
        ["mpn"] = ["mpn"],
        ["sku"] = ["sku", "id_sku"],
        ["category"] = ["category", "categoria", "departamento"],
        ["google_product_category"] = ["google_product_category"],
        ["product_type"] = ["product_type", "tipo_produto"],
        ["condition"] = ["condition", "condicao"],
        ["color"] = ["color", "cor"],
        ["size"] = ["size", "tamanho"],
        ["gender"] = ["gender", "genero"],
        ["age_group"] = ["age_group", "faixa_etaria"],
        ["item_group_id"] = ["item_group_id"],
        ["installment"] = ["installment", "parcelamento"],
    };

    /// <summary>Reads the products of the feed in <paramref name="stream"/>.</summary>
    /// <param name="stream">The feed. Not disposed.</param>
    /// <param name="options">Options; null = defaults.</param>
    public static IEnumerable<Product> Read(Stream stream, FeedReaderOptions? options = null)
    {
        options ??= new FeedReaderOptions();
        foreach (var item in RawFeedReader.Read(stream, options))
        {
            yield return ToProduct(item, options);
        }
    }

    /// <summary>Converts a raw item into a <see cref="Product"/>.</summary>
    /// <param name="item">The raw item.</param>
    /// <param name="options">Options; null = defaults.</param>
    public static Product ToProduct(RawFeedItem item, FeedReaderOptions? options = null)
    {
        options ??= new FeedReaderOptions();
        var f = item.Fields;
        var currency = options.DefaultCurrency;

        var availabilityRaw = First(f, "availability");

        return new Product
        {
            Id = First(f, "id"),
            Title = CleanIf(options, First(f, "title")),
            Description = CleanIf(options, First(f, "description")),
            Link = First(f, "link"),
            ImageLink = First(f, "image_link"),
            AdditionalImageLinks = All(f, "additional_image_link"),
            // Some feeds repeat the price (<price> and <g:price>); the last parseable one wins.
            Price = LastPrice(f, "price", currency),
            SalePrice = LastPrice(f, "sale_price", currency),
            OldPrice = LastPrice(f, "oldprice", currency),
            AvailabilityRaw = availabilityRaw,
            Availability = AvailabilityParser.Parse(availabilityRaw),
            Brand = CleanIf(options, First(f, "brand")),
            Gtin = First(f, "gtin"),
            Mpn = First(f, "mpn"),
            Sku = First(f, "sku"),
            Category = CleanIf(options, First(f, "category")),
            GoogleProductCategory = First(f, "google_product_category"),
            ProductType = CleanIf(options, First(f, "product_type")),
            Condition = First(f, "condition"),
            Color = First(f, "color"),
            Size = First(f, "size"),
            Gender = First(f, "gender"),
            AgeGroup = First(f, "age_group"),
            ItemGroupId = First(f, "item_group_id"),
            Installment = First(f, "installment"),
            Tags = Labels(f),
            Fields = item.Fields,
        };
    }

    private static IEnumerable<string> Candidates(string field)
    {
        foreach (var name in Aliases.TryGetValue(field, out var names) ? names : [field])
        {
            yield return name;
            yield return "google_" + name;
        }
    }

    private static string? First(IReadOnlyDictionary<string, IReadOnlyList<string>> fields, string field)
    {
        foreach (var name in Candidates(field))
        {
            if (fields.TryGetValue(name, out var values))
            {
                var value = values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));
                if (value is not null)
                {
                    return value.Trim();
                }
            }
        }

        return null;
    }

    private static IReadOnlyList<string> All(IReadOnlyDictionary<string, IReadOnlyList<string>> fields, string field) =>
        Candidates(field)
            .SelectMany(name => fields.TryGetValue(name, out var values) ? values : [])
            .SelectMany(v => v.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .Distinct(StringComparer.Ordinal)
            .ToList();

    private static Money? LastPrice(IReadOnlyDictionary<string, IReadOnlyList<string>> fields, string field, string? currency)
    {
        foreach (var name in Candidates(field))
        {
            if (!fields.TryGetValue(name, out var values))
            {
                continue;
            }

            for (var i = values.Count - 1; i >= 0; i--)
            {
                if (PriceParser.Parse(values[i], currency) is { } money)
                {
                    return money;
                }
            }
        }

        return null;
    }

    private static IReadOnlyList<string> Labels(IReadOnlyDictionary<string, IReadOnlyList<string>> fields) =>
        Enumerable.Range(0, 5)
            .Select(i => First(fields, "custom_label_" + i))
            .Where(v => v is not null)
            .Select(v => v!)
            .ToList();

    private static string? CleanIf(FeedReaderOptions options, string? value)
    {
        if (value is null || !options.CleanText)
        {
            return value;
        }

        return Spaces.Replace(WebUtility.HtmlDecode(value), " ").Trim();
    }
}
