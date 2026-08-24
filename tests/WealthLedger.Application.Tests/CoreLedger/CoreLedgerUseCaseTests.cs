using WealthLedger.Application.Common;
using WealthLedger.Application.CoreLedger;
using WealthLedger.Domain.Assets;
using WealthLedger.Domain.Ledger;
using WealthLedger.Domain.Lots;
using WealthLedger.Domain.Portfolios;
using WealthLedger.Domain.ValueObjects;

namespace WealthLedger.Application.Tests.CoreLedger;

public sealed class CoreLedgerUseCaseTests
{
    private static readonly Guid HouseholdId =
        Guid.Parse("10000000-0000-0000-0000-000000000001");

    private static readonly Guid OtherHouseholdId =
        Guid.Parse("10000000-0000-0000-0000-000000000002");

    private static readonly Guid PortfolioId =
        Guid.Parse("20000000-0000-0000-0000-000000000001");

    private static readonly Guid AccountId =
        Guid.Parse("30000000-0000-0000-0000-000000000001");

    private static readonly Guid CashAssetId =
        Guid.Parse("40000000-0000-0000-0000-000000000001");

    private static readonly Guid FundAssetId =
        Guid.Parse("40000000-0000-0000-0000-000000000002");

    private static readonly Guid HouseholdMemberId =
        Guid.Parse("50000000-0000-0000-0000-000000000001");

    private static readonly DateTimeOffset RecordedAtUtc =
        new(2026, 8, 24, 10, 0, 0, TimeSpan.Zero);

    private static readonly DateOnly ExecutionDate =
        new(2026, 8, 24);

    [Fact]
    public async Task Contribution_BuildsAndPostsDomainAggregate()
    {
        var references = CreateReferenceData();
        references.HouseholdMember = new HouseholdMemberReference(
            HouseholdMemberId,
            HouseholdId,
            IsActive: true);
        var store = new CapturingPostingStore();
        var useCase = new RecordContributionUseCase(
            references,
            store,
            new FixedTimeProvider(RecordedAtUtc));

        var result = await useCase.ExecuteAsync(
            new RecordContributionCommand(
                HouseholdId,
                PortfolioId,
                AccountId,
                CashAssetId,
                Money.FromMinorUnits(12_345, CurrencyCode.TRY),
                CashFlowCategory.Other,
                ExecutionDate,
                HouseholdMemberId));

        var transaction = Assert.IsType<LedgerTransaction>(
            store.Transaction);
        var entry = Assert.Single(transaction.Entries);

        Assert.Equal(result.TransactionId, transaction.Id);
        Assert.Equal(TransactionType.Contribution, transaction.Type);
        Assert.Equal(TransactionStatus.Posted, transaction.Status);
        Assert.Equal(12_345_000_000, entry.QuantityDelta.RawE8);
        Assert.Equal(EntryRole.Principal, entry.Role);
        Assert.Equal(
            HouseholdMemberId,
            transaction.CashFlowDetail?.HouseholdMemberId);
        Assert.Empty(store.NewLots);
    }

    [Fact]
    public async Task FundPurchase_BuildsPostedTransactionAndAcquisitionLot()
    {
        var references = CreateReferenceData();
        var store = new CapturingPostingStore();
        var useCase = new RecordFundPurchaseUseCase(
            references,
            store,
            new FixedTimeProvider(RecordedAtUtc));
        var quantity = Quantity.FromDecimal(6_412.34918m);
        var unitPrice = UnitPrice.FromDecimal(
            4.678473m,
            CurrencyCode.TRY);
        var consideration = Money.FromMinorUnits(
            3_000_000,
            CurrencyCode.TRY);

        var result = await useCase.ExecuteAsync(
            new RecordFundPurchaseCommand(
                HouseholdId,
                PortfolioId,
                AccountId,
                FundAssetId,
                CashAssetId,
                quantity,
                unitPrice,
                consideration,
                ExecutionDate));

        var transaction = Assert.IsType<LedgerTransaction>(
            store.Transaction);
        var lot = Assert.Single(store.NewLots);
        var principal = transaction.Entries.Single(
            x => x.Role == EntryRole.Principal);
        var cash = transaction.Entries.Single(
            x => x.Role == EntryRole.Consideration);
        var allocation = Assert.Single(lot.Allocations);

        Assert.Equal(result.TransactionId, transaction.Id);
        Assert.Equal(result.AssetLotId, lot.Id);
        Assert.Equal(TransactionStatus.Posted, transaction.Status);
        Assert.Equal(quantity.RawE8, principal.QuantityDelta.RawE8);
        Assert.Equal(unitPrice, principal.UnitPrice);
        Assert.Equal(-3_000_000_000_000, cash.QuantityDelta.RawE8);
        Assert.Equal(principal.Id, lot.OpeningTransactionEntryId);
        Assert.Equal(quantity.RawE8, allocation.QuantityDelta.RawE8);
        Assert.Equal(CostBasisStatus.Known, lot.CostBasis.Status);
        Assert.Equal(consideration, lot.CostBasis.Amount);
    }

    [Fact]
    public async Task Contribution_RejectsCrossHouseholdLocationBeforeWriting()
    {
        var references = CreateReferenceData();
        references.Location = references.Location! with
        {
            AccountHouseholdId = OtherHouseholdId
        };
        var store = new CapturingPostingStore();
        var useCase = new RecordContributionUseCase(
            references,
            store,
            new FixedTimeProvider(RecordedAtUtc));

        var exception = await Assert.ThrowsAsync<ApplicationRuleViolationException>(
            () => useCase.ExecuteAsync(
                new RecordContributionCommand(
                    HouseholdId,
                    PortfolioId,
                    AccountId,
                    CashAssetId,
                    Money.FromMinorUnits(100, CurrencyCode.TRY),
                    CashFlowCategory.Other,
                    ExecutionDate)));

        Assert.Contains("same household", exception.Message);
        Assert.Null(store.Transaction);
    }

    [Fact]
    public async Task Contribution_WhenE8ConversionOverflows_DoesNotWrite()
    {
        var references = CreateReferenceData();
        references.Currency = new CurrencyReference(
            CurrencyCode.TRY,
            MinorUnitDigits: 0);
        var store = new CapturingPostingStore();
        var useCase = new RecordContributionUseCase(
            references,
            store,
            new FixedTimeProvider(RecordedAtUtc));

        await Assert.ThrowsAsync<OverflowException>(
            () => useCase.ExecuteAsync(
                new RecordContributionCommand(
                    HouseholdId,
                    PortfolioId,
                    AccountId,
                    CashAssetId,
                    Money.FromMinorUnits(long.MaxValue, CurrencyCode.TRY),
                    CashFlowCategory.Other,
                    ExecutionDate)));

        Assert.Null(store.Transaction);
    }

    private static StubLedgerReferenceData CreateReferenceData()
    {
        var references = new StubLedgerReferenceData
        {
            Location = new LedgerLocationReference(
                PortfolioId,
                HouseholdId,
                PortfolioStatus.Active,
                AccountId,
                HouseholdId,
                AccountIsActive: true),
            Currency = new CurrencyReference(
                CurrencyCode.TRY,
                MinorUnitDigits: 2)
        };

        references.Assets[CashAssetId] = Asset.Create(
            CashAssetId,
            "TRY_CASH",
            "Synthetic Cash",
            AssetType.Cash,
            AssetUnit.CurrencyUnit,
            CurrencyCode.TRY,
            LotTrackingMode.None);

        references.Assets[FundAssetId] = Asset.Create(
            FundAssetId,
            "FUND_TEST",
            "Synthetic Fund",
            AssetType.Fund,
            AssetUnit.FundUnit,
            CurrencyCode.TRY,
            LotTrackingMode.Required);

        return references;
    }

    private sealed class StubLedgerReferenceData : ILedgerReferenceData
    {
        internal LedgerLocationReference? Location { get; set; }

        internal CurrencyReference? Currency { get; set; }

        internal HouseholdMemberReference? HouseholdMember { get; set; }

        internal Dictionary<Guid, Asset> Assets { get; } = [];

        public Task<LedgerLocationReference?> FindLocationAsync(
            Guid portfolioId,
            Guid accountId,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(Location);
        }

        public Task<Asset?> FindAssetAsync(
            Guid assetId,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Assets.TryGetValue(assetId, out var asset);
            return Task.FromResult(asset);
        }

        public Task<CurrencyReference?> FindCurrencyAsync(
            CurrencyCode currency,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(Currency);
        }

        public Task<HouseholdMemberReference?> FindHouseholdMemberAsync(
            Guid householdMemberId,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(HouseholdMember);
        }
    }

    private sealed class CapturingPostingStore : ILedgerPostingStore
    {
        internal LedgerTransaction? Transaction { get; private set; }

        internal IReadOnlyCollection<AssetLot> NewLots { get; private set; }
            = Array.Empty<AssetLot>();

        public Task SavePostedTransactionAsync(
            LedgerTransaction transaction,
            IReadOnlyCollection<AssetLot> newLots,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Transaction = transaction;
            NewLots = newLots;
            return Task.CompletedTask;
        }
    }

    private sealed class FixedTimeProvider : TimeProvider
    {
        private readonly DateTimeOffset _utcNow;

        internal FixedTimeProvider(DateTimeOffset utcNow)
        {
            _utcNow = utcNow;
        }

        public override DateTimeOffset GetUtcNow() => _utcNow;
    }
}
