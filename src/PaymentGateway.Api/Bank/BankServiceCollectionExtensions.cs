using Microsoft.Extensions.Options;

using Polly;
using Polly.CircuitBreaker;
using Polly.Timeout;

namespace PaymentGateway.Api.Bank;

public static class BankServiceCollectionExtensions
{
    public static IHttpClientBuilder AddBankClient(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<BankOptions>().Bind(configuration.GetSection(BankOptions.SectionName));

        services.AddSingleton(serviceProvider =>
        {
            var options = serviceProvider.GetRequiredService<IOptions<BankOptions>>().Value;
            var pipeline = new ResiliencePipelineBuilder<BankResult>
            {
                TimeProvider = serviceProvider.GetRequiredService<TimeProvider>()
            };

            // The shared breaker observes timeouts during both the send and the response body read.
            return pipeline.AddCircuitBreaker(new CircuitBreakerStrategyOptions<BankResult>
            {
                ShouldHandle = new PredicateBuilder<BankResult>()
                    .Handle<HttpRequestException>()
                    .Handle<TimeoutRejectedException>()
                    .HandleResult(result => result is BankResult.Unavailable or BankResult.Failed),
                FailureRatio = options.FailureRatio,
                MinimumThroughput = options.MinimumThroughput,
                SamplingDuration = options.SamplingDuration,
                BreakDuration = options.BreakDuration
            }).AddTimeout(options.Timeout).Build();
        });

        return services.AddHttpClient<IBankClient, BankClient>((serviceProvider, client) =>
        {
            client.BaseAddress = serviceProvider.GetRequiredService<IOptions<BankOptions>>().Value.BaseUrl;

            // The pipeline timeout applies instead, so the circuit breaker sees it.
            client.Timeout = Timeout.InfiniteTimeSpan;
        })
        // A followed 307 or 308 would resend the card data to wherever Location points.
        .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler { AllowAutoRedirect = false });
    }
}
