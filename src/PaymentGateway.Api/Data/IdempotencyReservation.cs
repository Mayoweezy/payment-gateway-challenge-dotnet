using PaymentGateway.Api.Models;

namespace PaymentGateway.Api.Data;

public abstract record IdempotencyReservation
{
    /// <summary>The key was free and is now reserved by this caller, who must call the bank.</summary>
    public sealed record Reserved : IdempotencyReservation;

    /// <summary>Another request with this key and fingerprint is still being processed.</summary>
    public sealed record InProgress : IdempotencyReservation;

    /// <summary>A request with this key and fingerprint already completed. Replay this payment.</summary>
    public sealed record Completed(Payment Payment) : IdempotencyReservation;

    /// <summary>A previous request with this key had an unknown bank outcome.</summary>
    public sealed record OutcomeUnknown : IdempotencyReservation;

    /// <summary>The key was used with a different fingerprint.</summary>
    public sealed record Mismatch : IdempotencyReservation;
}
