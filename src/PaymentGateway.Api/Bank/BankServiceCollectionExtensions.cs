using System.Net;

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
            var pipeline = new ResiliencePipelineBuilder<(HttpStatusCode Status, BankResult Result)>
            {
                TimeProvider = serviceProvider.GetRequiredService<TimeProvider>()
            };

            // The shared breaker observes timeouts during both the send and the response body read.
            return pipeline.AddCircuitBreaker(new CircuitBreakerStrategyOptions<(HttpStatusCode Status, BankResult Result)>
            {
                ShouldHandle = new PredicateBuilder<(HttpStatusCode Status, BankResult Result)>()
                    .Handle<HttpRequestException>()
                    .Handle<TimeoutRejectedException>()
                    .HandleResult(reply => (int)reply.Status >= 500 ||
                        reply.Status is HttpStatusCode.RequestTimeout or HttpStatusCode.TooManyRequests),
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
        });
    }
}
