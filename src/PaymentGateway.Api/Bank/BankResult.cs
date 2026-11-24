namespace PaymentGateway.Api.Bank;

public abstract record BankResult
{
    public sealed record Authorized(string AuthorizationCode) : BankResult;

    public sealed record Declined : BankResult;

    /// <summary>The bank definitely did not process the payment: it returned 503, or the circuit is open.</summary>
    public sealed record Unavailable : BankResult;

    /// <summary>No answer in time. The bank may or may not have processed the payment.</summary>
    public sealed record Timeout : BankResult;

    /// <summary>Any other failure or unexpected response. The outcome is unknown.</summary>
    public sealed record Failed(string Reason) : BankResult;
}
