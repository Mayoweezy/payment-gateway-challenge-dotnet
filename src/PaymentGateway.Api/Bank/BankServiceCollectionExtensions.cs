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

            // The pipeline timeout applies instead, so the circuit breaker sees it.
            client.Timeout = Timeout.InfiniteTimeSpan;
        });

        // No retries (so not AddStandardResilienceHandler): the bank isn't idempotent, so a retry could double charge.
        builder.AddResilienceHandler(ResiliencePipelineName, (pipeline, context) =>
        {
            var options = context.ServiceProvider.GetRequiredService<IOptions<BankOptions>>().Value;

            // Outer strategy, so it counts the timeouts below. Ignores 4xx other than 408 and 429.
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
