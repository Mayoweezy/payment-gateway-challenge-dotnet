using PaymentGateway.Api.Models.Requests;

namespace PaymentGateway.Api.Models;

/// <summary>
/// A payment request that passed validation, so every field is present. Holds the full card number and CVV, so it
/// is only ever sent to the bank, never stored or logged. Deliberately a class rather than a record: a record's
/// generated ToString would print the card number and CVV.
/// </summary>
public sealed class CardPayment
{
    public CardPayment(string cardNumber, int expiryMonth, int expiryYear, string currency, int amount, string cvv)
    {
        CardNumber = cardNumber;
        ExpiryMonth = expiryMonth;
        ExpiryYear = expiryYear;
        Currency = currency;
        Amount = amount;
        Cvv = cvv;
    }

    public string CardNumber { get; }
    public int ExpiryMonth { get; }
    public int ExpiryYear { get; }
    public string Currency { get; }
    public int Amount { get; }
    public string Cvv { get; }

    public string CardNumberLastFour => CardNumber[^4..];

    /// <summary>Only call this after the request has passed validation.</summary>
    public static CardPayment FromValidRequest(PostPaymentRequest request) => new(
        request.CardNumber ?? throw MissingField(nameof(request.CardNumber)),
        request.ExpiryMonth ?? throw MissingField(nameof(request.ExpiryMonth)),
        request.ExpiryYear ?? throw MissingField(nameof(request.ExpiryYear)),
        request.Currency ?? throw MissingField(nameof(request.Currency)),
        request.Amount ?? throw MissingField(nameof(request.Amount)),
        request.Cvv ?? throw MissingField(nameof(request.Cvv)));

    private static InvalidOperationException MissingField(string field) =>
        new($"{field} is missing. The request must be validated before it becomes a card payment.");
}
