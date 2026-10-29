// One platform detection result as JSON, offline.
//
//   dotnet run --project tools/DetectExample -- tests/BrCommerce.Tests/Fixtures
//
// Runs PlatformDetector against an in-memory handler that answers like the test
// Detects_vtex_with_feed_and_follows_relative_redirect: the home page redirects to /home, which
// serves vtex-home.html, and XMLData/googleshopping.xml serves google-shopping-rss.xml. Every other
// URL (robots.txt, the other feed paths) gets 404. No network. The JSON uses the same serializer
// options as `brcommerce detect --json`.

using System.Net;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using BrCommerce.Detect;

var fixtures = args.Length > 0 ? args[0] : Path.Combine("tests", "BrCommerce.Tests", "Fixtures");
string Fixture(string name) => File.ReadAllText(Path.Combine(fixtures, name));

var pages = new Dictionary<string, Func<HttpResponseMessage>>
{
    ["https://loja-exemplo.com.br/"] = () => new HttpResponseMessage(HttpStatusCode.MovedPermanently)
    {
        Headers = { Location = new Uri("/home", UriKind.Relative) },
    },
    ["https://loja-exemplo.com.br/home"] = () => Ok(Fixture("vtex-home.html"), "text/html"),
    ["https://loja-exemplo.com.br/XMLData/googleshopping.xml"] = () => Ok(Fixture("google-shopping-rss.xml"), "application/xml"),
};

using var http = new HttpClient(new MapHandler(pages));
using var detector = new PlatformDetector(http, new DetectorOptions { DelayBetweenRequests = TimeSpan.Zero });
var result = await detector.DetectAsync("loja-exemplo.com.br");

var json = new JsonSerializerOptions
{
    WriteIndented = true,
    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    Converters = { new JsonStringEnumConverter() },
};
Console.OutputEncoding = Encoding.UTF8;
Console.WriteLine(JsonSerializer.Serialize(result, json));

static HttpResponseMessage Ok(string body, string contentType) =>
    new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, contentType) };

internal sealed class MapHandler(Dictionary<string, Func<HttpResponseMessage>> pages) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
        Task.FromResult(pages.TryGetValue(request.RequestUri!.ToString(), out var make)
            ? make()
            : new HttpResponseMessage(HttpStatusCode.NotFound) { Content = new StringContent("not found") });
}
