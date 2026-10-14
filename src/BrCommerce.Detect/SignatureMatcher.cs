namespace BrCommerce.Detect;

/// <summary>Applies the signatures of a rule set to a piece of text. Useful on its own for offline analysis.</summary>
public static class SignatureMatcher
{
    /// <summary>
    /// Scores every rule against <paramref name="text"/>. Each rule gets the highest weight among its matching signatures.
    /// </summary>
    /// <param name="text">HTML, robots.txt or URL.</param>
    /// <param name="rules">Rules to apply.</param>
    /// <param name="source">Label used in the evidence (e.g. <c>html</c>, <c>robots.txt</c>, <c>url</c>).</param>
    /// <returns>Matches with score above zero, highest first.</returns>
    public static IReadOnlyList<PlatformMatch> Match(string text, IEnumerable<PlatformRule> rules, string source)
    {
        var matches = new List<PlatformMatch>();
        if (string.IsNullOrEmpty(text))
        {
            return matches;
        }

        foreach (var rule in rules)
        {
            double best = 0;
            var evidence = new List<string>();
            foreach (var signature in rule.Signatures)
            {
                var hit = signature.Pattern.Match(text);
                if (!hit.Success)
                {
                    continue;
                }

                best = Math.Max(best, signature.Weight);
                evidence.Add($"{source}: \"{Snippet(hit.Value)}\" (weight {signature.Weight:0.0})");
            }

            if (best > 0)
            {
                matches.Add(new PlatformMatch(rule.Id, rule.Name, best, evidence));
            }
        }

        return matches.OrderByDescending(m => m.Score).ToList();
    }

    /// <summary>Merges matches from several sources: score = highest, evidence = union.</summary>
    /// <param name="matches">Matches to merge.</param>
    public static IReadOnlyList<PlatformMatch> Merge(IEnumerable<PlatformMatch> matches) =>
        matches
            .GroupBy(m => m.Id)
            .Select(g => new PlatformMatch(
                g.Key,
                g.First().Name,
                g.Max(m => m.Score),
                g.SelectMany(m => m.Evidence).Distinct().ToList()))
            .OrderByDescending(m => m.Score)
            .ThenBy(m => m.Id, StringComparer.Ordinal)
            .ToList();

    private static string Snippet(string value) => value.Length <= 60 ? value : value[..57] + "...";
}
