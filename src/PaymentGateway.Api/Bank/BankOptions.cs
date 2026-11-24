namespace PaymentGateway.Api.Bank;

public sealed class BankOptions
{
    public const string SectionName = "Bank";

    public Uri BaseUrl { get; set; } = new("http://localhost:8080/");

    /// <summary>Timeout for a single bank call.</summary>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(10);

    public double FailureRatio { get; set; } = 0.5;
    public int MinimumThroughput { get; set; } = 10;
    public TimeSpan SamplingDuration { get; set; } = TimeSpan.FromSeconds(30);
    public TimeSpan BreakDuration { get; set; } = TimeSpan.FromSeconds(15);
}
