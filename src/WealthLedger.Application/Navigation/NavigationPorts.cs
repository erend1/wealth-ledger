namespace WealthLedger.Application.Navigation;

public interface IMasterNavigationReadStore
{
    Task<IReadOnlyList<HouseholdNavigationItem>> ListHouseholdsAsync(
        int take,
        NavigationCreatedAtKey? after,
        CancellationToken cancellationToken = default);

    Task<HouseholdNavigationItem?> FindHouseholdAsync(
        Guid householdId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<HouseholdMemberNavigationItem>> ListHouseholdMembersAsync(
        Guid householdId,
        bool includeInactive,
        int take,
        NavigationCreatedAtKey? after,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<InstitutionNavigationItem>> ListInstitutionsAsync(
        bool includeInactive,
        int take,
        NavigationCodeKey? after,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<PortfolioNavigationItem>> ListPortfoliosAsync(
        Guid householdId,
        bool includeInactive,
        int take,
        NavigationCodeKey? after,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<AccountNavigationItem>> ListAccountsAsync(
        Guid householdId,
        bool includeInactive,
        int take,
        NavigationCodeKey? after,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<CurrencyNavigationItem>> ListCurrenciesAsync(
        int take,
        NavigationCurrencyKey? after,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<AssetNavigationItem>> ListAssetsAsync(
        bool includeInactive,
        int take,
        NavigationCodeKey? after,
        CancellationToken cancellationToken = default);
}

public interface INavigationScopeReadStore
{
    Task<bool> HouseholdExistsAsync(
        Guid householdId,
        CancellationToken cancellationToken = default);

    Task<bool> PositionScopeExistsAsync(
        Guid householdId,
        Guid portfolioId,
        Guid accountId,
        Guid assetId,
        CancellationToken cancellationToken = default);
}

public interface ILedgerNavigationReadStore
{
    Task<IReadOnlyList<RecentLedgerTransactionNavigationItem>>
        ListRecentPostedTransactionsAsync(
            Guid householdId,
            int take,
            RecentLedgerNavigationKey? after,
            CancellationToken cancellationToken = default);
}
