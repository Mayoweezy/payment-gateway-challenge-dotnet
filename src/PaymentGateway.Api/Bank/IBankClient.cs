using PaymentGateway.Api.Models;

namespace PaymentGateway.Api.Bank;

public interface IBankClient
{
    /// <summary>Sends a validated payment to the bank. Never retries.</summary>
    Task<BankResult> AuthorizeAsync(CardPayment payment, CancellationToken cancellationToken = default);
}
