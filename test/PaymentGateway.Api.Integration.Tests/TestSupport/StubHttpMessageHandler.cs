using System.Net;
using System.Text;

namespace PaymentGateway.Api.Integration.Tests.TestSupport;

/// <summary>Stands in for the bank at the HTTP level. Counts calls and answers with a swappable responder.</summary>
internal sealed class StubHttpMessageHandler : HttpMessageHandler
{
    private int _callCount;

    public StubHttpMessageHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> responder)
    {
        Responder = responder;
    }

    public Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> Responder { get; set; }

    public int CallCount => Volatile.Read(ref _callCount);

    public static StubHttpMessageHandler Returning(HttpStatusCode statusCode, string? json = null) =>
        new((_, _) => Task.FromResult(Response(statusCode, json)));

    public static HttpResponseMessage Response(HttpStatusCode statusCode, string? json = null) =>
        new(statusCode)
        {
            Content = json is null ? null : new StringContent(json, Encoding.UTF8, "application/json")
        };

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _callCount);
        return Responder(request, cancellationToken);
    }
}
