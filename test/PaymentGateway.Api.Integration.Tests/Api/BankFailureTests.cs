using System.Net;

using PaymentGateway.Api.Bank;
using PaymentGateway.Api.Http;
using PaymentGateway.Api.Integration.Tests.TestSupport;

namespace PaymentGateway.Api.Integration.Tests.Api;

public class BankFailureTests : ApiTestBase
{
    [Fact]
    public async Task An_unavailable_bank_returns_503_with_retry_after_because_retrying_is_safe()
    {
        GivenBankReturns(new BankResult.Unavailable());

        var response = await PostPaymentAsync(ValidBody());

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        AssertIsProblemDetails(response);
        Assert.NotNull(response.Headers.RetryAfter);
    }

    [Fact]
    public async Task A_bank_refusal_returns_502_saying_the_payment_was_not_processed()
    {
        GivenBankReturns(new BankResult.Refused(400));

        var response = await PostPaymentAsync(ValidBody());

        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
        AssertIsProblemDetails(response);
        Assert.Null(response.Headers.RetryAfter);
        Assert.Equal(PaymentProblems.Types.BankRefused, (await ReadJsonAsync(response)).GetProperty("type").GetString());
    }

    [Fact]
    public async Task A_bank_timeout_returns_504_without_retry_after_because_the_outcome_is_unknown()
    {
        GivenBankReturns(new BankResult.Timeout());

        var response = await PostPaymentAsync(ValidBody());

        Assert.Equal(HttpStatusCode.GatewayTimeout, response.StatusCode);
        AssertIsProblemDetails(response);
        Assert.Null(response.Headers.RetryAfter);
    }

    [Fact]
    public async Task Any_other_bank_failure_returns_502_without_retry_after()
    {
        GivenBankReturns(new BankResult.Failed("unexpected status 500"));

        var response = await PostPaymentAsync(ValidBody());

        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
        AssertIsProblemDetails(response);
        Assert.Null(response.Headers.RetryAfter);
    }

    [Fact]
    public async Task A_bank_failure_response_never_contains_card_data()
    {
        GivenBankReturns(new BankResult.Failed("unexpected status 500"));

        var response = await PostPaymentAsync(ValidBody());

        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
        await AssertContainsNoCardDataAsync(response);
    }
}
