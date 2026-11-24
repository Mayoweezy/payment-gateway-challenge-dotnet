using PaymentGateway.Api.Models.Requests;

namespace PaymentGateway.Api.Bank;

public interface IBankClient
{
    /// <summary>
    /// Sends an already validated payment to the acquiring bank. Never retries.
    /// Caller cancellation propagates as an <see cref="OperationCanceledException"/>.
    /// </summary>
    Task<BankResult> AuthorizeAsync(PostPaymentRequest request, CancellationToken cancellationToken = default);
}
