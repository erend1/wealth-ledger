using System.Text;
using WealthLedger.Application.Navigation;

namespace WealthLedger.Application.Tests.Navigation;

public sealed class NavigationUseCaseTests
{
    private static readonly Guid HouseholdId =
        Guid.Parse("10000000-0000-0000-0000-000000000001");
    private static readonly DateTimeOffset CreatedAtUtc =
        new(2026, 9, 2, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Navigation_HouseholdsFirstContinuationFinalAndEmptyPages()
    {
        var items = new[]
        {
            CreateHousehold("10000000-0000-0000-0000-000000000001"),
            CreateHousehold("10000000-0000-0000-0000-000000000002"),
            CreateHousehold("10000000-0000-0000-0000-000000000003")
        };
        var store = new MasterStoreFake
        {
            ListHouseholds = (_, after) => items
                .Where(
                    item => after is null
                            || item.CreatedAtUtc > after.CreatedAtUtc
                            || (item.CreatedAtUtc == after.CreatedAtUtc
                                && item.HouseholdId.CompareTo(after.Id) > 0))
                .ToArray()
        };

        var first = await new ListHouseholdsUseCase(store).ExecuteAsync(
            new ListHouseholdsQuery(PageSize: 2));
        var restarted = await new ListHouseholdsUseCase(store).ExecuteAsync(
            new ListHouseholdsQuery(PageSize: 2, Cursor: first.NextCursor));
        store.ListHouseholds = (_, _) => [];
        var empty = await new ListHouseholdsUseCase(store).ExecuteAsync(
            new ListHouseholdsQuery(PageSize: 2));

        Assert.Equal(items[..2], first.Items);
        Assert.NotNull(first.NextCursor);
        Assert.Equal([items[2]], restarted.Items);
        Assert.Null(restarted.NextCursor);
        Assert.Empty(empty.Items);
        Assert.Null(empty.NextCursor);
        Assert.Equal(3, store.CallCount);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(101)]
    public async Task Navigation_PageSizeInvalid_PreventsPersistenceAccess(
        int pageSize)
    {
        var store = new MasterStoreFake();

        var exception = await Assert.ThrowsAsync<NavigationRequestException>(
            () => new ListAssetsUseCase(store).ExecuteAsync(
                new ListAssetsQuery(PageSize: pageSize)));

        Assert.Equal(
            NavigationRequestException.PageSizeInvalidCode,
            exception.ErrorCode);
        Assert.Equal(0, store.CallCount);
    }

    [Theory]
    [MemberData(nameof(InvalidCursorCases))]
    public async Task Navigation_InvalidCursor_PreventsPersistenceAccess(
        string cursor)
    {
        var store = new MasterStoreFake();

        var exception = await Assert.ThrowsAsync<NavigationRequestException>(
            () => new ListHouseholdsUseCase(store).ExecuteAsync(
                new ListHouseholdsQuery(Cursor: cursor)));

        Assert.Equal(
            NavigationRequestException.CursorInvalidCode,
            exception.ErrorCode);
        Assert.Equal(0, store.CallCount);
    }

    [Fact]
    public async Task Navigation_ResourceMismatchedCursor_PreventsPersistenceAccess()
    {
        var cursor = NavigationCursorCodec.EncodeCreatedAt(
            NavigationCursorResources.Households,
            householdId: null,
            includeInactive: null,
            new NavigationCreatedAtKey(CreatedAtUtc, HouseholdId));
        var store = new MasterStoreFake();

        var exception = await Assert.ThrowsAsync<NavigationRequestException>(
            () => new ListCurrenciesUseCase(store).ExecuteAsync(
                new ListCurrenciesQuery(Cursor: cursor)));

        Assert.Equal(
            NavigationRequestException.CursorScopeMismatchCode,
            exception.ErrorCode);
        Assert.Equal(0, store.CallCount);
    }

    [Fact]
    public async Task Navigation_HouseholdOrFilterMismatchedCursor_PreventsScopeRead()
    {
        var cursor = NavigationCursorCodec.EncodeCreatedAt(
            NavigationCursorResources.HouseholdMembers,
            HouseholdId,
            includeInactive: false,
            new NavigationCreatedAtKey(CreatedAtUtc, Guid.NewGuid()));
        var store = new MasterStoreFake();
        var scope = new ScopeStoreFake(householdExists: true);

        var householdMismatch = await Assert.ThrowsAsync<NavigationRequestException>(
            () => new ListHouseholdMembersUseCase(store, scope).ExecuteAsync(
                new ListHouseholdMembersQuery(
                    Guid.NewGuid(),
                    Cursor: cursor)));
        var filterMismatch = await Assert.ThrowsAsync<NavigationRequestException>(
            () => new ListHouseholdMembersUseCase(store, scope).ExecuteAsync(
                new ListHouseholdMembersQuery(
                    HouseholdId,
                    Cursor: cursor,
                    IncludeInactive: true)));

        Assert.Equal(
            NavigationRequestException.CursorScopeMismatchCode,
            householdMismatch.ErrorCode);
        Assert.Equal(
            NavigationRequestException.CursorScopeMismatchCode,
            filterMismatch.ErrorCode);
        Assert.Equal(0, scope.HouseholdCallCount);
        Assert.Equal(0, store.CallCount);
    }

    [Fact]
    public async Task Navigation_UnknownHouseholdDiffersFromKnownEmptyCollection()
    {
        var store = new MasterStoreFake();
        var missingScope = new ScopeStoreFake(householdExists: false);

        await Assert.ThrowsAsync<HouseholdNotFoundException>(
            () => new ListPortfoliosUseCase(store, missingScope).ExecuteAsync(
                new ListPortfoliosQuery(HouseholdId)));

        var knownScope = new ScopeStoreFake(householdExists: true);
        var result = await new ListPortfoliosUseCase(store, knownScope)
            .ExecuteAsync(new ListPortfoliosQuery(HouseholdId));

        Assert.Empty(result.Items);
        Assert.Null(result.NextCursor);
        Assert.Equal(1, missingScope.HouseholdCallCount);
        Assert.Equal(1, knownScope.HouseholdCallCount);
        Assert.Equal(1, store.CallCount);
    }

    [Fact]
    public async Task Navigation_IncludeInactiveIsBoundToCursorAndForwarded()
    {
        var store = new MasterStoreFake();

        await new ListInstitutionsUseCase(store).ExecuteAsync(
            new ListInstitutionsQuery(IncludeInactive: true));
        await new ListAssetsUseCase(store).ExecuteAsync(
            new ListAssetsQuery(IncludeInactive: true));

        Assert.Equal([true, true], store.IncludeInactiveValues);
    }

    [Fact]
    public async Task Navigation_CancellationPropagatesWithoutReturningAPartialPage()
    {
        var store = new MasterStoreFake();
        var scope = new ScopeStoreFake(householdExists: true);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => new ListHouseholdMembersUseCase(store, scope).ExecuteAsync(
                new ListHouseholdMembersQuery(HouseholdId),
                cancellation.Token));

        Assert.Equal(0, scope.HouseholdCallCount);
        Assert.Equal(0, store.CallCount);
    }

    [Fact]
    public async Task Navigation_RecentLedgerCursorIsRestartSafeAndHouseholdBound()
    {
        var transaction = new RecentLedgerTransactionNavigationItem(
            Guid.Parse("90000000-0000-0000-0000-000000000001"),
            HouseholdId,
            Domain.Ledger.TransactionType.Contribution,
            Domain.Ledger.TransactionStatus.Posted,
            OrderDate: null,
            new DateOnly(2026, 9, 1),
            SettlementDate: null,
            ExternalReference: null,
            ReversalOfTransactionId: null,
            ReversedByTransactionId: null,
            CreatedAtUtc,
            CreatedAtUtc.AddMinutes(1),
            EntryEffects: []);
        var ledgerStore = new LedgerStoreFake([transaction, transaction]);
        var scope = new ScopeStoreFake(householdExists: true);
        var first = await new ListRecentLedgerTransactionsUseCase(
                ledgerStore,
                scope)
            .ExecuteAsync(
                new ListRecentLedgerTransactionsQuery(
                    HouseholdId,
                    PageSize: 1));
        var restartedStore = new LedgerStoreFake([]);

        await new ListRecentLedgerTransactionsUseCase(restartedStore, scope)
            .ExecuteAsync(
                new ListRecentLedgerTransactionsQuery(
                    HouseholdId,
                    PageSize: 1,
                    Cursor: first.NextCursor));

        Assert.NotNull(first.NextCursor);
        Assert.Equal(
            transaction.PostedAtUtc,
            restartedStore.After!.PostedAtUtc);
        Assert.Equal(
            transaction.TransactionId,
            restartedStore.After.TransactionId);

        var mismatch = await Assert.ThrowsAsync<NavigationRequestException>(
            () => new ListRecentLedgerTransactionsUseCase(
                    restartedStore,
                    scope)
                .ExecuteAsync(
                    new ListRecentLedgerTransactionsQuery(
                        Guid.NewGuid(),
                        Cursor: first.NextCursor)));
        Assert.Equal(
            NavigationRequestException.CursorScopeMismatchCode,
            mismatch.ErrorCode);
    }

    public static TheoryData<string> InvalidCursorCases()
        => new()
        {
            "not+base64",
            new('A', 1_025),
            EncodeJson(
                "{\"v\":2,\"r\":\"HOUSEHOLDS\",\"h\":null,\"f\":null,"
                + "\"t\":\"2026-09-02T08:00:00.0000000+00:00\","
                + "\"c\":null,\"i\":\"10000000-0000-0000-0000-000000000001\"}"),
            EncodeJson(
                "{\"v\":1,\"r\":\"HOUSEHOLDS\",\"h\":null,\"f\":null,"
                + "\"t\":\"invalid\",\"c\":null,"
                + "\"i\":\"10000000-0000-0000-0000-000000000001\"}"),
            EncodeJson(
                "{\"v\":1,\"r\":\"HOUSEHOLDS\",\"h\":null,\"f\":null,"
                + "\"t\":\"2026-09-02T08:00:00.0000000+00:00\","
                + "\"c\":null,\"i\":\"00000000-0000-0000-0000-000000000000\"}")
        };

    private static HouseholdNavigationItem CreateHousehold(string id)
        => new(
            Guid.Parse(id),
            "Synthetic Household",
            new CurrencyNavigationItem(
                "TRY",
                "Synthetic Currency",
                MinorUnitDigits: 2),
            CreatedAtUtc);

    private static string EncodeJson(string json)
        => Convert.ToBase64String(Encoding.UTF8.GetBytes(json))
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');

    private sealed class MasterStoreFake : IMasterNavigationReadStore
    {
        internal Func<int, NavigationCreatedAtKey?,
            IReadOnlyList<HouseholdNavigationItem>> ListHouseholds
        {
            get;
            set;
        } = (_, _) => [];

        internal int CallCount { get; private set; }

        internal List<bool> IncludeInactiveValues { get; } = [];

        public Task<IReadOnlyList<HouseholdNavigationItem>> ListHouseholdsAsync(
            int take,
            NavigationCreatedAtKey? after,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            CallCount++;
            return Task.FromResult(ListHouseholds(take, after));
        }

        public Task<HouseholdNavigationItem?> FindHouseholdAsync(
            Guid householdId,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            CallCount++;
            return Task.FromResult<HouseholdNavigationItem?>(null);
        }

        public Task<IReadOnlyList<HouseholdMemberNavigationItem>>
            ListHouseholdMembersAsync(
                Guid householdId,
                bool includeInactive,
                int take,
                NavigationCreatedAtKey? after,
                CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            CallCount++;
            IncludeInactiveValues.Add(includeInactive);
            return Task.FromResult<IReadOnlyList<HouseholdMemberNavigationItem>>([]);
        }

        public Task<IReadOnlyList<InstitutionNavigationItem>>
            ListInstitutionsAsync(
                bool includeInactive,
                int take,
                NavigationCodeKey? after,
                CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            CallCount++;
            IncludeInactiveValues.Add(includeInactive);
            return Task.FromResult<IReadOnlyList<InstitutionNavigationItem>>([]);
        }

        public Task<IReadOnlyList<PortfolioNavigationItem>> ListPortfoliosAsync(
            Guid householdId,
            bool includeInactive,
            int take,
            NavigationCodeKey? after,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            CallCount++;
            IncludeInactiveValues.Add(includeInactive);
            return Task.FromResult<IReadOnlyList<PortfolioNavigationItem>>([]);
        }

        public Task<IReadOnlyList<AccountNavigationItem>> ListAccountsAsync(
            Guid householdId,
            bool includeInactive,
            int take,
            NavigationCodeKey? after,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            CallCount++;
            IncludeInactiveValues.Add(includeInactive);
            return Task.FromResult<IReadOnlyList<AccountNavigationItem>>([]);
        }

        public Task<IReadOnlyList<CurrencyNavigationItem>> ListCurrenciesAsync(
            int take,
            NavigationCurrencyKey? after,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            CallCount++;
            return Task.FromResult<IReadOnlyList<CurrencyNavigationItem>>([]);
        }

        public Task<IReadOnlyList<AssetNavigationItem>> ListAssetsAsync(
            bool includeInactive,
            int take,
            NavigationCodeKey? after,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            CallCount++;
            IncludeInactiveValues.Add(includeInactive);
            return Task.FromResult<IReadOnlyList<AssetNavigationItem>>([]);
        }
    }

    private sealed class ScopeStoreFake : INavigationScopeReadStore
    {
        private readonly bool _householdExists;

        internal ScopeStoreFake(bool householdExists)
        {
            _householdExists = householdExists;
        }

        internal int HouseholdCallCount { get; private set; }

        public Task<bool> HouseholdExistsAsync(
            Guid householdId,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            HouseholdCallCount++;
            return Task.FromResult(_householdExists);
        }

        public Task<bool> PositionScopeExistsAsync(
            Guid householdId,
            Guid portfolioId,
            Guid accountId,
            Guid assetId,
            CancellationToken cancellationToken = default)
            => Task.FromResult(false);
    }

    private sealed class LedgerStoreFake : ILedgerNavigationReadStore
    {
        private readonly IReadOnlyList<RecentLedgerTransactionNavigationItem>
            _items;

        internal LedgerStoreFake(
            IReadOnlyList<RecentLedgerTransactionNavigationItem> items)
        {
            _items = items;
        }

        internal RecentLedgerNavigationKey? After { get; private set; }

        public Task<IReadOnlyList<RecentLedgerTransactionNavigationItem>>
            ListRecentPostedTransactionsAsync(
                Guid householdId,
                int take,
                RecentLedgerNavigationKey? after,
                CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            After = after;
            return Task.FromResult(_items);
        }
    }
}
