using PaymentGateway.Api.Models;

namespace PaymentGateway.Api.Data;

public interface IIdempotencyStore
{
    /// <summary>Atomically reserves the key, or reports why it cannot be reserved.</summary>
    IdempotencyReservation TryReserve(string key, PaymentFingerprint fingerprint);

    /// <summary>Records the bank's definite answer so later requests with the key replay it.</summary>
    void Complete(string key, Payment payment);

    /// <summary>Frees the key because the bank definitely did not process the payment.</summary>
    void Release(string key);

    /// <summary>Locks the key because the bank outcome is unknown and a retry could double charge.</summary>
    void MarkOutcomeUnknown(string key);
}
