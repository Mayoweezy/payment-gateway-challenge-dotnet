using System.Text.Json;

using FluentValidation;

using PaymentGateway.Api.Bank;
using PaymentGateway.Api.Data;
using PaymentGateway.Api.Models;
using PaymentGateway.Api.Models.Requests;

namespace PaymentGateway.Api.Services;

/// <summary>Validates, calls the bank and stores the outcome. Never logs card data.</summary>
public sealed class PaymentService : IPaymentService
{
    private readonly IValidator<PostPaymentRequest> _validator;
    private readonly IBankClient _bankClient;
    private readonly IPaymentsRepository _paymentsRepository;
    private readonly ILogger<PaymentService> _logger;

    public PaymentService(
        IValidator<PostPaymentRequest> validator,
        IBankClient bankClient,
        IPaymentsRepository paymentsRepository,
        ILogger<PaymentService> logger)
    {
        _validator = validator;
        _bankClient = bankClient;
        _paymentsRepository = paymentsRepository;
        _logger = logger;
    }

    public async Task<PaymentResult> ProcessPaymentAsync(PostPaymentRequest request)
    {
        var errors = Validate(request);
        if (errors.Count > 0)
        {
            _logger.LogInformation("Payment rejected. Invalid fields: {InvalidFields}", string.Join(", ", errors.Keys));
            return new PaymentResult.Rejected(errors);
        }

        var payment = CardPayment.FromValidRequest(request);

        // Not cancellable: a disconnect must not abort a call the bank may complete.
        var bankResult = await _bankClient.AuthorizeAsync(payment, CancellationToken.None);

        return bankResult switch
        {
            BankResult.Authorized authorized => Store(payment, PaymentStatus.Authorized, authorized.AuthorizationCode),
            BankResult.Declined => Store(payment, PaymentStatus.Declined, authorizationCode: null),
            BankResult.Unavailable => BankUnavailable(),
            BankResult.Refused refused => BankRefused(refused.StatusCode),
            BankResult.Timeout => OutcomeUnknown(new PaymentResult.BankTimeout(), "the bank timed out"),
            BankResult.Failed failed => OutcomeUnknown(new PaymentResult.BankFailed(), failed.Reason),
            _ => throw new InvalidOperationException($"Unhandled bank result {bankResult.GetType().Name}.")
        };
    }

    public Payment? GetPayment(Guid id)
    {
        return _paymentsRepository.Get(id);
    }

    private Dictionary<string, string[]> Validate(PostPaymentRequest request)
    {
        // Only property names and messages are exposed. AttemptedValue would include the card number.
        return _validator.Validate(request).Errors
            .GroupBy(f => JsonNamingPolicy.CamelCase.ConvertName(f.PropertyName))
            .ToDictionary(g => g.Key, g => g.Select(f => f.ErrorMessage).Distinct().ToArray());
    }

    private PaymentResult Store(CardPayment cardPayment, PaymentStatus status, string? authorizationCode)
    {
        var payment = new Payment(
            Guid.NewGuid(),
            status,
            cardPayment.CardNumberLastFour,
            cardPayment.ExpiryMonth,
            cardPayment.ExpiryYear,
            cardPayment.Currency,
            cardPayment.Amount,
            authorizationCode);

        _paymentsRepository.Add(payment);

        _logger.LogInformation("Payment {PaymentId} processed with status {PaymentStatus}", payment.Id, payment.Status);
        return new PaymentResult.Processed(payment);
    }

    private PaymentResult BankUnavailable()
    {
        _logger.LogWarning("Payment not processed because the bank is unavailable");
        return new PaymentResult.BankUnavailable();
    }

    private PaymentResult BankRefused(int statusCode)
    {
        _logger.LogError("Payment not processed because the bank refused the request with status {StatusCode}", statusCode);
        return new PaymentResult.BankRefused();
    }

    private PaymentResult OutcomeUnknown(PaymentResult result, string reason)
    {
        _logger.LogError("Payment outcome unknown: {Reason}", reason);
        return result;
    }
}
