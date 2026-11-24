using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

using Microsoft.AspNetCore.Mvc.Testing;

namespace PaymentGateway.Api.Integration.Tests.Simulator;

/// <summary>
/// Runs the real application against the bank simulator. Start it first with "docker-compose up".
/// Skip these with: dotnet test --filter "Category!=RequiresDocker"
/// </summary>
[Trait("Category", "RequiresDocker")]
public sealed class BankSimulatorTests : IDisposable
{
    private readonly WebApplicationFactory<Program> _factory = new();
    private readonly HttpClient _client;

    public BankSimulatorTests()
    {
        _client = _factory.CreateClient();
    }

    [Fact]
    public async Task A_card_ending_in_an_odd_digit_is_authorized()
    {
        var response = await PostPaymentAsync("2222405343248877");

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal("Authorized", (await ReadJsonAsync(response)).GetProperty("status").GetString());
    }

    [Fact]
    public async Task A_card_ending_in_an_even_digit_is_declined()
    {
        var response = await PostPaymentAsync("2222405343248112");

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal("Declined", (await ReadJsonAsync(response)).GetProperty("status").GetString());
    }

    [Fact]
    public async Task A_card_ending_in_zero_makes_the_bank_unavailable()
    {
        var response = await PostPaymentAsync("2222405343248110");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
    }

    [Fact]
    public async Task An_authorized_payment_can_be_retrieved()
    {
        var created = await PostPaymentAsync("2222405343248877");
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var id = (await ReadJsonAsync(created)).GetProperty("id").GetGuid();

        var response = await _client.GetAsync($"/api/payments/{id}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(id, (await ReadJsonAsync(response)).GetProperty("id").GetGuid());
    }

    private Task<HttpResponseMessage> PostPaymentAsync(string cardNumber) =>
        _client.PostAsJsonAsync("/api/payments", new
        {
            cardNumber,
            expiryMonth = 4,
            expiryYear = DateTime.UtcNow.Year + 1,
            currency = "GBP",
            amount = 100,
            cvv = "123"
        });

    private static async Task<JsonElement> ReadJsonAsync(HttpResponseMessage response)
    {
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.Clone();
    }

    public void Dispose()
    {
        _client.Dispose();
        _factory.Dispose();
    }
}
