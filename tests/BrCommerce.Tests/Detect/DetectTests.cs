using System.Net;
using BrCommerce.Detect;

namespace BrCommerce.Tests.Detect;

public class PlatformRuleSetTests
{
    [Fact]
    public void Embedded_rules_load()
    {
        var rules = PlatformRuleSet.Default;

        Assert.Equal(20, rules.Platforms.Count);
        Assert.Equal(3, rules.Flags.Count);
        Assert.All(rules.Platforms, r => Assert.NotEmpty(r.Signatures));
        Assert.Equal(rules.Platforms.Count, rules.Platforms.Select(r => r.Id).Distinct().Count());
    }

    [Fact]
    public void Custom_rules_from_json()
    {
        var rules = PlatformRuleSet.FromJson("""
            { "platforms": [ { "id": "minha", "name": "Minha Plataforma",
              "signatures": [ { "pattern": "minha-plataforma\\.com", "weight": 1 } ],
              "feeds": [ { "path": "feed.xml", "mustContain": "<rss" } ] } ] }
            """);

        var rule = Assert.Single(rules.Platforms);
        Assert.Equal("minha", rule.Id);
        Assert.Single(rule.Feeds);
        Assert.Empty(rules.Flags);
    }

    [Fact]
    public void Invalid_json_throws_format_exception()
    {
        Assert.Throws<FormatException>(() => PlatformRuleSet.FromJson("{ not json"));
        Assert.Throws<FormatException>(() => PlatformRuleSet.FromJson("""{ "platforms": [ { "name": "sem id" } ] }"""));
    }
}

public class SignatureMatcherTests
{
    [Fact]
    public void Strong_signature_wins()
    {
        var matches = SignatureMatcher.Match(Fixture.Text("vtex-home.html"), PlatformRuleSet.Default.Platforms, "html");

        var vtex = matches[0];
        Assert.Equal("vtex", vtex.Id);
        Assert.Equal(1.0, vtex.Score);
        Assert.Contains(vtex.Evidence, e => e.Contains("vteximg.com.br", StringComparison.Ordinal));
    }

    [Fact]
    public void Short_words_need_word_boundaries()
    {
        // The old substring check matched "jet" inside "projeto" and "tray" inside "portrayal".
        var matches = SignatureMatcher.Match("Um projeto novo, a portrayal of trays", PlatformRuleSet.Default.Platforms, "html");
        Assert.Empty(matches);
    }

    [Fact]
    public void Weak_signals_stay_below_threshold()
    {
        var matches = SignatureMatcher.Match("Frete grátis no Wix", PlatformRuleSet.Default.Platforms, "html");
        var wix = Assert.Single(matches);
        Assert.True(wix.Score < 0.5);
    }

    [Fact]
    public void Merge_keeps_highest_score_and_all_evidence()
    {
        var merged = SignatureMatcher.Merge(
        [
            new PlatformMatch("shopify", "Shopify", 0.7, ["html: shopify"]),
            new PlatformMatch("shopify", "Shopify", 1.0, ["robots.txt: we use shopify"]),
        ]);

        var shopify = Assert.Single(merged);
        Assert.Equal(1.0, shopify.Score);
        Assert.Equal(2, shopify.Evidence.Count);
    }
}

public class StoreNameExtractorTests
{
    [Fact]
    public void Prefers_og_site_name_over_author()
    {
        var name = StoreNameExtractor.Extract(Fixture.Text("vtex-home.html"));
        Assert.Equal(new StoreName("Loja Exemplo Moda", "og:site_name"), name);
    }

    [Fact]
    public void Xtech_title()
    {
        var name = StoreNameExtractor.Extract(Fixture.Text("xtech-home.html"));
        Assert.Equal(new StoreName("Casa das Panelas", "title"), name);
    }

    [Theory]
    [InlineData("Home | A Casa das Panelas", "A Casa das Panelas")]
    [InlineData("Loja da Ana - Roupas e acessórios", "Loja da Ana")]
    [InlineData("Loja da Ana", "Loja da Ana")]
    [InlineData("Página inicial", "")]
    [InlineData("Ferramentas &amp; Cia | Início", "Ferramentas & Cia")]
    public void Title_heuristics(string title, string expected)
    {
        Assert.Equal(expected, StoreNameExtractor.FromTitle(title));
    }

    [Fact]
    public void Falls_back_to_razao_social()
    {
        const string html = "<html><body><footer><strong>Razão Social:</strong> Panelas Ltda<br>CNPJ ...</footer></body></html>";
        Assert.Equal(new StoreName("Panelas Ltda", "razao-social"), StoreNameExtractor.Extract(html));
    }

    [Fact]
    public void Meta_attributes_in_any_order()
    {
        const string html = "<meta content='Loja Invertida' property=\"og:site_name\" />";
        Assert.Equal("Loja Invertida", StoreNameExtractor.Extract(html)?.Value);
    }
}

public class PlatformDetectorTests
{
    private static DetectorOptions Fast(FeedProbeMode feeds = FeedProbeMode.All) => new()
    {
        DelayBetweenRequests = TimeSpan.Zero,
        FeedProbes = feeds,
    };

    [Theory]
    [InlineData("loja.com.br", "https://loja.com.br/")]
    [InlineData("http://loja.com.br", "http://loja.com.br/")]
    [InlineData(" https://loja.com.br/produto?x=1 ", "https://loja.com.br/produto?x=1")]
    public void Normalizes_urls(string input, string expected)
    {
        Assert.Equal(expected, PlatformDetector.NormalizeUrl(input).ToString());
    }

    [Theory]
    [InlineData("")]
    [InlineData("ftp://loja.com.br")]
    public void Rejects_invalid_urls(string input)
    {
        Assert.Throws<ArgumentException>(() => PlatformDetector.NormalizeUrl(input));
    }

    [Fact]
    public async Task Detects_vtex_with_feed_and_follows_relative_redirect()
    {
        var handler = FakeHandler.FromMap(new()
        {
            ["https://loja-exemplo.com.br/"] = () => FakeHandler.Redirect("/home"),
            ["https://loja-exemplo.com.br/home"] = () => FakeHandler.Ok(Fixture.Text("vtex-home.html")),
            ["https://loja-exemplo.com.br/XMLData/googleshopping.xml"] = () => FakeHandler.Ok(Fixture.Text("google-shopping-rss.xml"), "application/xml"),
        });

        using var http = new HttpClient(handler);
        using var detector = new PlatformDetector(http, Fast());
        var result = await detector.DetectAsync("loja-exemplo.com.br");

        Assert.True(result.Online);
        Assert.Equal("https://loja-exemplo.com.br/home", result.FinalUrl);
        Assert.Equal(new[] { "https://loja-exemplo.com.br/home" }, result.Redirects);
        Assert.Equal("vtex", result.Platform?.Id);
        Assert.Equal("Loja Exemplo Moda", result.StoreName?.Value);

        var feed = Assert.Single(result.Feeds);
        Assert.Equal("https://loja-exemplo.com.br/XMLData/googleshopping.xml", feed.Url);
        Assert.Contains(result.Platform!.Evidence, e => e.StartsWith("feed:", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Detects_shopify_from_robots_txt_only()
    {
        var handler = FakeHandler.FromMap(new()
        {
            ["https://loja-exemplo.com.br/"] = () => FakeHandler.Ok("<html><title>Loja</title></html>"),
            ["https://loja-exemplo.com.br/robots.txt"] = () => FakeHandler.Ok(Fixture.Text("shopify-robots.txt"), "text/plain"),
        });

        using var http = new HttpClient(handler);
        using var detector = new PlatformDetector(http, Fast(FeedProbeMode.None));
        var result = await detector.DetectAsync("https://loja-exemplo.com.br");

        Assert.Equal("shopify", result.Platform?.Id);
        Assert.Contains(result.Platform!.Evidence, e => e.StartsWith("robots.txt", StringComparison.Ordinal));
        Assert.Empty(result.Feeds);
    }

    [Fact]
    public async Task Soft_404_home_page_is_not_a_feed()
    {
        // Some stores answer 200 with the home page for any path.
        var handler = new FakeHandler(_ => FakeHandler.Ok("<!DOCTYPE html><html><body>CDATA <rss \"products\":</body></html>"));
        using var http = new HttpClient(handler);
        using var detector = new PlatformDetector(http, Fast());

        var result = await detector.DetectAsync("loja-exemplo.com.br");

        Assert.Empty(result.Feeds);
        Assert.Null(result.Platform);
    }

    [Fact]
    public async Task Builds_mercado_shops_feed_from_shop_id()
    {
        var handler = FakeHandler.FromMap(new()
        {
            ["https://loja-exemplo.com.br/"] = () => FakeHandler.Ok("<script>window.__PRELOADED__ = { shopid: '123456789', theme: 'mercadoshops' }</script>"),
            ["https://www.mercadoshops.com.br/darwin/rss/123456789/gshopping"] = () => FakeHandler.Ok("<?xml version=\"1.0\"?><rss><channel></channel></rss>", "application/rss+xml"),
        });

        using var http = new HttpClient(handler);
        using var detector = new PlatformDetector(http, Fast(FeedProbeMode.CandidatesOnly));
        var result = await detector.DetectAsync("loja-exemplo.com.br");

        Assert.Equal("mercadoshops", result.Platform?.Id);
        Assert.Equal("https://www.mercadoshops.com.br/darwin/rss/123456789/gshopping", Assert.Single(result.Feeds).Url);
    }

    [Fact]
    public async Task Flags_suspended_store()
    {
        var handler = FakeHandler.FromMap(new()
        {
            ["https://loja-exemplo.com.br/"] = () => FakeHandler.Redirect("https://loja-exemplo.com.br/cgi-sys/suspendedpage.cgi", HttpStatusCode.Found),
            ["https://loja-exemplo.com.br/cgi-sys/suspendedpage.cgi"] = () => FakeHandler.Ok("<html>Account suspended</html>"),
        });

        using var http = new HttpClient(handler);
        using var detector = new PlatformDetector(http, Fast(FeedProbeMode.None));
        var result = await detector.DetectAsync("loja-exemplo.com.br");

        Assert.Contains(result.Flags, f => f.Id == "suspended");
    }

    [Fact]
    public async Task Unreachable_store_reports_error()
    {
        var handler = new FakeHandler(_ => throw new HttpRequestException("connection refused"));
        using var http = new HttpClient(handler);
        using var detector = new PlatformDetector(http, Fast());

        var result = await detector.DetectAsync("loja-exemplo.com.br");

        Assert.False(result.Online);
        Assert.Equal("connection refused", result.Error);
        Assert.Equal(2, handler.Requests.Count); // https, then http fallback
    }
}
