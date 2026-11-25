using System.Net;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

using PaymentGateway.Api.Bank;
using PaymentGateway.Api.Integration.Tests.TestSupport;
using PaymentGateway.Api.Models;

namespace PaymentGateway.Api.Integration.Tests.Resilience;

/// <summary>
/// Exercises the real HttpClient and Polly pipeline registered by AddBankClient, with only the network replaced.
/// The circuit opens once 2 calls have been sampled and at least half of them failed. Time is faked, so nothing waits.
/// </summary>
public sealed class BankResilienceTests : IDisposable
{
    private const string AuthorizedJson = """{"authorized":true,"authorization_code":"auth-code"}""";
    private static readonly TimeSpan BreakDuration = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    private readonly FakeTimeProvider _time = new();
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
        _time.Advance(BreakDuration);

        Assert.IsType<BankResult.Authorized>(await bank.AuthorizeAsync(ValidRequest()));
    }

    [Fact]
    public async Task A_slow_bank_times_out_without_waiting_for_it()
    {
        var handler = HangingBank(out var called);
        var bank = CreateBankClient(handler, Timeout);

        var result = await AuthorizeAndTimeOutAsync(bank, called);

        Assert.IsType<BankResult.Timeout>(result);
        Assert.Equal(1, handler.CallCount);
    }

    [Fact]
    public async Task Timeouts_count_towards_opening_the_circuit()
    {
        var handler = HangingBank(out var called);
        var bank = CreateBankClient(handler, Timeout);

        await AuthorizeAndTimeOutAsync(bank, called);
        await AuthorizeAndTimeOutAsync(bank, called);
        var shortCircuited = await bank.AuthorizeAsync(ValidRequest());

        Assert.IsType<BankResult.Unavailable>(shortCircuited);
        Assert.Equal(2, handler.CallCount);
    }

    /// <summary>A bank that never answers. Signals each call, so the test knows when to advance the clock.</summary>
    private static StubHttpMessageHandler HangingBank(out SemaphoreSlim called)
    {
        var signal = new SemaphoreSlim(0);
        called = signal;
        return new StubHttpMessageHandler(async (_, cancellationToken) =>
        {
            signal.Release();
            await Task.Delay(System.Threading.Timeout.InfiniteTimeSpan, cancellationToken);
            throw new InvalidOperationException("unreachable");
        });
    }

    private async Task<BankResult> AuthorizeAndTimeOutAsync(IBankClient bank, SemaphoreSlim called)
    {
        var call = bank.AuthorizeAsync(ValidRequest());
        Assert.True(await called.WaitAsync(TimeSpan.FromSeconds(5)), "The bank was never called.");
        _time.Advance(Timeout);
        return await call.WaitAsync(TimeSpan.FromSeconds(5));
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
        services.AddSingleton<TimeProvider>(_time);
        services.AddBankClient(configuration).ConfigurePrimaryHttpMessageHandler(() => handler);

        _provider = services.BuildServiceProvider();
        return _provider.GetRequiredService<IBankClient>();
    }

    private static CardPayment ValidRequest() => new("2222405343248877", 4, 2027, "GBP", 100, "123");

    public void Dispose() => _provider?.Dispose();
}
