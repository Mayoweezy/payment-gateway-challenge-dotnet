using PaymentGateway.Api.Models;

namespace PaymentGateway.Api.Data;

public interface IPaymentsRepository
{
    void Add(Payment payment);

    Payment? Get(Guid id);
}
