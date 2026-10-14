namespace BrCommerce.Feeds;

/// <summary>Statistics about one field of a feed.</summary>
/// <param name="Name">Field name (namespace prefix removed).</param>
/// <param name="ItemsWithField">Items where the field appears at least once.</param>
/// <param name="Occurrences">Total occurrences (a field may repeat inside an item).</param>
/// <param name="MaxLength">Longest value, in characters. Useful to size database columns.</param>
/// <param name="Samples">Up to three distinct example values.</param>
public sealed record FieldStats(string Name, int ItemsWithField, int Occurrences, int MaxLength, IReadOnlyList<string> Samples);

/// <summary>What a feed contains.</summary>
/// <param name="ItemElement">Element that delimits items (<c>item</c>, <c>entry</c>, <c>product</c>, <c>produto</c>), or null when none was found.</param>
/// <param name="ItemCount">Number of items.</param>
/// <param name="Fields">Statistics per field, most frequent first.</param>
public sealed record FeedReport(string? ItemElement, int ItemCount, IReadOnlyList<FieldStats> Fields);

/// <summary>
/// Profiles a feed: item count, which fields exist, how often and how long they are.
/// Run it on an unknown feed before mapping it.
/// </summary>
public static class FeedInspector
{
    /// <summary>Profiles the feed in <paramref name="stream"/>.</summary>
    /// <param name="stream">The feed. Not disposed.</param>
    /// <param name="options">Options; null = defaults.</param>
    /// <param name="maxItems">Stop after this many items (null = read all).</param>
    public static FeedReport Inspect(Stream stream, FeedReaderOptions? options = null, int? maxItems = null)
    {
        var stats = new Dictionary<string, Accumulator>(StringComparer.OrdinalIgnoreCase);
        string? element = null;
        var count = 0;

        foreach (var item in RawFeedReader.Read(stream, options))
        {
            element ??= item.ElementName;
            count++;

            foreach (var (name, values) in item.Fields)
            {
                if (!stats.TryGetValue(name, out var acc))
                {
                    acc = new Accumulator();
                    stats[name] = acc;
                }

                acc.Items++;
                acc.Occurrences += values.Count;
                foreach (var value in values)
                {
                    acc.MaxLength = Math.Max(acc.MaxLength, value.Length);
                    if (acc.Samples.Count < 3 && !acc.Samples.Contains(value))
                    {
                        acc.Samples.Add(value.Length > 120 ? value[..117] + "..." : value);
                    }
                }
            }

            if (maxItems is not null && count >= maxItems)
            {
                break;
            }
        }

        var fields = stats
            .Select(p => new FieldStats(p.Key, p.Value.Items, p.Value.Occurrences, p.Value.MaxLength, p.Value.Samples))
            .OrderByDescending(s => s.ItemsWithField)
            .ThenBy(s => s.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new FeedReport(element, count, fields);
    }

    private sealed class Accumulator
    {
        public int Items { get; set; }
        public int Occurrences { get; set; }
        public int MaxLength { get; set; }
        public List<string> Samples { get; } = [];
    }
}
