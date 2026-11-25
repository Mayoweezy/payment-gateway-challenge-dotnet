using System.Text.Json.Serialization;

namespace PaymentGateway.Api.Bank.Responses;

/// <summary>Wire format returned by the acquiring bank simulator.</summary>
public sealed class BankPaymentResponse
{
    /// <summary>Nullable so a response without this field is treated as malformed rather than declined.</summary>
    [JsonPropertyName("authorized")]
    public bool? Authorized { get; init; }

    [JsonPropertyName("authorization_code")]
    public string? AuthorizationCode { get; init; }
}
