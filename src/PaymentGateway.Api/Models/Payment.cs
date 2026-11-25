namespace PaymentGateway.Api.Models;

/// <summary>A processed payment. Never holds the full card number or CVV.</summary>
public sealed record Payment(
    Guid Id,
    PaymentStatus Status,
    string CardNumberLastFour,
    int ExpiryMonth,
    int ExpiryYear,
    string Currency,
    int Amount,
    string? AuthorizationCode);
