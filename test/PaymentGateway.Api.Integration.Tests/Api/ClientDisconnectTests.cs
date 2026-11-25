using System.Net;

using NSubstitute;

using PaymentGateway.Api.Bank;
using PaymentGateway.Api.Integration.Tests.TestSupport;
using PaymentGateway.Api.Models;

namespace PaymentGateway.Api.Integration.Tests.Api;

/// <summary>
/// The bank may already have authorized a payment when the merchant drops the connection, so a disconnect must
/// not abort the bank call. Otherwise the shopper could be charged with no payment recorded.
/// </summary>
public class ClientDisconnectTests : ApiTestBase
{
    [Fact]
    public async Task The_bank_call_is_not_tied_to_the_merchants_connection()
    {
        // A token that cannot be cancelled cannot be cancelled by a disconnect either. Asserting this directly is
        // deterministic, unlike dropping a connection and waiting for the abort to reach the server.
        CancellationToken tokenSeenByBank = default;
        Bank.AuthorizeAsync(Arg.Any<CardPayment>(), Arg.Do<CancellationToken>(token => tokenSeenByBank = token))
            .Returns(new BankResult.Authorized("auth-code"));

        var response = await PostPaymentAsync(ValidBody());

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.False(tokenSeenByBank.CanBeCanceled, "The bank call can be cancelled by the merchant's request.");
    }
}
