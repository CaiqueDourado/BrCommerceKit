using BrCommerce.Feeds;

namespace BrCommerce.Tests.Feeds;

public class AvailabilityParserTests
{
    [Theory]
    [InlineData("in stock", Availability.InStock)]
    [InlineData("in_stock", Availability.InStock)]
    [InlineData("Disponível", Availability.InStock)]
    [InlineData("Em estoque", Availability.InStock)]
    [InlineData("http://schema.org/InStock", Availability.InStock)]
    [InlineData("https://schema.org/OutOfStock", Availability.OutOfStock)]
    [InlineData("out of stock", Availability.OutOfStock)]
    [InlineData("Esgotado", Availability.OutOfStock)]
    [InlineData("indisponivel", Availability.OutOfStock)]
    [InlineData("Não", Availability.OutOfStock)]
    [InlineData("true", Availability.InStock)]
    [InlineData("0", Availability.OutOfStock)]
    [InlineData("preorder", Availability.Preorder)]
    [InlineData("Pré-venda", Availability.Preorder)]
    [InlineData("backorder", Availability.Backorder)]
    [InlineData("talvez", Availability.Unknown)]
    [InlineData(null, Availability.Unknown)]
    public void Normalises(string? value, Availability expected)
    {
        Assert.Equal(expected, AvailabilityParser.Parse(value));
    }
}
