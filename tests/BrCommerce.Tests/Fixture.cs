namespace BrCommerce.Tests;

internal static class Fixture
{
    public static string PathOf(string name) => Path.Combine(AppContext.BaseDirectory, "Fixtures", name);

    public static Stream Open(string name) => File.OpenRead(PathOf(name));

    public static string Text(string name) => File.ReadAllText(PathOf(name));
}
