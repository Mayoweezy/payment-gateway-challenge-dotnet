using System.Net;

using PaymentGateway.Api.Integration.Tests.TestSupport;

namespace PaymentGateway.Api.Integration.Tests.Api;

/// <summary>Checks the published OpenAPI document matches what the API can actually return.</summary>
public class ApiContractTests : ApiTestBase
{
    [Fact]
    public async Task A_payment_status_is_only_ever_authorized_or_declined()
    {
        var response = await Client.GetAsync("/swagger/v1/swagger.json");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var statuses = (await ReadJsonAsync(response))
            .GetProperty("components").GetProperty("schemas").GetProperty("PaymentStatus").GetProperty("enum")
            .EnumerateArray().Select(value => value.ToString());

        Assert.Equal(["Authorized", "Declined"], statuses);
    }

    [Fact]
    public async Task Operations_carry_their_summary_comments()
    {
        var document = await ReadJsonAsync(await Client.GetAsync("/swagger/v1/swagger.json"));

        var summary = document.GetProperty("paths").GetProperty("/api/Payments").GetProperty("post")
            .GetProperty("summary").GetString();

        Assert.Equal("Processes a card payment through the acquiring bank.", summary);
    }
}
