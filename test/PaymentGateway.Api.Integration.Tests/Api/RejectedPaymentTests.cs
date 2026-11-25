using System.Net;
using System.Text.Json;

using NSubstitute;

using PaymentGateway.Api.Integration.Tests.TestSupport;

namespace PaymentGateway.Api.Integration.Tests.Api;

/// <summary>
/// A rejected payment is a ValidationProblemDetails. Its "status" field is the HTTP status code (400), as the
/// ProblemDetails standard requires, so the payment status is returned in a separate "paymentStatus" field.
/// </summary>
public class RejectedPaymentTests : ApiTestBase
{
    [Fact]
    public async Task An_invalid_request_returns_400_rejected_with_every_field_error()
    {
        var response = await PostPaymentAsync(new
        {
            cardNumber = "12ab",
            expiryMonth = 13,
            expiryYear = 2027,
            currency = "JPY",
            amount = 0,
            cvv = "1"
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        AssertIsProblemDetails(response);
        var problem = await ReadJsonAsync(response);
        AssertIsRejected(problem);
        AssertHasErrorFor(problem, "cardNumber");
        AssertHasErrorFor(problem, "expiryMonth");
        AssertHasErrorFor(problem, "currency");
        AssertHasErrorFor(problem, "amount");
        AssertHasErrorFor(problem, "cvv");
    }

    [Fact]
    public async Task An_expiry_date_in_the_past_is_rejected()
    {
        // The factory's clock is 15 June 2026, so May 2026 has expired.
        var response = await PostPaymentAsync(new
        {
            cardNumber = CardNumber,
            expiryMonth = 5,
            expiryYear = 2026,
            currency = "GBP",
            amount = 100,
            cvv = Cvv
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        AssertIsRejected(await ReadJsonAsync(response));
    }

    [Fact]
    public async Task The_bank_is_not_called_for_an_invalid_request()
    {
        var response = await PostPaymentAsync(new { cardNumber = "12ab" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        await Bank.DidNotReceiveWithAnyArgs().AuthorizeAsync(default!, default);
    }

    [Theory]
    [InlineData("")]
    [InlineData("{")]
    [InlineData("not json")]
    [InlineData("""{"cardNumber":"2222405343248877","expiryMonth":4,"expiryYear":2027,"currency":"GBP","amount":"abc","cvv":"123"}""")]
    [InlineData("""{"cardNumber":"2222405343248877","expiryMonth":4,"expiryYear":2027,"currency":"GBP","amount":10.5,"cvv":"123"}""")]
    [InlineData("""{"cardNumber":2222405343248877,"expiryMonth":4,"expiryYear":2027,"currency":"GBP","amount":100,"cvv":"123"}""")]
    public async Task Malformed_json_returns_the_same_rejected_shape(string json)
    {
        var response = await PostRawJsonAsync(json);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        AssertIsProblemDetails(response);
        AssertIsRejected(await ReadJsonAsync(response));
        await Bank.DidNotReceiveWithAnyArgs().AuthorizeAsync(default!, default);
    }

    [Fact]
    public async Task Malformed_json_is_not_echoed_back()
    {
        const string cardNumberSentAsNumber = "4242424242424242";
        var json = $$"""{"cardNumber":{{cardNumberSentAsNumber}},"expiryMonth":4,"expiryYear":2027,"currency":"GBP","amount":100,"cvv":"123"}""";

        var response = await PostRawJsonAsync(json);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        AssertIsRejected(await ReadJsonAsync(response));
        await AssertContainsNoCardDataAsync(response, cardNumberSentAsNumber);
    }

    [Fact]
    public async Task An_invalid_card_number_is_not_echoed_back()
    {
        const string invalidCardNumber = "4242abcd42424242";

        var response = await PostPaymentAsync(ValidBody(cardNumber: invalidCardNumber));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        AssertIsRejected(await ReadJsonAsync(response));
        await AssertContainsNoCardDataAsync(response, invalidCardNumber);
    }

    private static void AssertIsRejected(JsonElement problem)
    {
        Assert.Equal(400, problem.GetProperty("status").GetInt32());
        Assert.True(problem.TryGetProperty("paymentStatus", out var paymentStatus), "Expected a paymentStatus field.");
        Assert.Equal("Rejected", paymentStatus.GetString());
    }

    private static void AssertHasErrorFor(JsonElement problem, string field)
    {
        Assert.True(problem.TryGetProperty("errors", out var errors), "Expected an errors object.");
        Assert.Contains(errors.EnumerateObject(), error => error.Name.Equals(field, StringComparison.OrdinalIgnoreCase));
    }
}
