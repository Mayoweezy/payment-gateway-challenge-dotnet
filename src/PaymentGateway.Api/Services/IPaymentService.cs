using PaymentGateway.Api.Models;
using PaymentGateway.Api.Models.Requests;

namespace PaymentGateway.Api.Services;

public interface IPaymentService
{
    Task<PaymentResult> ProcessPaymentAsync(
        PostPaymentRequest request,
        string? idempotencyKey,
        CancellationToken cancellationToken = default);

    Payment? GetPayment(Guid id);
}
