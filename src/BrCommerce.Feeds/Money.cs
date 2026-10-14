using System.Globalization;
using System.Text.RegularExpressions;

namespace BrCommerce.Feeds;

/// <summary>An amount and an optional ISO 4217 currency code.</summary>
/// <param name="Amount">The amount.</param>
/// <param name="Currency">ISO code such as <c>BRL</c>, or null when the source did not say.</param>
public readonly record struct Money(decimal Amount, string? Currency)
{
    /// <inheritdoc />
    public override string ToString() =>
        Currency is null
            ? Amount.ToString("0.00", CultureInfo.InvariantCulture)
            : Amount.ToString("0.00", CultureInfo.InvariantCulture) + " " + Currency;
}

/// <summary>
/// Parses prices written in any of the ways stores write them: <c>R$ 1.299,90</c>, <c>1299.90 BRL</c>,
/// <c>1,299.90</c>, <c>US$ 10</c>, <c>ARS 15.000</c>.
/// </summary>
public static class PriceParser
{
    private static readonly (string Token, string Code)[] Currencies =
    [
        // Longer tokens first: "AR$" must not be read as "R$".
        ("US$", "USD"), ("AR$", "ARS"), ("CLP$", "CLP"), ("MX$", "MXN"), ("U$", "USD"), ("R$", "BRL"), ("S/.", "PEN"), ("S/", "PEN"),
        ("€", "EUR"), ("£", "GBP"),
        ("BRL", "BRL"), ("USD", "USD"), ("ARS", "ARS"), ("COP", "COP"), ("MXN", "MXN"), ("CLP", "CLP"), ("PEN", "PEN"),
        ("UYU", "UYU"), ("PYG", "PYG"), ("EUR", "EUR"), ("GBP", "GBP"),
    ];

    private static readonly Regex Number = new("[0-9][0-9.,\\s]*", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

    /// <summary>Parses <paramref name="text"/>; returns null when it holds no number.</summary>
    /// <param name="text">The price text.</param>
    /// <param name="defaultCurrency">Currency to assume when the text has none (e.g. <c>BRL</c> for a Brazilian feed).</param>
    public static Money? Parse(string? text, string? defaultCurrency = null)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var currency = DetectCurrency(text) ?? defaultCurrency;
        var match = Number.Match(text);
        if (!match.Success)
        {
            return null;
        }

        var amount = ParseNumber(match.Value.Replace(" ", string.Empty, StringComparison.Ordinal).Trim('.', ','), currency);
        return amount is null ? null : new Money(amount.Value, currency);
    }

    /// <summary>
    /// Parses the numeric part. Rules: when both '.' and ',' appear, the last one is the decimal separator.
    /// When only one appears and it is followed by exactly three digits, it is a thousands separator
    /// (<c>1.299</c> = 1299), except for <c>USD</c>/<c>GBP</c>/<c>MXN</c> with ',' followed by three digits.
    /// Otherwise it is the decimal separator.
    /// </summary>
    /// <param name="digits">Digits with separators, no currency.</param>
    /// <param name="currency">Currency, used only to break ties.</param>
    public static decimal? ParseNumber(string digits, string? currency = null)
    {
        if (string.IsNullOrEmpty(digits))
        {
            return null;
        }

        var lastDot = digits.LastIndexOf('.');
        var lastComma = digits.LastIndexOf(',');
        string normalized;

        if (lastDot >= 0 && lastComma >= 0)
        {
            normalized = lastComma > lastDot
                ? digits.Replace(".", string.Empty, StringComparison.Ordinal).Replace(',', '.')
                : digits.Replace(",", string.Empty, StringComparison.Ordinal);
        }
        else if (lastDot >= 0 || lastComma >= 0)
        {
            var separator = lastDot >= 0 ? '.' : ',';
            var occurrences = digits.Count(c => c == separator);
            var decimals = digits.Length - digits.LastIndexOf(separator) - 1;
            var dotDecimalCurrency = currency is "USD" or "GBP" or "MXN";

            var isThousands = occurrences > 1
                || (decimals == 3 && !(separator == '.' && dotDecimalCurrency));

            normalized = isThousands
                ? digits.Replace(separator.ToString(), string.Empty, StringComparison.Ordinal)
                : digits.Replace(',', '.');
        }
        else
        {
            normalized = digits;
        }

        return decimal.TryParse(normalized, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var value)
            ? value
            : null;
    }

    private static string? DetectCurrency(string text)
    {
        foreach (var (token, code) in Currencies)
        {
            if (text.Contains(token, StringComparison.OrdinalIgnoreCase))
            {
                return code;
            }
        }

        return null;
    }
}
