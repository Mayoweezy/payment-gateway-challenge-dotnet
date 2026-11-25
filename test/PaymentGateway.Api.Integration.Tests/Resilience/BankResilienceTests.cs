using System.Diagnostics;
using System.Net;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

using PaymentGateway.Api.Bank;
using PaymentGateway.Api.Integration.Tests.TestSupport;
using PaymentGateway.Api.Models;

namespace PaymentGateway.Api.Integration.Tests.Resilience;

/// <summary>
/// Exercises the real HttpClient and Polly pipeline registered by AddBankClient, with only the network replaced.
/// The circuit opens once 2 calls have been sampled and at least half of them failed.
/// </summary>
public sealed class BankResilienceTests : IDisposable
{
    private const string AuthorizedJson = """{"authorized":true,"authorization_code":"auth-code"}""";
    private static readonly TimeSpan BreakDuration = TimeSpan.FromSeconds(1);

    private ServiceProvider? _provider;

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    public async Task The_circuit_opens_after_repeated_failures_and_stops_calling_the_bank(HttpStatusCode failure)
    {
        var handler = StubHttpMessageHandler.Returning(failure, "{}");
        var bank = CreateBankClient(handler);

        await bank.AuthorizeAsync(ValidRequest());
        await bank.AuthorizeAsync(ValidRequest());
        var shortCircuited = await bank.AuthorizeAsync(ValidRequest());

        Assert.IsType<BankResult.Unavailable>(shortCircuited);
        Assert.Equal(2, handler.CallCount);
    }

    [Fact]
    public async Task Bad_requests_do_not_open_the_circuit()
    {
        var handler = StubHttpMessageHandler.Returning(HttpStatusCode.BadRequest, """{"error_message":"bad"}""");
        var bank = CreateBankClient(handler);

        for (var i = 0; i < 5; i++)
        {
            Assert.IsType<BankResult.Refused>(await bank.AuthorizeAsync(ValidRequest()));
        }

        Assert.Equal(5, handler.CallCount);
    }

    [Theory]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    [InlineData(HttpStatusCode.InternalServerError)]
    public async Task A_failed_call_is_never_retried(HttpStatusCode failure)
    {
        var handler = StubHttpMessageHandler.Returning(failure, "{}");
        var bank = CreateBankClient(handler);

        var result = await bank.AuthorizeAsync(ValidRequest());

        Assert.IsNotType<BankResult.Authorized>(result);
        Assert.Equal(1, handler.CallCount);
    }

    [Fact]
    public async Task The_circuit_closes_again_after_the_break_duration()
    {
        var handler = StubHttpMessageHandler.Returning(HttpStatusCode.InternalServerError, "{}");
        var bank = CreateBankClient(handler);
        await bank.AuthorizeAsync(ValidRequest());
        await bank.AuthorizeAsync(ValidRequest());
        Assert.IsType<BankResult.Unavailable>(await bank.AuthorizeAsync(ValidRequest()));

        handler.Responder = (_, _) => Task.FromResult(StubHttpMessageHandler.Response(HttpStatusCode.OK, AuthorizedJson));
        await Task.Delay(BreakDuration + TimeSpan.FromMilliseconds(300));

        Assert.IsType<BankResult.Authorized>(await bank.AuthorizeAsync(ValidRequest()));
    }

    [Fact]
    public async Task A_slow_bank_times_out_without_waiting_for_it()
    {
        var handler = new StubHttpMessageHandler(async (_, cancellationToken) =>
        {
            await Task.Delay(TimeSpan.FromSeconds(10), cancellationToken);
            return StubHttpMessageHandler.Response(HttpStatusCode.OK, AuthorizedJson);
        });
        var bank = CreateBankClient(handler, timeout: TimeSpan.FromMilliseconds(200));
        var stopwatch = Stopwatch.StartNew();

        var result = await bank.AuthorizeAsync(ValidRequest());

        Assert.IsType<BankResult.Timeout>(result);
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(5), $"Took {stopwatch.Elapsed}.");
        Assert.Equal(1, handler.CallCount);
    }

    [Fact]
    public async Task Timeouts_count_towards_opening_the_circuit()
    {
        var handler = new StubHttpMessageHandler(async (_, cancellationToken) =>
        {
            await Task.Delay(TimeSpan.FromSeconds(10), cancellationToken);
            return StubHttpMessageHandler.Response(HttpStatusCode.OK, AuthorizedJson);
        });
        var bank = CreateBankClient(handler, timeout: TimeSpan.FromMilliseconds(100));

        await bank.AuthorizeAsync(ValidRequest());
        await bank.AuthorizeAsync(ValidRequest());
        var shortCircuited = await bank.AuthorizeAsync(ValidRequest());

        Assert.IsType<BankResult.Unavailable>(shortCircuited);
        Assert.Equal(2, handler.CallCount);
    }

    private IBankClient CreateBankClient(StubHttpMessageHandler handler, TimeSpan? timeout = null)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Bank:BaseUrl"] = "http://bank.test/",
                ["Bank:Timeout"] = (timeout ?? TimeSpan.FromSeconds(5)).ToString(),
                ["Bank:FailureRatio"] = "0.5",
                ["Bank:MinimumThroughput"] = "2",
                ["Bank:SamplingDuration"] = TimeSpan.FromSeconds(30).ToString(),
                ["Bank:BreakDuration"] = BreakDuration.ToString()
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddBankClient(configuration).ConfigurePrimaryHttpMessageHandler(() => handler);

        _provider = services.BuildServiceProvider();
        return _provider.GetRequiredService<IBankClient>();
    }

    private static CardPayment ValidRequest() => new("2222405343248877", 4, 2027, "GBP", 100, "123");

    public void Dispose() => _provider?.Dispose();
}
