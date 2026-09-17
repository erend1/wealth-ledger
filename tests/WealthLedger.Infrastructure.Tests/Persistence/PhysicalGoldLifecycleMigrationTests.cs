using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using WealthLedger.Domain.Assets;
using WealthLedger.Domain.Ledger;
using WealthLedger.Domain.Lots;
using WealthLedger.Domain.Portfolios;
using WealthLedger.Infrastructure.Persistence;
using WealthLedger.Infrastructure.Persistence.Rows;

namespace WealthLedger.Infrastructure.Tests.Persistence;

public sealed class PhysicalGoldLifecycleMigrationTests
{
    private const string PreviousMigration =
        "20260913054039_007_FundTradeLifecycleGuards";

    private const string CurrentMigration =
        "20260915082550_008_PhysicalGoldLifecycle";

    private static readonly Guid ActiveVaultId =
        Guid.Parse("50000000-0000-0000-0000-0000000000a1");

    private static readonly Guid ReversedVaultId =
        Guid.Parse("50000000-0000-0000-0000-0000000000a2");

    [Fact]
    public async Task Migration_BackfillsProvableM007OpeningsAndExactReversal()
    {
        await using var database = await SqliteTestDatabase.CreateAsync(
            PreviousMigration);
        var active = await SeedLegacyOpeningAsync(
            database,
            ActiveVaultId,
            25_00000000L,
            3,
            reverse: false);
        var reversed = await SeedLegacyOpeningAsync(
            database,
            ReversedVaultId,
            10_00000000L,
            2,
            reverse: true);

        await using (var migrationContext = database.CreateContext())
        {
            await migrationContext.Database.MigrateAsync();
            Assert.False(migrationContext.Database.HasPendingModelChanges());
        }

        Assert.Equal(
            3L,
            Convert.ToInt64(await database.ExecuteScalarAsync(
                "SELECT COUNT(*) FROM PhysicalGoldLotAllocationDetail;")));
        Assert.Equal(
            3L,
            await ReadPieceDeltaAsync(database, active.OpeningAllocationId));
        Assert.Equal(
            2L,
            await ReadPieceDeltaAsync(database, reversed.OpeningAllocationId));
        Assert.Equal(
            -2L,
            await ReadPieceDeltaAsync(
                database,
                reversed.ReversalAllocationId!.Value));

        await using var restarted = database.CreateContext();
        var lot = await restarted.AssetLots
            .AsNoTracking()
            .SingleAsync(x => x.Id == active.LotId);
        var detail = await restarted.PhysicalGoldLotDetails
            .AsNoTracking()
            .SingleAsync(x => x.AssetLotId == active.LotId);
        Assert.Equal(25_00000000L, await restarted.LotEntryAllocations
            .Where(x => x.AssetLotId == lot.Id)
            .SumAsync(x => x.QuantityDeltaE8));
        Assert.Equal(3, detail.PieceCount);
    }

    [Fact]
    public async Task Migration_UnprovableLegacyMovementFailsClosedAndRollsBack()
    {
        await using var database = await SqliteTestDatabase.CreateAsync(
            PreviousMigration);
        var opening = await SeedLegacyOpeningAsync(
            database,
            ActiveVaultId,
            25_00000000L,
            3,
            reverse: false);
        await SeedUnprovableMovementAsync(
            database,
            opening,
            ActiveVaultId);

        await using var migrationContext = database.CreateContext();
        var exception = await Assert.ThrowsAsync<SqliteException>(
            () => migrationContext.Database.MigrateAsync());

        Assert.Equal(19, exception.SqliteErrorCode);
        Assert.Equal(
            PreviousMigration,
            Convert.ToString(await database.ExecuteScalarAsync(
                "SELECT MAX(MigrationId) FROM __EFMigrationsHistory;")));
        Assert.Equal(
            0L,
            Convert.ToInt64(await database.ExecuteScalarAsync(
                """
                SELECT COUNT(*)
                FROM sqlite_master
                WHERE type = 'table'
                  AND name IN (
                    'PhysicalGoldLotAllocationDetail',
                    'PhysicalGoldTradeDetail');
                """)));
    }

    [Fact]
    public async Task Migration_007To008To007To008_PreservesDataAndM008Guard()
    {
        await using var database = await SqliteTestDatabase.CreateAsync(
            PreviousMigration);
        var opening = await SeedLegacyOpeningAsync(
            database,
            ActiveVaultId,
            25_00000000L,
            3,
            reverse: false);
        var originalFundGuard = Convert.ToString(
            await database.ExecuteScalarAsync(
                """
                SELECT sql FROM sqlite_master
                WHERE type = 'trigger'
                  AND name = 'TR_LedgerTransaction_ValidateFundTradeBeforePosting';
                """));

        await using (var firstUp = database.CreateContext())
        {
            await firstUp.Database.MigrateAsync(CurrentMigration);
        }

        Assert.Equal(12L, await CountM009ObjectsAsync(database));
        Assert.Equal(
            3L,
            await ReadPieceDeltaAsync(database, opening.OpeningAllocationId));
        Assert.Equal(originalFundGuard, await ReadFundGuardAsync(database));

        await using (var down = database.CreateContext())
        {
            await down.Database.MigrateAsync(PreviousMigration);
        }

        Assert.Equal(0L, await CountM009ObjectsAsync(database));
        Assert.Equal(originalFundGuard, await ReadFundGuardAsync(database));
        Assert.Equal(
            1L,
            Convert.ToInt64(await database.ExecuteScalarAsync(
                "SELECT COUNT(*) FROM AssetLot WHERE Id = $id;",
                new SqliteParameter("$id", opening.LotId.ToString("D")))));

        await using (var secondUp = database.CreateContext())
        {
            await secondUp.Database.MigrateAsync(CurrentMigration);
            Assert.False(secondUp.Database.HasPendingModelChanges());
        }

        Assert.Equal(12L, await CountM009ObjectsAsync(database));
        Assert.Equal(
            3L,
            await ReadPieceDeltaAsync(database, opening.OpeningAllocationId));
        Assert.Equal(originalFundGuard, await ReadFundGuardAsync(database));
    }

    [Fact]
    public async Task DirectSql_UnsupportedBuySellFamilyFailsClosed()
    {
        await using var database = await SqliteTestDatabase.CreateAsync();
        var transactionId = Guid.NewGuid();
        var equityAssetId = Guid.NewGuid();

        await using (var context = database.CreateContext())
        {
            await CoreLedgerTestData.SeedMasterDataAsync(context);
            context.Assets.Add(new AssetRow
            {
                Id = equityAssetId,
                Code = "SYNTHETIC_EQUITY_M009",
                Name = "Synthetic Equity",
                Type = AssetType.Equity,
                BaseUnit = AssetUnit.Share,
                BaseCurrencyCode = "TRY",
                LotTrackingMode = LotTrackingMode.Optional,
                IsActive = true,
                CreatedAtUtc = CoreLedgerTestData.CreatedAtUtc
            });
            context.LedgerTransactions.Add(
                CoreLedgerTestData.CreateDraftTransaction(
                    transactionId,
                    TransactionType.Buy));
            context.TransactionEntries.AddRange(
                CoreLedgerTestData.CreateEntry(
                    Guid.NewGuid(),
                    transactionId,
                    0,
                    equityAssetId,
                    1_00000000L,
                    EntryRole.Principal,
                    unitPriceE8: 100_00000000L,
                    priceCurrencyCode: "TRY"),
                CoreLedgerTestData.CreateEntry(
                    Guid.NewGuid(),
                    transactionId,
                    1,
                    CoreLedgerTestData.CashAssetId,
                    -100_00000000L,
                    EntryRole.Consideration));
            await context.SaveChangesAsync();
        }

        await using var postingContext = database.CreateContext();
        var exception = await Assert.ThrowsAsync<DbUpdateException>(
            () => CoreLedgerTestData.PostAsync(
                postingContext,
                transactionId));

        Assert.Contains(
            "WL_M009_BUY_SELL_ASSET_FAMILY_UNSUPPORTED",
            exception.InnerException?.Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task DirectSql_PhysicalSaleWithoutPieceEvidenceIsRejected()
    {
        await using var database = await SqliteTestDatabase.CreateAsync();
        var opening = await SeedCurrentOpeningAsync(database);
        var saleId = Guid.NewGuid();
        var principalId = Guid.NewGuid();

        await using (var context = database.CreateContext())
        {
            context.LedgerTransactions.Add(new LedgerTransactionRow
            {
                Id = saleId,
                HouseholdId = CoreLedgerTestData.HouseholdId,
                Type = TransactionType.Sell,
                Status = TransactionStatus.Draft,
                ExecutionDate = CoreLedgerTestData.ExecutionDate,
                ExternalReference = "SYNTHETIC-DIRECT-GOLD-SALE",
                CreatedAtUtc = CoreLedgerTestData.CreatedAtUtc
            });
            context.TransactionEntries.AddRange(
                CoreLedgerTestData.CreateEntry(
                    principalId,
                    saleId,
                    0,
                    CoreLedgerTestData.GoldAssetId,
                    -1_00000000L,
                    EntryRole.Principal,
                    accountId: ActiveVaultId),
                CoreLedgerTestData.CreateEntry(
                    Guid.NewGuid(),
                    saleId,
                    1,
                    CoreLedgerTestData.CashAssetId,
                    100_00000000L,
                    EntryRole.Consideration,
                    accountId: CoreLedgerTestData.DestinationAccountId));
            context.LotEntryAllocations.Add(new LotEntryAllocationRow
            {
                Id = Guid.NewGuid(),
                AssetLotId = opening.LotId,
                TransactionEntryId = principalId,
                QuantityDeltaE8 = -1_00000000L,
                CreatedAtUtc = CoreLedgerTestData.CreatedAtUtc
            });
            context.PhysicalGoldTradeDetails.Add(
                new PhysicalGoldTradeDetailRow
                {
                    LedgerTransactionId = saleId
                });
            await context.SaveChangesAsync();
        }

        await using var postingContext = database.CreateContext();
        var exception = await Assert.ThrowsAsync<DbUpdateException>(
            () => CoreLedgerTestData.PostAsync(postingContext, saleId));

        Assert.Contains(
            "WL_M009_PHYSICAL_GOLD_PIECE_DETAIL_REQUIRED",
            exception.InnerException?.Message,
            StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("piece", "PHYSICAL_GOLD_PURCHASE_LOT_INVALID")]
    [InlineData("cash_currency", "PHYSICAL_GOLD_CASH_SCOPE_INVALID")]
    [InlineData("gold_account", "PHYSICAL_GOLD_PRINCIPAL_INVALID")]
    [InlineData("cost_cash", "PHYSICAL_GOLD_COST_CASH_ENTRY_MISSING")]
    [InlineData("counterparty", "PHYSICAL_GOLD_COUNTERPARTY_INVALID")]
    [InlineData("date", "PHYSICAL_GOLD_TRADE_DATE_OR_PROVENANCE_INVALID")]
    public async Task DirectSql_PhysicalPurchaseCompatibilityCannotBeBypassed(
        string mutation,
        string expectedCode)
    {
        await using var database = await SqliteTestDatabase.CreateAsync();
        var draft = await SeedValidDraftPurchaseAsync(database);

        await using (var context = database.CreateContext())
        {
            switch (mutation)
            {
                case "piece":
                    (await context.PhysicalGoldLotAllocationDetails
                            .SingleAsync(x =>
                                x.LotEntryAllocationId == draft.AllocationId))
                        .PieceDelta = 1;
                    break;

                case "cash_currency":
                    (await context.TransactionEntries.SingleAsync(x =>
                            x.Id == draft.ConsiderationEntryId))
                        .AssetId = draft.UsdCashAssetId;
                    break;

                case "gold_account":
                    (await context.TransactionEntries.SingleAsync(x =>
                            x.Id == draft.PrincipalEntryId))
                        .AccountId = CoreLedgerTestData.AccountId;
                    break;

                case "cost_cash":
                    context.TransactionCostComponents.Add(
                        new TransactionCostComponentRow
                        {
                            Id = Guid.NewGuid(),
                            TransactionId = draft.TransactionId,
                            Type = CostType.Commission,
                            Treatment = CostTreatment.AdditionalCashOutflow,
                            AmountMinor = 100,
                            CurrencyCode = "TRY"
                        });
                    (await context.AssetLots.SingleAsync(x =>
                            x.Id == draft.LotId))
                        .OriginalCostBasisMinor = 10_100;
                    break;

                case "counterparty":
                    (await context.Institutions.SingleAsync(x =>
                            x.Id == draft.CounterpartyId))
                        .IsActive = false;
                    break;

                case "date":
                    (await context.LedgerTransactions.SingleAsync(x =>
                            x.Id == draft.TransactionId))
                        .ExecutionDate = new DateOnly(2099, 1, 1);
                    break;

                default:
                    throw new InvalidOperationException(
                        "Unknown synthetic mutation.");
            }

            await context.SaveChangesAsync();
        }

        await using var postingContext = database.CreateContext();
        var exception = await Assert.ThrowsAsync<DbUpdateException>(
            () => CoreLedgerTestData.PostAsync(
                postingContext,
                draft.TransactionId));

        Assert.Contains(
            expectedCode,
            exception.InnerException?.Message,
            StringComparison.Ordinal);
        Assert.False(await postingContext.CommandReceipts.AnyAsync());
    }

    [Fact]
    public async Task DirectSql_TransferRequiresExactOppositeLotAndPieceMovement()
    {
        await using var database = await SqliteTestDatabase.CreateAsync();
        var opening = await SeedCurrentOpeningAsync(database);
        var destinationVaultId = Guid.NewGuid();
        var transactionId = Guid.NewGuid();
        var sourceEntryId = Guid.NewGuid();
        var destinationEntryId = Guid.NewGuid();
        var sourceAllocationId = Guid.NewGuid();
        var destinationAllocationId = Guid.NewGuid();

        await using (var context = database.CreateContext())
        {
            context.Accounts.Add(new AccountRow
            {
                Id = destinationVaultId,
                HouseholdId = CoreLedgerTestData.HouseholdId,
                Code = "DIRECT_TRANSFER_DESTINATION",
                Name = "Synthetic Transfer Destination",
                Type = AccountType.PhysicalVault,
                IsActive = true,
                OpenedOn = new DateOnly(2026, 1, 1)
            });
            context.LedgerTransactions.Add(new LedgerTransactionRow
            {
                Id = transactionId,
                HouseholdId = CoreLedgerTestData.HouseholdId,
                Type = TransactionType.Transfer,
                Status = TransactionStatus.Draft,
                ExecutionDate = CoreLedgerTestData.ExecutionDate,
                ExternalReference = "SYNTHETIC-DIRECT-GOLD-TRANSFER",
                CreatedAtUtc = CoreLedgerTestData.CreatedAtUtc
            });
            context.TransactionEntries.AddRange(
                CoreLedgerTestData.CreateEntry(
                    sourceEntryId,
                    transactionId,
                    0,
                    CoreLedgerTestData.GoldAssetId,
                    -1_00000000L,
                    EntryRole.Transfer,
                    accountId: ActiveVaultId),
                CoreLedgerTestData.CreateEntry(
                    destinationEntryId,
                    transactionId,
                    1,
                    CoreLedgerTestData.GoldAssetId,
                    1_00000000L,
                    EntryRole.Transfer,
                    accountId: destinationVaultId));
            context.LotEntryAllocations.AddRange(
                new LotEntryAllocationRow
                {
                    Id = sourceAllocationId,
                    AssetLotId = opening.LotId,
                    TransactionEntryId = sourceEntryId,
                    QuantityDeltaE8 = -1_00000000L,
                    CreatedAtUtc = CoreLedgerTestData.CreatedAtUtc
                },
                new LotEntryAllocationRow
                {
                    Id = destinationAllocationId,
                    AssetLotId = opening.LotId,
                    TransactionEntryId = destinationEntryId,
                    QuantityDeltaE8 = 1_00000000L,
                    CreatedAtUtc = CoreLedgerTestData.CreatedAtUtc
                });
            context.PhysicalGoldLotAllocationDetails.AddRange(
                new PhysicalGoldLotAllocationDetailRow
                {
                    LotEntryAllocationId = sourceAllocationId,
                    PieceDelta = -1
                },
                new PhysicalGoldLotAllocationDetailRow
                {
                    LotEntryAllocationId = destinationAllocationId,
                    PieceDelta = 2
                });
            await context.SaveChangesAsync();
        }

        await using var postingContext = database.CreateContext();
        var exception = await Assert.ThrowsAsync<DbUpdateException>(
            () => CoreLedgerTestData.PostAsync(
                postingContext,
                transactionId));

        Assert.Contains(
            "WL_M009_PHYSICAL_GOLD_TRANSFER_ALLOCATION_INVALID",
            exception.InnerException?.Message,
            StringComparison.Ordinal);
    }

    private static async Task<LegacyOpening> SeedLegacyOpeningAsync(
        SqliteTestDatabase database,
        Guid vaultId,
        long grossWeightRawE8,
        int pieceCount,
        bool reverse)
    {
        var transactionId = Guid.NewGuid();
        var entryId = Guid.NewGuid();
        var lotId = Guid.NewGuid();
        var allocationId = Guid.NewGuid();

        await using (var context = database.CreateContext())
        {
            if (!await context.Households.AnyAsync())
            {
                await CoreLedgerTestData.SeedMasterDataAsync(context);
            }

            context.Accounts.Add(new AccountRow
            {
                Id = vaultId,
                HouseholdId = CoreLedgerTestData.HouseholdId,
                Code = $"LEGACY_VAULT_{vaultId:N}",
                Name = "Synthetic Legacy Vault",
                Type = AccountType.PhysicalVault,
                IsActive = true,
                OpenedOn = new DateOnly(2026, 1, 1)
            });
            context.LedgerTransactions.Add(
                CoreLedgerTestData.CreateDraftTransaction(
                    transactionId,
                    TransactionType.OpeningBalance));
            context.TransactionEntries.Add(
                CoreLedgerTestData.CreateEntry(
                    entryId,
                    transactionId,
                    0,
                    CoreLedgerTestData.GoldAssetId,
                    grossWeightRawE8,
                    EntryRole.Principal,
                    accountId: vaultId));
            context.AssetLots.Add(new AssetLotRow
            {
                Id = lotId,
                AssetId = CoreLedgerTestData.GoldAssetId,
                OpeningTransactionEntryId = entryId,
                AcquiredOn = CoreLedgerTestData.ExecutionDate,
                CostBasisStatus = CostBasisStatus.Unknown,
                CreatedAtUtc = CoreLedgerTestData.CreatedAtUtc
            });
            context.PhysicalGoldLotDetails.Add(
                new PhysicalGoldLotDetailRow
                {
                    AssetLotId = lotId,
                    ActualFinenessPpm = 916_000,
                    PieceCount = pieceCount
                });
            context.LotEntryAllocations.Add(new LotEntryAllocationRow
            {
                Id = allocationId,
                AssetLotId = lotId,
                TransactionEntryId = entryId,
                QuantityDeltaE8 = grossWeightRawE8,
                CreatedAtUtc = CoreLedgerTestData.CreatedAtUtc
            });
            await context.SaveChangesAsync();
            await CoreLedgerTestData.PostAsync(context, transactionId);
        }

        Guid? reversalId = null;
        Guid? reversalAllocationId = null;
        if (reverse)
        {
            reversalId = Guid.NewGuid();
            var reversalEntryId = Guid.NewGuid();
            reversalAllocationId = Guid.NewGuid();
            await using var context = database.CreateContext();
            context.LedgerTransactions.Add(
                CoreLedgerTestData.CreateDraftTransaction(
                    reversalId.Value,
                    TransactionType.Reversal,
                    reversalOfTransactionId: transactionId));
            context.TransactionEntries.Add(
                CoreLedgerTestData.CreateEntry(
                    reversalEntryId,
                    reversalId.Value,
                    0,
                    CoreLedgerTestData.GoldAssetId,
                    checked(-grossWeightRawE8),
                    EntryRole.Principal,
                    accountId: vaultId));
            context.LotEntryAllocations.Add(new LotEntryAllocationRow
            {
                Id = reversalAllocationId.Value,
                AssetLotId = lotId,
                TransactionEntryId = reversalEntryId,
                QuantityDeltaE8 = checked(-grossWeightRawE8),
                CreatedAtUtc = CoreLedgerTestData.CreatedAtUtc.AddMinutes(2)
            });
            await context.SaveChangesAsync();
            await CoreLedgerTestData.PostAsync(
                context,
                reversalId.Value,
                CoreLedgerTestData.CreatedAtUtc.AddMinutes(3));
        }

        return new LegacyOpening(
            transactionId,
            entryId,
            lotId,
            allocationId,
            reversalId,
            reversalAllocationId);
    }

    private static async Task SeedUnprovableMovementAsync(
        SqliteTestDatabase database,
        LegacyOpening opening,
        Guid vaultId)
    {
        var transactionId = Guid.NewGuid();
        var entryId = Guid.NewGuid();
        await using var context = database.CreateContext();
        context.LedgerTransactions.Add(
            CoreLedgerTestData.CreateDraftTransaction(
                transactionId,
                TransactionType.Adjustment));
        context.TransactionEntries.Add(
            CoreLedgerTestData.CreateEntry(
                entryId,
                transactionId,
                0,
                CoreLedgerTestData.GoldAssetId,
                -5_00000000L,
                EntryRole.Adjustment,
                accountId: vaultId));
        context.LotEntryAllocations.Add(new LotEntryAllocationRow
        {
            Id = Guid.NewGuid(),
            AssetLotId = opening.LotId,
            TransactionEntryId = entryId,
            QuantityDeltaE8 = -5_00000000L,
            CreatedAtUtc = CoreLedgerTestData.CreatedAtUtc.AddMinutes(4)
        });
        await context.SaveChangesAsync();
        await CoreLedgerTestData.PostAsync(
            context,
            transactionId,
            CoreLedgerTestData.CreatedAtUtc.AddMinutes(5));
    }

    private static async Task<LegacyOpening> SeedCurrentOpeningAsync(
        SqliteTestDatabase database)
    {
        var transactionId = Guid.NewGuid();
        var entryId = Guid.NewGuid();
        var lotId = Guid.NewGuid();
        var allocationId = Guid.NewGuid();
        await using var context = database.CreateContext();
        await CoreLedgerTestData.SeedMasterDataAsync(context);
        context.Accounts.Add(new AccountRow
        {
            Id = ActiveVaultId,
            HouseholdId = CoreLedgerTestData.HouseholdId,
            Code = "CURRENT_GOLD_VAULT",
            Name = "Synthetic Current Vault",
            Type = AccountType.PhysicalVault,
            IsActive = true,
            OpenedOn = new DateOnly(2026, 1, 1)
        });
        context.LedgerTransactions.Add(
            CoreLedgerTestData.CreateDraftTransaction(
                transactionId,
                TransactionType.OpeningBalance));
        context.TransactionEntries.Add(
            CoreLedgerTestData.CreateEntry(
                entryId,
                transactionId,
                0,
                CoreLedgerTestData.GoldAssetId,
                2_00000000L,
                EntryRole.Principal,
                accountId: ActiveVaultId));
        context.AssetLots.Add(new AssetLotRow
        {
            Id = lotId,
            AssetId = CoreLedgerTestData.GoldAssetId,
            OpeningTransactionEntryId = entryId,
            AcquiredOn = CoreLedgerTestData.ExecutionDate,
            CostBasisStatus = CostBasisStatus.Unknown,
            CreatedAtUtc = CoreLedgerTestData.CreatedAtUtc
        });
        context.PhysicalGoldLotDetails.Add(new PhysicalGoldLotDetailRow
        {
            AssetLotId = lotId,
            ActualFinenessPpm = 916_000,
            PieceCount = 2
        });
        context.LotEntryAllocations.Add(new LotEntryAllocationRow
        {
            Id = allocationId,
            AssetLotId = lotId,
            TransactionEntryId = entryId,
            QuantityDeltaE8 = 2_00000000L,
            CreatedAtUtc = CoreLedgerTestData.CreatedAtUtc
        });
        context.PhysicalGoldLotAllocationDetails.Add(
            new PhysicalGoldLotAllocationDetailRow
            {
                LotEntryAllocationId = allocationId,
                PieceDelta = 2
            });
        await context.SaveChangesAsync();
        await CoreLedgerTestData.PostAsync(context, transactionId);

        return new LegacyOpening(
            transactionId,
            entryId,
            lotId,
            allocationId,
            null,
            null);
    }

    private static async Task<DraftPurchase> SeedValidDraftPurchaseAsync(
        SqliteTestDatabase database)
    {
        var transactionId = Guid.NewGuid();
        var principalEntryId = Guid.NewGuid();
        var considerationEntryId = Guid.NewGuid();
        var lotId = Guid.NewGuid();
        var allocationId = Guid.NewGuid();
        var counterpartyId = Guid.NewGuid();
        var usdCashAssetId = Guid.NewGuid();
        await using var context = database.CreateContext();
        await CoreLedgerTestData.SeedMasterDataAsync(context);
        context.Accounts.Add(new AccountRow
        {
            Id = ActiveVaultId,
            HouseholdId = CoreLedgerTestData.HouseholdId,
            Code = "DIRECT_PURCHASE_VAULT",
            Name = "Synthetic Purchase Vault",
            Type = AccountType.PhysicalVault,
            IsActive = true,
            OpenedOn = new DateOnly(2026, 1, 1)
        });
        context.Institutions.Add(new InstitutionRow
        {
            Id = counterpartyId,
            Code = "DIRECT_PURCHASE_COUNTERPARTY",
            Name = "Synthetic Counterparty",
            Type = InstitutionType.Jeweler,
            IsActive = true
        });
        context.Assets.Add(new AssetRow
        {
            Id = usdCashAssetId,
            Code = "USD_DIRECT_CASH",
            Name = "Synthetic USD Cash",
            Type = AssetType.Cash,
            BaseUnit = AssetUnit.CurrencyUnit,
            BaseCurrencyCode = "USD",
            LotTrackingMode = LotTrackingMode.None,
            IsActive = true,
            CreatedAtUtc = CoreLedgerTestData.CreatedAtUtc
        });
        context.LedgerTransactions.Add(new LedgerTransactionRow
        {
            Id = transactionId,
            HouseholdId = CoreLedgerTestData.HouseholdId,
            Type = TransactionType.Buy,
            Status = TransactionStatus.Draft,
            ExecutionDate = CoreLedgerTestData.ExecutionDate,
            ExternalReference = "SYNTHETIC-DIRECT-GOLD-PURCHASE",
            CreatedAtUtc = CoreLedgerTestData.CreatedAtUtc
        });
        context.TransactionEntries.AddRange(
            CoreLedgerTestData.CreateEntry(
                principalEntryId,
                transactionId,
                0,
                CoreLedgerTestData.GoldAssetId,
                2_00000000L,
                EntryRole.Principal,
                accountId: ActiveVaultId),
            CoreLedgerTestData.CreateEntry(
                considerationEntryId,
                transactionId,
                1,
                CoreLedgerTestData.CashAssetId,
                -100_00000000L,
                EntryRole.Consideration,
                accountId: CoreLedgerTestData.DestinationAccountId));
        context.AssetLots.Add(new AssetLotRow
        {
            Id = lotId,
            AssetId = CoreLedgerTestData.GoldAssetId,
            OpeningTransactionEntryId = principalEntryId,
            AcquiredOn = CoreLedgerTestData.ExecutionDate,
            OriginalCostBasisMinor = 10_000,
            CostBasisCurrencyCode = "TRY",
            CostBasisStatus = CostBasisStatus.Known,
            CreatedAtUtc = CoreLedgerTestData.CreatedAtUtc
        });
        context.PhysicalGoldLotDetails.Add(new PhysicalGoldLotDetailRow
        {
            AssetLotId = lotId,
            ActualFinenessPpm = 916_000,
            PieceCount = 2
        });
        context.LotEntryAllocations.Add(new LotEntryAllocationRow
        {
            Id = allocationId,
            AssetLotId = lotId,
            TransactionEntryId = principalEntryId,
            QuantityDeltaE8 = 2_00000000L,
            CreatedAtUtc = CoreLedgerTestData.CreatedAtUtc
        });
        context.PhysicalGoldLotAllocationDetails.Add(
            new PhysicalGoldLotAllocationDetailRow
            {
                LotEntryAllocationId = allocationId,
                PieceDelta = 2
            });
        context.PhysicalGoldTradeDetails.Add(
            new PhysicalGoldTradeDetailRow
            {
                LedgerTransactionId = transactionId,
                CounterpartyInstitutionId = counterpartyId
            });
        await context.SaveChangesAsync();

        return new DraftPurchase(
            transactionId,
            principalEntryId,
            considerationEntryId,
            lotId,
            allocationId,
            counterpartyId,
            usdCashAssetId);
    }

    private static async Task<long> ReadPieceDeltaAsync(
        SqliteTestDatabase database,
        Guid allocationId)
        => Convert.ToInt64(await database.ExecuteScalarAsync(
            """
            SELECT PieceDelta
            FROM PhysicalGoldLotAllocationDetail
            WHERE LotEntryAllocationId = $id;
            """,
            new SqliteParameter("$id", allocationId.ToString("D"))));

    private static async Task<long> CountM009ObjectsAsync(
        SqliteTestDatabase database)
        => Convert.ToInt64(await database.ExecuteScalarAsync(
            """
            SELECT COUNT(*)
            FROM sqlite_master
            WHERE (type = 'table' AND name IN (
                    'PhysicalGoldLotAllocationDetail',
                    'PhysicalGoldTradeDetail'))
               OR (type = 'trigger' AND name IN (
                    'TR_PhysicalGoldAllocationDetail_ValidateInsert',
                    'TR_PhysicalGoldAllocationDetail_ValidateUpdate',
                    'TR_PhysicalGoldAllocationDetail_ValidateDelete',
                    'TR_PhysicalGoldTradeDetail_ValidateInsert',
                    'TR_PhysicalGoldTradeDetail_ValidateUpdate',
                    'TR_PhysicalGoldTradeDetail_ValidateDelete',
                    'TR_LedgerTransaction_ValidateBuySellDispatch',
                    'TR_LedgerTransaction_ValidatePhysicalGoldAllocationPosting',
                    'TR_LedgerTransaction_ValidatePhysicalGoldTradePosting',
                    'TR_LedgerTransaction_ValidatePhysicalGoldTransferPosting'));
            """));

    private static async Task<string?> ReadFundGuardAsync(
        SqliteTestDatabase database)
        => Convert.ToString(await database.ExecuteScalarAsync(
            """
            SELECT sql FROM sqlite_master
            WHERE type = 'trigger'
              AND name = 'TR_LedgerTransaction_ValidateFundTradeBeforePosting';
            """));

    private sealed record LegacyOpening(
        Guid TransactionId,
        Guid EntryId,
        Guid LotId,
        Guid OpeningAllocationId,
        Guid? ReversalTransactionId,
        Guid? ReversalAllocationId);

    private sealed record DraftPurchase(
        Guid TransactionId,
        Guid PrincipalEntryId,
        Guid ConsiderationEntryId,
        Guid LotId,
        Guid AllocationId,
        Guid CounterpartyId,
        Guid UsdCashAssetId);
}
