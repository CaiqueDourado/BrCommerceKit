# Changelog

All notable changes to this project are documented here. The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/) and the project uses [Semantic Versioning](https://semver.org/).

## [0.1.0] - 2026-10-14

### Added

- `BrCommerce.Detect`: platform detection for 20 platforms common in Brazil, flags (suspended store, WordPress, Confere Cartões), feed discovery on well-known paths and templated feeds (Mercado Shops), store name extraction, manual redirect handling with relative `Location`, http fallback, soft-404 protection, rules in JSON.
- `BrCommerce.Feeds`: streaming reader for RSS/Atom/`product`/`produto` feeds with Portuguese aliases and undeclared namespace prefixes; `PriceParser`; `AvailabilityParser`; `FeedInspector`; `ShopifyCatalogReader`; `FeedSource` with automatic gzip.
- `BrCommerce.Quality`: GTIN validation, shared identifier and placeholder image detection.
- `brcommerce` CLI: `detect`, `feed inspect`, `feed read`, `shopify`, `quality`, `rules`.
