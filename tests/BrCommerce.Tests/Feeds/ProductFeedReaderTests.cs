using BrCommerce.Feeds;

namespace BrCommerce.Tests.Feeds;

public class ProductFeedReaderTests
{
    [Fact]
    public void Reads_google_merchant_rss()
    {
        using var stream = Fixture.Open("google-shopping-rss.xml");
        var products = ProductFeedReader.Read(stream).ToList();

        Assert.Equal(2, products.Count);
        var p = products[0];
        Assert.Equal("SKU-001", p.Id);
        Assert.Equal("Tênis Corrida & Caminhada Azul", p.Title);
        Assert.Equal("https://loja-exemplo.com.br/tenis-corrida-azul", p.Link);
        Assert.Equal("https://loja-exemplo.com.br/img/1.jpg", p.ImageLink);
        Assert.Equal(2, p.AdditionalImageLinks.Count);
        Assert.Equal(299.90m, p.Price!.Value.Amount);
        Assert.Equal(249.90m, p.SalePrice!.Value.Amount);
        Assert.Equal(249.90m, p.EffectivePrice!.Value.Amount);
        Assert.Equal("BRL", p.Price.Value.Currency);
        Assert.Equal(Availability.InStock, p.Availability);
        Assert.Equal("7891000315507", p.Gtin);
        Assert.Equal("Calçados > Tênis", p.ProductType);
        Assert.Equal("187", p.GoogleProductCategory);
        Assert.Equal(Availability.OutOfStock, products[1].Availability);
    }

    [Fact]
    public void Nested_shipping_price_does_not_override_product_price()
    {
        using var stream = Fixture.Open("google-shopping-rss.xml");
        var p = ProductFeedReader.Read(stream).First();

        Assert.Equal(299.90m, p.Price!.Value.Amount);
        Assert.Equal("19.90 BRL", p.Fields["shipping.price"][0]);
        Assert.Equal("BR", p.Fields["shipping.country"][0]);
    }

    [Fact]
    public void Tolerates_undeclared_prefixes_and_takes_the_last_repeated_price()
    {
        using var stream = Fixture.Open("undeclared-prefix.xml");
        var p = Assert.Single(ProductFeedReader.Read(stream));

        Assert.Equal("Produto sem xmlns", p.Title);
        Assert.Equal(1299.90m, p.Price!.Value.Amount);
        Assert.Equal("Marca Google Prefix", p.Brand);
    }

    [Fact]
    public void Reads_brazilian_dialect_in_latin1()
    {
        using var stream = Fixture.Open("produtos-br.xml");
        var products = ProductFeedReader.Read(stream).ToList();

        Assert.Equal(2, products.Count);
        var p = products[0];
        Assert.Equal("9001", p.Id);
        Assert.Equal("Geladeira Frost Free 375 Litros", p.Title);
        Assert.Equal("Geladeira duplex <b>inox</b>", p.Description);
        Assert.Equal("Marca Fria", p.Brand);
        Assert.Equal("Eletrodomésticos", p.Category);
        Assert.Equal(4299.00m, p.OldPrice!.Value.Amount);
        Assert.Equal(3799.90m, p.Price!.Value.Amount);
        Assert.Equal(Availability.InStock, p.Availability);
        Assert.Equal("7896004000855", p.Gtin);
        Assert.Equal("https://loja-exemplo.com.br/geladeira", p.Link);
        Assert.Equal("https://loja-exemplo.com.br/img/geladeira.jpg", p.ImageLink);
        Assert.Equal("Unissex", p.Gender);

        Assert.Equal(1299m, products[1].Price!.Value.Amount);
        Assert.Equal(Availability.OutOfStock, products[1].Availability);
    }

    [Fact]
    public void Reads_atom_entries_with_link_href()
    {
        using var stream = Fixture.Open("atom.xml");
        var p = Assert.Single(ProductFeedReader.Read(stream));

        Assert.Equal("A1", p.Id);
        Assert.Equal("Produto Atom", p.Title);
        Assert.Equal("https://loja-exemplo.com.br/produto-atom", p.Link);
        Assert.Equal(59.90m, p.Price!.Value.Amount);
        Assert.Equal(Availability.InStock, p.Availability);
    }

    [Fact]
    public void Custom_item_element_name()
    {
        const string xml = "<catalogo><oferta><titulo>X</titulo><valor>10,00</valor></oferta></catalogo>";
        using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(xml));
        var options = new FeedReaderOptions { ItemElementNames = ["oferta"] };

        var p = Assert.Single(ProductFeedReader.Read(stream, options));
        Assert.Equal("X", p.Title);
        Assert.Equal(10.00m, p.Price!.Value.Amount);
    }
}
