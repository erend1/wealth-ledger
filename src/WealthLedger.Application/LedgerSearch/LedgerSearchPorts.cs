namespace WealthLedger.Application.LedgerSearch;

public interface ILedgerSearchReadStore
{
    Task<bool> HouseholdExistsAsync(
        Guid householdId,
        CancellationToken cancellationToken = default);

    Task<bool> FilterScopeExistsAsync(
        Guid householdId,
        Guid? portfolioId,
        Guid? accountId,
        Guid? assetId,
        Guid? institutionId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<LedgerSearchTransactionItem>> SearchPostedTransactionsAsync(
        Guid householdId,
        LedgerSearchNormalizedFilters filters,
        int take,
        LedgerSearchCursorKey? after,
        CancellationToken cancellationToken = default);
}
