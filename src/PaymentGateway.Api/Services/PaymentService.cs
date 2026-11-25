using System.Text.Json;

using FluentValidation;

using PaymentGateway.Api.Bank;
using PaymentGateway.Api.Data;
using PaymentGateway.Api.Models;
using PaymentGateway.Api.Models.Requests;

namespace PaymentGateway.Api.Services;

/// <summary>
/// Validates a payment, sends it to the bank and stores the outcome. Never logs the full card number or the CVV.
/// </summary>
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

        // Not cancellable by the merchant: the bank may authorize the payment even if the merchant disconnects,
        // and the gateway must still record it. The bank client's own timeout bounds the call.
        var bankResult = await _bankClient.AuthorizeAsync(request, CancellationToken.None);

        return bankResult switch
        {
            BankResult.Authorized authorized => Store(request, PaymentStatus.Authorized, authorized.AuthorizationCode),
            BankResult.Declined => Store(request, PaymentStatus.Declined, authorizationCode: null),
            BankResult.Unavailable => BankUnavailable(),
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

    private PaymentResult Store(PostPaymentRequest request, PaymentStatus status, string? authorizationCode)
    {
        var payment = new Payment(
            Guid.NewGuid(),
            status,
            request.CardNumber![^4..],
            request.ExpiryMonth!.Value,
            request.ExpiryYear!.Value,
            request.Currency!,
            request.Amount!.Value,
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

    private PaymentResult OutcomeUnknown(PaymentResult result, string reason)
    {
        _logger.LogError("Payment outcome unknown: {Reason}", reason);
        return result;
    }
}
