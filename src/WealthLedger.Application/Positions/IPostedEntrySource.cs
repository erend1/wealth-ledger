using WealthLedger.Domain.ValueObjects;

namespace WealthLedger.Application.Positions;

public interface IPostedEntrySource
{
    Task<IReadOnlyList<PostedEntryFact>> ListPositionEntriesAsync(
        Guid householdId,
        Guid portfolioId,
        Guid accountId,
        Guid assetId,
        CancellationToken cancellationToken = default);
}

public sealed record PostedEntryFact(
    Guid TransactionId,
    DateOnly ExecutionDate,
    DateTimeOffset TransactionCreatedAtUtc,
    int EntrySequence,
    QuantityDelta QuantityDelta);
