using Microsoft.EntityFrameworkCore;
using WealthLedger.Application.Inventory;
using WealthLedger.Domain.Assets;
using WealthLedger.Domain.Ledger;
using WealthLedger.Domain.Lots;
using WealthLedger.Domain.Portfolios;
using WealthLedger.Infrastructure.Persistence;
using WealthLedger.Infrastructure.Persistence.Rows;

namespace WealthLedger.Infrastructure.Tests.Persistence;

public sealed class InventoryReadStoreTests
{
    private static readonly Guid FundLotId =
        Guid.Parse("83000000-0000-0000-0000-000000000001");
    private static readonly Guid FundOpeningTransactionId =
        Guid.Parse("73000000-0000-0000-0000-000000000001");
    private static readonly Guid FundOpeningEntryId =
        Guid.Parse("73100000-0000-0000-0000-000000000001");
    private static readonly Guid FundSaleTransactionId =
        Guid.Parse("73000000-0000-0000-0000-000000000002");
    private static readonly Guid FundSaleEntryId =
        Guid.Parse("73100000-0000-0000-0000-000000000002");
    private static readonly Guid FundReversalTransactionId =
        Guid.Parse("73000000-0000-0000-0000-000000000003");
    private static readonly Guid FundReversalEntryId =
        Guid.Parse("73100000-0000-0000-0000-000000000003");
    private static readonly Guid FundTransferTransactionId =
        Guid.Parse("73000000-0000-0000-0000-000000000004");
    private static readonly Guid FundTransferSourceEntryId =
        Guid.Parse("73100000-0000-0000-0000-000000000004");
    private static readonly Guid FundTransferDestinationEntryId =
        Guid.Parse("73100000-0000-0000-0000-000000000005");
    private static readonly Guid GoldLotId =
        Guid.Parse("83000000-0000-0000-0000-000000000002");
    private static readonly Guid GoldOpeningTransactionId =
        Guid.Parse("73000000-0000-0000-0000-000000000010");
    private static readonly Guid GoldOpeningEntryId =
        Guid.Parse("73100000-0000-0000-0000-000000000010");
    private static readonly Guid GoldSaleTransactionId =
        Guid.Parse("73000000-0000-0000-0000-000000000011");
    private static readonly Guid GoldSaleEntryId =
        Guid.Parse("73100000-0000-0000-0000-000000000011");
    private static readonly Guid GoldTransferTransactionId =
        Guid.Parse("73000000-0000-0000-0000-000000000012");
    private static readonly Guid GoldTransferSourceEntryId =
        Guid.Parse("73100000-0000-0000-0000-000000000012");
    private static readonly Guid GoldTransferDestinationEntryId =
        Guid.Parse("73100000-0000-0000-0000-000000000013");

    [Fact]
    public async Task PositionInventory_CurrentAndAsOfUseOnlyEffectivePostedEntries()
    {
        await using var database = await SqliteTestDatabase.CreateAsync();
        await SeedInventoryHistoryAsync(database);
        await using var context = database.CreateContext();
        var useCase = new ListPositionInventoryUseCase(
            new EfCoreInventoryReadStore(context));

        var beforeTransfer = await useCase.ExecuteAsync(
            new ListPositionInventoryQuery(
                CoreLedgerTestData.HouseholdId,
                AssetId: CoreLedgerTestData.FundAssetId,
                AsOf: new DateOnly(2026, 9, 2),
                IncludeZero: true));
        var current = await useCase.ExecuteAsync(
            new ListPositionInventoryQuery(
                CoreLedgerTestData.HouseholdId,
                AssetId: CoreLedgerTestData.FundAssetId,
                IncludeZero: true));

        Assert.Single(beforeTransfer.Items);
        Assert.Equal(
            10 * 100_000_000L,
            beforeTransfer.Items.Single().QuantityRawE8);
        Assert.Equal(2, current.Items.Count);
        Assert.Equal(
            6 * 100_000_000L,
            current.Items.Single(
                item => item.AccountId == CoreLedgerTestData.AccountId)
                .QuantityRawE8);
        Assert.Equal(
            4 * 100_000_000L,
            current.Items.Single(
                item => item.AccountId == CoreLedgerTestData.DestinationAccountId)
                .QuantityRawE8);
        Assert.DoesNotContain(
            current.Items.SelectMany(item => item.SourceTransactionIds),
            transactionId => transactionId == DraftTransactionId);
    }

    [Fact]
    public async Task LotInventory_DerivesFundLineageCustodyAndReversalAsOf()
    {
        await using var database = await SqliteTestDatabase.CreateAsync();
        await SeedInventoryHistoryAsync(database);

        await using (var context = database.CreateContext())
        {
            var useCase = new ListLotInventoryUseCase(
                new EfCoreInventoryReadStore(context));
            var afterSaleBeforeReversal = await useCase.ExecuteAsync(
                new ListLotInventoryQuery(
                    CoreLedgerTestData.HouseholdId,
                    AssetId: CoreLedgerTestData.FundAssetId,
                    AsOf: new DateOnly(2026, 9, 1)));
            var current = await useCase.ExecuteAsync(
                new ListLotInventoryQuery(
                    CoreLedgerTestData.HouseholdId,
                    AssetId: CoreLedgerTestData.FundAssetId));

            var historical = Assert.Single(afterSaleBeforeReversal.Items);
            Assert.Equal(7 * 100_000_000L, historical.GlobalQuantityRawE8);
            var lot = Assert.Single(current.Items);
            Assert.Equal(FundLotId, lot.AssetLotId);
            Assert.Equal(FundOpeningTransactionId, lot.CreatingTransactionId);
            Assert.Equal(10 * 100_000_000L, lot.OriginalQuantityRawE8);
            Assert.Equal(10 * 100_000_000L, lot.GlobalQuantityRawE8);
            Assert.Equal(CostBasisStatus.Known, lot.CostStatus);
            Assert.Equal(100_000, lot.CostMinorUnits);
            Assert.Equal("TRY", lot.CostCurrencyCode);
            Assert.Equal(2, lot.Custody.Count);
            Assert.Contains(
                lot.Allocations,
                allocation => allocation.TransactionId
                              == FundReversalTransactionId
                              && allocation.QuantityDeltaRawE8
                              == 3 * 100_000_000L);
        }

        await using var restarted = database.CreateContext();
        var restartedInventory = await new ListLotInventoryUseCase(
                new EfCoreInventoryReadStore(restarted))
            .ExecuteAsync(
                new ListLotInventoryQuery(
                    CoreLedgerTestData.HouseholdId,
                    AccountId: CoreLedgerTestData.DestinationAccountId,
                    AssetId: CoreLedgerTestData.FundAssetId));

        Assert.Equal(
            4 * 100_000_000L,
            Assert.Single(Assert.Single(restartedInventory.Items).Custody,
                item => item.AccountId == CoreLedgerTestData.DestinationAccountId)
                .QuantityRawE8);
    }

    [Fact]
    public async Task LotInventory_PhysicalGoldKeepsGrossPiecesAndFinenessIndependent()
    {
        await using var database = await SqliteTestDatabase.CreateAsync();
        await SeedInventoryHistoryAsync(database);
        await using var context = database.CreateContext();
        var result = await new ListLotInventoryUseCase(
                new EfCoreInventoryReadStore(context))
            .ExecuteAsync(
                new ListLotInventoryQuery(
                    CoreLedgerTestData.HouseholdId,
                    AssetId: CoreLedgerTestData.GoldAssetId));

        var lot = Assert.Single(result.Items);
        Assert.Equal(GoldLotId, lot.AssetLotId);
        Assert.Equal(20 * 100_000_000L, lot.OriginalQuantityRawE8);
        Assert.Equal(15 * 100_000_000L, lot.GlobalQuantityRawE8);
        var physical = Assert.IsType<PhysicalGoldLotInventoryDetail>(lot.PhysicalGold);
        Assert.Equal(916_000, physical.FinenessPpm);
        Assert.Equal(4, physical.OriginalPieceCount);
        Assert.Equal(3, physical.GlobalPieceCount);
        Assert.Equal(13.74m, physical.GlobalFineWeightGrams);
        Assert.Equal(2, lot.Custody.Count);
        Assert.Equal(
            2,
            lot.Custody.Single(
                item => item.AccountId == CoreLedgerTestData.AccountId)
                .PieceCount);
        Assert.Equal(
            1,
            lot.Custody.Single(
                item => item.AccountId == CoreLedgerTestData.DestinationAccountId)
                .PieceCount);
    }

    [Fact]
    public async Task Inventory_ExactEmptyScopeIsZeroButCrossHouseholdFailsClosed()
    {
        await using var database = await SqliteTestDatabase.CreateAsync();
        await SeedInventoryHistoryAsync(database);
        await using var context = database.CreateContext();
        var useCase = new ListPositionInventoryUseCase(
            new EfCoreInventoryReadStore(context));

        var empty = await useCase.ExecuteAsync(
            new ListPositionInventoryQuery(
                CoreLedgerTestData.HouseholdId,
                CoreLedgerTestData.PortfolioId,
                CoreLedgerTestData.AccountId,
                CoreLedgerTestData.InstitutionId,
                CoreLedgerTestData.OtherFundAssetId,
                IncludeZero: true));

        Assert.Equal(0, Assert.Single(empty.Items).QuantityRawE8);
        await Assert.ThrowsAsync<InventoryScopeNotFoundException>(
            () => useCase.ExecuteAsync(
                new ListPositionInventoryQuery(
                    CoreLedgerTestData.HouseholdId,
                    CoreLedgerTestData.OtherPortfolioId,
                    CoreLedgerTestData.AccountId,
                    AssetId: CoreLedgerTestData.FundAssetId,
                    IncludeZero: true)));
    }

    private static readonly Guid DraftTransactionId =
        Guid.Parse("73000000-0000-0000-0000-000000000099");
    private static readonly Guid DraftEntryId =
        Guid.Parse("73100000-0000-0000-0000-000000000099");

    private static async Task SeedInventoryHistoryAsync(SqliteTestDatabase database)
    {
        await using var context = database.CreateContext();
        await CoreLedgerTestData.SeedMasterDataAsync(context);

        var fundOpening = CoreLedgerTestData.CreateDraftTransaction(
            FundOpeningTransactionId,
            TransactionType.OpeningBalance,
            executionDate: new DateOnly(2026, 8, 31));
        context.LedgerTransactions.Add(fundOpening);
        context.TransactionEntries.Add(
            CoreLedgerTestData.CreateEntry(
                FundOpeningEntryId,
                FundOpeningTransactionId,
                0,
                CoreLedgerTestData.FundAssetId,
                10 * 100_000_000L,
                EntryRole.Principal));
        context.AssetLots.Add(
            new AssetLotRow
            {
                Id = FundLotId,
                AssetId = CoreLedgerTestData.FundAssetId,
                OpeningTransactionEntryId = FundOpeningEntryId,
                AcquiredOn = new DateOnly(2026, 8, 31),
                OriginalCostBasisMinor = 100_000,
                CostBasisCurrencyCode = "TRY",
                CostBasisStatus = CostBasisStatus.Known,
                CreatedAtUtc = CoreLedgerTestData.CreatedAtUtc
            });
        context.LotEntryAllocations.Add(
            new LotEntryAllocationRow
            {
                Id = Guid.Parse("84000000-0000-0000-0000-000000000001"),
                AssetLotId = FundLotId,
                TransactionEntryId = FundOpeningEntryId,
                QuantityDeltaE8 = 10 * 100_000_000L,
                CreatedAtUtc = CoreLedgerTestData.CreatedAtUtc
            });
        await context.SaveChangesAsync();
        await CoreLedgerTestData.PostAsync(
            context,
            FundOpeningTransactionId,
            new DateTime(2026, 8, 31, 9, 0, 0, DateTimeKind.Utc));

        context.LedgerTransactions.Add(
            CoreLedgerTestData.CreateDraftTransaction(
                FundSaleTransactionId,
                TransactionType.Sell,
                executionDate: new DateOnly(2026, 9, 1)));
        context.TransactionEntries.Add(
            CoreLedgerTestData.CreateEntry(
                FundSaleEntryId,
                FundSaleTransactionId,
                0,
                CoreLedgerTestData.FundAssetId,
                -3 * 100_000_000L,
                EntryRole.Principal));
        context.LotEntryAllocations.Add(
            new LotEntryAllocationRow
            {
                Id = Guid.Parse("84000000-0000-0000-0000-000000000002"),
                AssetLotId = FundLotId,
                TransactionEntryId = FundSaleEntryId,
                QuantityDeltaE8 = -3 * 100_000_000L,
                CreatedAtUtc = CoreLedgerTestData.CreatedAtUtc.AddMinutes(1)
            });
        await context.SaveChangesAsync();
        await CoreLedgerTestData.PostAsync(
            context,
            FundSaleTransactionId,
            new DateTime(2026, 9, 1, 10, 0, 0, DateTimeKind.Utc));

        context.LedgerTransactions.Add(
            CoreLedgerTestData.CreateDraftTransaction(
                FundReversalTransactionId,
                TransactionType.Reversal,
                executionDate: new DateOnly(2026, 9, 2),
                reversalOfTransactionId: FundSaleTransactionId));
        context.TransactionEntries.Add(
            CoreLedgerTestData.CreateEntry(
                FundReversalEntryId,
                FundReversalTransactionId,
                0,
                CoreLedgerTestData.FundAssetId,
                3 * 100_000_000L,
                EntryRole.Principal));
        context.LotEntryAllocations.Add(
            new LotEntryAllocationRow
            {
                Id = Guid.Parse("84000000-0000-0000-0000-000000000003"),
                AssetLotId = FundLotId,
                TransactionEntryId = FundReversalEntryId,
                QuantityDeltaE8 = 3 * 100_000_000L,
                CreatedAtUtc = CoreLedgerTestData.CreatedAtUtc.AddMinutes(2)
            });
        await context.SaveChangesAsync();
        await CoreLedgerTestData.PostAsync(
            context,
            FundReversalTransactionId,
            new DateTime(2026, 9, 2, 10, 0, 0, DateTimeKind.Utc));

        context.LedgerTransactions.Add(
            CoreLedgerTestData.CreateDraftTransaction(
                FundTransferTransactionId,
                TransactionType.Transfer,
                executionDate: new DateOnly(2026, 9, 3)));
        context.TransactionEntries.AddRange(
            CoreLedgerTestData.CreateEntry(
                FundTransferSourceEntryId,
                FundTransferTransactionId,
                0,
                CoreLedgerTestData.FundAssetId,
                -4 * 100_000_000L,
                EntryRole.Transfer),
            CoreLedgerTestData.CreateEntry(
                FundTransferDestinationEntryId,
                FundTransferTransactionId,
                1,
                CoreLedgerTestData.FundAssetId,
                4 * 100_000_000L,
                EntryRole.Transfer,
                accountId: CoreLedgerTestData.DestinationAccountId));
        context.LotEntryAllocations.AddRange(
            new LotEntryAllocationRow
            {
                Id = Guid.Parse("84000000-0000-0000-0000-000000000004"),
                AssetLotId = FundLotId,
                TransactionEntryId = FundTransferSourceEntryId,
                QuantityDeltaE8 = -4 * 100_000_000L,
                CreatedAtUtc = CoreLedgerTestData.CreatedAtUtc.AddMinutes(3)
            },
            new LotEntryAllocationRow
            {
                Id = Guid.Parse("84000000-0000-0000-0000-000000000005"),
                AssetLotId = FundLotId,
                TransactionEntryId = FundTransferDestinationEntryId,
                QuantityDeltaE8 = 4 * 100_000_000L,
                CreatedAtUtc = CoreLedgerTestData.CreatedAtUtc.AddMinutes(3)
            });
        await context.SaveChangesAsync();
        await CoreLedgerTestData.PostAsync(
            context,
            FundTransferTransactionId,
            new DateTime(2026, 9, 3, 10, 0, 0, DateTimeKind.Utc));

        context.LedgerTransactions.Add(
            CoreLedgerTestData.CreateDraftTransaction(
                DraftTransactionId,
                TransactionType.Adjustment,
                executionDate: new DateOnly(2026, 9, 4)));
        context.TransactionEntries.Add(
            CoreLedgerTestData.CreateEntry(
                DraftEntryId,
                DraftTransactionId,
                0,
                CoreLedgerTestData.FundAssetId,
                99 * 100_000_000L,
                EntryRole.Adjustment));
        await context.SaveChangesAsync();

        await SeedGoldAsync(context);
    }

    private static async Task SeedGoldAsync(WealthLedgerDbContext context)
    {
        context.LedgerTransactions.Add(
            CoreLedgerTestData.CreateDraftTransaction(
                GoldOpeningTransactionId,
                TransactionType.OpeningBalance,
                executionDate: new DateOnly(2026, 8, 31)));
        context.TransactionEntries.Add(
            CoreLedgerTestData.CreateEntry(
                GoldOpeningEntryId,
                GoldOpeningTransactionId,
                0,
                CoreLedgerTestData.GoldAssetId,
                20 * 100_000_000L,
                EntryRole.Principal));
        context.AssetLots.Add(
            new AssetLotRow
            {
                Id = GoldLotId,
                AssetId = CoreLedgerTestData.GoldAssetId,
                OpeningTransactionEntryId = GoldOpeningEntryId,
                AcquiredOn = new DateOnly(2026, 8, 31),
                OriginalCostBasisMinor = null,
                CostBasisCurrencyCode = null,
                CostBasisStatus = CostBasisStatus.Unknown,
                CreatedAtUtc = CoreLedgerTestData.CreatedAtUtc.AddMinutes(10)
            });
        context.PhysicalGoldLotDetails.Add(
            new PhysicalGoldLotDetailRow
            {
                AssetLotId = GoldLotId,
                ActualFinenessPpm = 916_000,
                PieceCount = 4,
                Hallmark = "TEST",
                CertificateReference = "CERT",
                Note = null
            });
        var openingAllocationId =
            Guid.Parse("84000000-0000-0000-0000-000000000010");
        context.LotEntryAllocations.Add(
            new LotEntryAllocationRow
            {
                Id = openingAllocationId,
                AssetLotId = GoldLotId,
                TransactionEntryId = GoldOpeningEntryId,
                QuantityDeltaE8 = 20 * 100_000_000L,
                CreatedAtUtc = CoreLedgerTestData.CreatedAtUtc.AddMinutes(10)
            });
        context.PhysicalGoldLotAllocationDetails.Add(
            new PhysicalGoldLotAllocationDetailRow
            {
                LotEntryAllocationId = openingAllocationId,
                PieceDelta = 4
            });
        await context.SaveChangesAsync();
        await CoreLedgerTestData.PostAsync(
            context,
            GoldOpeningTransactionId,
            new DateTime(2026, 8, 31, 11, 0, 0, DateTimeKind.Utc));

        context.LedgerTransactions.Add(
            CoreLedgerTestData.CreateDraftTransaction(
                GoldSaleTransactionId,
                TransactionType.Sell,
                executionDate: new DateOnly(2026, 9, 1)));
        context.TransactionEntries.Add(
            CoreLedgerTestData.CreateEntry(
                GoldSaleEntryId,
                GoldSaleTransactionId,
                0,
                CoreLedgerTestData.GoldAssetId,
                -5 * 100_000_000L,
                EntryRole.Principal));
        var saleAllocationId =
            Guid.Parse("84000000-0000-0000-0000-000000000011");
        context.LotEntryAllocations.Add(
            new LotEntryAllocationRow
            {
                Id = saleAllocationId,
                AssetLotId = GoldLotId,
                TransactionEntryId = GoldSaleEntryId,
                QuantityDeltaE8 = -5 * 100_000_000L,
                CreatedAtUtc = CoreLedgerTestData.CreatedAtUtc.AddMinutes(11)
            });
        context.PhysicalGoldLotAllocationDetails.Add(
            new PhysicalGoldLotAllocationDetailRow
            {
                LotEntryAllocationId = saleAllocationId,
                PieceDelta = -1
            });
        await context.SaveChangesAsync();
        await CoreLedgerTestData.PostAsync(
            context,
            GoldSaleTransactionId,
            new DateTime(2026, 9, 1, 12, 0, 0, DateTimeKind.Utc));

        context.LedgerTransactions.Add(
            CoreLedgerTestData.CreateDraftTransaction(
                GoldTransferTransactionId,
                TransactionType.Transfer,
                executionDate: new DateOnly(2026, 9, 2)));
        context.TransactionEntries.AddRange(
            CoreLedgerTestData.CreateEntry(
                GoldTransferSourceEntryId,
                GoldTransferTransactionId,
                0,
                CoreLedgerTestData.GoldAssetId,
                -5 * 100_000_000L,
                EntryRole.Transfer),
            CoreLedgerTestData.CreateEntry(
                GoldTransferDestinationEntryId,
                GoldTransferTransactionId,
                1,
                CoreLedgerTestData.GoldAssetId,
                5 * 100_000_000L,
                EntryRole.Transfer,
                accountId: CoreLedgerTestData.DestinationAccountId));
        var transferSourceAllocationId =
            Guid.Parse("84000000-0000-0000-0000-000000000012");
        var transferDestinationAllocationId =
            Guid.Parse("84000000-0000-0000-0000-000000000013");
        context.LotEntryAllocations.AddRange(
            new LotEntryAllocationRow
            {
                Id = transferSourceAllocationId,
                AssetLotId = GoldLotId,
                TransactionEntryId = GoldTransferSourceEntryId,
                QuantityDeltaE8 = -5 * 100_000_000L,
                CreatedAtUtc = CoreLedgerTestData.CreatedAtUtc.AddMinutes(12)
            },
            new LotEntryAllocationRow
            {
                Id = transferDestinationAllocationId,
                AssetLotId = GoldLotId,
                TransactionEntryId = GoldTransferDestinationEntryId,
                QuantityDeltaE8 = 5 * 100_000_000L,
                CreatedAtUtc = CoreLedgerTestData.CreatedAtUtc.AddMinutes(12)
            });
        context.PhysicalGoldLotAllocationDetails.AddRange(
            new PhysicalGoldLotAllocationDetailRow
            {
                LotEntryAllocationId = transferSourceAllocationId,
                PieceDelta = -1
            },
            new PhysicalGoldLotAllocationDetailRow
            {
                LotEntryAllocationId = transferDestinationAllocationId,
                PieceDelta = 1
            });
        await context.SaveChangesAsync();
        await CoreLedgerTestData.PostAsync(
            context,
            GoldTransferTransactionId,
            new DateTime(2026, 9, 2, 13, 0, 0, DateTimeKind.Utc));
    }
}
