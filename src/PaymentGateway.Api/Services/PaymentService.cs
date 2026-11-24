using System.Text.Json;

using FluentValidation;
using FluentValidation.Results;

using PaymentGateway.Api.Bank;
using PaymentGateway.Api.Data;
using PaymentGateway.Api.Models;
using PaymentGateway.Api.Models.Requests;

namespace PaymentGateway.Api.Services;

/// <summary>
/// Validates a payment, protects it with an optional idempotency key, sends it to the bank and stores the outcome.
/// Never logs the full card number or the CVV.
/// </summary>
public sealed class PaymentService : IPaymentService
{
    public const int MaxIdempotencyKeyLength = 255;
    public const string IdempotencyKeyField = "idempotencyKey";

    private readonly IValidator<PostPaymentRequest> _validator;
    private readonly IBankClient _bankClient;
    private readonly IPaymentsRepository _paymentsRepository;
    private readonly IIdempotencyStore _idempotencyStore;
    private readonly ILogger<PaymentService> _logger;

    public PaymentService(
        IValidator<PostPaymentRequest> validator,
        IBankClient bankClient,
        IPaymentsRepository paymentsRepository,
        IIdempotencyStore idempotencyStore,
        ILogger<PaymentService> logger)
    {
        _validator = validator;
        _bankClient = bankClient;
        _paymentsRepository = paymentsRepository;
        _idempotencyStore = idempotencyStore;
        _logger = logger;
    }

    public async Task<PaymentResult> ProcessPaymentAsync(
        PostPaymentRequest request,
        string? idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        var errors = Validate(request, idempotencyKey);
        if (errors.Count > 0)
        {
            _logger.LogInformation("Payment rejected. Invalid fields: {InvalidFields}", string.Join(", ", errors.Keys));
            return new PaymentResult.Rejected(errors);
        }

        if (idempotencyKey is not null)
        {
            var reservation = _idempotencyStore.TryReserve(idempotencyKey, Fingerprint(request));
            if (reservation is not IdempotencyReservation.Reserved)
            {
                return FromExistingReservation(reservation);
            }
        }

        BankResult bankResult;
        try
        {
            bankResult = await _bankClient.AuthorizeAsync(request, cancellationToken);
        }
        catch
        {
            // Cancellation or an unexpected error: the request may already have reached the bank.
            if (idempotencyKey is not null)
            {
                _idempotencyStore.MarkOutcomeUnknown(idempotencyKey);
            }

            throw;
        }

        return bankResult switch
        {
            BankResult.Authorized authorized => Store(request, PaymentStatus.Authorized, authorized.AuthorizationCode, idempotencyKey),
            BankResult.Declined => Store(request, PaymentStatus.Declined, authorizationCode: null, idempotencyKey),
            BankResult.Unavailable => BankUnavailable(idempotencyKey),
            BankResult.Timeout => OutcomeUnknown(idempotencyKey, new PaymentResult.BankTimeout(), "the bank timed out"),
            BankResult.Failed failed => OutcomeUnknown(idempotencyKey, new PaymentResult.BankFailed(), failed.Reason),
            _ => throw new InvalidOperationException($"Unhandled bank result {bankResult.GetType().Name}.")
        };
    }

    public Payment? GetPayment(Guid id)
    {
        return _paymentsRepository.Get(id);
    }

    private Dictionary<string, string[]> Validate(PostPaymentRequest request, string? idempotencyKey)
    {
        var failures = _validator.Validate(request).Errors;

        if (idempotencyKey is not null &&
            (string.IsNullOrWhiteSpace(idempotencyKey) || idempotencyKey.Length > MaxIdempotencyKeyLength))
        {
            failures.Add(new ValidationFailure(
                IdempotencyKeyField,
                $"Idempotency key must be between 1 and {MaxIdempotencyKeyLength} characters and not blank."));
        }

        // Only property names and messages are exposed. AttemptedValue would include the card number.
        return failures
            .GroupBy(f => JsonNamingPolicy.CamelCase.ConvertName(f.PropertyName))
            .ToDictionary(g => g.Key, g => g.Select(f => f.ErrorMessage).Distinct().ToArray());
    }

    private PaymentResult FromExistingReservation(IdempotencyReservation reservation)
    {
        switch (reservation)
        {
            case IdempotencyReservation.Completed completed:
                _logger.LogInformation("Payment {PaymentId} replayed for a repeated idempotency key", completed.Payment.Id);
                return new PaymentResult.Processed(completed.Payment, IsReplay: true);
            case IdempotencyReservation.InProgress:
                _logger.LogInformation("Payment request is a duplicate of one still in progress");
                return new PaymentResult.IdempotencyKeyInProgress();
            case IdempotencyReservation.OutcomeUnknown:
                _logger.LogWarning("Payment request reuses an idempotency key whose bank outcome is unknown");
                return new PaymentResult.IdempotencyKeyOutcomeUnknown();
            case IdempotencyReservation.Mismatch:
                _logger.LogWarning("Payment request reuses an idempotency key with different payment details");
                return new PaymentResult.IdempotencyKeyMismatch();
            default:
                throw new InvalidOperationException($"Unhandled reservation {reservation.GetType().Name}.");
        }
    }

    private PaymentResult Store(PostPaymentRequest request, PaymentStatus status, string? authorizationCode, string? idempotencyKey)
    {
        var payment = new Payment(
            Guid.NewGuid(),
            status,
            LastFour(request.CardNumber!),
            request.ExpiryMonth!.Value,
            request.ExpiryYear!.Value,
            request.Currency!,
            request.Amount!.Value,
            authorizationCode);

        _paymentsRepository.Add(payment);
        if (idempotencyKey is not null)
        {
            _idempotencyStore.Complete(idempotencyKey, payment);
        }

        _logger.LogInformation("Payment {PaymentId} processed with status {PaymentStatus}", payment.Id, payment.Status);
        return new PaymentResult.Processed(payment, IsReplay: false);
    }

    private PaymentResult BankUnavailable(string? idempotencyKey)
    {
        // The bank definitely did not process the payment, so the merchant may retry with the same key.
        if (idempotencyKey is not null)
        {
            _idempotencyStore.Release(idempotencyKey);
        }

        _logger.LogWarning("Payment not processed because the bank is unavailable");
        return new PaymentResult.BankUnavailable();
    }

    private PaymentResult OutcomeUnknown(string? idempotencyKey, PaymentResult result, string reason)
    {
        // Retrying could double charge, so the key stays locked until the merchant reconciles.
        if (idempotencyKey is not null)
        {
            _idempotencyStore.MarkOutcomeUnknown(idempotencyKey);
        }

        _logger.LogError("Payment outcome unknown: {Reason}", reason);
        return result;
    }

    private static PaymentFingerprint Fingerprint(PostPaymentRequest request) => new(
        LastFour(request.CardNumber!),
        request.ExpiryMonth!.Value,
        request.ExpiryYear!.Value,
        request.Currency!,
        request.Amount!.Value);

    private static string LastFour(string cardNumber) => cardNumber[^4..];
}
