using System.Net;

using NSubstitute;

using PaymentGateway.Api.Bank;
using PaymentGateway.Api.Integration.Tests.TestSupport;
using PaymentGateway.Api.Models.Requests;

namespace PaymentGateway.Api.Integration.Tests.Api;

/// <summary>The README promises that every error is an RFC 9457 problem response, including the unplanned ones.</summary>
public class UnexpectedErrorTests : ApiTestBase
{
    [Fact]
    public async Task An_unhandled_exception_returns_a_500_problem_without_card_data()
    {
        Bank.AuthorizeAsync(Arg.Any<PostPaymentRequest>(), Arg.Any<CancellationToken>())
            .Returns<Task<BankResult>>(_ => throw new InvalidOperationException("unexpected failure"));

        var response = await PostPaymentAsync(ValidBody());

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        AssertIsProblemDetails(response);
        await AssertContainsNoCardDataAsync(response);
    }

    [Fact]
    public async Task A_payment_id_that_is_not_a_guid_returns_a_404_problem()
    {
        var response = await Client.GetAsync($"{PaymentsPath}/not-a-guid");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        AssertIsProblemDetails(response);
    }
}
