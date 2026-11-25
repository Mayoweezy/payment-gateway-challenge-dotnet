using PaymentGateway.Api.Models;

namespace PaymentGateway.Api.Services;

/// <summary>
/// Every outcome of processing a payment. The controller maps each case to an HTTP response.
/// </summary>
public abstract record PaymentResult
{
    /// <summary>The bank authorized or declined the payment.</summary>
    public sealed record Processed(Payment Payment) : PaymentResult;

    /// <summary>The request was invalid, so the bank was not called. Keys are request property names.</summary>
    public sealed record Rejected(IReadOnlyDictionary<string, string[]> Errors) : PaymentResult;

    /// <summary>The bank definitely did not process the payment (circuit open or bank returned 503). Safe to retry.</summary>
    public sealed record BankUnavailable : PaymentResult;

    /// <summary>The bank refused the request, so it definitely did not process the payment. A gateway error.</summary>
    public sealed record BankRefused : PaymentResult;

    /// <summary>The bank did not answer in time. The outcome is unknown.</summary>
    public sealed record BankTimeout : PaymentResult;

    /// <summary>The bank failed or answered unexpectedly. The outcome is unknown.</summary>
    public sealed record BankFailed : PaymentResult;
}
