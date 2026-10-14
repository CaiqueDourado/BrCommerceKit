using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text.Json;

namespace BrCommerce.Feeds;

/// <summary>Options for <see cref="ShopifyCatalogReader"/>.</summary>
public sealed class ShopifyReaderOptions
{
    /// <summary>Products per page (Shopify accepts up to 250). Default: 250.</summary>
    public int PageSize { get; set; } = 250;

    /// <summary>Stop after this many pages. Default: 200 (50,000 products).</summary>
    public int MaxPages { get; set; } = 200;

    /// <summary>Pause between pages. Default: 600 ms.</summary>
    public TimeSpan DelayBetweenPages { get; set; } = TimeSpan.FromMilliseconds(600);

    /// <summary>User-Agent sent with requests.</summary>
    public string UserAgent { get; set; } = "BrCommerceKit/0.1 (+https://github.com/CaiqueDourado/BrCommerceKit)";

    /// <summary>Currency of the store. Shopify's public JSON does not include it. Default: <c>BRL</c>.</summary>
    public string? Currency { get; set; } = "BRL";
}

/// <summary>
/// Reads the public catalog of a Shopify store from <c>/products.json</c>, expanding every variant into its own
/// <see cref="Product"/> (title "Product / Variant", link with <c>?variant=</c>).
/// </summary>
/// <remarks>
/// Pagination uses <c>?limit=&amp;page=</c> and stops at an empty page or when a page repeats the previous one.
/// Some stores disable or limit this endpoint; respect their robots.txt and terms of use.
/// </remarks>
public sealed class ShopifyCatalogReader
{
    private readonly HttpClient _http;
    private readonly ShopifyReaderOptions _options;

    /// <summary>Creates a reader.</summary>
    /// <param name="httpClient">HTTP client to use.</param>
    /// <param name="options">Options; null = defaults.</param>
    public ShopifyCatalogReader(HttpClient httpClient, ShopifyReaderOptions? options = null)
    {
        _http = httpClient;
        _options = options ?? new ShopifyReaderOptions();
    }

    /// <summary>Reads every product variant of the store.</summary>
    /// <param name="storeUrl">Store root, e.g. <c>https://loja.com.br</c>.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async IAsyncEnumerable<Product> ReadAsync(Uri storeUrl, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var root = new Uri(storeUrl.GetLeftPart(UriPartial.Authority) + "/");
        string? previous = null;

        for (var page = 1; page <= _options.MaxPages; page++)
        {
            if (page > 1 && _options.DelayBetweenPages > TimeSpan.Zero)
            {
                await Task.Delay(_options.DelayBetweenPages, cancellationToken).ConfigureAwait(false);
            }

            var url = new Uri(root, $"products.json?limit={_options.PageSize}&page={page}");
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.TryAddWithoutValidation("User-Agent", _options.UserAgent);
            request.Headers.TryAddWithoutValidation("Accept", "application/json");

            using var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

            if (json == previous)
            {
                yield break;
            }

            previous = json;
            var products = ParsePage(json, root, _options.Currency);
            if (products.Count == 0)
            {
                yield break;
            }

            foreach (var product in products)
            {
                yield return product;
            }
        }
    }

    /// <summary>Parses one <c>products.json</c> page.</summary>
    /// <param name="json">The page JSON.</param>
    /// <param name="storeRoot">Store root, used to build product links.</param>
    /// <param name="currency">Currency to attach to prices.</param>
    /// <returns>One <see cref="Product"/> per variant.</returns>
    public static IReadOnlyList<Product> ParsePage(string json, Uri storeRoot, string? currency = "BRL")
    {
        var result = new List<Product>();
        using var document = JsonDocument.Parse(json);
        if (!document.RootElement.TryGetProperty("products", out var products) || products.ValueKind != JsonValueKind.Array)
        {
            return result;
        }

        foreach (var product in products.EnumerateArray())
        {
            var title = Str(product, "title");
            var handle = Str(product, "handle");
            var tags = ReadTags(product);
            var images = product.TryGetProperty("images", out var imgs) && imgs.ValueKind == JsonValueKind.Array
                ? imgs.EnumerateArray().Select(i => Str(i, "src")).Where(s => s is not null).Select(s => s!).ToList()
                : [];

            if (!product.TryGetProperty("variants", out var variants) || variants.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            foreach (var variant in variants.EnumerateArray())
            {
                var variantId = Str(variant, "id");
                var variantTitle = Str(variant, "title");
                var fullTitle = string.IsNullOrEmpty(variantTitle) || variantTitle == "Default Title"
                    ? title
                    : $"{title} / {variantTitle}";

                var image = variant.TryGetProperty("featured_image", out var featured) && featured.ValueKind == JsonValueKind.Object
                    ? Str(featured, "src")
                    : images.FirstOrDefault();

                bool? available = variant.TryGetProperty("available", out var a) && a.ValueKind is JsonValueKind.True or JsonValueKind.False
                    ? a.GetBoolean()
                    : null;

                result.Add(new Product
                {
                    Id = variantId,
                    ItemGroupId = Str(product, "id"),
                    Title = fullTitle,
                    Description = StripTags(Str(product, "body_html")),
                    Link = handle is null ? null : new Uri(storeRoot, $"products/{handle}?variant={variantId}").ToString(),
                    ImageLink = image,
                    AdditionalImageLinks = images.Where(i => i != image).ToList(),
                    Price = Money(Str(variant, "price"), currency),
                    OldPrice = Money(Str(variant, "compare_at_price"), currency),
                    Availability = available switch
                    {
                        true => Availability.InStock,
                        false => Availability.OutOfStock,
                        null => Availability.Unknown,
                    },
                    AvailabilityRaw = available?.ToString(CultureInfo.InvariantCulture),
                    Brand = Str(product, "vendor"),
                    Gtin = Str(variant, "barcode"),
                    Sku = Str(variant, "sku"),
                    ProductType = Str(product, "product_type"),
                    Tags = tags,
                });
            }
        }

        return result;
    }

    private static Money? Money(string? value, string? currency) =>
        value is not null && decimal.TryParse(value, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var amount)
            ? new Money(amount, currency)
            : null;

    private static IReadOnlyList<string> ReadTags(JsonElement product)
    {
        if (!product.TryGetProperty("tags", out var tags))
        {
            return [];
        }

        return tags.ValueKind switch
        {
            JsonValueKind.Array => tags.EnumerateArray().Select(t => t.GetString()).Where(t => !string.IsNullOrWhiteSpace(t)).Select(t => t!.Trim()).ToList(),
            JsonValueKind.String => (tags.GetString() ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
            _ => [],
        };
    }

    private static string? Str(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var value))
        {
            return null;
        }

        return value.ValueKind switch
        {
            JsonValueKind.String => string.IsNullOrWhiteSpace(value.GetString()) ? null : value.GetString(),
            JsonValueKind.Number => value.GetRawText(),
            _ => null,
        };
    }

    private static string? StripTags(string? html)
    {
        if (string.IsNullOrWhiteSpace(html))
        {
            return null;
        }

        var text = System.Text.RegularExpressions.Regex.Replace(html, "<[^>]+>", " ", System.Text.RegularExpressions.RegexOptions.None, TimeSpan.FromSeconds(1));
        text = System.Net.WebUtility.HtmlDecode(text);
        return System.Text.RegularExpressions.Regex.Replace(text, "\\s+", " ", System.Text.RegularExpressions.RegexOptions.None, TimeSpan.FromSeconds(1)).Trim();
    }
}
