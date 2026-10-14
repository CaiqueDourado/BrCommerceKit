using BrCommerce.Feeds;

namespace BrCommerce.Tests.Feeds;

public class FeedInspectorTests
{
    [Fact]
    public void Profiles_fields()
    {
        using var stream = Fixture.Open("google-shopping-rss.xml");
        var report = FeedInspector.Inspect(stream);

        Assert.Equal("item", report.ItemElement);
        Assert.Equal(2, report.ItemCount);

        var price = report.Fields.Single(f => f.Name == "price");
        Assert.Equal(2, price.ItemsWithField);

        var additional = report.Fields.Single(f => f.Name == "additional_image_link");
        Assert.Equal(1, additional.ItemsWithField);
        Assert.Equal(2, additional.Occurrences);

        // Most frequent fields come first.
        Assert.Equal(2, report.Fields[0].ItemsWithField);
    }

    [Fact]
    public void Stops_at_max_items()
    {
        using var stream = Fixture.Open("google-shopping-rss.xml");
        Assert.Equal(1, FeedInspector.Inspect(stream, maxItems: 1).ItemCount);
    }
}

public class ShopifyCatalogReaderTests
{
    private static readonly Uri Store = new("https://loja-exemplo.com.br/");

    [Fact]
    public void Expands_variants()
    {
        var products = ShopifyCatalogReader.ParsePage(Fixture.Text("shopify-products.json"), Store);

        Assert.Equal(3, products.Count);

        var first = products[0];
        Assert.Equal("901", first.Id);
        Assert.Equal("111", first.ItemGroupId);
        Assert.Equal("Camiseta Básica / P / Branca", first.Title);
        Assert.Equal("https://loja-exemplo.com.br/products/camiseta-basica?variant=901", first.Link);
        Assert.Equal(79.90m, first.Price!.Value.Amount);
        Assert.Equal(99.90m, first.OldPrice!.Value.Amount);
        Assert.Equal(Availability.InStock, first.Availability);
        Assert.Equal("Marca Algodão", first.Brand);
        Assert.Equal("7891000315507", first.Gtin);
        Assert.Equal("Algodão 100%", first.Description);
        Assert.Equal("https://cdn.shopify.com/s/files/camiseta-1.jpg", first.ImageLink);
        Assert.Equal(new[] { "verão", "básicos" }, first.Tags);

        var second = products[1];
        Assert.Equal(Availability.OutOfStock, second.Availability);
        Assert.Null(second.OldPrice);
        Assert.Null(second.Gtin);
        Assert.Equal("https://cdn.shopify.com/s/files/camiseta-preta.jpg", second.ImageLink);

        var mug = products[2];
        Assert.Equal("Caneca", mug.Title);
        Assert.Equal(new[] { "cozinha", "presente" }, mug.Tags);
        Assert.Null(mug.ImageLink);
    }

    [Fact]
    public void Empty_page_yields_nothing()
    {
        Assert.Empty(ShopifyCatalogReader.ParsePage("{\"products\":[]}", Store));
    }

    [Fact]
    public async Task Paginates_until_empty_page()
    {
        var page1 = Fixture.Text("shopify-products.json");
        var handler = new FakeHandler(request => request.RequestUri!.Query.Contains("page=1", StringComparison.Ordinal)
            ? FakeHandler.Ok(page1, "application/json")
            : FakeHandler.Ok("{\"products\":[]}", "application/json"));

        using var http = new HttpClient(handler);
        var reader = new ShopifyCatalogReader(http, new ShopifyReaderOptions { DelayBetweenPages = TimeSpan.Zero });

        var products = new List<Product>();
        await foreach (var p in reader.ReadAsync(Store))
        {
            products.Add(p);
        }

        Assert.Equal(3, products.Count);
        Assert.Equal(2, handler.Requests.Count);
    }

    [Fact]
    public async Task Stops_when_a_page_repeats()
    {
        var page = Fixture.Text("shopify-products.json");
        var handler = new FakeHandler(_ => FakeHandler.Ok(page, "application/json"));
        using var http = new HttpClient(handler);
        var reader = new ShopifyCatalogReader(http, new ShopifyReaderOptions { DelayBetweenPages = TimeSpan.Zero });

        var count = 0;
        await foreach (var _ in reader.ReadAsync(Store))
        {
            count++;
        }

        Assert.Equal(3, count);
        Assert.Equal(2, handler.Requests.Count);
    }
}
