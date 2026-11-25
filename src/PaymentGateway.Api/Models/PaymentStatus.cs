namespace PaymentGateway.Api.Models;

/// <summary>A rejected request never becomes a payment.</summary>
public enum PaymentStatus
{
    Authorized,
    Declined
}
