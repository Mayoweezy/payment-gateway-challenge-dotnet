using System.Net;
using System.Text.Json;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Logging.Testing;

using PaymentGateway.Api.Bank;
using PaymentGateway.Api.Tests.TestSupport;

using Polly;
using Polly.CircuitBreaker;
using Polly.Timeout;

namespace PaymentGateway.Api.Tests.Bank;

/// <summary>Contract tests for the bank's wire format and for how each bank answer is interpreted.</summary>
public class BankClientTests
{
    private const string AuthorizedJson = """{"authorized":true,"authorization_code":"0bb07405-6d44-4b50-a14f-7ae0beff13ad"}""";
    private const string DeclinedJson = """{"authorized":false,"authorization_code":""}""";

    // Request contract

    [Fact]
    public async Task Posts_snake_case_json_to_the_payments_endpoint()
    {
        var handler = StubHttpMessageHandler.Returning(HttpStatusCode.OK, AuthorizedJson);

        await CreateClient(handler).AuthorizeAsync(TestData.ValidPayment());

        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal(new Uri("http://bank.test/payments"), request.RequestUri);

        using var body = JsonDocument.Parse(request.Body!);
        var root = body.RootElement;
        Assert.Equal(TestData.CardNumber, root.GetProperty("card_number").GetString());
        Assert.Equal("04/2027", root.GetProperty("expiry_date").GetString());
        Assert.Equal("GBP", root.GetProperty("currency").GetString());
        Assert.Equal(100, root.GetProperty("amount").GetInt32());
        Assert.Equal(TestData.Cvv, root.GetProperty("cvv").GetString());
        Assert.Equal(5, root.EnumerateObject().Count());
    }

    [Theory]
    [InlineData(1, 2027, "01/2027")]
    [InlineData(12, 2030, "12/2030")]
    public async Task Formats_the_expiry_date_as_two_digit_month_and_four_digit_year(int month, int year, string expected)
    {
        var handler = StubHttpMessageHandler.Returning(HttpStatusCode.OK, AuthorizedJson);
        await CreateClient(handler).AuthorizeAsync(TestData.ValidPayment(month, year));

        using var body = JsonDocument.Parse(Assert.Single(handler.Requests).Body!);
        Assert.Equal(expected, body.RootElement.GetProperty("expiry_date").GetString());
    }

    // Response interpretation

    [Fact]
    public async Task An_authorized_response_returns_the_authorization_code()
    {
        var handler = StubHttpMessageHandler.Returning(HttpStatusCode.OK, AuthorizedJson);

        var result = await CreateClient(handler).AuthorizeAsync(TestData.ValidPayment());

        var authorized = Assert.IsType<BankResult.Authorized>(result);
        Assert.Equal("0bb07405-6d44-4b50-a14f-7ae0beff13ad", authorized.AuthorizationCode);
    }

    [Fact]
    public async Task An_unauthorized_response_is_declined()
    {
        var handler = StubHttpMessageHandler.Returning(HttpStatusCode.OK, DeclinedJson);

        var result = await CreateClient(handler).AuthorizeAsync(TestData.ValidPayment());

        Assert.IsType<BankResult.Declined>(result);
    }

    [Theory]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.RequestTimeout)]
    public async Task A_503_429_or_408_means_the_bank_is_unavailable(HttpStatusCode statusCode)
    {
        var handler = StubHttpMessageHandler.Returning(statusCode, "{}");

        var result = await CreateClient(handler).AuthorizeAsync(TestData.ValidPayment());

        Assert.IsType<BankResult.Unavailable>(result);
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.UnprocessableEntity)]
    public async Task A_4xx_means_the_bank_refused_the_payment_without_processing_it(HttpStatusCode statusCode)
    {
        var handler = StubHttpMessageHandler.Returning(statusCode, """{"error_message":"Not all required properties were sent in the request"}""");

        var result = await CreateClient(handler).AuthorizeAsync(TestData.ValidPayment());

        Assert.Equal((int)statusCode, Assert.IsType<BankResult.Refused>(result).StatusCode);
    }

    [Theory]
    [InlineData(HttpStatusCode.Found)]
    [InlineData(HttpStatusCode.TemporaryRedirect)]
    [InlineData(HttpStatusCode.PermanentRedirect)]
    public async Task A_redirect_is_an_unknown_outcome(HttpStatusCode statusCode)
    {
        var handler = StubHttpMessageHandler.Returning(statusCode);

        var result = await CreateClient(handler).AuthorizeAsync(TestData.ValidPayment());

        Assert.IsType<BankResult.Failed>(result);
    }

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.BadGateway)]
    public async Task Any_other_error_status_is_a_failure(HttpStatusCode statusCode)
    {
        var handler = StubHttpMessageHandler.Returning(statusCode, """{"error_message":"boom"}""");

        var result = await CreateClient(handler).AuthorizeAsync(TestData.ValidPayment());

        Assert.IsType<BankResult.Failed>(result);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("")]
    [InlineData("{}")]                                              // "authorized" missing, so not a decline
    [InlineData("""{"authorized":"yes"}""")]
    [InlineData("""{"authorized":true}""")]                         // authorized without a code
    [InlineData("""{"authorized":true,"authorization_code":""}""")]
    public async Task A_malformed_success_response_is_a_failure(string json)
    {
        var handler = StubHttpMessageHandler.Returning(HttpStatusCode.OK, json);

        var result = await CreateClient(handler).AuthorizeAsync(TestData.ValidPayment());

        Assert.IsType<BankResult.Failed>(result);
    }

    // Exceptions

    public static TheoryData<Exception> TimeoutExceptions => new()
    {
        new TimeoutRejectedException(),                              // Polly timeout
        new TaskCanceledException("timed out", new TimeoutException()) // HttpClient.Timeout
    };

    [Theory]
    [MemberData(nameof(TimeoutExceptions))]
    public async Task A_timeout_is_reported_as_a_timeout(Exception exception)
    {
        var handler = StubHttpMessageHandler.Throwing(exception);

        var result = await CreateClient(handler).AuthorizeAsync(TestData.ValidPayment());

        Assert.IsType<BankResult.Timeout>(result);
    }

    [Fact]
    public async Task An_open_circuit_means_the_bank_is_unavailable()
    {
        var handler = StubHttpMessageHandler.Throwing(new BrokenCircuitException());

        var result = await CreateClient(handler).AuthorizeAsync(TestData.ValidPayment());

        Assert.IsType<BankResult.Unavailable>(result);
    }

    [Theory]
    [InlineData(HttpRequestError.ConnectionError)]
    [InlineData(HttpRequestError.NameResolutionError)]
    public async Task A_connection_that_was_never_made_means_the_bank_is_unavailable(HttpRequestError error)
    {
        // The request never reached the bank, so it definitely was not processed and is safe to retry.
        var handler = StubHttpMessageHandler.Throwing(new HttpRequestException(error, "connection refused"));

        var result = await CreateClient(handler).AuthorizeAsync(TestData.ValidPayment());

        Assert.IsType<BankResult.Unavailable>(result);
    }

    [Fact]
    public async Task A_network_error_is_a_failure()
    {
        var handler = StubHttpMessageHandler.Throwing(new HttpRequestException("connection reset"));

        var result = await CreateClient(handler).AuthorizeAsync(TestData.ValidPayment());

        Assert.IsType<BankResult.Failed>(result);
    }

    [Fact]
    public async Task Caller_cancellation_is_not_swallowed()
    {
        var handler = new StubHttpMessageHandler(async (_, cancellationToken) =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            throw new InvalidOperationException("unreachable");
        });
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            CreateClient(handler).AuthorizeAsync(TestData.ValidPayment(), cancellation.Token));
    }

    [Fact]
    public async Task Never_logs_the_card_number_or_cvv()
    {
        var logger = new FakeLogger<BankClient>();
        var handler = StubHttpMessageHandler.Returning(HttpStatusCode.InternalServerError, "{}");
        var client = CreateClient(handler, logger);

        await client.AuthorizeAsync(TestData.ValidPayment());

        Assert.All(logger.Collector.GetSnapshot(), record =>
        {
            Assert.DoesNotContain(TestData.CardNumber, record.Message);
            Assert.DoesNotContain(record.StructuredState ?? [], pair =>
                pair.Value == TestData.Cvv || pair.Value == TestData.CardNumber);
        });
    }

    private static BankClient CreateClient(StubHttpMessageHandler handler,
        ILogger<BankClient>? logger = null) =>
        new(new HttpClient(handler) { BaseAddress = new Uri("http://bank.test/") },
            logger ?? NullLogger<BankClient>.Instance, ResiliencePipeline<BankResult>.Empty);
}
