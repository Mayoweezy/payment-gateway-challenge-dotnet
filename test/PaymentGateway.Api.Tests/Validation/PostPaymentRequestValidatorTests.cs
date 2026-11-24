using FluentValidation.TestHelper;

using Microsoft.Extensions.Time.Testing;

using PaymentGateway.Api.Models.Requests;
using PaymentGateway.Api.Tests.TestSupport;
using PaymentGateway.Api.Validation;

namespace PaymentGateway.Api.Tests.Validation;

public class PostPaymentRequestValidatorTests
{
    // "Now" is 15 June 2026 UTC.
    private readonly PostPaymentRequestValidator _validator = new(new FakeTimeProvider(TestData.Now));

    [Fact]
    public void Accepts_a_valid_request()
    {
        _validator.TestValidate(TestData.ValidRequest()).ShouldNotHaveAnyValidationErrors();
    }

    // Card number

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("1234567890123")]          // 13 characters
    [InlineData("12345678901234567890")]   // 20 characters
    [InlineData("2222 4053 4324 8877")]    // spaces
    [InlineData("222240534324887A")]       // letter
    [InlineData("-222405343248877")]       // sign
    public void Rejects_an_invalid_card_number(string? cardNumber)
    {
        var request = With(r => r.CardNumber = cardNumber);

        _validator.TestValidate(request).ShouldHaveValidationErrorFor(r => r.CardNumber);
    }

    [Fact]
    public void Rejects_a_card_number_made_of_non_ascii_digits()
    {
        // Arabic-Indic digit four. char.IsDigit accepts it, char.IsAsciiDigit does not.
        var request = With(r => r.CardNumber = new string('٤', 16));

        _validator.TestValidate(request).ShouldHaveValidationErrorFor(r => r.CardNumber);
    }

    [Theory]
    [InlineData("12345678901234")]         // 14 characters
    [InlineData("1234567890123456789")]    // 19 characters
    public void Accepts_a_card_number_at_the_length_boundaries(string cardNumber)
    {
        var request = With(r => r.CardNumber = cardNumber);

        _validator.TestValidate(request).ShouldNotHaveValidationErrorFor(r => r.CardNumber);
    }

    // Expiry month and year

    [Theory]
    [InlineData(null)]
    [InlineData(0)]
    [InlineData(13)]
    [InlineData(-1)]
    public void Rejects_an_invalid_expiry_month(int? month)
    {
        var request = With(r => r.ExpiryMonth = month);

        _validator.TestValidate(request).ShouldHaveValidationErrorFor(r => r.ExpiryMonth);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(12)]
    public void Accepts_an_expiry_month_at_the_boundaries(int month)
    {
        var request = With(r => r.ExpiryMonth = month);

        _validator.TestValidate(request).ShouldNotHaveAnyValidationErrors();
    }

    [Theory]
    [InlineData(null)]
    [InlineData(27)]       // two-digit year
    [InlineData(10000)]    // five-digit year
    public void Rejects_an_invalid_expiry_year(int? year)
    {
        var request = With(r => r.ExpiryYear = year);

        _validator.TestValidate(request).ShouldHaveValidationErrorFor(r => r.ExpiryYear);
    }

    [Theory]
    [InlineData(5, 2026)]      // previous month
    [InlineData(12, 2025)]     // previous year
    [InlineData(6, 2025)]      // same month, previous year
    public void Rejects_an_expiry_date_in_the_past(int month, int year)
    {
        var request = With(r =>
        {
            r.ExpiryMonth = month;
            r.ExpiryYear = year;
        });

        Assert.False(_validator.TestValidate(request).IsValid);
    }

    [Theory]
    [InlineData(6, 2026)]      // current month: the card is valid until the end of the month
    [InlineData(7, 2026)]      // next month
    [InlineData(1, 2027)]      // next year
    public void Accepts_an_expiry_date_from_the_current_month_onwards(int month, int year)
    {
        var request = With(r =>
        {
            r.ExpiryMonth = month;
            r.ExpiryYear = year;
        });

        _validator.TestValidate(request).ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void Treats_the_current_month_as_valid_until_its_last_moment_in_utc()
    {
        var lastMomentOfJune = new DateTimeOffset(2026, 6, 30, 23, 59, 59, TimeSpan.Zero);
        var validator = new PostPaymentRequestValidator(new FakeTimeProvider(lastMomentOfJune));
        var request = With(r =>
        {
            r.ExpiryMonth = 6;
            r.ExpiryYear = 2026;
        });

        validator.TestValidate(request).ShouldNotHaveAnyValidationErrors();
    }

    // Currency

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("GB")]
    [InlineData("GBPP")]
    [InlineData("JPY")]    // valid ISO 4217, but not one of the three supported currencies
    [InlineData("gbp")]
    [InlineData("Gbp")]
    public void Rejects_an_invalid_or_unsupported_currency(string? currency)
    {
        var request = With(r => r.Currency = currency);

        _validator.TestValidate(request).ShouldHaveValidationErrorFor(r => r.Currency);
    }

    [Theory]
    [InlineData("GBP")]
    [InlineData("USD")]
    [InlineData("EUR")]
    public void Accepts_a_supported_currency(string currency)
    {
        var request = With(r => r.Currency = currency);

        _validator.TestValidate(request).ShouldNotHaveValidationErrorFor(r => r.Currency);
    }

    // Amount

    [Theory]
    [InlineData(null)]
    [InlineData(0)]
    [InlineData(-1)]
    public void Rejects_a_missing_or_non_positive_amount(int? amount)
    {
        var request = With(r => r.Amount = amount);

        _validator.TestValidate(request).ShouldHaveValidationErrorFor(r => r.Amount);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(int.MaxValue)]
    public void Accepts_a_positive_amount(int amount)
    {
        var request = With(r => r.Amount = amount);

        _validator.TestValidate(request).ShouldNotHaveValidationErrorFor(r => r.Amount);
    }

    // CVV

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("12")]
    [InlineData("12345")]
    [InlineData("12a")]
    [InlineData("1 3")]
    [InlineData("١٢٣")]     // Arabic-Indic digits
    public void Rejects_an_invalid_cvv(string? cvv)
    {
        var request = With(r => r.Cvv = cvv);

        _validator.TestValidate(request).ShouldHaveValidationErrorFor(r => r.Cvv);
    }

    [Theory]
    [InlineData("123")]
    [InlineData("1234")]
    [InlineData("012")]    // leading zero is preserved because CVV is a string
    public void Accepts_a_valid_cvv(string cvv)
    {
        var request = With(r => r.Cvv = cvv);

        _validator.TestValidate(request).ShouldNotHaveValidationErrorFor(r => r.Cvv);
    }

    // All fields together

    [Fact]
    public void Reports_every_invalid_field_at_once()
    {
        var request = new PostPaymentRequest();

        var result = _validator.TestValidate(request);

        result.ShouldHaveValidationErrorFor(r => r.CardNumber);
        result.ShouldHaveValidationErrorFor(r => r.ExpiryMonth);
        result.ShouldHaveValidationErrorFor(r => r.ExpiryYear);
        result.ShouldHaveValidationErrorFor(r => r.Currency);
        result.ShouldHaveValidationErrorFor(r => r.Amount);
        result.ShouldHaveValidationErrorFor(r => r.Cvv);
    }

    [Fact]
    public void Error_messages_never_contain_the_card_number_or_cvv()
    {
        const string invalidCardNumber = "4242abcd42424242";
        const string invalidCvv = "98x";
        var request = With(r =>
        {
            r.CardNumber = invalidCardNumber;
            r.Cvv = invalidCvv;
        });

        var result = _validator.TestValidate(request);

        Assert.NotEmpty(result.Errors);
        Assert.All(result.Errors, error =>
        {
            Assert.DoesNotContain(invalidCardNumber, error.ErrorMessage);
            Assert.DoesNotContain(invalidCvv, error.ErrorMessage);
        });
    }

    private static PostPaymentRequest With(Action<PostPaymentRequest> change)
    {
        var request = TestData.ValidRequest();
        change(request);
        return request;
    }
}
