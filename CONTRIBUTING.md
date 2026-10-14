# Contributing

Thanks for helping! Issues and pull requests are welcome in English or Portuguese.

## Getting started

```bash
git clone https://github.com/CaiqueDourado/BrCommerceKit.git
cd BrCommerceKit
dotnet test
```

Requirements: .NET 8 SDK or newer.

## Good first contributions

- **A platform that is missing or misdetected** — usually one rule in `rules.json` plus a test. See [docs/rules.md](docs/rules.md).
- **A feed dialect that is not understood** — add a small, anonymised sample to `tests/BrCommerce.Tests/Fixtures`, the alias to `ProductFeedReader.Aliases` and a test.
- **A price or availability format** — add it to the `InlineData` of `PriceParserTests` or `AvailabilityParserTests`.

## Guidelines

- Keep the libraries free of third-party dependencies.
- Tests must not touch the network: use `FakeHandler` and fixtures.
- Fixtures must not contain real customer data, real store credentials or copyrighted catalog content. Use invented names and `loja-exemplo.com.br`.
- The build treats warnings as errors; public members need XML documentation.
- One topic per pull request, with a short description of the real-world case behind it.

## Responsible use

Do not submit features whose purpose is to evade blocking (proxy rotation, browser impersonation, CAPTCHA solving) or to collect personal data.
