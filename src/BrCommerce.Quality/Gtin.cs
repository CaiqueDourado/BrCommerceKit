namespace BrCommerce.Quality;

/// <summary>GTIN family of a code.</summary>
public enum GtinKind
{
    /// <summary>Not a GTIN length.</summary>
    None,

    /// <summary>EAN-8.</summary>
    Gtin8,

    /// <summary>UPC-A.</summary>
    Gtin12,

    /// <summary>EAN-13 (and ISBN-13).</summary>
    Gtin13,

    /// <summary>GTIN-14 / DUN-14.</summary>
    Gtin14,
}

/// <summary>GTIN / EAN / UPC / ISBN-13 validation (GS1 mod-10 check digit).</summary>
public static class Gtin
{
    /// <summary>Removes spaces, dashes and dots; returns null when anything other than digits remains.</summary>
    /// <param name="value">Raw code as typed in a feed.</param>
    public static string? Clean(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        Span<char> buffer = stackalloc char[Math.Min(value.Length, 64)];
        var length = 0;
        foreach (var c in value)
        {
            if (c is ' ' or '-' or '.' or '\t')
            {
                continue;
            }

            if (!char.IsAsciiDigit(c) || length == buffer.Length)
            {
                return null;
            }

            buffer[length++] = c;
        }

        return length == 0 ? null : new string(buffer[..length]);
    }

    /// <summary>The GTIN family by length, after <see cref="Clean"/>.</summary>
    /// <param name="value">Raw code.</param>
    public static GtinKind GetKind(string? value) => Clean(value)?.Length switch
    {
        8 => GtinKind.Gtin8,
        12 => GtinKind.Gtin12,
        13 => GtinKind.Gtin13,
        14 => GtinKind.Gtin14,
        _ => GtinKind.None,
    };

    /// <summary>True when the code has a GTIN length, a correct check digit and is not a run of one digit (0000000000000).</summary>
    /// <param name="value">Raw code.</param>
    public static bool IsValid(string? value)
    {
        var digits = Clean(value);
        if (digits is null || GetKind(digits) == GtinKind.None)
        {
            return false;
        }

        if (digits.All(c => c == digits[0]))
        {
            return false;
        }

        return ComputeCheckDigit(digits.AsSpan(0, digits.Length - 1)) == digits[^1] - '0';
    }

    /// <summary>Computes the GS1 check digit for the digits that precede it.</summary>
    /// <param name="digitsWithoutCheck">7, 11, 12 or 13 digits.</param>
    public static int ComputeCheckDigit(ReadOnlySpan<char> digitsWithoutCheck)
    {
        var sum = 0;
        for (var i = 0; i < digitsWithoutCheck.Length; i++)
        {
            // Weights alternate 3,1,3,1... starting from the digit next to the check digit.
            var digit = digitsWithoutCheck[digitsWithoutCheck.Length - 1 - i] - '0';
            sum += digit * (i % 2 == 0 ? 3 : 1);
        }

        return (10 - (sum % 10)) % 10;
    }

    /// <summary>Returns the code as GTIN-14 (left-padded with zeros) when valid; otherwise null.</summary>
    /// <param name="value">Raw code.</param>
    public static string? ToGtin14(string? value) => IsValid(value) ? Clean(value)!.PadLeft(14, '0') : null;
}
