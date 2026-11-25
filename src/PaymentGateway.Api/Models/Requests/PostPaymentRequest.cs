namespace PaymentGateway.Api.Models.Requests;

/// <summary>
/// Properties are nullable so a missing field can be told apart from a zero or empty value.
/// Digit fields are strings so leading zeros are preserved.
/// </summary>
public class PostPaymentRequest
{
    public string? CardNumber { get; set; }
    public int? ExpiryMonth { get; set; }
    public int? ExpiryYear { get; set; }
    public string? Currency { get; set; }
    public int? Amount { get; set; }
    public string? Cvv { get; set; }
}
