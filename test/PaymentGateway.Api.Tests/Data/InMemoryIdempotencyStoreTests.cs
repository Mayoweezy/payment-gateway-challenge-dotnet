using PaymentGateway.Api.Data;
using PaymentGateway.Api.Tests.TestSupport;

namespace PaymentGateway.Api.Tests.Data;

public class InMemoryIdempotencyStoreTests
{
    private const string Key = "key-1";

    private static readonly PaymentFingerprint Fingerprint = new(TestData.CardNumberLastFour, 4, 2027, "GBP", 100);
    private static readonly PaymentFingerprint DifferentFingerprint = Fingerprint with { Amount = 200 };

    private readonly InMemoryIdempotencyStore _store = new();

    [Fact]
    public void A_new_key_is_reserved()
    {
        Assert.IsType<IdempotencyReservation.Reserved>(_store.TryReserve(Key, Fingerprint));
    }

    [Fact]
    public void A_reserved_key_is_in_progress_for_the_same_fingerprint()
    {
        _store.TryReserve(Key, Fingerprint);

        Assert.IsType<IdempotencyReservation.InProgress>(_store.TryReserve(Key, Fingerprint));
    }

    [Fact]
    public void A_completed_key_returns_the_stored_payment()
    {
        var payment = TestData.Payment();
        _store.TryReserve(Key, Fingerprint);
        _store.Complete(Key, payment);

        var completed = Assert.IsType<IdempotencyReservation.Completed>(_store.TryReserve(Key, Fingerprint));
        Assert.Equal(payment, completed.Payment);
    }

    [Fact]
    public void A_different_fingerprint_is_a_mismatch_while_in_progress()
    {
        _store.TryReserve(Key, Fingerprint);

        Assert.IsType<IdempotencyReservation.Mismatch>(_store.TryReserve(Key, DifferentFingerprint));
    }

    [Fact]
    public void A_different_fingerprint_is_a_mismatch_after_completion()
    {
        _store.TryReserve(Key, Fingerprint);
        _store.Complete(Key, TestData.Payment());

        Assert.IsType<IdempotencyReservation.Mismatch>(_store.TryReserve(Key, DifferentFingerprint));
    }

    [Fact]
    public void A_released_key_can_be_reserved_again()
    {
        _store.TryReserve(Key, Fingerprint);
        _store.Release(Key);

        Assert.IsType<IdempotencyReservation.Reserved>(_store.TryReserve(Key, Fingerprint));
    }

    [Fact]
    public void A_key_with_an_unknown_outcome_stays_locked()
    {
        _store.TryReserve(Key, Fingerprint);
        _store.MarkOutcomeUnknown(Key);

        Assert.IsType<IdempotencyReservation.OutcomeUnknown>(_store.TryReserve(Key, Fingerprint));
    }

    [Fact]
    public void Keys_are_independent()
    {
        _store.TryReserve(Key, Fingerprint);

        Assert.IsType<IdempotencyReservation.Reserved>(_store.TryReserve("key-2", Fingerprint));
    }

    [Fact]
    public async Task Exactly_one_concurrent_caller_reserves_a_key()
    {
        using var start = new ManualResetEventSlim();
        var attempts = Enumerable.Range(0, 50)
            .Select(_ => Task.Run(() =>
            {
                start.Wait();
                return _store.TryReserve(Key, Fingerprint);
            }))
            .ToList();

        start.Set();
        var results = await Task.WhenAll(attempts);

        Assert.Single(results, result => result is IdempotencyReservation.Reserved);
        Assert.All(results, result => Assert.True(
            result is IdempotencyReservation.Reserved or IdempotencyReservation.InProgress));
    }
}
