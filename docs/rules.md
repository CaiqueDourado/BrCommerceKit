# Platform rules

Detection is driven by [`src/BrCommerce.Detect/Rules/rules.json`](../src/BrCommerce.Detect/Rules/rules.json). You can also pass your own file: `PlatformRuleSet.FromJson(json)` in code, or `brcommerce detect --rules my-rules.json`.

## Format

```json
{
  "platforms": [
    {
      "id": "vtex",
      "name": "VTEX",
      "signatures": [
        { "pattern": "vteximg\\.com\\.br", "weight": 1.0 },
        { "pattern": "\\bvtex\\b", "weight": 0.6 }
      ],
      "feeds": [
        { "path": "XMLData/googleshopping.xml", "mustContain": "CDATA" }
      ],
      "feedTemplates": [
        {
          "extract": "shopid:\\s*['\"]?([A-Za-z0-9_-]+)",
          "url": "https://www.mercadoshops.com.br/darwin/rss/{0}/gshopping",
          "mustContain": "<rss"
        }
      ]
    }
  ],
  "flags": [ { "id": "suspended", "name": "...", "signatures": [ ... ] } ]
}
```

| Field | Meaning |
| --- | --- |
| `signatures[].pattern` | .NET regular expression, case-insensitive, applied to the home page HTML, to `robots.txt` and (for flags) to the final URL. Use `\\b` around short words. |
| `signatures[].weight` | 0 to 1. The platform score is the highest weight among matching signatures. |
| `feeds[].path` | Path relative to the store root. |
| `feeds[].mustContain` | Text that must appear in the first 64 KB of the response (case-insensitive). |
| `feedTemplates` | For feeds whose URL depends on a value found in the home page. `extract` must have one capture group; it replaces `{0}` in `url`. |
| `flags` | Facts that are not platforms (suspended store, WordPress, payment seals). Same format, no feeds. |

## Choosing weights

| Weight | Use for | Example |
| --- | --- | --- |
| 1.0 | Something only that platform produces | its CDN host, a fixed comment in `robots.txt` |
| 0.7–0.8 | The platform's distinctive name | `\\bnuvem ?shop\\b`, `\\bprestashop\\b` |
| 0.3–0.4 | A short name that is also a common word | `\\btray\\b`, `\\bwix\\b`, `\\blinx\\b` |

The default threshold to report a platform is 0.5, so weak signals never decide alone. A verified feed always counts as 1.0.

## Adding a platform

1. Add the rule to `rules.json`.
2. Add a small HTML or `robots.txt` sample to `tests/BrCommerce.Tests/Fixtures` (remove anything that identifies a real store).
3. Add a test to `tests/BrCommerce.Tests/Detect/DetectTests.cs` and update the platform count in `Embedded_rules_load`.
4. Run `dotnet test`.
