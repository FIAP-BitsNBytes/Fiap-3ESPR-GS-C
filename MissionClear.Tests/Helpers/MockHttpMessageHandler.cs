using System.Net;
using System.Text;

namespace MissionClear.Tests.Helpers;

/// <summary>
/// Configurable HTTP handler for unit testing services that use IHttpClientFactory.
/// </summary>
public sealed class MockHttpMessageHandler : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, HttpResponseMessage> _handler;

    public MockHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> handler)
        => _handler = handler;

    /// <summary>Returns JSON body with given status code.</summary>
    public static MockHttpMessageHandler Json(string json, HttpStatusCode status = HttpStatusCode.OK)
        => new(_ => new HttpResponseMessage(status)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        });

    /// <summary>Returns plain text body (e.g. TLE format) with given status code.</summary>
    public static MockHttpMessageHandler PlainText(string text, HttpStatusCode status = HttpStatusCode.OK)
        => new(_ => new HttpResponseMessage(status)
        {
            Content = new StringContent(text, Encoding.UTF8, "text/plain"),
        });

    /// <summary>Returns empty body with given status code.</summary>
    public static MockHttpMessageHandler Status(HttpStatusCode status)
        => new(_ => new HttpResponseMessage(status));

    /// <summary>Throws the given exception when called (simulates network error).</summary>
    public static MockHttpMessageHandler Throws(Exception ex)
        => new(_ => throw ex);

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken ct)
        => Task.FromResult(_handler(request));
}
