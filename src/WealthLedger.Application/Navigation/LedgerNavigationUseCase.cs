namespace WealthLedger.Application.Navigation;

public sealed class ListRecentLedgerTransactionsUseCase
{
    private readonly ILedgerNavigationReadStore _readStore;
    private readonly INavigationScopeReadStore _scopeReadStore;

    public ListRecentLedgerTransactionsUseCase(
        ILedgerNavigationReadStore readStore,
        INavigationScopeReadStore scopeReadStore)
    {
        _readStore = readStore
            ?? throw new ArgumentNullException(nameof(readStore));
        _scopeReadStore = scopeReadStore
            ?? throw new ArgumentNullException(nameof(scopeReadStore));
    }

    public async Task<NavigationPage<RecentLedgerTransactionNavigationItem>>
        ExecuteAsync(
            ListRecentLedgerTransactionsQuery query,
            CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        NavigationQueryValidation.EnsureHouseholdId(query.HouseholdId);
        NavigationQueryValidation.EnsurePageSize(query.PageSize);
        var after = NavigationCursorCodec.DecodeRecentLedger(
            query.Cursor,
            query.HouseholdId);

        await NavigationScopeValidation.EnsureHouseholdExistsAsync(
            _scopeReadStore,
            query.HouseholdId,
            cancellationToken);
        var rows = await _readStore.ListRecentPostedTransactionsAsync(
            query.HouseholdId,
            query.PageSize + 1,
            after,
            cancellationToken);

        return NavigationPageFactory.Create(
            rows,
            query.PageSize,
            item => NavigationCursorCodec.EncodeRecentLedger(
                query.HouseholdId,
                new RecentLedgerNavigationKey(
                    item.PostedAtUtc,
                    item.TransactionId)));
    }
}
