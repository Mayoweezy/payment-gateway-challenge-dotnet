using PaymentGateway.Api.Models;
using PaymentGateway.Api.Models.Requests;

namespace PaymentGateway.Api.Services;

public interface IPaymentService
{
    /// <summary>
    /// Validates the payment, sends it to the bank and stores the outcome. Deliberately takes no cancellation token:
    /// once the bank has been called, the payment must be recorded even if the merchant has disconnected.
    /// </summary>
    Task<PaymentResult> ProcessPaymentAsync(PostPaymentRequest request);

    Payment? GetPayment(Guid id);
}
