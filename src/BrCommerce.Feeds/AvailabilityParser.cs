using System.Text;

namespace BrCommerce.Feeds;

/// <summary>Normalises availability values written in English, Portuguese, schema.org URLs or booleans.</summary>
public static class AvailabilityParser
{
    /// <summary>Parses an availability value.</summary>
    /// <param name="value">E.g. <c>in stock</c>, <c>disponível</c>, <c>https://schema.org/OutOfStock</c>, <c>true</c>, <c>0</c>.</param>
    public static Availability Parse(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return Availability.Unknown;
        }

        var key = Simplify(value);
        var slash = key.LastIndexOf('/');
        if (slash >= 0)
        {
            key = key[(slash + 1)..];
        }

        return key switch
        {
            "instock" or "disponivel" or "emestoque" or "available" or "sim" or "s" or "yes" or "y" or "true" or "1"
                or "limitedavailability" or "onlineonly" or "instoreonly" => Availability.InStock,
            "outofstock" or "indisponivel" or "esgotado" or "semestoque" or "soldout" or "nao" or "n" or "no" or "false" or "0"
                or "discontinued" or "foradeestoque" => Availability.OutOfStock,
            "preorder" or "prevenda" or "encomenda" or "presale" => Availability.Preorder,
            "backorder" or "sobencomenda" => Availability.Backorder,
            _ => Availability.Unknown,
        };
    }

    private static string Simplify(string value)
    {
        var lower = value.Trim().ToLowerInvariant();
        var builder = new StringBuilder(lower.Length);
        foreach (var original in lower)
        {
            var c = Diacritics.Strip(original);
            if (char.IsLetterOrDigit(c) || c == '/')
            {
                builder.Append(c);
            }
        }

        return builder.ToString();
    }
}
