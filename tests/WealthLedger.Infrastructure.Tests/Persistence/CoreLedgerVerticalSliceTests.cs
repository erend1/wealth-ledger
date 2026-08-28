using Microsoft.EntityFrameworkCore;
using WealthLedger.Application.CoreLedger;
using WealthLedger.Application.Positions;
using WealthLedger.Domain.Ledger;
using WealthLedger.Domain.Lots;
using WealthLedger.Domain.ValueObjects;
using WealthLedger.Infrastructure.Persistence;

namespace WealthLedger.Infrastructure.Tests.Persistence;

public sealed class CoreLedgerVerticalSliceTests
{
    private static readonly DateTimeOffset RecordedAtUtc =
        new(2026, 8, 24, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task ContributionAndFundPurchase_RoundTripAndDerivePositions()
    {
        await using var database = await SqliteTestDatabase.CreateAsync();

        await using (var seedContext = database.CreateContext())
        {
            await CoreLedgerTestData.SeedMasterDataAsync(seedContext);
        }

        RecordContributionResult contributionResult;
        RecordFundPurchaseResult purchaseResult;
        var fundQuantity = Quantity.FromDecimal(6_412.34918m);

        await using (var writeContext = database.CreateContext())
        {
            var referenceData = new EfCoreLedgerReferenceData(writeContext);
            var postingStore = new EfCoreLedgerPostingStore(writeContext);
            var timeProvider = new FixedTimeProvider(RecordedAtUtc);

            var contribution = new RecordContributionUseCase(
                referenceData,
                postingStore,
                timeProvider);
            contributionResult = await contribution.ExecuteAsync(
                "vertical-slice-contribution-001",
                new RecordContributionCommand(
                    CoreLedgerTestData.HouseholdId,
                    CoreLedgerTestData.PortfolioId,
                    CoreLedgerTestData.AccountId,
                    CoreLedgerTestData.CashAssetId,
                    Money.FromMinorUnits(
                        3_000_000,
                        CurrencyCode.TRY),
                    CashFlowCategory.Other,
                    CoreLedgerTestData.ExecutionDate));

            var purchase = new RecordFundPurchaseUseCase(
                referenceData,
                postingStore,
                timeProvider);
            purchaseResult = await purchase.ExecuteAsync(
                "vertical-slice-purchase-001",
                new RecordFundPurchaseCommand(
                    CoreLedgerTestData.HouseholdId,
                    CoreLedgerTestData.PortfolioId,
                    CoreLedgerTestData.AccountId,
                    CoreLedgerTestData.FundAssetId,
                    CoreLedgerTestData.CashAssetId,
                    fundQuantity,
                    UnitPrice.FromDecimal(
                        4.678473m,
                        CurrencyCode.TRY),
                    Money.FromMinorUnits(
                        3_000_000,
                        CurrencyCode.TRY),
                    CoreLedgerTestData.ExecutionDate));

            var ignoredDraftId = Guid.NewGuid();
            writeContext.LedgerTransactions.Add(
                CoreLedgerTestData.CreateDraftTransaction(
                    ignoredDraftId,
                    TransactionType.Adjustment));
            writeContext.TransactionEntries.Add(
                CoreLedgerTestData.CreateEntry(
                    Guid.NewGuid(),
                    ignoredDraftId,
                    0,
                    CoreLedgerTestData.FundAssetId,
                    999,
                    EntryRole.Adjustment));
            await writeContext.SaveChangesAsync();
        }

        await using var reopenedContext = database.CreateContext();
        var positionUseCase = new GetPositionUseCase(
            new EfCorePostedEntrySource(reopenedContext));

        var fundPosition = await positionUseCase.ExecuteAsync(
            new GetPositionQuery(
                CoreLedgerTestData.HouseholdId,
                CoreLedgerTestData.PortfolioId,
                CoreLedgerTestData.AccountId,
                CoreLedgerTestData.FundAssetId));
        var cashPosition = await positionUseCase.ExecuteAsync(
            new GetPositionQuery(
                CoreLedgerTestData.HouseholdId,
                CoreLedgerTestData.PortfolioId,
                CoreLedgerTestData.AccountId,
                CoreLedgerTestData.CashAssetId));

        var transactionStatuses = await reopenedContext.LedgerTransactions
            .AsNoTracking()
            .Where(x =>
                x.Id == contributionResult.TransactionId
                || x.Id == purchaseResult.TransactionId)
            .Select(x => x.Status)
            .ToListAsync();
        var lot = await reopenedContext.AssetLots
            .AsNoTracking()
            .SingleAsync(x => x.Id == purchaseResult.AssetLotId);
        var allocation = await reopenedContext.LotEntryAllocations
            .AsNoTracking()
            .SingleAsync(x => x.AssetLotId == purchaseResult.AssetLotId);

        Assert.Equal(fundQuantity.RawE8, fundPosition.Quantity.RawE8);
        Assert.Equal(1, fundPosition.SourceEntryCount);
        Assert.Equal(0, cashPosition.Quantity.RawE8);
        Assert.Equal(2, cashPosition.SourceEntryCount);
        Assert.Equal(2, transactionStatuses.Count);
        Assert.All(
            transactionStatuses,
            status => Assert.Equal(TransactionStatus.Posted, status));
        Assert.Equal(CostBasisStatus.Known, lot.CostBasisStatus);
        Assert.Equal(3_000_000, lot.OriginalCostBasisMinor);
        Assert.Equal("TRY", lot.CostBasisCurrencyCode);
        Assert.Equal(fundQuantity.RawE8, allocation.QuantityDeltaE8);
    }

    [Fact]
    public async Task PostingFailure_RollsBackDraftGraphAtomically()
    {
        await using var database = await SqliteTestDatabase.CreateAsync();
        var transactionId = Guid.NewGuid();

        await using (var context = database.CreateContext())
        {
            await CoreLedgerTestData.SeedMasterDataAsync(context);

            var transaction = LedgerTransaction.CreateDraft(
                transactionId,
                CoreLedgerTestData.HouseholdId,
                TransactionType.Buy,
                RecordedAtUtc,
                executionDate: CoreLedgerTestData.ExecutionDate);
            transaction.AddEntry(
                CoreLedgerTestData.PortfolioId,
                CoreLedgerTestData.AccountId,
                CoreLedgerTestData.FundAssetId,
                QuantityDelta.FromRaw(100),
                EntryRole.Principal,
                UnitPrice.FromRaw(50, CurrencyCode.TRY));
            transaction.AddEntry(
                CoreLedgerTestData.PortfolioId,
                CoreLedgerTestData.AccountId,
                CoreLedgerTestData.CashAssetId,
                QuantityDelta.FromRaw(-100),
                EntryRole.Consideration);
            transaction.Post(RecordedAtUtc);

            var store = new EfCoreLedgerPostingStore(context);

            await Assert.ThrowsAsync<CoreLedgerPersistenceException>(
                () => store.SavePostedTransactionAsync(
                    transaction,
                    Array.Empty<AssetLot>()));
        }

        await using var verificationContext = database.CreateContext();
        var transactionCount = await verificationContext.LedgerTransactions
            .AsNoTracking()
            .CountAsync(x => x.Id == transactionId);
        var entryCount = await verificationContext.TransactionEntries
            .AsNoTracking()
            .CountAsync(x => x.TransactionId == transactionId);

        Assert.Equal(0, transactionCount);
        Assert.Equal(0, entryCount);
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
