using System.Net;
using System.Text;

namespace BrCommerce.Tests;

/// <summary>An <see cref="HttpMessageHandler"/> that answers from a function and records requests. No network.</summary>
internal sealed class FakeHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
{
    public List<Uri> Requests { get; } = [];

    public static HttpResponseMessage Ok(string body, string contentType = "text/html") => new(HttpStatusCode.OK)
    {
        Content = new StringContent(body, Encoding.UTF8, contentType),
    };

    public static HttpResponseMessage NotFound() => new(HttpStatusCode.NotFound) { Content = new StringContent("not found") };

    public static HttpResponseMessage Redirect(string location, HttpStatusCode status = HttpStatusCode.MovedPermanently)
    {
        var response = new HttpResponseMessage(status);
        response.Headers.Location = new Uri(location, UriKind.RelativeOrAbsolute);
        return response;
    }

    /// <summary>Builds a handler from a map of absolute URL to response; anything else is 404.</summary>
    public static FakeHandler FromMap(Dictionary<string, Func<HttpResponseMessage>> map) =>
        new(request => map.TryGetValue(request.RequestUri!.ToString(), out var make) ? make() : NotFound());

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Requests.Add(request.RequestUri!);
        return Task.FromResult(respond(request));
    }
}
