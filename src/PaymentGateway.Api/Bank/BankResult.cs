namespace PaymentGateway.Api.Bank;

public abstract record BankResult
{
    public sealed record Authorized(string AuthorizationCode) : BankResult;

    public sealed record Declined : BankResult;

    /// <summary>Not processed and safe to retry later: a 503, 429 or 408, no connection, or an open circuit.</summary>
    public sealed record Unavailable : BankResult;

    /// <summary>Any other 4xx: not processed, and validation should have prevented it.</summary>
    public sealed record Refused(int StatusCode) : BankResult;

    /// <summary>No answer in time. Outcome unknown.</summary>
    public sealed record Timeout : BankResult;

    /// <summary>Any other failure. Outcome unknown.</summary>
    public sealed record Failed(string Reason) : BankResult;
}
