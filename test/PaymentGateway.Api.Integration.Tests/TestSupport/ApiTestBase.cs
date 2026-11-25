using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

using NSubstitute;

using PaymentGateway.Api.Bank;
using PaymentGateway.Api.Models;

namespace PaymentGateway.Api.Integration.Tests.TestSupport;

/// <summary>Each test gets its own application instance, so in-memory state never leaks between tests.</summary>
public abstract class ApiTestBase : IDisposable
{
    /// <summary>Ends in 7, so the simulator would authorize it.</summary>
    protected const string CardNumber = "2222405343248877";
    protected const string CardNumberLastFour = "8877";
    protected const string Cvv = "123";
    protected const string PaymentsPath = "/api/payments";

    protected ApiTestBase()
    {
        Factory = new PaymentGatewayApiFactory();
        Client = Factory.CreateClient();
        GivenBankReturns(new BankResult.Authorized("auth-code"));
    }

    protected PaymentGatewayApiFactory Factory { get; }

    protected HttpClient Client { get; }

    protected IBankClient Bank => Factory.Bank;

    protected void GivenBankReturns(BankResult result) =>
        Bank.AuthorizeAsync(Arg.Any<CardPayment>(), Arg.Any<CancellationToken>()).Returns(result);

    protected static object ValidBody(string cardNumber = CardNumber, int amount = 100) => new
    {
        cardNumber,
        expiryMonth = 4,
        expiryYear = 2027,
        currency = "GBP",
        amount,
        cvv = Cvv
    };

    protected Task<HttpResponseMessage> PostPaymentAsync(object body) => Client.PostAsJsonAsync(PaymentsPath, body);

    protected Task<HttpResponseMessage> PostRawJsonAsync(string json) =>
        Client.PostAsync(PaymentsPath, new StringContent(json, Encoding.UTF8, "application/json"));

    protected static async Task<JsonElement> ReadJsonAsync(HttpResponseMessage response)
    {
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.Clone();
    }

    protected static void AssertIsProblemDetails(HttpResponseMessage response) =>
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);

    protected static async Task AssertContainsNoCardDataAsync(HttpResponseMessage response, string cardNumber = CardNumber)
    {
        var body = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain(cardNumber, body);

        using var document = JsonDocument.Parse(body);
        Assert.DoesNotContain(document.RootElement.EnumerateObject(), property =>
            property.Name.Equals("cardNumber", StringComparison.OrdinalIgnoreCase) ||
            property.Name.Equals("cvv", StringComparison.OrdinalIgnoreCase));
    }

    public void Dispose()
    {
        Client.Dispose();
        Factory.Dispose();
        GC.SuppressFinalize(this);
    }
}
