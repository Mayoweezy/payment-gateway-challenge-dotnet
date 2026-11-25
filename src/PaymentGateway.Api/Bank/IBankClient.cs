using PaymentGateway.Api.Models;

namespace PaymentGateway.Api.Bank;

public interface IBankClient
{
    /// <summary>
    /// Sends a validated payment to the acquiring bank. Never retries.
    /// Caller cancellation propagates as an <see cref="OperationCanceledException"/>.
    /// </summary>
    Task<BankResult> AuthorizeAsync(CardPayment payment, CancellationToken cancellationToken = default);
}
