using System.Text.Json.Serialization;

namespace PaymentGateway.Api.Bank.Requests;

/// <summary>Wire format expected by the acquiring bank simulator.</summary>
public sealed class BankPaymentRequest
{
    [JsonPropertyName("card_number")]
    public required string CardNumber { get; init; }

    /// <summary>MM/YYYY, for example 04/2027.</summary>
    [JsonPropertyName("expiry_date")]
    public required string ExpiryDate { get; init; }

    [JsonPropertyName("currency")]
    public required string Currency { get; init; }

    [JsonPropertyName("amount")]
    public required int Amount { get; init; }

    [JsonPropertyName("cvv")]
    public required string Cvv { get; init; }
}
