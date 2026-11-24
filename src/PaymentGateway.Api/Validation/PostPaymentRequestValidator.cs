using FluentValidation;

using PaymentGateway.Api.Models.Requests;

namespace PaymentGateway.Api.Validation;

/// <summary>
/// Error messages never include the submitted value, so card data cannot leak through a rejection.
/// </summary>
public sealed class PostPaymentRequestValidator : AbstractValidator<PostPaymentRequest>
{
    public static readonly IReadOnlySet<string> SupportedCurrencies =
        new HashSet<string>(StringComparer.Ordinal) { "GBP", "USD", "EUR" };

    private readonly TimeProvider _timeProvider;

    public PostPaymentRequestValidator(TimeProvider timeProvider)
    {
        _timeProvider = timeProvider;

        // Report only the first failure per field, for example "required" rather than also "wrong length".
        RuleLevelCascadeMode = CascadeMode.Stop;

        RuleFor(r => r.CardNumber)
            .NotEmpty().WithMessage("Card number is required.")
            .Length(14, 19).WithMessage("Card number must be between 14 and 19 characters long.")
            .Must(BeAsciiDigits).WithMessage("Card number must contain only numeric characters.");

        RuleFor(r => r.ExpiryMonth)
            .NotNull().WithMessage("Expiry month is required.")
            .InclusiveBetween(1, 12).WithMessage("Expiry month must be between 1 and 12.");

        RuleFor(r => r.ExpiryYear)
            .NotNull().WithMessage("Expiry year is required.")
            .InclusiveBetween(1000, 9999).WithMessage("Expiry year must be a four-digit year.");

        RuleFor(r => r.ExpiryYear)
            .Must((request, year) => IsNotExpired(request.ExpiryMonth!.Value, year!.Value))
            .When(r => r.ExpiryMonth is >= 1 and <= 12 && r.ExpiryYear is >= 1000 and <= 9999)
            .WithMessage("The card has expired. The expiry month and year must not be in the past.");

        RuleFor(r => r.Currency)
            .NotEmpty().WithMessage("Currency is required.")
            .Length(3).WithMessage("Currency must be a 3-character ISO 4217 code.")
            .Must(c => SupportedCurrencies.Contains(c!))
            .WithMessage($"Currency must be one of: {string.Join(", ", SupportedCurrencies)}.");

        RuleFor(r => r.Amount)
            .NotNull().WithMessage("Amount is required.")
            .GreaterThan(0).WithMessage("Amount must be a positive integer in the currency's minor unit.");

        RuleFor(r => r.Cvv)
            .NotEmpty().WithMessage("CVV is required.")
            .Length(3, 4).WithMessage("CVV must be 3 or 4 characters long.")
            .Must(BeAsciiDigits).WithMessage("CVV must contain only numeric characters.");
    }

    /// <summary>A card is valid until the end of its expiry month, in UTC.</summary>
    private bool IsNotExpired(int month, int year)
    {
        var now = _timeProvider.GetUtcNow();
        return (year, month).CompareTo((now.Year, now.Month)) >= 0;
    }

    // char.IsDigit would also accept non-ASCII digits such as Arabic-Indic numerals.
    private static bool BeAsciiDigits(string? value) => value is not null && value.All(char.IsAsciiDigit);
}
