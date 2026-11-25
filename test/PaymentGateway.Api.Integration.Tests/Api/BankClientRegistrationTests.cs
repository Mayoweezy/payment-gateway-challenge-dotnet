using Microsoft.Extensions.DependencyInjection;

using PaymentGateway.Api.Bank;
using PaymentGateway.Api.Integration.Tests.TestSupport;

namespace PaymentGateway.Api.Integration.Tests.Api;

public class BankClientRegistrationTests : ApiTestBase
{
    [Fact]
    public void The_bank_client_never_follows_redirects()
    {
        var factory = Factory.Services.GetRequiredService<IHttpMessageHandlerFactory>();
        HttpMessageHandler? handler = factory.CreateHandler(nameof(IBankClient));
        while (handler is DelegatingHandler delegating)
        {
            handler = delegating.InnerHandler;
        }

        Assert.False(Assert.IsType<SocketsHttpHandler>(handler).AllowAutoRedirect);
    }
}
