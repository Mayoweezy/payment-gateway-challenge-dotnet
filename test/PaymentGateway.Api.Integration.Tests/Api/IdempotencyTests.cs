using System.Net;

using NSubstitute;

using PaymentGateway.Api.Bank;
using PaymentGateway.Api.Controllers;
using PaymentGateway.Api.Integration.Tests.TestSupport;
using PaymentGateway.Api.Models.Requests;

namespace PaymentGateway.Api.Integration.Tests.Api;

public class IdempotencyTests : ApiTestBase
{
    private const string Key = "order-1234-attempt";

    [Fact]
    public async Task Without_a_key_identical_requests_create_separate_payments()
    {
        var first = await PostPaymentAsync(ValidBody());
        var second = await PostPaymentAsync(ValidBody());

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.Created, second.StatusCode);
        Assert.NotEqual(await IdOf(first), await IdOf(second));
        await BankReceivedCalls(2);
    }

    [Fact]
    public async Task The_same_key_and_details_replay_the_original_payment()
    {
        var first = await PostPaymentAsync(ValidBody(), Key);
        var replay = await PostPaymentAsync(ValidBody(), Key);

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.Created, replay.StatusCode);
        Assert.Equal(await IdOf(first), await IdOf(replay));
        Assert.False(first.Headers.Contains(PaymentsController.IdempotentReplayedHeader));
        Assert.Equal("true", Assert.Single(replay.Headers.GetValues(PaymentsController.IdempotentReplayedHeader)));
        await BankReceivedCalls(1);
    }

    [Fact]
    public async Task The_same_key_with_different_details_returns_422()
    {
        var first = await PostPaymentAsync(ValidBody(amount: 100), Key);
        var mismatch = await PostPaymentAsync(ValidBody(amount: 200), Key);

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, mismatch.StatusCode);
        AssertIsProblemDetails(mismatch);
        await BankReceivedCalls(1);
    }

    [Fact]
    public async Task A_duplicate_while_the_original_is_in_flight_returns_409_with_retry_after()
    {
        var bankCalled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var bankAnswer = new TaskCompletionSource<BankResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        Bank.AuthorizeAsync(Arg.Any<PostPaymentRequest>(), Arg.Any<CancellationToken>()).Returns(_ =>
        {
            bankCalled.TrySetResult();
            return bankAnswer.Task;
        });

        var original = PostPaymentAsync(ValidBody(), Key);
        await Task.WhenAny(bankCalled.Task, original, Task.Delay(TimeSpan.FromSeconds(5)));
        Assert.True(bankCalled.Task.IsCompleted, "The original request never reached the bank.");

        var duplicate = await PostPaymentAsync(ValidBody(), Key);
        bankAnswer.SetResult(new BankResult.Authorized("auth-code"));

        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
        AssertIsProblemDetails(duplicate);
        Assert.Equal(TimeSpan.FromSeconds(1), duplicate.Headers.RetryAfter?.Delta);
        Assert.Equal(HttpStatusCode.Created, (await original).StatusCode);
        await BankReceivedCalls(1);
    }

    [Fact]
    public async Task The_key_can_be_retried_after_the_bank_was_unavailable()
    {
        Bank.AuthorizeAsync(Arg.Any<PostPaymentRequest>(), Arg.Any<CancellationToken>())
            .Returns(new BankResult.Unavailable(), new BankResult.Authorized("auth-code"));

        var first = await PostPaymentAsync(ValidBody(), Key);
        var retry = await PostPaymentAsync(ValidBody(), Key);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, first.StatusCode);
        Assert.Equal(HttpStatusCode.Created, retry.StatusCode);
        Assert.False(retry.Headers.Contains(PaymentsController.IdempotentReplayedHeader));
        await BankReceivedCalls(2);
    }

    [Fact]
    public async Task The_key_is_locked_after_an_unknown_outcome_and_returns_409_without_retry_after()
    {
        Bank.AuthorizeAsync(Arg.Any<PostPaymentRequest>(), Arg.Any<CancellationToken>())
            .Returns(new BankResult.Timeout(), new BankResult.Authorized("auth-code"));

        var first = await PostPaymentAsync(ValidBody(), Key);
        var retry = await PostPaymentAsync(ValidBody(), Key);

        Assert.Equal(HttpStatusCode.GatewayTimeout, first.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, retry.StatusCode);
        AssertIsProblemDetails(retry);
        Assert.Null(retry.Headers.RetryAfter);
        await BankReceivedCalls(1);
    }

    [Fact]
    public async Task The_two_conflict_responses_have_different_problem_types()
    {
        // Key A: outcome unknown.
        Bank.AuthorizeAsync(Arg.Any<PostPaymentRequest>(), Arg.Any<CancellationToken>()).Returns(new BankResult.Timeout());
        await PostPaymentAsync(ValidBody(), "key-a");
        var outcomeUnknown = await PostPaymentAsync(ValidBody(), "key-a");

        // Key B: still in flight.
        var bankAnswer = new TaskCompletionSource<BankResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        Bank.AuthorizeAsync(Arg.Any<PostPaymentRequest>(), Arg.Any<CancellationToken>()).Returns(bankAnswer.Task);
        var original = PostPaymentAsync(ValidBody(), "key-b");
        await WaitForBankCalls(2);
        var inProgress = await PostPaymentAsync(ValidBody(), "key-b");
        bankAnswer.SetResult(new BankResult.Authorized("auth-code"));
        await original;

        Assert.Equal(HttpStatusCode.Conflict, outcomeUnknown.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, inProgress.StatusCode);
        var outcomeUnknownType = (await ReadJsonAsync(outcomeUnknown)).GetProperty("type").GetString();
        var inProgressType = (await ReadJsonAsync(inProgress)).GetProperty("type").GetString();
        Assert.False(string.IsNullOrWhiteSpace(outcomeUnknownType));
        Assert.False(string.IsNullOrWhiteSpace(inProgressType));
        Assert.NotEqual(outcomeUnknownType, inProgressType);
    }

    [Fact]
    public async Task A_rejected_request_does_not_consume_the_key()
    {
        var rejected = await PostPaymentAsync(ValidBody(amount: 0), Key);
        var corrected = await PostPaymentAsync(ValidBody(), Key);

        Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);
        Assert.Equal(HttpStatusCode.Created, corrected.StatusCode);
        Assert.False(corrected.Headers.Contains(PaymentsController.IdempotentReplayedHeader));
    }

    [Fact]
    public async Task A_blank_key_is_rejected()
    {
        var response = await PostPaymentAsync(ValidBody(), " ");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await ReadJsonAsync(response);
        Assert.Equal("Rejected", problem.GetProperty("paymentStatus").GetString());
        await BankReceivedCalls(0);
    }

    private static async Task<Guid> IdOf(HttpResponseMessage response) =>
        (await ReadJsonAsync(response)).GetProperty("id").GetGuid();

    private Task BankReceivedCalls(int count) =>
        Bank.Received(count).AuthorizeAsync(Arg.Any<PostPaymentRequest>(), Arg.Any<CancellationToken>());

    private async Task WaitForBankCalls(int count)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (Bank.ReceivedCalls().Count() < count && DateTime.UtcNow < deadline)
        {
            await Task.Delay(10);
        }

        Assert.True(Bank.ReceivedCalls().Count() >= count, $"Expected {count} bank calls.");
    }
}
