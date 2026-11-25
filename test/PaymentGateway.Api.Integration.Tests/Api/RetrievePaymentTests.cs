using System.Net;

using PaymentGateway.Api.Integration.Tests.TestSupport;

namespace PaymentGateway.Api.Integration.Tests.Api;

public class RetrievePaymentTests : ApiTestBase
{
    [Fact]
    public async Task Returns_404_problem_details_for_an_unknown_payment()
    {
        var response = await Client.GetAsync($"{PaymentsPath}/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        AssertIsProblemDetails(response);
    }

    [Fact]
    public async Task Returns_a_previously_processed_payment()
    {
        var created = await PostPaymentAsync(ValidBody());
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var id = (await ReadJsonAsync(created)).GetProperty("id").GetGuid();

        var response = await Client.GetAsync($"{PaymentsPath}/{id}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payment = await ReadJsonAsync(response);
        Assert.Equal(id, payment.GetProperty("id").GetGuid());
        Assert.Equal("Authorized", payment.GetProperty("status").GetString());
        Assert.Equal(CardNumberLastFour, payment.GetProperty("cardNumberLastFour").GetString());
        Assert.Equal(4, payment.GetProperty("expiryMonth").GetInt32());
        Assert.Equal(2027, payment.GetProperty("expiryYear").GetInt32());
        Assert.Equal("GBP", payment.GetProperty("currency").GetString());
        Assert.Equal(100, payment.GetProperty("amount").GetInt32());
    }

    [Fact]
    public async Task A_retrieved_payment_never_contains_the_card_number_or_cvv()
    {
        var created = await PostPaymentAsync(ValidBody());
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);

        var response = await Client.GetAsync(created.Headers.Location);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        await AssertContainsNoCardDataAsync(response);
    }
}
