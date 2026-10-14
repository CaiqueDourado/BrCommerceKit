# BrCommerceKit

[![CI](https://github.com/CaiqueDourado/BrCommerceKit/actions/workflows/ci.yml/badge.svg)](https://github.com/CaiqueDourado/BrCommerceKit/actions/workflows/ci.yml)
[![License: MIT](https://img.shields.io/badge/license-MIT-blue.svg)](LICENSE)
![.NET 8](https://img.shields.io/badge/.NET-8.0-512BD4)
![Dependencies: 0](https://img.shields.io/badge/dependencies-0-brightgreen)

**Toolkit for e-commerce data as it really exists in Brazil.** Detect which platform a store runs on and where its product feeds are, read those feeds whatever dialect they use, and find catalog problems that only show up across thousands of products.

[Leia em português](README.pt-BR.md)

```text
$ brcommerce detect lojaexemplo.com.br
https://lojaexemplo.com.br/
  status:    200
  name:      Loja Exemplo Moda  [og:site_name]
  platform:  VTEX (100%)
  feed:      https://lojaexemplo.com.br/XMLData/googleshopping.xml
  evidence:  html: "vteximg.com.br" (weight 1.0)
  evidence:  feed: https://lojaexemplo.com.br/XMLData/googleshopping.xml
```

## Why

Generic tools (Wappalyzer, BuiltWith) cover the big global platforms but miss most Brazilian ones — Loja Integrada, Nuvemshop, Tray, VNDA, Moovin, Wbuy, Iluria, Xtech, JET, E-com Club, EzCommerce, UOL Loja — and none of them **finds the product feed**. Feed parsers, in turn, expect clean Google Merchant XML, while real Brazilian feeds use `nome`, `preco_por`, `disponibilidade` and `ean`, repeat `<price>` and `<g:price>`, forget to declare `xmlns:g` and write prices as `R$ 1.299,90`.

This library comes from years of crawling and normalising catalogs of thousands of Brazilian stores. The rules and edge cases are field-tested; the code is a clean, tested rewrite.

## Packages

| Package | What it does |
| --- | --- |
| **BrCommerce.Detect** | Platform detection (20 platforms + flags such as "store suspended"), feed discovery on well-known paths, store name extraction. Rules live in a JSON file you can extend. |
| **BrCommerce.Feeds** | Streaming reader for Google Merchant RSS/Atom, Facebook catalog and Brazilian XML dialects; Shopify `products.json` reader with variants; price and availability parsing; feed profiler. |
| **BrCommerce.Quality** | GTIN/EAN check digit validation; identifiers shared by unrelated products; placeholder images ("sem foto") shared across brands. |
| **BrCommerce.Cli** | `brcommerce` command-line tool on top of the three libraries. |

No third-party dependencies: only the .NET base library.

## Install

Until the packages are published, clone and reference the projects, or build the CLI:

```bash
git clone https://github.com/CaiqueDourado/BrCommerceKit.git
cd BrCommerceKit
dotnet build -c Release
dotnet run --project src/BrCommerce.Cli -- detect lojaexemplo.com.br
```

Each release also attaches the `.nupkg` packages and the CLI to the [GitHub Releases](https://github.com/CaiqueDourado/BrCommerceKit/releases) page.

## Usage

### Detect a store

```csharp
using BrCommerce.Detect;

using var detector = new PlatformDetector();
var result = await detector.DetectAsync("lojaexemplo.com.br");

Console.WriteLine(result.StoreName?.Value);   // "Loja Exemplo Moda"
Console.WriteLine(result.Platform?.Name);     // "VTEX"
Console.WriteLine(result.Platform?.Score);    // 1.0 (a verified feed is conclusive)
foreach (var feed in result.Feeds)
    Console.WriteLine(feed.Url);              // ".../XMLData/googleshopping.xml"
```

How it decides:

1. Reads the home page (following and recording redirects, falling back to http if https fails) and `robots.txt`.
2. Applies weighted signatures: a platform CDN such as `vteximg.com.br` weighs 1.0; a short common word such as `tray` weighs 0.3 and never decides alone. All patterns use word boundaries.
3. Tries the well-known feed paths (`XMLData/googleshopping.xml`, `fb_catalog.xml/`, `products.json`, `media/amfeed/feeds/google.xml`, ...). A feed that answers and passes validation sets the score to 1.0. Soft-404 home pages are rejected.
4. Extracts the store name: `og:site_name` → `application-name` → `DC.title` → `<title>` (with heuristics for Xtech and JET titles) → "Razão Social" → `author`/`copyright`.

Supported platforms: VTEX, Nuvemshop, Shopify, Magento, WooCommerce, Loja Integrada, Tray, Wix, Mercado Shops, PrestaShop, UOL Loja, Iluria, Linx, EzCommerce/Lynx, Xtech, JET, E-com Club, VNDA, Moovin, Wbuy. Run `brcommerce rules` to list them. To add or improve one, see [docs/rules.md](docs/rules.md).

### Read a product feed

```csharp
using BrCommerce.Feeds;

await using var stream = await FeedSource.OpenAsync("https://lojaexemplo.com.br/XMLData/googleshopping.xml");
foreach (var p in ProductFeedReader.Read(stream))
{
    Console.WriteLine($"{p.Title} | {p.EffectivePrice} | {p.Availability} | {p.Gtin}");
}
```

- Streams item by item, so large feeds do not need to fit in memory. Gzip is detected automatically.
- Understands `item` (RSS), `entry` (Atom), `product` and `produto`; field names with or without `g:` or `google_`; Portuguese aliases (`nome`, `descricao`, `preco_de`, `preco_por`, `disponibilidade`, `marca`, `ean`, `link_produto`, `link_imagem`...). See `ProductFeedReader.Aliases`.
- `Price`, `SalePrice`, `OldPrice` are `Money` values; `EffectivePrice` is what the buyer pays.
- Every original field is still available in `Product.Fields`, nested elements flattened as `shipping.price`.

Prices are parsed with `PriceParser`:

| Input | Result |
| --- | --- |
| `R$ 1.299,90` | 1299.90 BRL |
| `1299.90 BRL` | 1299.90 BRL |
| `1,299.90 USD` | 1299.90 USD |
| `AR$ 15.000` | 15000 ARS |
| `1.299` | 1299 (a single separator followed by three digits is a thousands separator) |

### Profile an unknown feed

```bash
$ brcommerce feed inspect https://lojaexemplo.com.br/feed.xml
items: 2 <produto>

field                               items  max len  sample
disponibilidade                         2       10  Disponível
nome                                    2       31  Geladeira Frost Free 375 Litros
preco_por                               2       11  R$ 3.799,90
ean                                     1       13  7896004000855
```

### Dump a Shopify catalog

```csharp
using var http = new HttpClient();
var reader = new ShopifyCatalogReader(http);
await foreach (var p in reader.ReadAsync(new Uri("https://lojaexemplo.com.br")))
    Console.WriteLine($"{p.Title} {p.Price} {p.Link}");   // one Product per variant
```

### Audit a catalog

```csharp
using BrCommerce.Quality;

Gtin.IsValid("7891000315507");                                   // true
var shared = CatalogQualityAnalyzer.FindSharedIdentifiers(items);  // same EAN on a fridge and a sneaker
var placeholders = CatalogQualityAnalyzer.FindPlaceholderImages(items);
```

A real EAN or product photo belongs to one product. When the same value appears on products whose names start with different words and whose brands differ (generic brands such as "Importado" are ignored), it is almost certainly a placeholder. Thresholds are configurable in `QualityOptions`.

```bash
$ brcommerce quality catalog.csv      # columns: id,name,brand,gtin,image_hash,source
products:            6
invalid GTINs:       2
shared identifiers:  1
placeholder images:  1
```

## CLI reference

```text
brcommerce detect <url-or-domain>... [--json] [--feeds all|candidates|none] [--no-robots] [--rules file.json]
brcommerce feed inspect <path-or-url> [--max-items N] [--json]
brcommerce feed read <path-or-url> [--limit N] [--format jsonl|csv] [--currency BRL]
brcommerce shopify <store-url> [--max-pages N] [--limit N] [--format jsonl|csv]
brcommerce quality <catalog.csv> [--json]
brcommerce rules
```

## Responsible use

The tools identify themselves in the User-Agent, pause between requests and only read public pages and feeds. They do not rotate proxies or impersonate browsers. Respect `robots.txt`, rate limits and the terms of use of the sites you analyse.

## Contributing

New platforms, feed paths and real-world edge cases are very welcome — most contributions are a few lines in `rules.json` plus a test. See [CONTRIBUTING.md](CONTRIBUTING.md).

## License

[MIT](LICENSE) © Caique Dourado
