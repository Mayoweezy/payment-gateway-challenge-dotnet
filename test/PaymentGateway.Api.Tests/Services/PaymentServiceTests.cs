using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using Microsoft.Extensions.Time.Testing;

using NSubstitute;

using PaymentGateway.Api.Bank;
using PaymentGateway.Api.Data;
using PaymentGateway.Api.Models;
using PaymentGateway.Api.Models.Requests;
using PaymentGateway.Api.Services;
using PaymentGateway.Api.Tests.TestSupport;
using PaymentGateway.Api.Validation;

namespace PaymentGateway.Api.Tests.Services;

/// <summary>
/// Uses the real validator, repository and idempotency store. Only the bank, the external boundary, is mocked.
/// </summary>
public class PaymentServiceTests
{
    private readonly IBankClient _bank = Substitute.For<IBankClient>();
    private readonly PaymentsRepository _repository = new();
    private readonly InMemoryIdempotencyStore _idempotencyStore = new();
    private readonly FakeLogger<PaymentService> _logger = new();
    private readonly PaymentService _service;

    public PaymentServiceTests()
    {
        _service = new PaymentService(
            new PostPaymentRequestValidator(new FakeTimeProvider(TestData.Now)),
            _bank,
            _repository,
            _idempotencyStore,
            _logger);

        GivenBankReturns(new BankResult.Authorized("auth-code"));
    }

    // Retrieving

    [Fact]
    public void GetPayment_returns_a_stored_payment()
    {
        var payment = TestData.Payment();
        _repository.Add(payment);

        Assert.Equal(payment, _service.GetPayment(payment.Id));
    }

    [Fact]
    public void GetPayment_returns_null_for_an_unknown_id()
    {
        Assert.Null(_service.GetPayment(Guid.NewGuid()));
    }

    // Processing

    [Fact]
    public async Task An_authorized_payment_is_stored_with_only_the_last_four_digits()
    {
        var result = await _service.ProcessPaymentAsync(TestData.ValidRequest(), idempotencyKey: null);

        var processed = Assert.IsType<PaymentResult.Processed>(result);
        Assert.False(processed.IsReplay);
        var payment = processed.Payment;
        Assert.NotEqual(Guid.Empty, payment.Id);
        Assert.Equal(PaymentStatus.Authorized, payment.Status);
        Assert.Equal(TestData.CardNumberLastFour, payment.CardNumberLastFour);
        Assert.Equal(4, payment.ExpiryMonth);
        Assert.Equal(2027, payment.ExpiryYear);
        Assert.Equal("GBP", payment.Currency);
        Assert.Equal(100, payment.Amount);
        Assert.Equal("auth-code", payment.AuthorizationCode);
        Assert.Equal(payment, _repository.Get(payment.Id));
    }

    [Fact]
    public async Task A_declined_payment_is_stored_as_declined()
    {
        GivenBankReturns(new BankResult.Declined());

        var result = await _service.ProcessPaymentAsync(TestData.ValidRequest(), idempotencyKey: null);

        var payment = Assert.IsType<PaymentResult.Processed>(result).Payment;
        Assert.Equal(PaymentStatus.Declined, payment.Status);
        Assert.Null(payment.AuthorizationCode);
        Assert.Equal(payment, _repository.Get(payment.Id));
    }

    [Fact]
    public async Task Sends_the_full_payment_details_to_the_bank()
    {
        await _service.ProcessPaymentAsync(TestData.ValidRequest(), idempotencyKey: null);

        await _bank.Received(1).AuthorizeAsync(
            Arg.Is<PostPaymentRequest>(r =>
                r.CardNumber == TestData.CardNumber &&
                r.ExpiryMonth == 4 &&
                r.ExpiryYear == 2027 &&
                r.Currency == "GBP" &&
                r.Amount == 100 &&
                r.Cvv == TestData.Cvv),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Each_payment_gets_a_new_id()
    {
        var first = await _service.ProcessPaymentAsync(TestData.ValidRequest(), idempotencyKey: null);
        var second = await _service.ProcessPaymentAsync(TestData.ValidRequest(), idempotencyKey: null);

        Assert.NotEqual(
            Assert.IsType<PaymentResult.Processed>(first).Payment.Id,
            Assert.IsType<PaymentResult.Processed>(second).Payment.Id);
        await _bank.Received(2).AuthorizeAsync(Arg.Any<PostPaymentRequest>(), Arg.Any<CancellationToken>());
    }

    // Rejecting

    [Fact]
    public async Task An_invalid_request_is_rejected_without_calling_the_bank()
    {
        var request = TestData.ValidRequest();
        request.CardNumber = "not-a-card";
        request.Cvv = "1";

        var result = await _service.ProcessPaymentAsync(request, idempotencyKey: null);

        var rejected = Assert.IsType<PaymentResult.Rejected>(result);
        AssertHasErrorFor(rejected, nameof(PostPaymentRequest.CardNumber));
        AssertHasErrorFor(rejected, nameof(PostPaymentRequest.Cvv));
        await _bank.DidNotReceiveWithAnyArgs().AuthorizeAsync(default!, default);
    }

    // Bank failures

    public static TheoryData<BankResult, Type> BankFailures => new()
    {
        { new BankResult.Unavailable(), typeof(PaymentResult.BankUnavailable) },
        { new BankResult.Timeout(), typeof(PaymentResult.BankTimeout) },
        { new BankResult.Failed("unexpected status 500"), typeof(PaymentResult.BankFailed) }
    };

    [Theory]
    [MemberData(nameof(BankFailures))]
    public async Task Maps_each_bank_failure_to_its_result(BankResult bankResult, Type expectedResult)
    {
        GivenBankReturns(bankResult);

        var result = await _service.ProcessPaymentAsync(TestData.ValidRequest(), idempotencyKey: null);

        Assert.IsType(expectedResult, result);
    }

    // Logging

    [Fact]
    public async Task Logs_the_payment_id_and_outcome()
    {
        var result = await _service.ProcessPaymentAsync(TestData.ValidRequest(), idempotencyKey: null);

        var payment = Assert.IsType<PaymentResult.Processed>(result).Payment;
        Assert.Contains(_logger.Collector.GetSnapshot(), record =>
            record.Message.Contains(payment.Id.ToString()) &&
            record.Message.Contains(nameof(PaymentStatus.Authorized)));
    }

    public static TheoryData<BankResult> BankFailureResults => new()
    {
        new BankResult.Unavailable(),
        new BankResult.Timeout(),
        new BankResult.Failed("unexpected status 500")
    };

    [Theory]
    [MemberData(nameof(BankFailureResults))]
    public async Task Logs_bank_failures_as_warnings_or_above(BankResult bankResult)
    {
        GivenBankReturns(bankResult);

        await _service.ProcessPaymentAsync(TestData.ValidRequest(), idempotencyKey: null);

        Assert.Contains(_logger.Collector.GetSnapshot(), record => record.Level >= LogLevel.Warning);
    }

    [Fact]
    public async Task Never_logs_the_card_number_or_cvv()
    {
        var invalid = TestData.ValidRequest();
        invalid.CardNumber = "4242abcd42424242";
        invalid.Cvv = "98x";

        await _service.ProcessPaymentAsync(TestData.ValidRequest(), idempotencyKey: null);
        await _service.ProcessPaymentAsync(invalid, idempotencyKey: null);

        var records = _logger.Collector.GetSnapshot();
        Assert.NotEmpty(records);
        Assert.All(records, record =>
        {
            Assert.DoesNotContain(TestData.CardNumber, record.Message);
            Assert.DoesNotContain(invalid.CardNumber, record.Message);
            Assert.DoesNotContain(invalid.Cvv, record.Message);
            Assert.DoesNotContain(record.StructuredState ?? [], pair =>
                pair.Value == TestData.Cvv || pair.Value == TestData.CardNumber);
        });
    }

    // Idempotency

    [Fact]
    public async Task The_same_key_and_details_replay_the_original_payment()
    {
        var first = await _service.ProcessPaymentAsync(TestData.ValidRequest(), "key-1");
        var second = await _service.ProcessPaymentAsync(TestData.ValidRequest(), "key-1");

        var original = Assert.IsType<PaymentResult.Processed>(first);
        var replay = Assert.IsType<PaymentResult.Processed>(second);
        Assert.False(original.IsReplay);
        Assert.True(replay.IsReplay);
        Assert.Equal(original.Payment, replay.Payment);
        await _bank.Received(1).AuthorizeAsync(Arg.Any<PostPaymentRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task The_same_key_with_different_details_is_a_mismatch()
    {
        var different = TestData.ValidRequest();
        different.Amount = 200;

        await _service.ProcessPaymentAsync(TestData.ValidRequest(), "key-1");
        var result = await _service.ProcessPaymentAsync(different, "key-1");

        Assert.IsType<PaymentResult.IdempotencyKeyMismatch>(result);
        await _bank.Received(1).AuthorizeAsync(Arg.Any<PostPaymentRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task The_same_key_with_only_a_different_cvv_replays_because_the_cvv_is_not_fingerprinted()
    {
        var differentCvv = TestData.ValidRequest();
        differentCvv.Cvv = "456";

        await _service.ProcessPaymentAsync(TestData.ValidRequest(), "key-1");
        var result = await _service.ProcessPaymentAsync(differentCvv, "key-1");

        Assert.True(Assert.IsType<PaymentResult.Processed>(result).IsReplay);
    }

    [Fact]
    public async Task A_duplicate_while_the_original_is_in_flight_is_reported_as_in_progress()
    {
        var bankAnswer = new TaskCompletionSource<BankResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        _bank.AuthorizeAsync(Arg.Any<PostPaymentRequest>(), Arg.Any<CancellationToken>()).Returns(bankAnswer.Task);

        var first = _service.ProcessPaymentAsync(TestData.ValidRequest(), "key-1");
        var second = await _service.ProcessPaymentAsync(TestData.ValidRequest(), "key-1");
        bankAnswer.SetResult(new BankResult.Authorized("auth-code"));

        Assert.IsType<PaymentResult.IdempotencyKeyInProgress>(second);
        Assert.IsType<PaymentResult.Processed>(await first);
        await _bank.Received(1).AuthorizeAsync(Arg.Any<PostPaymentRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task The_key_is_released_when_the_bank_definitely_did_not_process_the_payment()
    {
        _bank.AuthorizeAsync(Arg.Any<PostPaymentRequest>(), Arg.Any<CancellationToken>())
            .Returns(new BankResult.Unavailable(), new BankResult.Authorized("auth-code"));

        var first = await _service.ProcessPaymentAsync(TestData.ValidRequest(), "key-1");
        var retry = await _service.ProcessPaymentAsync(TestData.ValidRequest(), "key-1");

        Assert.IsType<PaymentResult.BankUnavailable>(first);
        Assert.False(Assert.IsType<PaymentResult.Processed>(retry).IsReplay);
        await _bank.Received(2).AuthorizeAsync(Arg.Any<PostPaymentRequest>(), Arg.Any<CancellationToken>());
    }

    public static TheoryData<BankResult> UnknownOutcomes => new()
    {
        new BankResult.Timeout(),
        new BankResult.Failed("unexpected status 500")
    };

    [Theory]
    [MemberData(nameof(UnknownOutcomes))]
    public async Task The_key_stays_locked_when_the_bank_outcome_is_unknown(BankResult unknownOutcome)
    {
        _bank.AuthorizeAsync(Arg.Any<PostPaymentRequest>(), Arg.Any<CancellationToken>())
            .Returns(unknownOutcome, new BankResult.Authorized("auth-code"));

        await _service.ProcessPaymentAsync(TestData.ValidRequest(), "key-1");
        var retry = await _service.ProcessPaymentAsync(TestData.ValidRequest(), "key-1");

        Assert.IsType<PaymentResult.IdempotencyKeyOutcomeUnknown>(retry);
        await _bank.Received(1).AuthorizeAsync(Arg.Any<PostPaymentRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_rejected_request_does_not_consume_the_key()
    {
        var invalid = TestData.ValidRequest();
        invalid.Currency = "JPY";

        var rejected = await _service.ProcessPaymentAsync(invalid, "key-1");
        var corrected = await _service.ProcessPaymentAsync(TestData.ValidRequest(), "key-1");

        Assert.IsType<PaymentResult.Rejected>(rejected);
        Assert.False(Assert.IsType<PaymentResult.Processed>(corrected).IsReplay);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null, 256)]
    public async Task An_invalid_idempotency_key_is_rejected_without_calling_the_bank(string? key, int length = 0)
    {
        key ??= new string('k', length);

        var result = await _service.ProcessPaymentAsync(TestData.ValidRequest(), key);

        AssertHasErrorFor(Assert.IsType<PaymentResult.Rejected>(result), "idempotencyKey");
        await _bank.DidNotReceiveWithAnyArgs().AuthorizeAsync(default!, default);
    }

    [Fact]
    public async Task A_255_character_idempotency_key_is_accepted()
    {
        var result = await _service.ProcessPaymentAsync(TestData.ValidRequest(), new string('k', 255));

        Assert.IsType<PaymentResult.Processed>(result);
    }

    private void GivenBankReturns(BankResult result) =>
        _bank.AuthorizeAsync(Arg.Any<PostPaymentRequest>(), Arg.Any<CancellationToken>()).Returns(result);

    private static void AssertHasErrorFor(PaymentResult.Rejected rejected, string propertyName) =>
        Assert.Contains(rejected.Errors.Keys, key => string.Equals(key, propertyName, StringComparison.OrdinalIgnoreCase));
}
