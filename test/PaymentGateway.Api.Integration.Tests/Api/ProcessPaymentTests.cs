using System.Net;
using System.Text.Json;

using NSubstitute;

using PaymentGateway.Api.Bank;
using PaymentGateway.Api.Integration.Tests.TestSupport;
using PaymentGateway.Api.Models.Requests;

namespace PaymentGateway.Api.Integration.Tests.Api;

public class ProcessPaymentTests : ApiTestBase
{
    [Fact]
    public async Task An_authorized_payment_returns_201_with_its_location_and_details()
    {
        var response = await PostPaymentAsync(ValidBody());

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var payment = await ReadJsonAsync(response);
        var id = payment.GetProperty("id").GetGuid();
        Assert.NotEqual(Guid.Empty, id);
        Assert.NotNull(response.Headers.Location);
        Assert.EndsWith($"{PaymentsPath}/{id}", response.Headers.Location!.ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.Equal("Authorized", payment.GetProperty("status").GetString());
        Assert.Equal(CardNumberLastFour, payment.GetProperty("cardNumberLastFour").GetString());
        Assert.Equal(4, payment.GetProperty("expiryMonth").GetInt32());
        Assert.Equal(2027, payment.GetProperty("expiryYear").GetInt32());
        Assert.Equal("GBP", payment.GetProperty("currency").GetString());
        Assert.Equal(100, payment.GetProperty("amount").GetInt32());
    }

    [Fact]
    public async Task A_declined_payment_returns_201_declined()
    {
        GivenBankReturns(new BankResult.Declined());

        var response = await PostPaymentAsync(ValidBody());

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal("Declined", (await ReadJsonAsync(response)).GetProperty("status").GetString());
    }

    [Fact]
    public async Task The_last_four_digits_keep_a_leading_zero()
    {
        var response = await PostPaymentAsync(ValidBody(cardNumber: "4242424242420123"));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var lastFour = (await ReadJsonAsync(response)).GetProperty("cardNumberLastFour");
        Assert.Equal(JsonValueKind.String, lastFour.ValueKind);
        Assert.Equal("0123", lastFour.GetString());
    }

    [Fact]
    public async Task The_response_never_contains_the_card_number_or_cvv()
    {
        var response = await PostPaymentAsync(ValidBody());

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        await AssertContainsNoCardDataAsync(response);
    }

    [Fact]
    public async Task Sends_the_payment_to_the_bank()
    {
        var response = await PostPaymentAsync(ValidBody());

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        await Bank.Received(1).AuthorizeAsync(
            Arg.Is<PostPaymentRequest>(r => r.CardNumber == CardNumber && r.Cvv == Cvv && r.Amount == 100),
            Arg.Any<CancellationToken>());
    }
}
