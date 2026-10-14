namespace BrCommerce.Quality;

/// <summary>The minimum a quality check needs to know about a product.</summary>
/// <param name="Id">Your identifier for the product.</param>
/// <param name="Name">Product name.</param>
/// <param name="Brand">Brand, if known.</param>
/// <param name="Gtin">GTIN/EAN as in the source, if any.</param>
/// <param name="ImageHash">Any hash of the main image (perceptual or exact), if computed.</param>
/// <param name="Source">Store or feed the product came from, if relevant.</param>
public sealed record CatalogItem(
    string Id,
    string Name,
    string? Brand = null,
    string? Gtin = null,
    string? ImageHash = null,
    string? Source = null);

/// <summary>A value (identifier or image hash) shared by products that do not look like the same product.</summary>
/// <param name="Value">The shared value.</param>
/// <param name="Products">How many products carry it.</param>
/// <param name="DistinctFirstWords">How many different first words their names have ("Tênis", "Geladeira", ...).</param>
/// <param name="DistinctBrands">How many different brands (generic brands such as "Importado" not counted).</param>
/// <param name="DistinctSources">How many different sources.</param>
/// <param name="Reasons">Why it was flagged.</param>
/// <param name="ProductIds">Up to 20 example product ids.</param>
public sealed record SuspiciousValue(
    string Value,
    int Products,
    int DistinctFirstWords,
    int DistinctBrands,
    int DistinctSources,
    IReadOnlyList<string> Reasons,
    IReadOnlyList<string> ProductIds);
