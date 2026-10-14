using BrCommerce.Quality;

namespace BrCommerce.Tests.Quality;

public class GtinTests
{
    [Theory]
    [InlineData("4006381333931", GtinKind.Gtin13)]
    [InlineData("7891000315507", GtinKind.Gtin13)]
    [InlineData("036000291452", GtinKind.Gtin12)]
    [InlineData("73513537", GtinKind.Gtin8)]
    [InlineData("00012345678905", GtinKind.Gtin14)]
    [InlineData("789-1000-31550-7", GtinKind.Gtin13)]
    public void Valid_codes(string code, GtinKind kind)
    {
        Assert.True(Gtin.IsValid(code));
        Assert.Equal(kind, Gtin.GetKind(code));
    }

    [Theory]
    [InlineData("4006381333932")] // wrong check digit
    [InlineData("0000000000000")] // run of one digit
    [InlineData("12345")] // wrong length
    [InlineData("ABC1234567890")] // letters
    [InlineData("")]
    public void Invalid_codes(string? code)
    {
        Assert.False(Gtin.IsValid(code));
    }

    [Fact]
    public void Null_is_invalid()
    {
        Assert.False(Gtin.IsValid(null));
    }

    [Fact]
    public void Computes_check_digit()
    {
        Assert.Equal(1, Gtin.ComputeCheckDigit("400638133393"));
        Assert.Equal(7, Gtin.ComputeCheckDigit("789100031550"));
    }

    [Fact]
    public void Pads_to_gtin14()
    {
        Assert.Equal("04006381333931", Gtin.ToGtin14("4006381333931"));
        Assert.Null(Gtin.ToGtin14("4006381333932"));
    }
}

public class CatalogQualityAnalyzerTests
{
    private static List<CatalogItem> Catalog() =>
    [
        new("1", "Tênis Corrida Azul", "Marca A", "7891000315507", "aaa111", "loja1"),
        new("2", "Geladeira Duplex", "Marca B", "7891000315507", "ph-semfoto", "loja1"),
        new("3", "Fogão 4 bocas", "Marca C", "7891000315507", "ph-semfoto", "loja2"),
        new("4", "Camiseta Básica", "Marca D", "123", "ph-semfoto", "loja2"),
        new("5", "Panela de pressão", "Importado", "7896004000855", "ph-semfoto", "loja3"),
        new("6", "Panela antiaderente", "Marca E", "7896004000856", "bbb222", "loja3"),
    ];

    [Fact]
    public void Flags_identifier_shared_by_unrelated_products()
    {
        var shared = CatalogQualityAnalyzer.FindSharedIdentifiers(Catalog());

        var value = Assert.Single(shared);
        Assert.Equal("7891000315507", value.Value);
        Assert.Equal(3, value.Products);
        Assert.Equal(3, value.DistinctFirstWords);
        Assert.Equal(3, value.DistinctBrands);
        Assert.Equal(new[] { "1", "2", "3" }, value.ProductIds);
    }

    [Fact]
    public void Flags_placeholder_image_and_ignores_generic_brands()
    {
        var placeholders = CatalogQualityAnalyzer.FindPlaceholderImages(Catalog());

        var value = Assert.Single(placeholders);
        Assert.Equal("ph-semfoto", value.Value);
        Assert.Equal(4, value.Products);
        Assert.Equal(3, value.DistinctBrands); // "Importado" is not a brand
        Assert.Equal(3, value.DistinctSources);
    }

    [Fact]
    public void Same_product_in_two_stores_is_not_flagged()
    {
        var items = new[]
        {
            new CatalogItem("1", "Geladeira Duplex 375L", "Marca B", "7891000315507", "h1", "loja1"),
            new CatalogItem("2", "Geladeira Duplex 375 Litros Inox", "Marca B", "7891000315507", "h1", "loja2"),
        };

        Assert.Empty(CatalogQualityAnalyzer.FindSharedIdentifiers(items));
        Assert.Empty(CatalogQualityAnalyzer.FindPlaceholderImages(items));
    }

    [Fact]
    public void Finds_invalid_gtins()
    {
        var invalid = CatalogQualityAnalyzer.FindInvalidGtins(Catalog());
        Assert.Equal(new[] { "4", "6" }, invalid.Select(i => i.Id));
    }

    [Theory]
    [InlineData("7891000315507", true)]
    [InlineData("MO9007", true)]
    [InlineData("000000000001", false)]
    [InlineData("123456", false)]
    [InlineData("AB12", false)]
    [InlineData(null, false)]
    public void Product_code_heuristic(string? value, bool expected)
    {
        Assert.Equal(expected, CatalogQualityAnalyzer.LooksLikeProductCode(value));
    }

    [Theory]
    [InlineData("Tênis Corrida", "tenis")]
    [InlineData("  FOGÃO 4 bocas", "fogao")]
    [InlineData("", "")]
    public void First_word_is_normalised(string name, string expected)
    {
        Assert.Equal(expected, CatalogQualityAnalyzer.FirstWord(name));
    }
}
