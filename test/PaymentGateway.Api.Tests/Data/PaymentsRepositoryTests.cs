using PaymentGateway.Api.Data;
using PaymentGateway.Api.Tests.TestSupport;

namespace PaymentGateway.Api.Tests.Data;

public class PaymentsRepositoryTests
{
    private readonly PaymentsRepository _repository = new();

    [Fact]
    public void Returns_a_payment_that_was_added()
    {
        var payment = TestData.Payment();

        _repository.Add(payment);

        Assert.Equal(payment, _repository.Get(payment.Id));
    }

    [Fact]
    public void Returns_null_for_an_unknown_id()
    {
        Assert.Null(_repository.Get(Guid.NewGuid()));
    }

    [Fact]
    public void Concurrent_adds_are_all_stored()
    {
        var payments = Enumerable.Range(0, 1_000).Select(_ => TestData.Payment()).ToList();

        Parallel.ForEach(payments, _repository.Add);

        Assert.All(payments, payment => Assert.Equal(payment, _repository.Get(payment.Id)));
    }
}
