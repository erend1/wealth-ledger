using WealthLedger.Domain.ValueObjects;
using WealthLedger.Application.Navigation;

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
    private readonly INavigationScopeReadStore _scopeReadStore;

    public GetPositionUseCase(
        IPostedEntrySource entrySource,
        INavigationScopeReadStore scopeReadStore)
    {
        _entrySource = entrySource
            ?? throw new ArgumentNullException(nameof(entrySource));
        _scopeReadStore = scopeReadStore
            ?? throw new ArgumentNullException(nameof(scopeReadStore));
    }

    public async Task<GetPositionResult> ExecuteAsync(
        GetPositionQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        if (query.HouseholdId == Guid.Empty
            || query.PortfolioId == Guid.Empty
            || query.AccountId == Guid.Empty
            || query.AssetId == Guid.Empty)
        {
            throw new PositionScopeNotFoundException();
        }

        if (!await _scopeReadStore.PositionScopeExistsAsync(
                query.HouseholdId,
                query.PortfolioId,
                query.AccountId,
                query.AssetId,
                cancellationToken))
        {
            throw new PositionScopeNotFoundException();
        }

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

}
