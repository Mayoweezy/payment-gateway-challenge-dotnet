namespace PaymentGateway.Api.Models;

/// <summary>The status of a payment that was sent to the bank. A rejected request never becomes a payment.</summary>
public enum PaymentStatus
{
    Authorized,
    Declined
}
