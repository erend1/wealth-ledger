namespace WealthLedger.Application.Navigation;

public sealed class ListHouseholdsUseCase
{
    private readonly IMasterNavigationReadStore _readStore;

    public ListHouseholdsUseCase(IMasterNavigationReadStore readStore)
    {
        _readStore = readStore
            ?? throw new ArgumentNullException(nameof(readStore));
    }

    public async Task<NavigationPage<HouseholdNavigationItem>> ExecuteAsync(
        ListHouseholdsQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        NavigationQueryValidation.EnsurePageSize(query.PageSize);
        var after = NavigationCursorCodec.DecodeCreatedAt(
            query.Cursor,
            NavigationCursorResources.Households,
            expectedHouseholdId: null,
            expectedIncludeInactive: null);
        var rows = await _readStore.ListHouseholdsAsync(
            query.PageSize + 1,
            after,
            cancellationToken);

        return NavigationPageFactory.Create(
            rows,
            query.PageSize,
            item => NavigationCursorCodec.EncodeCreatedAt(
                NavigationCursorResources.Households,
                householdId: null,
                includeInactive: null,
                new NavigationCreatedAtKey(
                    item.CreatedAtUtc,
                    item.HouseholdId)));
    }
}

public sealed class GetHouseholdUseCase
{
    private readonly IMasterNavigationReadStore _readStore;

    public GetHouseholdUseCase(IMasterNavigationReadStore readStore)
    {
        _readStore = readStore
            ?? throw new ArgumentNullException(nameof(readStore));
    }

    public async Task<HouseholdNavigationItem> ExecuteAsync(
        GetHouseholdQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        NavigationQueryValidation.EnsureHouseholdId(query.HouseholdId);

        return await _readStore.FindHouseholdAsync(
                   query.HouseholdId,
                   cancellationToken)
               ?? throw new HouseholdNotFoundException();
    }
}

public sealed class ListHouseholdMembersUseCase
{
    private readonly IMasterNavigationReadStore _readStore;
    private readonly INavigationScopeReadStore _scopeReadStore;

    public ListHouseholdMembersUseCase(
        IMasterNavigationReadStore readStore,
        INavigationScopeReadStore scopeReadStore)
    {
        _readStore = readStore
            ?? throw new ArgumentNullException(nameof(readStore));
        _scopeReadStore = scopeReadStore
            ?? throw new ArgumentNullException(nameof(scopeReadStore));
    }

    public async Task<NavigationPage<HouseholdMemberNavigationItem>>
        ExecuteAsync(
            ListHouseholdMembersQuery query,
            CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        NavigationQueryValidation.EnsureHouseholdId(query.HouseholdId);
        NavigationQueryValidation.EnsurePageSize(query.PageSize);
        var after = NavigationCursorCodec.DecodeCreatedAt(
            query.Cursor,
            NavigationCursorResources.HouseholdMembers,
            query.HouseholdId,
            query.IncludeInactive);

        await NavigationScopeValidation.EnsureHouseholdExistsAsync(
            _scopeReadStore,
            query.HouseholdId,
            cancellationToken);
        var rows = await _readStore.ListHouseholdMembersAsync(
            query.HouseholdId,
            query.IncludeInactive,
            query.PageSize + 1,
            after,
            cancellationToken);

        return NavigationPageFactory.Create(
            rows,
            query.PageSize,
            item => NavigationCursorCodec.EncodeCreatedAt(
                NavigationCursorResources.HouseholdMembers,
                query.HouseholdId,
                query.IncludeInactive,
                new NavigationCreatedAtKey(
                    item.CreatedAtUtc,
                    item.HouseholdMemberId)));
    }
}

public sealed class ListInstitutionsUseCase
{
    private readonly IMasterNavigationReadStore _readStore;

    public ListInstitutionsUseCase(IMasterNavigationReadStore readStore)
    {
        _readStore = readStore
            ?? throw new ArgumentNullException(nameof(readStore));
    }

    public async Task<NavigationPage<InstitutionNavigationItem>> ExecuteAsync(
        ListInstitutionsQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        NavigationQueryValidation.EnsurePageSize(query.PageSize);
        var after = NavigationCursorCodec.DecodeCode(
            query.Cursor,
            NavigationCursorResources.Institutions,
            expectedHouseholdId: null,
            query.IncludeInactive);
        var rows = await _readStore.ListInstitutionsAsync(
            query.IncludeInactive,
            query.PageSize + 1,
            after,
            cancellationToken);

        return NavigationPageFactory.Create(
            rows,
            query.PageSize,
            item => NavigationCursorCodec.EncodeCode(
                NavigationCursorResources.Institutions,
                householdId: null,
                query.IncludeInactive,
                new NavigationCodeKey(item.Code, item.InstitutionId)));
    }
}

public sealed class ListPortfoliosUseCase
{
    private readonly IMasterNavigationReadStore _readStore;
    private readonly INavigationScopeReadStore _scopeReadStore;

    public ListPortfoliosUseCase(
        IMasterNavigationReadStore readStore,
        INavigationScopeReadStore scopeReadStore)
    {
        _readStore = readStore
            ?? throw new ArgumentNullException(nameof(readStore));
        _scopeReadStore = scopeReadStore
            ?? throw new ArgumentNullException(nameof(scopeReadStore));
    }

    public async Task<NavigationPage<PortfolioNavigationItem>> ExecuteAsync(
        ListPortfoliosQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        NavigationQueryValidation.EnsureHouseholdId(query.HouseholdId);
        NavigationQueryValidation.EnsurePageSize(query.PageSize);
        var after = NavigationCursorCodec.DecodeCode(
            query.Cursor,
            NavigationCursorResources.Portfolios,
            query.HouseholdId,
            query.IncludeInactive);

        await NavigationScopeValidation.EnsureHouseholdExistsAsync(
            _scopeReadStore,
            query.HouseholdId,
            cancellationToken);
        var rows = await _readStore.ListPortfoliosAsync(
            query.HouseholdId,
            query.IncludeInactive,
            query.PageSize + 1,
            after,
            cancellationToken);

        return NavigationPageFactory.Create(
            rows,
            query.PageSize,
            item => NavigationCursorCodec.EncodeCode(
                NavigationCursorResources.Portfolios,
                query.HouseholdId,
                query.IncludeInactive,
                new NavigationCodeKey(item.Code, item.PortfolioId)));
    }
}

public sealed class ListAccountsUseCase
{
    private readonly IMasterNavigationReadStore _readStore;
    private readonly INavigationScopeReadStore _scopeReadStore;

    public ListAccountsUseCase(
        IMasterNavigationReadStore readStore,
        INavigationScopeReadStore scopeReadStore)
    {
        _readStore = readStore
            ?? throw new ArgumentNullException(nameof(readStore));
        _scopeReadStore = scopeReadStore
            ?? throw new ArgumentNullException(nameof(scopeReadStore));
    }

    public async Task<NavigationPage<AccountNavigationItem>> ExecuteAsync(
        ListAccountsQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        NavigationQueryValidation.EnsureHouseholdId(query.HouseholdId);
        NavigationQueryValidation.EnsurePageSize(query.PageSize);
        var after = NavigationCursorCodec.DecodeCode(
            query.Cursor,
            NavigationCursorResources.Accounts,
            query.HouseholdId,
            query.IncludeInactive);

        await NavigationScopeValidation.EnsureHouseholdExistsAsync(
            _scopeReadStore,
            query.HouseholdId,
            cancellationToken);
        var rows = await _readStore.ListAccountsAsync(
            query.HouseholdId,
            query.IncludeInactive,
            query.PageSize + 1,
            after,
            cancellationToken);

        return NavigationPageFactory.Create(
            rows,
            query.PageSize,
            item => NavigationCursorCodec.EncodeCode(
                NavigationCursorResources.Accounts,
                query.HouseholdId,
                query.IncludeInactive,
                new NavigationCodeKey(item.Code, item.AccountId)));
    }
}

public sealed class ListCurrenciesUseCase
{
    private readonly IMasterNavigationReadStore _readStore;

    public ListCurrenciesUseCase(IMasterNavigationReadStore readStore)
    {
        _readStore = readStore
            ?? throw new ArgumentNullException(nameof(readStore));
    }

    public async Task<NavigationPage<CurrencyNavigationItem>> ExecuteAsync(
        ListCurrenciesQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        NavigationQueryValidation.EnsurePageSize(query.PageSize);
        var after = NavigationCursorCodec.DecodeCurrency(query.Cursor);
        var rows = await _readStore.ListCurrenciesAsync(
            query.PageSize + 1,
            after,
            cancellationToken);

        return NavigationPageFactory.Create(
            rows,
            query.PageSize,
            item => NavigationCursorCodec.EncodeCurrency(
                new NavigationCurrencyKey(item.Code)));
    }
}

public sealed class ListAssetsUseCase
{
    private readonly IMasterNavigationReadStore _readStore;

    public ListAssetsUseCase(IMasterNavigationReadStore readStore)
    {
        _readStore = readStore
            ?? throw new ArgumentNullException(nameof(readStore));
    }

    public async Task<NavigationPage<AssetNavigationItem>> ExecuteAsync(
        ListAssetsQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        NavigationQueryValidation.EnsurePageSize(query.PageSize);
        var after = NavigationCursorCodec.DecodeCode(
            query.Cursor,
            NavigationCursorResources.Assets,
            expectedHouseholdId: null,
            query.IncludeInactive);
        var rows = await _readStore.ListAssetsAsync(
            query.IncludeInactive,
            query.PageSize + 1,
            after,
            cancellationToken);

        return NavigationPageFactory.Create(
            rows,
            query.PageSize,
            item => NavigationCursorCodec.EncodeCode(
                NavigationCursorResources.Assets,
                householdId: null,
                query.IncludeInactive,
                new NavigationCodeKey(item.Code, item.AssetId)));
    }
}

internal static class NavigationQueryValidation
{
    internal static void EnsurePageSize(int pageSize)
    {
        if (pageSize is < 1 or > 100)
        {
            throw new NavigationRequestException(
                NavigationRequestException.PageSizeInvalidCode,
                "Page size must be between 1 and 100.");
        }
    }

    internal static void EnsureHouseholdId(Guid householdId)
    {
        if (householdId == Guid.Empty)
        {
            throw new HouseholdNotFoundException();
        }
    }
}

internal static class NavigationScopeValidation
{
    internal static async Task EnsureHouseholdExistsAsync(
        INavigationScopeReadStore scopeReadStore,
        Guid householdId,
        CancellationToken cancellationToken)
    {
        if (!await scopeReadStore.HouseholdExistsAsync(
                householdId,
                cancellationToken))
        {
            throw new HouseholdNotFoundException();
        }
    }
}

internal static class NavigationPageFactory
{
    internal static NavigationPage<T> Create<T>(
        IReadOnlyList<T> rows,
        int pageSize,
        Func<T, string> encodeCursor)
    {
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(encodeCursor);

        if (rows.Count > pageSize + 1)
        {
            throw new NavigationPersistenceException(
                "A navigation query exceeded its requested bound.");
        }

        var hasMore = rows.Count > pageSize;
        var items = rows.Take(pageSize).ToArray();
        var nextCursor = hasMore
            ? encodeCursor(items[^1])
            : null;

        return new NavigationPage<T>(items, nextCursor);
    }
}
