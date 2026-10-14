using System.Globalization;

namespace BrCommerce.Cli;

/// <summary>Tiny argument parser: <c>command [sub] positional... --flag --key value</c>.</summary>
internal sealed class Args
{
    private static readonly HashSet<string> CommandsWithSub = ["feed"];
    private static readonly HashSet<string> Switches = ["json", "no-robots", "help"];

    private readonly Dictionary<string, string?> _options = new(StringComparer.OrdinalIgnoreCase);

    public string Command { get; private set; } = "help";

    public string? Sub { get; private set; }

    public List<string> Positional { get; } = [];

    public static Args Parse(string[] args)
    {
        var result = new Args();
        var queue = new Queue<string>(args);
        if (queue.Count > 0)
        {
            result.Command = queue.Dequeue().ToLowerInvariant();
        }

        if (CommandsWithSub.Contains(result.Command) && queue.Count > 0)
        {
            result.Sub = queue.Dequeue().ToLowerInvariant();
        }

        while (queue.Count > 0)
        {
            var token = queue.Dequeue();
            if (token.StartsWith("--", StringComparison.Ordinal))
            {
                var key = token[2..];
                string? value = null;
                var eq = key.IndexOf('=', StringComparison.Ordinal);
                if (eq >= 0)
                {
                    value = key[(eq + 1)..];
                    key = key[..eq];
                }
                else if (!Switches.Contains(key) && queue.Count > 0 && !queue.Peek().StartsWith("--", StringComparison.Ordinal))
                {
                    value = queue.Dequeue();
                }

                result._options[key] = value;
            }
            else
            {
                result.Positional.Add(token);
            }
        }

        if (result._options.ContainsKey("help"))
        {
            result.Command = "help";
        }

        return result;
    }

    public bool Has(string key) => _options.ContainsKey(key);

    public string? Get(string key) => _options.TryGetValue(key, out var value) ? value : null;

    public int? GetInt(string key)
    {
        var value = Get(key);
        if (value is null)
        {
            return null;
        }

        return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number) && number > 0
            ? number
            : throw new ArgumentException($"--{key} must be a positive integer.");
    }
}
