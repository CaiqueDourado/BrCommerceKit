namespace BrCommerce.Quality;

/// <summary>
/// Removes accents from Latin letters without relying on ICU, so it behaves the same in apps that run with
/// <c>InvariantGlobalization</c> (where <c>string.Normalize</c> does not decompose characters).
/// </summary>
internal static class Diacritics
{
    private const string From = "ÀÁÂÃÄÅàáâãäåÇçÈÉÊËèéêëÌÍÎÏìíîïÑñÒÓÔÕÖØòóôõöøÙÚÛÜùúûüÝýÿŠšŽžŒœ";
    private const string To   = "AAAAAAaaaaaaCcEEEEeeeeIIIIiiiiNnOOOOOOooooooUUUUuuuuYyySsZzOo";

    public static char Strip(char c)
    {
        if (c < 128)
        {
            return c;
        }

        var index = From.IndexOf(c, StringComparison.Ordinal);
        return index >= 0 ? To[index] : c;
    }
}
