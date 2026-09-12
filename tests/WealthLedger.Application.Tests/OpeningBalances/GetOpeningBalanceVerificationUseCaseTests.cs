using WealthLedger.Application.CoreLedger;
using WealthLedger.Application.Navigation;
using WealthLedger.Application.OpeningBalances;
using WealthLedger.Application.Positions;
using WealthLedger.Domain.Ledger;
using WealthLedger.Domain.ValueObjects;

namespace WealthLedger.Application.Tests.OpeningBalances;

public sealed class GetOpeningBalanceVerificationUseCaseTests
{
    private static readonly Guid HouseholdId = Guid.NewGuid();
    private static readonly Guid PortfolioId = Guid.NewGuid();
    private static readonly Guid AccountId = Guid.NewGuid();
    private static readonly Guid AssetId = Guid.NewGuid();
    private static readonly Guid TransactionId = Guid.NewGuid();
    private static readonly Guid EntryId = Guid.NewGuid();

    [Fact]
    public async Task Execute_ReadsPersistedOpeningAndDerivesCurrentPosition()
    {
        var store = new VerificationStoreFake
        {
            Transaction = CreateTransaction(),
            PositionEntries =
            [
                CreatePositionEntry(TransactionId, 1_234_567_800_000)
            ]
        };

        var result = await CreateUseCase(store).ExecuteAsync(
            new GetOpeningBalanceVerificationQuery(
                HouseholdId,
                TransactionId));

        Assert.Equal(1_234_567_800_000, result.PersistedQuantityRawE8);
        Assert.Equal(0, result.AllocationTotalRawE8);
        Assert.True(result.AllocationsReconcile);
        Assert.Equal(1_234_567_800_000, result.CurrentPositionRawE8);
        Assert.Equal(1, result.PositionSourceEntryCount);
        Assert.True(result.CurrentPositionEqualsOpeningQuantity);
        Assert.False(result.HasAdditionalEffectiveHistory);
        Assert.False(result.IsIndependentlyReconciled);
        Assert.Equal(
            ["OPENING_BALANCE_INDEPENDENT_RECONCILIATION_REQUIRED"],
            result.WarningCodes);
    }

    [Fact]
    public async Task Execute_ReportsLaterHistoryWithoutTreatingItAsCorruption()
    {
        var store = new VerificationStoreFake
        {
            Transaction = CreateTransaction(),
            PositionEntries =
            [
                CreatePositionEntry(TransactionId, 1_234_567_800_000),
                CreatePositionEntry(Guid.NewGuid(), 100_000_000)
            ]
        };

        var result = await CreateUseCase(store).ExecuteAsync(
            new GetOpeningBalanceVerificationQuery(
                HouseholdId,
                TransactionId));

        Assert.Equal(1_234_667_800_000, result.CurrentPositionRawE8);
        Assert.Equal(2, result.PositionSourceEntryCount);
        Assert.False(result.CurrentPositionEqualsOpeningQuantity);
        Assert.True(result.HasAdditionalEffectiveHistory);
        Assert.Contains(
            "OPENING_BALANCE_POSITION_CHANGED_SINCE_POSTING",
            result.WarningCodes);
    }

    [Theory]
    [InlineData(true, TransactionType.OpeningBalance)]
    [InlineData(false, TransactionType.Adjustment)]
    public async Task Execute_HidesCrossHouseholdAndNonOpeningTransactions(
        bool wrongHousehold,
        TransactionType type)
    {
        var store = new VerificationStoreFake
        {
            Transaction = CreateTransaction(
                wrongHousehold ? Guid.NewGuid() : HouseholdId,
                type)
        };

        var exception = await Assert.ThrowsAsync<OpeningBalanceException>(
            () => CreateUseCase(store).ExecuteAsync(
                new GetOpeningBalanceVerificationQuery(
                    HouseholdId,
                    TransactionId)));

        Assert.Equal(OpeningBalanceErrorCodes.NotFound, exception.ErrorCode);
        Assert.Equal(0, store.PositionReadCount);
    }

    private static GetOpeningBalanceVerificationUseCase CreateUseCase(
        VerificationStoreFake store)
        => new(
            store,
            new GetPositionUseCase(store, store));

    private static LedgerTransactionDetail CreateTransaction(
        Guid? householdId = null,
        TransactionType type = TransactionType.OpeningBalance)
        => new(
            TransactionId,
            householdId ?? HouseholdId,
            type,
            TransactionStatus.Posted,
            OrderDate: null,
            ExecutionDate: new DateOnly(2026, 8, 31),
            SettlementDate: null,
            ExternalReference: "SYNTHETIC-REFERENCE",
            Note: "Synthetic opening source.",
            ReversalOfTransactionId: null,
            ReversedByTransactionId: null,
            CreatedAtUtc: DateTimeOffset.UnixEpoch,
            PostedAtUtc: DateTimeOffset.UnixEpoch,
            Entries:
            [
                new LedgerTransactionEntryDetail(
                    EntryId,
                    0,
                    PortfolioId,
                    AccountId,
                    AssetId,
                    1_234_567_800_000,
                    EntryRole.Principal,
                    UnitPriceRawE8: null,
                    PriceCurrencyCode: null,
                    DateTimeOffset.UnixEpoch)
            ],
            CashFlow: null,
            Costs: [],
            CreatedLots: [],
            LotAllocations: []);

    private static PostedEntryFact CreatePositionEntry(
        Guid transactionId,
        long quantityRawE8)
        => new(
            transactionId,
            new DateOnly(2026, 8, 31),
            DateTimeOffset.UnixEpoch,
            0,
            QuantityDelta.FromRaw(quantityRawE8));

    private sealed class VerificationStoreFake
        : ILedgerTransactionReadStore,
          IPostedEntrySource,
          INavigationScopeReadStore
    {
        internal LedgerTransactionDetail? Transaction { get; init; }

        internal IReadOnlyList<PostedEntryFact> PositionEntries { get; init; }
            = [];

        internal int PositionReadCount { get; private set; }

        public Task<LedgerTransactionDetail?> FindByIdAsync(
            Guid transactionId,
            CancellationToken cancellationToken = default)
            => Task.FromResult(
                transactionId == TransactionId ? Transaction : null);

        public Task<IReadOnlyList<PostedEntryFact>> ListPositionEntriesAsync(
            Guid householdId,
            Guid portfolioId,
            Guid accountId,
            Guid assetId,
            CancellationToken cancellationToken = default)
        {
            PositionReadCount++;
            return Task.FromResult(PositionEntries);
        }

        public Task<bool> HouseholdExistsAsync(
            Guid householdId,
            CancellationToken cancellationToken = default)
            => Task.FromResult(householdId == HouseholdId);

        public Task<bool> PositionScopeExistsAsync(
            Guid householdId,
            Guid portfolioId,
            Guid accountId,
            Guid assetId,
            CancellationToken cancellationToken = default)
            => Task.FromResult(
                householdId == HouseholdId
                && portfolioId == PortfolioId
                && accountId == AccountId
                && assetId == AssetId);
    }
}
