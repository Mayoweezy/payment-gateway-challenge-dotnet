namespace PaymentGateway.Api.Models;

/// <summary>
/// A payment that was sent to the acquiring bank. Never holds the full card number or the CVV.
/// </summary>
public sealed record Payment(
    Guid Id,
    PaymentStatus Status,
    string CardNumberLastFour,
    int ExpiryMonth,
    int ExpiryYear,
    string Currency,
    int Amount,
    string? AuthorizationCode);
