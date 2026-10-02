using Microsoft.EntityFrameworkCore;
using WealthLedger.Application.CoreLedger;
using WealthLedger.Application.Inventory;
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
    private static readonly Guid FundAdjustmentTransactionId =
        Guid.Parse("73000000-0000-0000-0000-000000000002");
    private static readonly Guid FundAdjustmentEntryId =
        Guid.Parse("73100000-0000-0000-0000-000000000002");
    private static readonly Guid FundMovementTransactionId =
        Guid.Parse("73000000-0000-0000-0000-000000000004");
    private static readonly Guid FundMovementSourceEntryId =
        Guid.Parse("73100000-0000-0000-0000-000000000004");
    private static readonly Guid FundMovementDestinationEntryId =
        Guid.Parse("73100000-0000-0000-0000-000000000005");
    private static readonly Guid GoldLotId =
        Guid.Parse("83000000-0000-0000-0000-000000000002");
    private static readonly Guid GoldOpeningTransactionId =
        Guid.Parse("73000000-0000-0000-0000-000000000010");
    private static readonly Guid GoldOpeningEntryId =
        Guid.Parse("73100000-0000-0000-0000-000000000010");
    private static readonly Guid GoldAdjustmentTransactionId =
        Guid.Parse("73000000-0000-0000-0000-000000000011");
    private static readonly Guid GoldAdjustmentEntryId =
        Guid.Parse("73100000-0000-0000-0000-000000000011");
    private static readonly Guid GoldMovementTransactionId =
        Guid.Parse("73000000-0000-0000-0000-000000000012");
    private static readonly Guid GoldMovementSourceEntryId =
        Guid.Parse("73100000-0000-0000-0000-000000000012");
    private static readonly Guid GoldMovementDestinationEntryId =
        Guid.Parse("73100000-0000-0000-0000-000000000013");
    private static readonly Guid GoldSourceVaultId =
        Guid.Parse("50000000-0000-0000-0000-000000000091");
    private static readonly Guid GoldDestinationVaultId =
        Guid.Parse("50000000-0000-0000-0000-000000000092");
    private static readonly Guid DraftTransactionId =
        Guid.Parse("73000000-0000-0000-0000-000000000099");
    private static readonly Guid DraftEntryId =
        Guid.Parse("73100000-0000-0000-0000-000000000099");

    [Fact]
    public async Task PositionInventory_CurrentAndAsOfUseOnlyEffectivePostedEntries()
    {
        await using var database = await SqliteTestDatabase.CreateAsync();
        await SeedInventoryHistoryAsync(database);
        await using var context = database.CreateContext();
        var useCase = new ListPositionInventoryUseCase(
            new EfCoreInventoryReadStore(context));

        var beforeMovement = await useCase.ExecuteAsync(
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

        Assert.Single(beforeMovement.Items);
        Assert.Equal(
            10 * 100_000_000L,
            beforeMovement.Items.Single().QuantityRawE8);
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
    public async Task LotInventory_DerivesFundLineageCustodyAndEffectiveReversal()
    {
        await using var database = await SqliteTestDatabase.CreateAsync();
        var seed = await SeedInventoryHistoryAsync(database);

        await using (var context = database.CreateContext())
        {
            var useCase = new ListLotInventoryUseCase(
                new EfCoreInventoryReadStore(context));
            var effectiveAsOfAdjustmentDate = await useCase.ExecuteAsync(
                new ListLotInventoryQuery(
                    CoreLedgerTestData.HouseholdId,
                    AssetId: CoreLedgerTestData.FundAssetId,
                    AsOf: new DateOnly(2026, 9, 1)));
            var current = await useCase.ExecuteAsync(
                new ListLotInventoryQuery(
                    CoreLedgerTestData.HouseholdId,
                    AssetId: CoreLedgerTestData.FundAssetId));

            // Reversal keeps the original financial date. Therefore a current
            // as-of reconstruction for 2026-09-01 includes both the original
            // adjustment and its later-posted reversal.
            Assert.Equal(
                10 * 100_000_000L,
                Assert.Single(effectiveAsOfAdjustmentDate.Items)
                    .GlobalQuantityRawE8);

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
                allocation => allocation.TransactionId == seed.ReversalTransactionId
                              && allocation.TransactionType
                              == TransactionType.Reversal
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
            Assert.Single(
                    Assert.Single(restartedInventory.Items).Custody,
                    item => item.AccountId
                            == CoreLedgerTestData.DestinationAccountId)
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
            lot.Custody.Single(item => item.AccountId == GoldSourceVaultId)
                .PieceCount);
        Assert.Equal(
            1,
            lot.Custody.Single(item => item.AccountId == GoldDestinationVaultId)
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

    private static async Task<SeedResult> SeedInventoryHistoryAsync(
        SqliteTestDatabase database)
    {
        await using var context = database.CreateContext();
        await CoreLedgerTestData.SeedMasterDataAsync(context);
        context.Accounts.AddRange(
            new AccountRow
            {
                Id = GoldSourceVaultId,
                HouseholdId = CoreLedgerTestData.HouseholdId,
                InstitutionId = null,
                Code = "GOLD_SOURCE",
                Name = "Gold Source Vault",
                Type = AccountType.PhysicalVault,
                IsActive = true,
                OpenedOn = new DateOnly(2026, 1, 1)
            },
            new AccountRow
            {
                Id = GoldDestinationVaultId,
                HouseholdId = CoreLedgerTestData.HouseholdId,
                InstitutionId = null,
                Code = "GOLD_DESTINATION",
                Name = "Gold Destination Vault",
                Type = AccountType.PhysicalVault,
                IsActive = true,
                OpenedOn = new DateOnly(2026, 1, 1)
            });
        await context.SaveChangesAsync();

        await SeedFundOpeningAsync(context);
        await SeedFundAdjustmentAsync(context);

        var reversalStore = new EfCoreLedgerReversalStore(context);
        var reversal = await new ReversePostedTransactionUseCase(
                reversalStore,
                new FixedTimeProvider(
                    new DateTimeOffset(2026, 9, 2, 10, 0, 0, TimeSpan.Zero)))
            .ExecuteAsync(
                "inventory-fund-adjustment-reversal",
                new ReversePostedTransactionCommand(
                    FundAdjustmentTransactionId,
                    "Synthetic inventory reversal."));
        Assert.NotNull(reversal);

        await SeedFundMovementAsync(context);

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

        return new SeedResult(reversal!.ReversalTransactionId);
    }

    private static async Task SeedFundOpeningAsync(WealthLedgerDbContext context)
    {
        context.LedgerTransactions.Add(
            CoreLedgerTestData.CreateDraftTransaction(
                FundOpeningTransactionId,
                TransactionType.OpeningBalance,
                executionDate: new DateOnly(2026, 8, 31)));
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
    }

    private static async Task SeedFundAdjustmentAsync(
        WealthLedgerDbContext context)
    {
        context.LedgerTransactions.Add(
            CoreLedgerTestData.CreateDraftTransaction(
                FundAdjustmentTransactionId,
                TransactionType.Adjustment,
                executionDate: new DateOnly(2026, 9, 1)));
        context.TransactionEntries.Add(
            CoreLedgerTestData.CreateEntry(
                FundAdjustmentEntryId,
                FundAdjustmentTransactionId,
                0,
                CoreLedgerTestData.FundAssetId,
                -3 * 100_000_000L,
                EntryRole.Adjustment));
        context.LotEntryAllocations.Add(
            new LotEntryAllocationRow
            {
                Id = Guid.Parse("84000000-0000-0000-0000-000000000002"),
                AssetLotId = FundLotId,
                TransactionEntryId = FundAdjustmentEntryId,
                QuantityDeltaE8 = -3 * 100_000_000L,
                CreatedAtUtc = CoreLedgerTestData.CreatedAtUtc.AddMinutes(1)
            });
        await context.SaveChangesAsync();
        await CoreLedgerTestData.PostAsync(
            context,
            FundAdjustmentTransactionId,
            new DateTime(2026, 9, 1, 10, 0, 0, DateTimeKind.Utc));
    }

    private static async Task SeedFundMovementAsync(
        WealthLedgerDbContext context)
    {
        context.LedgerTransactions.Add(
            CoreLedgerTestData.CreateDraftTransaction(
                FundMovementTransactionId,
                TransactionType.Adjustment,
                executionDate: new DateOnly(2026, 9, 3)));
        context.TransactionEntries.AddRange(
            CoreLedgerTestData.CreateEntry(
                FundMovementSourceEntryId,
                FundMovementTransactionId,
                0,
                CoreLedgerTestData.FundAssetId,
                -4 * 100_000_000L,
                EntryRole.Adjustment),
            CoreLedgerTestData.CreateEntry(
                FundMovementDestinationEntryId,
                FundMovementTransactionId,
                1,
                CoreLedgerTestData.FundAssetId,
                4 * 100_000_000L,
                EntryRole.Adjustment,
                accountId: CoreLedgerTestData.DestinationAccountId));
        context.LotEntryAllocations.AddRange(
            new LotEntryAllocationRow
            {
                Id = Guid.Parse("84000000-0000-0000-0000-000000000004"),
                AssetLotId = FundLotId,
                TransactionEntryId = FundMovementSourceEntryId,
                QuantityDeltaE8 = -4 * 100_000_000L,
                CreatedAtUtc = CoreLedgerTestData.CreatedAtUtc.AddMinutes(3)
            },
            new LotEntryAllocationRow
            {
                Id = Guid.Parse("84000000-0000-0000-0000-000000000005"),
                AssetLotId = FundLotId,
                TransactionEntryId = FundMovementDestinationEntryId,
                QuantityDeltaE8 = 4 * 100_000_000L,
                CreatedAtUtc = CoreLedgerTestData.CreatedAtUtc.AddMinutes(3)
            });
        await context.SaveChangesAsync();
        await CoreLedgerTestData.PostAsync(
            context,
            FundMovementTransactionId,
            new DateTime(2026, 9, 3, 10, 0, 0, DateTimeKind.Utc));
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
                EntryRole.Principal,
                accountId: GoldSourceVaultId));
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
                GoldAdjustmentTransactionId,
                TransactionType.Adjustment,
                executionDate: new DateOnly(2026, 9, 1)));
        context.TransactionEntries.Add(
            CoreLedgerTestData.CreateEntry(
                GoldAdjustmentEntryId,
                GoldAdjustmentTransactionId,
                0,
                CoreLedgerTestData.GoldAssetId,
                -5 * 100_000_000L,
                EntryRole.Adjustment,
                accountId: GoldSourceVaultId));
        var adjustmentAllocationId =
            Guid.Parse("84000000-0000-0000-0000-000000000011");
        context.LotEntryAllocations.Add(
            new LotEntryAllocationRow
            {
                Id = adjustmentAllocationId,
                AssetLotId = GoldLotId,
                TransactionEntryId = GoldAdjustmentEntryId,
                QuantityDeltaE8 = -5 * 100_000_000L,
                CreatedAtUtc = CoreLedgerTestData.CreatedAtUtc.AddMinutes(11)
            });
        context.PhysicalGoldLotAllocationDetails.Add(
            new PhysicalGoldLotAllocationDetailRow
            {
                LotEntryAllocationId = adjustmentAllocationId,
                PieceDelta = -1
            });
        await context.SaveChangesAsync();
        await CoreLedgerTestData.PostAsync(
            context,
            GoldAdjustmentTransactionId,
            new DateTime(2026, 9, 1, 12, 0, 0, DateTimeKind.Utc));

        context.LedgerTransactions.Add(
            CoreLedgerTestData.CreateDraftTransaction(
                GoldMovementTransactionId,
                TransactionType.Adjustment,
                executionDate: new DateOnly(2026, 9, 2)));
        context.TransactionEntries.AddRange(
            CoreLedgerTestData.CreateEntry(
                GoldMovementSourceEntryId,
                GoldMovementTransactionId,
                0,
                CoreLedgerTestData.GoldAssetId,
                -5 * 100_000_000L,
                EntryRole.Adjustment,
                accountId: GoldSourceVaultId),
            CoreLedgerTestData.CreateEntry(
                GoldMovementDestinationEntryId,
                GoldMovementTransactionId,
                1,
                CoreLedgerTestData.GoldAssetId,
                5 * 100_000_000L,
                EntryRole.Adjustment,
                accountId: GoldDestinationVaultId));
        var movementSourceAllocationId =
            Guid.Parse("84000000-0000-0000-0000-000000000012");
        var movementDestinationAllocationId =
            Guid.Parse("84000000-0000-0000-0000-000000000013");
        context.LotEntryAllocations.AddRange(
            new LotEntryAllocationRow
            {
                Id = movementSourceAllocationId,
                AssetLotId = GoldLotId,
                TransactionEntryId = GoldMovementSourceEntryId,
                QuantityDeltaE8 = -5 * 100_000_000L,
                CreatedAtUtc = CoreLedgerTestData.CreatedAtUtc.AddMinutes(12)
            },
            new LotEntryAllocationRow
            {
                Id = movementDestinationAllocationId,
                AssetLotId = GoldLotId,
                TransactionEntryId = GoldMovementDestinationEntryId,
                QuantityDeltaE8 = 5 * 100_000_000L,
                CreatedAtUtc = CoreLedgerTestData.CreatedAtUtc.AddMinutes(12)
            });
        context.PhysicalGoldLotAllocationDetails.AddRange(
            new PhysicalGoldLotAllocationDetailRow
            {
                LotEntryAllocationId = movementSourceAllocationId,
                PieceDelta = -1
            },
            new PhysicalGoldLotAllocationDetailRow
            {
                LotEntryAllocationId = movementDestinationAllocationId,
                PieceDelta = 1
            });
        await context.SaveChangesAsync();
        await CoreLedgerTestData.PostAsync(
            context,
            GoldMovementTransactionId,
            new DateTime(2026, 9, 2, 13, 0, 0, DateTimeKind.Utc));
    }

    private sealed record SeedResult(Guid ReversalTransactionId);

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
