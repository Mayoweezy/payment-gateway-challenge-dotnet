using PaymentGateway.Api.Models;
using PaymentGateway.Api.Models.Requests;

namespace PaymentGateway.Api.Tests.TestSupport;

internal static class TestData
{
    /// <summary>Fixed "now" for every time-dependent test: 15 June 2026, midday UTC.</summary>
    public static readonly DateTimeOffset Now = new(2026, 6, 15, 12, 0, 0, TimeSpan.Zero);

    /// <summary>Ends in 7, so the simulator would authorize it.</summary>
    public const string CardNumber = "2222405343248877";
    public const string CardNumberLastFour = "8877";
    public const string Cvv = "123";

    public static PostPaymentRequest ValidRequest() => new()
    {
        CardNumber = CardNumber,
        ExpiryMonth = 4,
        ExpiryYear = 2027,
        Currency = "GBP",
        Amount = 100,
        Cvv = Cvv
    };

    public static Payment Payment(PaymentStatus status = PaymentStatus.Authorized) => new(
        Guid.NewGuid(),
        status,
        CardNumberLastFour,
        4,
        2027,
        "GBP",
        100,
        status == PaymentStatus.Authorized ? "auth-code" : null);
}
