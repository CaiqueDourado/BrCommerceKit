using System.Xml;

namespace BrCommerce.Feeds;

/// <summary>Options for reading XML feeds.</summary>
public sealed class FeedReaderOptions
{
    /// <summary>
    /// Element names that represent one product, tried in this order. The first one found in the document wins.
    /// Default: <c>item</c> (RSS), <c>entry</c> (Atom), <c>product</c>, <c>produto</c>.
    /// </summary>
    public IReadOnlyList<string> ItemElementNames { get; set; } = ["item", "entry", "product", "produto"];

    /// <summary>Currency assumed when a price has none. Default: <c>BRL</c>.</summary>
    public string? DefaultCurrency { get; set; } = "BRL";

    /// <summary>Decode HTML entities and collapse whitespace in title and description. Default: true.</summary>
    public bool CleanText { get; set; } = true;
}

/// <summary>One item of a feed: field name (namespace prefix removed) and all its values, in order.</summary>
/// <param name="ElementName">The element that delimited the item (e.g. <c>item</c>).</param>
/// <param name="Fields">Field values. Nested elements are flattened as <c>parent.child</c>.</param>
public sealed record RawFeedItem(string ElementName, IReadOnlyDictionary<string, IReadOnlyList<string>> Fields);

/// <summary>
/// Streams the items of an XML feed without loading the whole document. Tolerates the problems common in real
/// feeds: undeclared namespace prefixes (<c>g:price</c> without <c>xmlns:g</c>), DTDs, invalid characters.
/// </summary>
public static class RawFeedReader
{
    /// <summary>Reads the items of the feed in <paramref name="stream"/>.</summary>
    /// <param name="stream">The feed. Not disposed.</param>
    /// <param name="options">Options; null = defaults.</param>
    public static IEnumerable<RawFeedItem> Read(Stream stream, FeedReaderOptions? options = null)
    {
        options ??= new FeedReaderOptions();
        var itemNames = new HashSet<string>(options.ItemElementNames, StringComparer.OrdinalIgnoreCase);

        using var reader = CreateReader(stream);
        string? itemElement = null;

        while (reader.Read())
        {
            if (reader.NodeType != XmlNodeType.Element)
            {
                continue;
            }

            var name = LocalName(reader.Name);
            if (itemElement is null)
            {
                if (!itemNames.Contains(name))
                {
                    continue;
                }

                itemElement = name;
            }
            else if (!string.Equals(name, itemElement, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (reader.IsEmptyElement)
            {
                continue;
            }

            yield return ReadItem(reader, itemElement);
        }
    }

    private static RawFeedItem ReadItem(XmlReader reader, string itemElement)
    {
        var fields = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        var itemDepth = reader.Depth;
        var stack = new Stack<OpenElement>();

        while (reader.Read())
        {
            switch (reader.NodeType)
            {
                case XmlNodeType.Element:
                {
                    var name = LocalName(reader.Name);
                    var key = stack.Count == 0 ? name : stack.Peek().Key + "." + name;
                    var href = reader.GetAttribute("href");

                    if (stack.Count > 0)
                    {
                        stack.Peek().HasChildElements = true;
                    }

                    if (reader.IsEmptyElement)
                    {
                        // Atom-style <link href="..." rel="alternate"/>
                        if (!string.IsNullOrWhiteSpace(href))
                        {
                            Add(fields, key, href.Trim());
                        }
                    }
                    else
                    {
                        stack.Push(new OpenElement(key, href));
                    }

                    break;
                }

                case XmlNodeType.Text:
                case XmlNodeType.CDATA:
                case XmlNodeType.SignificantWhitespace:
                    if (stack.Count > 0)
                    {
                        stack.Peek().Text.Append(reader.Value);
                    }

                    break;

                case XmlNodeType.EndElement:
                    if (reader.Depth == itemDepth || stack.Count == 0)
                    {
                        return Build(itemElement, fields);
                    }

                    var closed = stack.Pop();
                    if (!closed.HasChildElements)
                    {
                        var value = closed.Text.ToString().Trim();
                        if (value.Length == 0 && !string.IsNullOrWhiteSpace(closed.Href))
                        {
                            value = closed.Href.Trim();
                        }

                        if (value.Length > 0)
                        {
                            Add(fields, closed.Key, value);
                        }
                    }

                    break;
            }
        }

        return Build(itemElement, fields);
    }

    private static RawFeedItem Build(string itemElement, Dictionary<string, List<string>> fields) =>
        new(itemElement, fields.ToDictionary(p => p.Key, p => (IReadOnlyList<string>)p.Value, StringComparer.OrdinalIgnoreCase));

    private sealed class OpenElement(string key, string? href)
    {
        public string Key { get; } = key;
        public string? Href { get; } = href;
        public System.Text.StringBuilder Text { get; } = new();
        public bool HasChildElements { get; set; }
    }

    internal static XmlReader CreateReader(Stream stream)
    {
#pragma warning disable SYSLIB0026 // XmlTextReader is needed to read undeclared namespace prefixes.
        var reader = new XmlTextReader(stream)
        {
            Namespaces = false,
            DtdProcessing = DtdProcessing.Ignore,
            XmlResolver = null,
            WhitespaceHandling = WhitespaceHandling.Significant,
            Normalization = false,
        };
#pragma warning restore SYSLIB0026
        return reader;
    }

    /// <summary>Removes a namespace prefix: <c>g:price</c> becomes <c>price</c>.</summary>
    /// <param name="qualifiedName">Element name as written.</param>
    public static string LocalName(string qualifiedName)
    {
        var colon = qualifiedName.IndexOf(':', StringComparison.Ordinal);
        return colon >= 0 ? qualifiedName[(colon + 1)..] : qualifiedName;
    }

    private static void Add(Dictionary<string, List<string>> fields, string key, string value)
    {
        if (!fields.TryGetValue(key, out var list))
        {
            list = [];
            fields[key] = list;
        }

        list.Add(value);
    }
}
