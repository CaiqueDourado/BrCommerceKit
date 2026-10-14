using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using BrCommerce.Cli;
using BrCommerce.Detect;
using BrCommerce.Feeds;
using BrCommerce.Quality;

Console.OutputEncoding = System.Text.Encoding.UTF8;

var json = new JsonSerializerOptions
{
    WriteIndented = true,
    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    Converters = { new JsonStringEnumConverter() },
};

var jsonLine = new JsonSerializerOptions(json) { WriteIndented = false };

try
{
    var cli = Args.Parse(args);
    return cli.Command switch
    {
        "detect" => await DetectAsync(cli),
        "feed" when cli.Sub == "inspect" => await InspectAsync(cli),
        "feed" when cli.Sub == "read" => await ReadFeedAsync(cli),
        "shopify" => await ShopifyAsync(cli),
        "quality" => Quality(cli),
        "rules" => Rules(),
        "version" or "--version" => Print(typeof(PlatformDetector).Assembly.GetName().Version?.ToString(3) ?? "?"),
        _ => Help(),
    };
}
catch (ArgumentException ex)
{
    Console.Error.WriteLine("Error: " + ex.Message);
    return 2;
}
catch (Exception ex) when (ex is HttpRequestException or IOException or System.Xml.XmlException or JsonException)
{
    Console.Error.WriteLine("Error: " + ex.Message);
    return 1;
}

async Task<int> DetectAsync(Args cli)
{
    if (cli.Positional.Count == 0)
    {
        throw new ArgumentException("usage: brcommerce detect <url-or-domain>... [--json] [--feeds all|candidates|none] [--no-robots]");
    }

    var options = new DetectorOptions
    {
        CheckRobotsTxt = !cli.Has("no-robots"),
        FeedProbes = cli.Get("feeds") switch
        {
            null or "all" => FeedProbeMode.All,
            "candidates" => FeedProbeMode.CandidatesOnly,
            "none" => FeedProbeMode.None,
            var other => throw new ArgumentException($"--feeds must be all, candidates or none (got '{other}')."),
        },
    };

    if (cli.Get("rules") is { } rulesPath)
    {
        options.Rules = PlatformRuleSet.FromJson(await File.ReadAllTextAsync(rulesPath));
    }

    using var detector = new PlatformDetector(options);
    var results = new List<DetectionResult>();
    foreach (var target in cli.Positional)
    {
        var result = await detector.DetectAsync(target);
        results.Add(result);
        if (!cli.Has("json"))
        {
            PrintDetection(result);
        }
    }

    if (cli.Has("json"))
    {
        Console.WriteLine(JsonSerializer.Serialize(results.Count == 1 ? (object)results[0] : results, json));
    }

    return 0;
}

void PrintDetection(DetectionResult r)
{
    Console.WriteLine(r.RequestedUrl);
    if (r.Error is not null)
    {
        Console.WriteLine($"  error:     {r.Error}");
        Console.WriteLine();
        return;
    }

    Console.WriteLine($"  status:    {r.StatusCode}{(r.Redirects.Count > 0 ? $" (after {r.Redirects.Count} redirect(s) to {r.FinalUrl})" : string.Empty)}");
    Console.WriteLine($"  name:      {r.StoreName?.Value ?? "-"}{(r.StoreName is null ? string.Empty : $"  [{r.StoreName.Source}]")}");
    Console.WriteLine($"  platform:  {(r.Platform is null ? "not identified" : $"{r.Platform.Name} ({Percent(r.Platform.Score)})")}");
    foreach (var candidate in r.Candidates.Where(c => c != r.Platform))
    {
        Console.WriteLine($"  also:      {candidate.Name} ({Percent(candidate.Score)})");
    }

    foreach (var flag in r.Flags)
    {
        Console.WriteLine($"  flag:      {flag.Name}");
    }

    foreach (var feed in r.Feeds)
    {
        Console.WriteLine($"  feed:      {feed.Url}");
    }

    if (r.Platform is not null)
    {
        foreach (var evidence in r.Platform.Evidence)
        {
            Console.WriteLine($"  evidence:  {evidence}");
        }
    }

    Console.WriteLine();
}

async Task<int> InspectAsync(Args cli)
{
    var source = cli.Positional.FirstOrDefault() ?? throw new ArgumentException("usage: brcommerce feed inspect <path-or-url> [--max-items N] [--json]");
    await using var stream = await FeedSource.OpenAsync(source);
    var report = FeedInspector.Inspect(stream, maxItems: cli.GetInt("max-items"));

    if (cli.Has("json"))
    {
        Console.WriteLine(JsonSerializer.Serialize(report, json));
        return 0;
    }

    Console.WriteLine($"items: {report.ItemCount} <{report.ItemElement ?? "?"}>");
    Console.WriteLine();
    Console.WriteLine($"{"field",-32} {"items",8} {"max len",8}  sample");
    foreach (var field in report.Fields)
    {
        var sample = field.Samples.FirstOrDefault() ?? string.Empty;
        if (sample.Length > 60)
        {
            sample = sample[..57] + "...";
        }

        Console.WriteLine($"{field.Name,-32} {field.ItemsWithField,8} {field.MaxLength,8}  {sample.ReplaceLineEndings(" ")}");
    }

    return 0;
}

async Task<int> ReadFeedAsync(Args cli)
{
    var source = cli.Positional.FirstOrDefault() ?? throw new ArgumentException("usage: brcommerce feed read <path-or-url> [--limit N] [--format jsonl|csv] [--currency BRL]");
    await using var stream = await FeedSource.OpenAsync(source);
    var options = new FeedReaderOptions { DefaultCurrency = cli.Get("currency") ?? "BRL" };
    WriteProducts(ProductFeedReader.Read(stream, options), cli);
    return 0;
}

async Task<int> ShopifyAsync(Args cli)
{
    var store = cli.Positional.FirstOrDefault() ?? throw new ArgumentException("usage: brcommerce shopify <store-url> [--max-pages N] [--limit N] [--format jsonl|csv]");
    using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
    var reader = new ShopifyCatalogReader(http, new ShopifyReaderOptions
    {
        MaxPages = cli.GetInt("max-pages") ?? 200,
        Currency = cli.Get("currency") ?? "BRL",
    });

    var products = new List<Product>();
    var limit = cli.GetInt("limit");
    await foreach (var product in reader.ReadAsync(PlatformDetector.NormalizeUrl(store)))
    {
        products.Add(product);
        if (limit is not null && products.Count >= limit)
        {
            break;
        }
    }

    WriteProducts(products, cli);
    return 0;
}

void WriteProducts(IEnumerable<Product> products, Args cli)
{
    var limit = cli.GetInt("limit");
    var format = cli.Get("format") ?? "jsonl";
    if (format == "csv")
    {
        Console.WriteLine(Csv.Line(["id", "title", "brand", "gtin", "sku", "price", "sale_price", "old_price", "effective_price", "currency", "availability", "category", "link", "image_link"]));
    }
    else if (format != "jsonl")
    {
        throw new ArgumentException("--format must be jsonl or csv.");
    }

    var count = 0;
    foreach (var p in products)
    {
        if (format == "csv")
        {
            Console.WriteLine(Csv.Line([
                p.Id, p.Title, p.Brand, p.Gtin, p.Sku,
                Amount(p.Price), Amount(p.SalePrice), Amount(p.OldPrice), Amount(p.EffectivePrice),
                p.EffectivePrice?.Currency, p.Availability.ToString(), p.Category, p.Link, p.ImageLink,
            ]));
        }
        else
        {
            Console.WriteLine(JsonSerializer.Serialize(new
            {
                p.Id, p.Title, p.Brand, p.Gtin, p.Sku, p.Mpn,
                Price = p.Price?.Amount, SalePrice = p.SalePrice?.Amount, OldPrice = p.OldPrice?.Amount,
                EffectivePrice = p.EffectivePrice?.Amount, Currency = p.EffectivePrice?.Currency,
                p.Availability, p.Category, p.ProductType, p.GoogleProductCategory, p.Link, p.ImageLink, p.ItemGroupId,
            }, jsonLine));
        }

        if (limit is not null && ++count >= limit)
        {
            break;
        }
    }
}

static string Percent(double score) => (score * 100).ToString("0", System.Globalization.CultureInfo.InvariantCulture) + "%";

static string? Amount(Money? money) => money?.Amount.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture);

int Quality(Args cli)
{
    var path = cli.Positional.FirstOrDefault()
        ?? throw new ArgumentException("usage: brcommerce quality <catalog.csv> [--json]  (columns: id,name,brand,gtin,image_hash,source)");

    using var reader = new StreamReader(path);
    var rows = Csv.Read(reader);
    if (rows.Count < 2)
    {
        throw new ArgumentException("The CSV needs a header and at least one row.");
    }

    var header = rows[0].Select(h => h.Trim().ToLowerInvariant()).ToList();
    int Col(params string[] names) => names.Select(n => header.IndexOf(n)).FirstOrDefault(i => i >= 0, -1);
    var (id, name, brand, gtin, image, src) = (Col("id"), Col("name", "title", "nome"), Col("brand", "marca"), Col("gtin", "ean"), Col("image_hash", "hash"), Col("source", "loja"));
    if (id < 0 || name < 0)
    {
        throw new ArgumentException("The CSV needs at least the columns 'id' and 'name'.");
    }

    string? At(string[] row, int index) => index >= 0 && index < row.Length && row[index].Length > 0 ? row[index] : null;
    var items = rows.Skip(1)
        .Select(r => new CatalogItem(At(r, id) ?? string.Empty, At(r, name) ?? string.Empty, At(r, brand), At(r, gtin), At(r, image), At(r, src)))
        .ToList();

    var invalid = CatalogQualityAnalyzer.FindInvalidGtins(items);
    var shared = CatalogQualityAnalyzer.FindSharedIdentifiers(items);
    var placeholders = CatalogQualityAnalyzer.FindPlaceholderImages(items);

    if (cli.Has("json"))
    {
        Console.WriteLine(JsonSerializer.Serialize(new { Products = items.Count, InvalidGtins = invalid, SharedIdentifiers = shared, PlaceholderImages = placeholders }, json));
        return 0;
    }

    Console.WriteLine($"products:            {items.Count}");
    Console.WriteLine($"invalid GTINs:       {invalid.Count}");
    Console.WriteLine($"shared identifiers:  {shared.Count}");
    Console.WriteLine($"placeholder images:  {placeholders.Count}");
    foreach (var (title, list) in new[] { ("Shared identifiers", shared), ("Placeholder images", placeholders) })
    {
        if (list.Count == 0)
        {
            continue;
        }

        Console.WriteLine();
        Console.WriteLine(title);
        foreach (var s in list.Take(50))
        {
            Console.WriteLine($"  {s.Value}  ({s.Products} products) - {string.Join("; ", s.Reasons)}");
        }
    }

    return 0;
}

int Rules()
{
    foreach (var rule in PlatformRuleSet.Default.Platforms)
    {
        var feeds = rule.Feeds.Count + rule.FeedTemplates.Count;
        Console.WriteLine($"{rule.Id,-15} {rule.Name,-26} {rule.Signatures.Count} signature(s), {feeds} feed path(s)");
    }

    return 0;
}

static int Print(string text)
{
    Console.WriteLine(text);
    return 0;
}

static int Help()
{
    Console.WriteLine("""
        brcommerce - e-commerce toolkit for Brazilian stores

        Usage:
          brcommerce detect <url-or-domain>... [--json] [--feeds all|candidates|none] [--no-robots] [--rules file.json]
          brcommerce feed inspect <path-or-url> [--max-items N] [--json]
          brcommerce feed read <path-or-url> [--limit N] [--format jsonl|csv] [--currency BRL]
          brcommerce shopify <store-url> [--max-pages N] [--limit N] [--format jsonl|csv]
          brcommerce quality <catalog.csv> [--json]
          brcommerce rules
          brcommerce version

        Be a good citizen: the tool identifies itself in the User-Agent and pauses between requests.
        Respect robots.txt and the terms of use of the sites you analyse.
        """);
    return 0;
}
