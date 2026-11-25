using System.Net.Http.Json;

using NSubstitute;

using PaymentGateway.Api.Bank;
using PaymentGateway.Api.Integration.Tests.TestSupport;
using PaymentGateway.Api.Models.Requests;

namespace PaymentGateway.Api.Integration.Tests.Api;

/// <summary>
/// The bank may already have authorized a payment when the merchant drops the connection, so a disconnect must
/// not abort the bank call. Otherwise the shopper could be charged with no payment recorded.
/// </summary>
public class ClientDisconnectTests : ApiTestBase
{
    [Fact]
    public async Task A_merchant_disconnecting_does_not_cancel_the_bank_call()
    {
        var bankCalled = new TaskCompletionSource<CancellationToken>(TaskCreationOptions.RunContinuationsAsynchronously);
        var bankAnswer = new TaskCompletionSource<BankResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        Bank.AuthorizeAsync(Arg.Any<PostPaymentRequest>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                bankCalled.TrySetResult(call.Arg<CancellationToken>());
                return bankAnswer.Task;
            });

        using var merchantConnection = new CancellationTokenSource();
        var request = new HttpRequestMessage(HttpMethod.Post, PaymentsPath) { Content = JsonContent.Create(ValidBody()) };
        var sending = Client.SendAsync(request, merchantConnection.Token);

        bool bankCallCancelled;
        try
        {
            var tokenSeenByBank = await bankCalled.Task.WaitAsync(TimeSpan.FromSeconds(5));
            merchantConnection.Cancel();

            // The test server aborts the request asynchronously, so give it a moment to propagate.
            await Task.Delay(TimeSpan.FromMilliseconds(200));
            bankCallCancelled = tokenSeenByBank.IsCancellationRequested;
        }
        finally
        {
            // Always let the server-side request finish, so the test cannot hang.
            bankAnswer.TrySetResult(new BankResult.Authorized("auth-code"));
        }

        // The merchant's side ends either way (cancelled, or a response nobody reads). Only the bank's token matters.
        try
        {
            await sending.WaitAsync(TimeSpan.FromSeconds(5));
        }
        catch (OperationCanceledException)
        {
        }

        Assert.False(bankCallCancelled, "The merchant disconnecting cancelled the bank call.");
    }
}
