namespace BrCommerce.Feeds;

/// <summary>A product normalised to the Google Merchant vocabulary, whatever the source format.</summary>
public sealed record Product
{
    /// <summary>Item identifier (<c>id</c>, <c>id_produto</c>, <c>idproduto</c>).</summary>
    public string? Id { get; init; }

    /// <summary>Title (<c>title</c>, <c>name</c>, <c>nome_produto</c>, <c>nome</c>).</summary>
    public string? Title { get; init; }

    /// <summary>Description (<c>description</c>, <c>descricao</c>, <c>summary</c>).</summary>
    public string? Description { get; init; }

    /// <summary>Product page URL (<c>link</c>, <c>url</c>, <c>link_produto</c>).</summary>
    public string? Link { get; init; }

    /// <summary>Main image (<c>image_link</c>, <c>image_1</c>, <c>link_imagem</c>, <c>image</c>).</summary>
    public string? ImageLink { get; init; }

    /// <summary>Additional images (<c>additional_image_link</c>).</summary>
    public IReadOnlyList<string> AdditionalImageLinks { get; init; } = [];

    /// <summary>Regular price (<c>price</c>, <c>preco_por</c>, <c>preco_promocao</c>, <c>preco</c>).</summary>
    public Money? Price { get; init; }

    /// <summary>Sale price (<c>sale_price</c>).</summary>
    public Money? SalePrice { get; init; }

    /// <summary>"From" price shown crossed out (<c>oldprice</c>, <c>preco_de</c>, <c>compare_at_price</c>).</summary>
    public Money? OldPrice { get; init; }

    /// <summary>The price a buyer pays: <see cref="SalePrice"/> when positive, otherwise <see cref="Price"/>.</summary>
    public Money? EffectivePrice => SalePrice is { Amount: > 0 } ? SalePrice : Price;

    /// <summary>Normalised availability.</summary>
    public Availability Availability { get; init; } = Availability.Unknown;

    /// <summary>Availability text exactly as in the feed.</summary>
    public string? AvailabilityRaw { get; init; }

    /// <summary>Brand (<c>brand</c>, <c>branding</c>, <c>marca</c>, Shopify <c>vendor</c>).</summary>
    public string? Brand { get; init; }

    /// <summary>GTIN/EAN/ISBN as in the feed (<c>gtin</c>, <c>ean</c>, <c>isbn</c>, <c>barcode</c>).</summary>
    public string? Gtin { get; init; }

    /// <summary>Manufacturer part number.</summary>
    public string? Mpn { get; init; }

    /// <summary>SKU (<c>sku</c>, <c>id_sku</c>).</summary>
    public string? Sku { get; init; }

    /// <summary>Store category (<c>category</c>, <c>categoria</c>, <c>departamento</c>).</summary>
    public string? Category { get; init; }

    /// <summary>Google product taxonomy value.</summary>
    public string? GoogleProductCategory { get; init; }

    /// <summary>Product type / breadcrumb (<c>product_type</c>).</summary>
    public string? ProductType { get; init; }

    /// <summary>Condition (<c>new</c>, <c>used</c>, <c>refurbished</c>).</summary>
    public string? Condition { get; init; }

    /// <summary>Color (<c>color</c>, <c>cor</c>).</summary>
    public string? Color { get; init; }

    /// <summary>Size (<c>size</c>, <c>tamanho</c>).</summary>
    public string? Size { get; init; }

    /// <summary>Gender (<c>gender</c>, <c>genero</c>).</summary>
    public string? Gender { get; init; }

    /// <summary>Age group.</summary>
    public string? AgeGroup { get; init; }

    /// <summary>Groups variants of the same product.</summary>
    public string? ItemGroupId { get; init; }

    /// <summary>Instalment text as in the feed.</summary>
    public string? Installment { get; init; }

    /// <summary>Tags (Shopify) or custom labels.</summary>
    public IReadOnlyList<string> Tags { get; init; } = [];

    /// <summary>
    /// Every field of the source item, by name without namespace prefix, with all its values in order.
    /// Nested elements are flattened as <c>parent.child</c>.
    /// </summary>
    public IReadOnlyDictionary<string, IReadOnlyList<string>> Fields { get; init; } =
        new Dictionary<string, IReadOnlyList<string>>();
}

/// <summary>Normalised availability.</summary>
public enum Availability
{
    /// <summary>Not present or not recognised.</summary>
    Unknown,

    /// <summary>In stock.</summary>
    InStock,

    /// <summary>Out of stock.</summary>
    OutOfStock,

    /// <summary>Pre-order.</summary>
    Preorder,

    /// <summary>Back-order.</summary>
    Backorder,
}
