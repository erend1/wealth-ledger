using WealthLedger.Application.LedgerSearch;
using WealthLedger.Domain.Ledger;

namespace WealthLedger.Application.Tests.LedgerSearch;

public sealed class LedgerSearchUseCaseTests
{
    private static readonly Guid HouseholdId =
        Guid.Parse("10000000-0000-0000-0000-000000000001");
    private static readonly Guid AssetId =
        Guid.Parse("60000000-0000-0000-0000-000000000001");
    private static readonly DateTimeOffset PostedAtUtc =
        new(2026, 10, 2, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Search_NormalizesFiltersAndDefaultsToPostedHistory()
    {
        var store = new SearchStoreFake();
        var useCase = new SearchLedgerTransactionsUseCase(store);

        await useCase.ExecuteAsync(
            new SearchLedgerTransactionsQuery(
                HouseholdId,
                Types: [TransactionType.Sell, TransactionType.Buy, TransactionType.Buy],
                AssetId: AssetId,
                ExternalReferenceContains: "  abc-123  "));

        Assert.NotNull(store.Filters);
        Assert.Equal(
            [TransactionType.Buy, TransactionType.Sell],
            store.Filters.Types);
        Assert.Equal([TransactionStatus.Posted], store.Filters.Statuses);
        Assert.Equal(AssetId, store.Filters.AssetId);
        Assert.Equal("ABC-123", store.Filters.ExternalReferenceContains);
        Assert.Equal(64, store.Filters.Fingerprint.Length);
        Assert.Equal(1, store.HouseholdCallCount);
        Assert.Equal(1, store.FilterScopeCallCount);
        Assert.Equal(1, store.SearchCallCount);
    }

    [Theory]
    [InlineData(TransactionStatus.Draft)]
    [InlineData(TransactionStatus.Ordered)]
    [InlineData(TransactionStatus.Cancelled)]
    public async Task Search_NonPostedStatusIsRejectedBeforePersistence(
        TransactionStatus status)
    {
        var store = new SearchStoreFake();

        var exception = await Assert.ThrowsAsync<LedgerSearchRequestException>(
            () => new SearchLedgerTransactionsUseCase(store).ExecuteAsync(
                new SearchLedgerTransactionsQuery(
                    HouseholdId,
                    Statuses: [status])));

        Assert.Equal(
            LedgerSearchRequestException.FilterInvalidCode,
            exception.ErrorCode);
        Assert.Equal(0, store.TotalCallCount);
    }

    [Fact]
    public async Task Search_InvalidDateRangeIsRejectedBeforePersistence()
    {
        var store = new SearchStoreFake();

        var exception = await Assert.ThrowsAsync<LedgerSearchRequestException>(
            () => new SearchLedgerTransactionsUseCase(store).ExecuteAsync(
                new SearchLedgerTransactionsQuery(
                    HouseholdId,
                    ExecutedFrom: new DateOnly(2026, 10, 3),
                    ExecutedTo: new DateOnly(2026, 10, 2))));

        Assert.Equal(
            LedgerSearchRequestException.FilterInvalidCode,
            exception.ErrorCode);
        Assert.Equal(0, store.TotalCallCount);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(101)]
    public async Task Search_InvalidPageSizeIsRejectedBeforePersistence(
        int pageSize)
    {
        var store = new SearchStoreFake();

        var exception = await Assert.ThrowsAsync<LedgerSearchRequestException>(
            () => new SearchLedgerTransactionsUseCase(store).ExecuteAsync(
                new SearchLedgerTransactionsQuery(
                    HouseholdId,
                    PageSize: pageSize)));

        Assert.Equal(
            LedgerSearchRequestException.PageSizeInvalidCode,
            exception.ErrorCode);
        Assert.Equal(0, store.TotalCallCount);
    }

    [Fact]
    public async Task Search_FilterBoundCursorIsRestartSafeAndRejectsChangedFilters()
    {
        var firstItem = CreateItem(
            "90000000-0000-0000-0000-000000000003",
            new DateOnly(2026, 10, 2),
            PostedAtUtc.AddMinutes(2));
        var secondItem = CreateItem(
            "90000000-0000-0000-0000-000000000002",
            new DateOnly(2026, 10, 2),
            PostedAtUtc.AddMinutes(1));
        var firstStore = new SearchStoreFake([firstItem, secondItem]);
        var first = await new SearchLedgerTransactionsUseCase(firstStore)
            .ExecuteAsync(
                new SearchLedgerTransactionsQuery(
                    HouseholdId,
                    Types: [TransactionType.Buy],
                    PageSize: 1));

        Assert.Single(first.Items);
        Assert.NotNull(first.NextCursor);

        var restartedStore = new SearchStoreFake();
        await new SearchLedgerTransactionsUseCase(restartedStore)
            .ExecuteAsync(
                new SearchLedgerTransactionsQuery(
                    HouseholdId,
                    Types: [TransactionType.Buy],
                    PageSize: 1,
                    Cursor: first.NextCursor));

        Assert.NotNull(restartedStore.After);
        Assert.Equal(firstItem.ExecutionDate, restartedStore.After.ExecutionDate);
        Assert.Equal(firstItem.PostedAtUtc, restartedStore.After.PostedAtUtc);
        Assert.Equal(firstItem.TransactionId, restartedStore.After.TransactionId);

        var changedStore = new SearchStoreFake();
        var mismatch = await Assert.ThrowsAsync<LedgerSearchRequestException>(
            () => new SearchLedgerTransactionsUseCase(changedStore)
                .ExecuteAsync(
                    new SearchLedgerTransactionsQuery(
                        HouseholdId,
                        Types: [TransactionType.Sell],
                        PageSize: 1,
                        Cursor: first.NextCursor)));

        Assert.Equal(
            LedgerSearchRequestException.CursorScopeMismatchCode,
            mismatch.ErrorCode);
        Assert.Equal(0, changedStore.TotalCallCount);
    }

    [Fact]
    public async Task Search_UnknownHouseholdAndInvalidFilterScopeAreDistinct()
    {
        var missingHousehold = new SearchStoreFake
        {
            HouseholdExists = false
        };

        await Assert.ThrowsAsync<LedgerSearchHouseholdNotFoundException>(
            () => new SearchLedgerTransactionsUseCase(missingHousehold)
                .ExecuteAsync(new SearchLedgerTransactionsQuery(HouseholdId)));
        Assert.Equal(1, missingHousehold.HouseholdCallCount);
        Assert.Equal(0, missingHousehold.FilterScopeCallCount);
        Assert.Equal(0, missingHousehold.SearchCallCount);

        var missingFilter = new SearchStoreFake
        {
            FilterScopeExists = false
        };

        await Assert.ThrowsAsync<LedgerSearchFilterScopeNotFoundException>(
            () => new SearchLedgerTransactionsUseCase(missingFilter)
                .ExecuteAsync(
                    new SearchLedgerTransactionsQuery(
                        HouseholdId,
                        AssetId: AssetId)));
        Assert.Equal(1, missingFilter.HouseholdCallCount);
        Assert.Equal(1, missingFilter.FilterScopeCallCount);
        Assert.Equal(0, missingFilter.SearchCallCount);
    }

    [Fact]
    public async Task Search_CancellationPreventsPersistenceAccess()
    {
        var store = new SearchStoreFake();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => new SearchLedgerTransactionsUseCase(store)
                .ExecuteAsync(
                    new SearchLedgerTransactionsQuery(HouseholdId),
                    cancellation.Token));

        Assert.Equal(0, store.TotalCallCount);
    }

    private static LedgerSearchTransactionItem CreateItem(
        string transactionId,
        DateOnly executionDate,
        DateTimeOffset postedAtUtc)
        => new(
            Guid.Parse(transactionId),
            HouseholdId,
            TransactionType.Buy,
            TransactionStatus.Posted,
            OrderDate: null,
            executionDate,
            SettlementDate: null,
            ExternalReference: null,
            ReversalOfTransactionId: null,
            ReversedByTransactionId: null,
            postedAtUtc.AddMinutes(-1),
            postedAtUtc,
            EntryEffects: []);

    private sealed class SearchStoreFake : ILedgerSearchReadStore
    {
        private readonly IReadOnlyList<LedgerSearchTransactionItem> _rows;

        internal SearchStoreFake(
            IReadOnlyList<LedgerSearchTransactionItem>? rows = null)
        {
            _rows = rows ?? [];
        }

        internal bool HouseholdExists { get; init; } = true;
        internal bool FilterScopeExists { get; init; } = true;
        internal LedgerSearchNormalizedFilters? Filters { get; private set; }
        internal LedgerSearchCursorKey? After { get; private set; }
        internal int HouseholdCallCount { get; private set; }
        internal int FilterScopeCallCount { get; private set; }
        internal int SearchCallCount { get; private set; }
        internal int TotalCallCount =>
            HouseholdCallCount + FilterScopeCallCount + SearchCallCount;

        public Task<bool> HouseholdExistsAsync(
            Guid householdId,
            CancellationToken cancellationToken = default)
        {
            HouseholdCallCount++;
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(HouseholdExists);
        }

        public Task<bool> FilterScopeExistsAsync(
            Guid householdId,
            Guid? portfolioId,
            Guid? accountId,
            Guid? assetId,
            Guid? institutionId,
            CancellationToken cancellationToken = default)
        {
            FilterScopeCallCount++;
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(FilterScopeExists);
        }

        public Task<IReadOnlyList<LedgerSearchTransactionItem>>
            SearchPostedTransactionsAsync(
                Guid householdId,
                LedgerSearchNormalizedFilters filters,
                int take,
                LedgerSearchCursorKey? after,
                CancellationToken cancellationToken = default)
        {
            SearchCallCount++;
            cancellationToken.ThrowIfCancellationRequested();
            Filters = filters;
            After = after;
            return Task.FromResult<IReadOnlyList<LedgerSearchTransactionItem>>(
                _rows.Take(take).ToArray());
        }
    }
}
