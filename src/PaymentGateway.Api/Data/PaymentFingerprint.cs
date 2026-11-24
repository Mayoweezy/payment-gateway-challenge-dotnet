namespace PaymentGateway.Api.Data;

/// <summary>
/// Identifies "the same payment" for idempotency. Deliberately excludes the full card number and the CVV,
/// which must not be retained, even hashed.
/// </summary>
public sealed record PaymentFingerprint(
    string CardNumberLastFour,
    int ExpiryMonth,
    int ExpiryYear,
    string Currency,
    int Amount);
