using BrCommerce.Feeds;

namespace BrCommerce.Tests.Feeds;

public class PriceParserTests
{
    [Theory]
    [InlineData("R$ 1.299,90", 1299.90, "BRL")]
    [InlineData("R$1.299,90", 1299.90, "BRL")]
    [InlineData("1299.90 BRL", 1299.90, "BRL")]
    [InlineData("BRL 1299,90", 1299.90, "BRL")]
    [InlineData("1,299.90 USD", 1299.90, "USD")]
    [InlineData("US$ 10", 10.0, "USD")]
    [InlineData("AR$ 15.000", 15000.0, "ARS")]
    [InlineData("ARS 15.000,50", 15000.50, "ARS")]
    [InlineData("59,90", 59.90, null)]
    [InlineData("59.9", 59.9, null)]
    [InlineData("1.299", 1299.0, null)]
    [InlineData("1.299.999,00", 1299999.00, null)]
    [InlineData("2,500,000", 2500000.0, null)]
    [InlineData("€ 12,50", 12.50, "EUR")]
    public void Parses_common_formats(string text, double expected, string? currency)
    {
        var money = PriceParser.Parse(text);

        Assert.NotNull(money);
        Assert.Equal((decimal)expected, money!.Value.Amount);
        Assert.Equal(currency, money.Value.Currency);
    }

    [Fact]
    public void Uses_default_currency_when_text_has_none()
    {
        Assert.Equal("BRL", PriceParser.Parse("10,00", "BRL")!.Value.Currency);
    }

    [Fact]
    public void Returns_null_for_null()
    {
        Assert.Null(PriceParser.Parse(null));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("consulte")]
    public void Returns_null_without_a_number(string? text)
    {
        Assert.Null(PriceParser.Parse(text));
    }

    [Fact]
    public void Dot_with_three_decimals_in_usd_is_a_decimal_point()
    {
        Assert.Equal(1.299m, PriceParser.ParseNumber("1.299", "USD"));
    }
}
