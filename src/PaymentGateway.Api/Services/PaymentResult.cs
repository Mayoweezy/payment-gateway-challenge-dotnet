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

    /// <summary>Not processed. Safe to retry later.</summary>
    public sealed record BankUnavailable : PaymentResult;

    /// <summary>The bank refused the request. Not processed.</summary>
    public sealed record BankRefused : PaymentResult;

    /// <summary>The bank timed out. Outcome unknown.</summary>
    public sealed record BankTimeout : PaymentResult;

    /// <summary>The bank call failed. Outcome unknown.</summary>
    public sealed record BankFailed : PaymentResult;
}
