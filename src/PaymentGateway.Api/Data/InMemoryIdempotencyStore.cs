using System.Collections.Concurrent;

using PaymentGateway.Api.Models;

namespace PaymentGateway.Api.Data;

/// <summary>In-memory, thread-safe idempotency key store. Keys are global and never expire.</summary>
public sealed class InMemoryIdempotencyStore : IIdempotencyStore
{
    private readonly ConcurrentDictionary<string, Entry> _entries = new(StringComparer.Ordinal);

    public IdempotencyReservation TryReserve(string key, PaymentFingerprint fingerprint)
    {
        var reservation = new Entry(fingerprint, EntryState.InProgress, Payment: null);

        // Loop because a concurrent Release can remove the entry between TryAdd and TryGetValue.
        while (true)
        {
            if (_entries.TryAdd(key, reservation))
            {
                return new IdempotencyReservation.Reserved();
            }

            if (_entries.TryGetValue(key, out var existing))
            {
                return Describe(existing, fingerprint);
            }
        }
    }

    public void Complete(string key, Payment payment) =>
        Transition(key, entry => entry with { State = EntryState.Completed, Payment = payment });

    public void Release(string key) => _entries.TryRemove(key, out _);

    public void MarkOutcomeUnknown(string key) =>
        Transition(key, entry => entry with { State = EntryState.OutcomeUnknown });

    private static IdempotencyReservation Describe(Entry existing, PaymentFingerprint fingerprint)
    {
        if (existing.Fingerprint != fingerprint)
        {
            return new IdempotencyReservation.Mismatch();
        }

        return existing.State switch
        {
            EntryState.InProgress => new IdempotencyReservation.InProgress(),
            EntryState.Completed => new IdempotencyReservation.Completed(existing.Payment!),
            EntryState.OutcomeUnknown => new IdempotencyReservation.OutcomeUnknown(),
            _ => throw new InvalidOperationException($"Unknown idempotency state {existing.State}.")
        };
    }

    private void Transition(string key, Func<Entry, Entry> change)
    {
        // Only the caller that reserved the key transitions it, so a plain read-then-write is safe.
        if (_entries.TryGetValue(key, out var entry))
        {
            _entries[key] = change(entry);
        }
    }

    private enum EntryState
    {
        InProgress,
        Completed,
        OutcomeUnknown
    }

    private sealed record Entry(PaymentFingerprint Fingerprint, EntryState State, Payment? Payment);
}
