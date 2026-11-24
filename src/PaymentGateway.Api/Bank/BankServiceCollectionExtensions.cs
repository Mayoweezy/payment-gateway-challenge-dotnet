using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Options;

using Polly;

namespace PaymentGateway.Api.Bank;

public static class BankServiceCollectionExtensions
{
    public const string ResiliencePipelineName = "bank";

    public static IHttpClientBuilder AddBankClient(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<BankOptions>().Bind(configuration.GetSection(BankOptions.SectionName));

        var builder = services.AddHttpClient<IBankClient, BankClient>((serviceProvider, client) =>
        {
            client.BaseAddress = serviceProvider.GetRequiredService<IOptions<BankOptions>>().Value.BaseUrl;

            // The pipeline's timeout governs instead, so it is counted by the circuit breaker.
            client.Timeout = Timeout.InfiniteTimeSpan;
        });

        // Deliberately no retry strategy, and not AddStandardResilienceHandler, which includes retries:
        // the bank has no idempotency support, so a retry after a timeout could charge the shopper twice.
        builder.AddResilienceHandler(ResiliencePipelineName, (pipeline, context) =>
        {
            var options = context.ServiceProvider.GetRequiredService<IOptions<BankOptions>>().Value;

            // Added first, so it is the outer strategy and sees the timeouts below as failures.
            // The default ShouldHandle counts 5xx, 408, 429, network errors and timeouts, but not 400s.
            pipeline.AddCircuitBreaker(new HttpCircuitBreakerStrategyOptions
            {
                FailureRatio = options.FailureRatio,
                MinimumThroughput = options.MinimumThroughput,
                SamplingDuration = options.SamplingDuration,
                BreakDuration = options.BreakDuration
            });

            pipeline.AddTimeout(options.Timeout);
        });

        return builder;
    }
}
