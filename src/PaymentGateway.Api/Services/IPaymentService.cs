using PaymentGateway.Api.Models;
using PaymentGateway.Api.Models.Requests;

namespace PaymentGateway.Api.Services;

public interface IPaymentService
{
    /// <summary>No cancellation token: once the bank is called, the outcome must be recorded.</summary>
    Task<PaymentResult> ProcessPaymentAsync(PostPaymentRequest request);

    Payment? GetPayment(Guid id);
}
