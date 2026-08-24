using WealthLedger.Domain.ValueObjects;

namespace WealthLedger.Application.Positions;

public sealed record GetPositionQuery(
    Guid HouseholdId,
    Guid PortfolioId,
    Guid AccountId,
    Guid AssetId);

public sealed record GetPositionResult(
    Guid HouseholdId,
    Guid PortfolioId,
    Guid AccountId,
    Guid AssetId,
    QuantityDelta Quantity,
    int SourceEntryCount);

public sealed class GetPositionUseCase
{
    private readonly IPostedEntrySource _entrySource;

    public GetPositionUseCase(IPostedEntrySource entrySource)
    {
        _entrySource = entrySource
            ?? throw new ArgumentNullException(nameof(entrySource));
    }

    public async Task<GetPositionResult> ExecuteAsync(
        GetPositionQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        EnsureNonEmpty(query.HouseholdId, nameof(query.HouseholdId));
        EnsureNonEmpty(query.PortfolioId, nameof(query.PortfolioId));
        EnsureNonEmpty(query.AccountId, nameof(query.AccountId));
        EnsureNonEmpty(query.AssetId, nameof(query.AssetId));

        var entries = await _entrySource.ListPositionEntriesAsync(
            query.HouseholdId,
            query.PortfolioId,
            query.AccountId,
            query.AssetId,
            cancellationToken);

        var quantity = QuantityDelta.Zero;

        foreach (var entry in entries
                     .OrderBy(x => x.ExecutionDate)
                     .ThenBy(x => x.TransactionCreatedAtUtc)
                     .ThenBy(x => x.TransactionId)
                     .ThenBy(x => x.EntrySequence))
        {
            quantity = quantity.Add(entry.QuantityDelta);
        }

        return new GetPositionResult(
            query.HouseholdId,
            query.PortfolioId,
            query.AccountId,
            query.AssetId,
            quantity,
            entries.Count);
    }

    private static void EnsureNonEmpty(Guid value, string parameterName)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException(
                $"{parameterName} cannot be empty.",
                parameterName);
        }
    }
}
