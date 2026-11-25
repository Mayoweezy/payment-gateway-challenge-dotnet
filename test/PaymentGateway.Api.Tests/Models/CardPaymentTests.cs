using PaymentGateway.Api.Models;
using PaymentGateway.Api.Tests.TestSupport;

namespace PaymentGateway.Api.Tests.Models;

public class CardPaymentTests
{
    [Fact]
    public void Copies_every_field_from_a_valid_request()
    {
        var payment = CardPayment.FromValidRequest(TestData.ValidRequest());

        Assert.Equal(TestData.CardNumber, payment.CardNumber);
        Assert.Equal(TestData.CardNumberLastFour, payment.CardNumberLastFour);
        Assert.Equal(4, payment.ExpiryMonth);
        Assert.Equal(2027, payment.ExpiryYear);
        Assert.Equal("GBP", payment.Currency);
        Assert.Equal(100, payment.Amount);
        Assert.Equal(TestData.Cvv, payment.Cvv);
    }

    [Fact]
    public void Refuses_a_request_that_was_not_validated()
    {
        var request = TestData.ValidRequest();
        request.Amount = null;

        Assert.Throws<InvalidOperationException>(() => CardPayment.FromValidRequest(request));
    }

    [Fact]
    public void ToString_never_prints_the_card_number_or_cvv()
    {
        var text = TestData.ValidPayment().ToString();

        Assert.DoesNotContain(TestData.CardNumber, text);
        Assert.DoesNotContain(TestData.Cvv, text);
    }
}
