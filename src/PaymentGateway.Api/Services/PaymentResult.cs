using PaymentGateway.Api.Models;

namespace PaymentGateway.Api.Services;

/// <summary>
/// Every outcome of processing a payment. The controller maps each case to an HTTP response.
/// </summary>
public abstract record PaymentResult
{
    /// <summary>The bank authorized or declined the payment, or a completed payment was replayed.</summary>
    public sealed record Processed(Payment Payment, bool IsReplay) : PaymentResult;

    /// <summary>The request was invalid, so the bank was not called. Keys are request property names.</summary>
    public sealed record Rejected(IReadOnlyDictionary<string, string[]> Errors) : PaymentResult;

    /// <summary>The bank definitely did not process the payment (circuit open or bank returned 503). Safe to retry.</summary>
    public sealed record BankUnavailable : PaymentResult;

    /// <summary>The bank did not answer in time. The outcome is unknown.</summary>
    public sealed record BankTimeout : PaymentResult;

    /// <summary>The bank failed or answered unexpectedly. The outcome is unknown.</summary>
    public sealed record BankFailed : PaymentResult;

    /// <summary>The idempotency key was already used with different payment details.</summary>
    public sealed record IdempotencyKeyMismatch : PaymentResult;

    /// <summary>A request with this idempotency key is still being processed. Retry shortly.</summary>
    public sealed record IdempotencyKeyInProgress : PaymentResult;

    /// <summary>A previous request with this key had an unknown outcome. The merchant must reconcile.</summary>
    public sealed record IdempotencyKeyOutcomeUnknown : PaymentResult;
}
