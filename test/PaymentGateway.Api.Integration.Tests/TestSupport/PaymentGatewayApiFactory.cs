using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Time.Testing;

using NSubstitute;

using PaymentGateway.Api.Bank;

namespace PaymentGateway.Api.Integration.Tests.TestSupport;

/// <summary>The real application with the bank replaced by a mock and the clock fixed.</summary>
public sealed class PaymentGatewayApiFactory : WebApplicationFactory<Program>
{
    /// <summary>15 June 2026, midday UTC.</summary>
    public static readonly DateTimeOffset Now = new(2026, 6, 15, 12, 0, 0, TimeSpan.Zero);

    public IBankClient Bank { get; } = Substitute.For<IBankClient>();

    public FakeTimeProvider TimeProvider { get; } = new(Now);

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IBankClient>();
            services.AddSingleton(Bank);

            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(TimeProvider);
        });
    }
}
