using System.Net;
using System.Text;

namespace PaymentGateway.Api.Tests.TestSupport;

/// <summary>Records each request and answers with a configurable responder.</summary>
internal sealed class StubHttpMessageHandler : HttpMessageHandler
{
    private readonly List<RecordedRequest> _requests = new();

    public StubHttpMessageHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> responder)
    {
        Responder = responder;
    }

    public Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> Responder { get; set; }

    public IReadOnlyList<RecordedRequest> Requests
    {
        get { lock (_requests) { return _requests.ToList(); } }
    }

    public static StubHttpMessageHandler Returning(HttpStatusCode statusCode, string? json = null) =>
        new((_, _) => Task.FromResult(Response(statusCode, json)));

    public static StubHttpMessageHandler Throwing(Exception exception) =>
        new((_, _) => Task.FromException<HttpResponseMessage>(exception));

    public static HttpResponseMessage Response(HttpStatusCode statusCode, string? json = null) =>
        new(statusCode)
        {
            Content = json is null ? null : new StringContent(json, Encoding.UTF8, "application/json")
        };

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
        lock (_requests)
        {
            _requests.Add(new RecordedRequest(request.Method, request.RequestUri, body));
        }

        return await Responder(request, cancellationToken);
    }

    internal sealed record RecordedRequest(HttpMethod Method, Uri? RequestUri, string? Body);
}
