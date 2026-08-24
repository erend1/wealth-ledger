using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using WealthLedger.Domain.Ledger;
using WealthLedger.Domain.Lots;
using WealthLedger.Infrastructure.Persistence;
using WealthLedger.Infrastructure.Persistence.Rows;

namespace WealthLedger.Infrastructure.Tests.Persistence;

public sealed class LotIntegrityTriggerTests
{
    [Fact]
    public async Task Allocation_RejectsAssetMismatchAndOppositeSign()
    {
        await using var database = await SqliteTestDatabase.CreateAsync();
        PostedLot postedLot;
        var transactionId = Guid.NewGuid();
        var mismatchedEntryId = Guid.NewGuid();
        var negativeEntryId = Guid.NewGuid();

        await using (var context = database.CreateContext())
        {
            await CoreLedgerTestData.SeedMasterDataAsync(context);
            postedLot = await CreateAndPostOpeningLotAsync(
                context,
                CoreLedgerTestData.FundAssetId,
                quantityE8: 100);

            context.LedgerTransactions.Add(
                CoreLedgerTestData.CreateDraftTransaction(
                    transactionId,
                    TransactionType.Adjustment));

            context.TransactionEntries.AddRange(
                CoreLedgerTestData.CreateEntry(
                    mismatchedEntryId,
                    transactionId,
                    0,
                    CoreLedgerTestData.OtherFundAssetId,
                    10,
                    EntryRole.Adjustment),
                CoreLedgerTestData.CreateEntry(
                    negativeEntryId,
                    transactionId,
                    1,
                    CoreLedgerTestData.FundAssetId,
                    -10,
                    EntryRole.Adjustment));

            await context.SaveChangesAsync();
        }

        await AssertSqliteFailureAsync(
            () => InsertAllocationAsync(
                database,
                postedLot.LotId,
                mismatchedEntryId,
                10),
            "asset mismatch");

        await AssertSqliteFailureAsync(
            () => InsertAllocationAsync(
                database,
                postedLot.LotId,
                negativeEntryId,
                10),
            "sign must match");
    }

    [Fact]
    public async Task Allocation_RejectsEntryOverAllocationAndNegativeLotBalance()
    {
        await using var database = await SqliteTestDatabase.CreateAsync();
        PostedLot postedLot;
        var transactionId = Guid.NewGuid();
        var overAllocatedEntryId = Guid.NewGuid();
        var overdrawnEntryId = Guid.NewGuid();

        await using (var context = database.CreateContext())
        {
            await CoreLedgerTestData.SeedMasterDataAsync(context);
            postedLot = await CreateAndPostOpeningLotAsync(
                context,
                CoreLedgerTestData.FundAssetId,
                quantityE8: 100);

            context.LedgerTransactions.Add(
                CoreLedgerTestData.CreateDraftTransaction(
                    transactionId,
                    TransactionType.Adjustment));

            context.TransactionEntries.AddRange(
                CoreLedgerTestData.CreateEntry(
                    overAllocatedEntryId,
                    transactionId,
                    0,
                    CoreLedgerTestData.FundAssetId,
                    -100,
                    EntryRole.Adjustment),
                CoreLedgerTestData.CreateEntry(
                    overdrawnEntryId,
                    transactionId,
                    1,
                    CoreLedgerTestData.FundAssetId,
                    -101,
                    EntryRole.Adjustment));

            await context.SaveChangesAsync();
        }

        await AssertSqliteFailureAsync(
            () => InsertAllocationAsync(
                database,
                postedLot.LotId,
                overAllocatedEntryId,
                -101),
            "cannot exceed");

        await AssertSqliteFailureAsync(
            () => InsertAllocationAsync(
                database,
                postedLot.LotId,
                overdrawnEntryId,
                -101),
            "cannot become negative");
    }

    [Fact]
    public async Task Posting_RequiresExactAllocationForRequiredAsset()
    {
        await using var database = await SqliteTestDatabase.CreateAsync();

        var transactionId = Guid.NewGuid();
        var entryId = Guid.NewGuid();
        var lotId = Guid.NewGuid();
        var allocationId = Guid.NewGuid();

        await using (var context = database.CreateContext())
        {
            await CoreLedgerTestData.SeedMasterDataAsync(context);

            context.LedgerTransactions.Add(
                CoreLedgerTestData.CreateDraftTransaction(
                    transactionId,
                    TransactionType.OpeningBalance));

            context.TransactionEntries.Add(
                CoreLedgerTestData.CreateEntry(
                    entryId,
                    transactionId,
                    0,
                    CoreLedgerTestData.FundAssetId,
                    100,
                    EntryRole.Principal));

            context.AssetLots.Add(new AssetLotRow
            {
                Id = lotId,
                AssetId = CoreLedgerTestData.FundAssetId,
                OpeningTransactionEntryId = entryId,
                CostBasisStatus = CostBasisStatus.Unknown,
                CreatedAtUtc = CoreLedgerTestData.CreatedAtUtc
            });

            context.LotEntryAllocations.Add(new LotEntryAllocationRow
            {
                Id = allocationId,
                AssetLotId = lotId,
                TransactionEntryId = entryId,
                QuantityDeltaE8 = 90,
                CreatedAtUtc = CoreLedgerTestData.CreatedAtUtc
            });

            await context.SaveChangesAsync();
        }

        await using (var context = database.CreateContext())
        {
            var exception = await Assert.ThrowsAsync<DbUpdateException>(() =>
                CoreLedgerTestData.PostAsync(context, transactionId));

            Assert.Contains("exactly match", exception.InnerException?.Message);
        }

        await using (var context = database.CreateContext())
        {
            var allocation = await context.LotEntryAllocations
                .SingleAsync(x => x.Id == allocationId);
            allocation.QuantityDeltaE8 = 100;
            await context.SaveChangesAsync();

            await CoreLedgerTestData.PostAsync(context, transactionId);
        }

        var status = Convert.ToString(
            await database.ExecuteScalarAsync(
                "SELECT StatusCode FROM LedgerTransaction WHERE Id = $id;",
                new SqliteParameter("$id", transactionId.ToString("D"))));

        Assert.Equal("POSTED", status);
    }

    [Fact]
    public async Task PostedLotAndPhysicalGoldGraph_IsImmutable()
    {
        await using var database = await SqliteTestDatabase.CreateAsync();
        PostedLot postedLot;

        await using (var context = database.CreateContext())
        {
            await CoreLedgerTestData.SeedMasterDataAsync(context);
            postedLot = await CreateAndPostOpeningLotAsync(
                context,
                CoreLedgerTestData.GoldAssetId,
                quantityE8: 1_000,
                includePhysicalGoldDetail: true);
        }

        await AssertSqliteFailureAsync(
            () => database.ExecuteNonQueryAsync(
                "UPDATE AssetLot SET AcquiredOn = '2026-08-25' WHERE Id = $id;",
                new SqliteParameter("$id", postedLot.LotId.ToString("D"))),
            "immutable");

        await AssertSqliteFailureAsync(
            () => database.ExecuteNonQueryAsync(
                "DELETE FROM AssetLot WHERE Id = $id;",
                new SqliteParameter("$id", postedLot.LotId.ToString("D"))),
            "cannot be deleted");

        await AssertSqliteFailureAsync(
            () => database.ExecuteNonQueryAsync(
                "UPDATE PhysicalGoldLotDetail SET PieceCount = 2 WHERE AssetLotId = $id;",
                new SqliteParameter("$id", postedLot.LotId.ToString("D"))),
            "immutable");

        await AssertSqliteFailureAsync(
            () => database.ExecuteNonQueryAsync(
                "DELETE FROM PhysicalGoldLotDetail WHERE AssetLotId = $id;",
                new SqliteParameter("$id", postedLot.LotId.ToString("D"))),
            "cannot be deleted");

        await AssertSqliteFailureAsync(
            () => database.ExecuteNonQueryAsync(
                "UPDATE LotEntryAllocation SET QuantityDeltaE8 = 999 WHERE Id = $id;",
                new SqliteParameter("$id", postedLot.AllocationId.ToString("D"))),
            "immutable");

        await AssertSqliteFailureAsync(
            () => database.ExecuteNonQueryAsync(
                "DELETE FROM LotEntryAllocation WHERE Id = $id;",
                new SqliteParameter("$id", postedLot.AllocationId.ToString("D"))),
            "cannot be deleted");
    }

    [Fact]
    public async Task Reversal_RequiresAllocationToMirrorOriginalLot()
    {
        await using var database = await SqliteTestDatabase.CreateAsync();
        PostedLot originalLot;
        PostedLot alternateLot;
        var reversalTransactionId = Guid.NewGuid();
        var reversalEntryId = Guid.NewGuid();
        var reversalAllocationId = Guid.NewGuid();

        await using (var context = database.CreateContext())
        {
            await CoreLedgerTestData.SeedMasterDataAsync(context);
            originalLot = await CreateAndPostOpeningLotAsync(
                context,
                CoreLedgerTestData.FundAssetId,
                quantityE8: 100);
            alternateLot = await CreateAndPostOpeningLotAsync(
                context,
                CoreLedgerTestData.FundAssetId,
                quantityE8: 100);

            context.LedgerTransactions.Add(
                CoreLedgerTestData.CreateDraftTransaction(
                    reversalTransactionId,
                    TransactionType.Reversal,
                    reversalOfTransactionId: originalLot.TransactionId));
            context.TransactionEntries.Add(
                CoreLedgerTestData.CreateEntry(
                    reversalEntryId,
                    reversalTransactionId,
                    0,
                    CoreLedgerTestData.FundAssetId,
                    -100,
                    EntryRole.Principal));
            context.LotEntryAllocations.Add(new LotEntryAllocationRow
            {
                Id = reversalAllocationId,
                AssetLotId = alternateLot.LotId,
                TransactionEntryId = reversalEntryId,
                QuantityDeltaE8 = -100,
                CreatedAtUtc = CoreLedgerTestData.CreatedAtUtc
            });

            await context.SaveChangesAsync();
        }

        await using (var context = database.CreateContext())
        {
            var exception = await Assert.ThrowsAsync<DbUpdateException>(() =>
                CoreLedgerTestData.PostAsync(context, reversalTransactionId));

            Assert.Contains("same lots", exception.InnerException?.Message);
        }

        await using (var context = database.CreateContext())
        {
            var allocation = await context.LotEntryAllocations
                .SingleAsync(x => x.Id == reversalAllocationId);
            allocation.AssetLotId = originalLot.LotId;
            await context.SaveChangesAsync();

            await CoreLedgerTestData.PostAsync(context, reversalTransactionId);
        }

        var originalLotQuantity = Convert.ToInt64(
            await database.ExecuteScalarAsync(
                "SELECT SUM(QuantityDeltaE8) FROM LotEntryAllocation WHERE AssetLotId = $id;",
                new SqliteParameter("$id", originalLot.LotId.ToString("D"))));
        var alternateLotQuantity = Convert.ToInt64(
            await database.ExecuteScalarAsync(
                "SELECT SUM(QuantityDeltaE8) FROM LotEntryAllocation WHERE AssetLotId = $id;",
                new SqliteParameter("$id", alternateLot.LotId.ToString("D"))));

        Assert.Equal(0, originalLotQuantity);
        Assert.Equal(100, alternateLotQuantity);
    }

    [Fact]
    public async Task AssetClassification_CannotInvalidateExistingLotDetails()
    {
        await using var database = await SqliteTestDatabase.CreateAsync();

        await using (var context = database.CreateContext())
        {
            await CoreLedgerTestData.SeedMasterDataAsync(context);
            await CreateAndPostOpeningLotAsync(
                context,
                CoreLedgerTestData.GoldAssetId,
                quantityE8: 1_000,
                includePhysicalGoldDetail: true);
        }

        await AssertSqliteFailureAsync(
            () => database.ExecuteNonQueryAsync(
                "UPDATE Asset SET LotTrackingModeCode = 'NONE' WHERE Id = $id;",
                new SqliteParameter("$id", CoreLedgerTestData.GoldAssetId.ToString("D"))),
            "cannot disable lot tracking");

        await AssertSqliteFailureAsync(
            () => database.ExecuteNonQueryAsync(
                "UPDATE Asset SET AssetTypeCode = 'FUND' WHERE Id = $id;",
                new SqliteParameter("$id", CoreLedgerTestData.GoldAssetId.ToString("D"))),
            "must remain physical gold");
    }

    [Fact]
    public async Task Posting_UsesOnlyEffectiveHistoryForLotBalance()
    {
        await using var database = await SqliteTestDatabase.CreateAsync();
        PostedLot postedLot;
        var unpostedTransactionId = Guid.NewGuid();
        var unpostedEntryId = Guid.NewGuid();
        var disposalTransactionId = Guid.NewGuid();
        var disposalEntryId = Guid.NewGuid();

        await using (var context = database.CreateContext())
        {
            await CoreLedgerTestData.SeedMasterDataAsync(context);
            postedLot = await CreateAndPostOpeningLotAsync(
                context,
                CoreLedgerTestData.FundAssetId,
                quantityE8: 100);

            context.LedgerTransactions.Add(
                CoreLedgerTestData.CreateDraftTransaction(
                    unpostedTransactionId,
                    TransactionType.Adjustment));
            context.TransactionEntries.Add(
                CoreLedgerTestData.CreateEntry(
                    unpostedEntryId,
                    unpostedTransactionId,
                    0,
                    CoreLedgerTestData.FundAssetId,
                    100,
                    EntryRole.Adjustment));
            context.LotEntryAllocations.Add(new LotEntryAllocationRow
            {
                Id = Guid.NewGuid(),
                AssetLotId = postedLot.LotId,
                TransactionEntryId = unpostedEntryId,
                QuantityDeltaE8 = 100,
                CreatedAtUtc = CoreLedgerTestData.CreatedAtUtc
            });
            await context.SaveChangesAsync();

            context.LedgerTransactions.Add(
                CoreLedgerTestData.CreateDraftTransaction(
                    disposalTransactionId,
                    TransactionType.Adjustment));
            context.TransactionEntries.Add(
                CoreLedgerTestData.CreateEntry(
                    disposalEntryId,
                    disposalTransactionId,
                    0,
                    CoreLedgerTestData.FundAssetId,
                    -150,
                    EntryRole.Adjustment));
            context.LotEntryAllocations.Add(new LotEntryAllocationRow
            {
                Id = Guid.NewGuid(),
                AssetLotId = postedLot.LotId,
                TransactionEntryId = disposalEntryId,
                QuantityDeltaE8 = -150,
                CreatedAtUtc = CoreLedgerTestData.CreatedAtUtc
            });
            await context.SaveChangesAsync();
        }

        await using var postingContext = database.CreateContext();
        var exception = await Assert.ThrowsAsync<DbUpdateException>(() =>
            CoreLedgerTestData.PostAsync(postingContext, disposalTransactionId));

        Assert.Contains("effective lot quantity negative", exception.InnerException?.Message);
    }

    [Fact]
    public async Task Posting_RejectsAllocationWhoseOpeningLineageIsUnposted()
    {
        await using var database = await SqliteTestDatabase.CreateAsync();
        var openingTransactionId = Guid.NewGuid();
        var openingEntryId = Guid.NewGuid();
        var lotId = Guid.NewGuid();
        var disposalTransactionId = Guid.NewGuid();
        var disposalEntryId = Guid.NewGuid();

        await using (var context = database.CreateContext())
        {
            await CoreLedgerTestData.SeedMasterDataAsync(context);
            context.LedgerTransactions.Add(
                CoreLedgerTestData.CreateDraftTransaction(
                    openingTransactionId,
                    TransactionType.OpeningBalance));
            context.TransactionEntries.Add(
                CoreLedgerTestData.CreateEntry(
                    openingEntryId,
                    openingTransactionId,
                    0,
                    CoreLedgerTestData.FundAssetId,
                    100,
                    EntryRole.Principal));
            context.AssetLots.Add(new AssetLotRow
            {
                Id = lotId,
                AssetId = CoreLedgerTestData.FundAssetId,
                OpeningTransactionEntryId = openingEntryId,
                CostBasisStatus = CostBasisStatus.Unknown,
                CreatedAtUtc = CoreLedgerTestData.CreatedAtUtc
            });
            context.LotEntryAllocations.Add(new LotEntryAllocationRow
            {
                Id = Guid.NewGuid(),
                AssetLotId = lotId,
                TransactionEntryId = openingEntryId,
                QuantityDeltaE8 = 100,
                CreatedAtUtc = CoreLedgerTestData.CreatedAtUtc
            });
            await context.SaveChangesAsync();

            context.LedgerTransactions.Add(
                CoreLedgerTestData.CreateDraftTransaction(
                    disposalTransactionId,
                    TransactionType.Adjustment));
            context.TransactionEntries.Add(
                CoreLedgerTestData.CreateEntry(
                    disposalEntryId,
                    disposalTransactionId,
                    0,
                    CoreLedgerTestData.FundAssetId,
                    -100,
                    EntryRole.Adjustment));
            context.LotEntryAllocations.Add(new LotEntryAllocationRow
            {
                Id = Guid.NewGuid(),
                AssetLotId = lotId,
                TransactionEntryId = disposalEntryId,
                QuantityDeltaE8 = -100,
                CreatedAtUtc = CoreLedgerTestData.CreatedAtUtc
            });
            await context.SaveChangesAsync();
        }

        await using var postingContext = database.CreateContext();
        var exception = await Assert.ThrowsAsync<DbUpdateException>(() =>
            CoreLedgerTestData.PostAsync(postingContext, disposalTransactionId));

        Assert.Contains("posted acquisition lineage", exception.InnerException?.Message);
    }

    [Fact]
    public async Task Posting_RequiresEveryNewLotToHaveItsOpeningAllocation()
    {
        await using var database = await SqliteTestDatabase.CreateAsync();
        PostedLot existingLot;
        var transactionId = Guid.NewGuid();
        var entryId = Guid.NewGuid();

        await using (var context = database.CreateContext())
        {
            await CoreLedgerTestData.SeedMasterDataAsync(context);
            existingLot = await CreateAndPostOpeningLotAsync(
                context,
                CoreLedgerTestData.FundAssetId,
                quantityE8: 100);

            context.LedgerTransactions.Add(
                CoreLedgerTestData.CreateDraftTransaction(
                    transactionId,
                    TransactionType.OpeningBalance));
            context.TransactionEntries.Add(
                CoreLedgerTestData.CreateEntry(
                    entryId,
                    transactionId,
                    0,
                    CoreLedgerTestData.FundAssetId,
                    100,
                    EntryRole.Principal));
            context.AssetLots.Add(new AssetLotRow
            {
                Id = Guid.NewGuid(),
                AssetId = CoreLedgerTestData.FundAssetId,
                OpeningTransactionEntryId = entryId,
                CostBasisStatus = CostBasisStatus.Unknown,
                CreatedAtUtc = CoreLedgerTestData.CreatedAtUtc
            });
            context.LotEntryAllocations.Add(new LotEntryAllocationRow
            {
                Id = Guid.NewGuid(),
                AssetLotId = existingLot.LotId,
                TransactionEntryId = entryId,
                QuantityDeltaE8 = 100,
                CreatedAtUtc = CoreLedgerTestData.CreatedAtUtc
            });
            await context.SaveChangesAsync();
        }

        await using var postingContext = database.CreateContext();
        var exception = await Assert.ThrowsAsync<DbUpdateException>(() =>
            CoreLedgerTestData.PostAsync(postingContext, transactionId));

        Assert.Contains("positive allocation from its opening entry", exception.InnerException?.Message);
    }

    [Fact]
    public async Task AcquisitionReversal_RejectsLaterPostedLotDependencies()
    {
        await using var database = await SqliteTestDatabase.CreateAsync();
        PostedLot originalLot;
        var transferTransactionId = Guid.NewGuid();
        var sourceEntryId = Guid.NewGuid();
        var destinationEntryId = Guid.NewGuid();
        var reversalTransactionId = Guid.NewGuid();
        var reversalEntryId = Guid.NewGuid();

        await using (var context = database.CreateContext())
        {
            await CoreLedgerTestData.SeedMasterDataAsync(context);
            originalLot = await CreateAndPostOpeningLotAsync(
                context,
                CoreLedgerTestData.FundAssetId,
                quantityE8: 100);

            context.LedgerTransactions.Add(
                CoreLedgerTestData.CreateDraftTransaction(
                    transferTransactionId,
                    TransactionType.Transfer));
            context.TransactionEntries.AddRange(
                CoreLedgerTestData.CreateEntry(
                    sourceEntryId,
                    transferTransactionId,
                    0,
                    CoreLedgerTestData.FundAssetId,
                    -100,
                    EntryRole.Transfer),
                CoreLedgerTestData.CreateEntry(
                    destinationEntryId,
                    transferTransactionId,
                    1,
                    CoreLedgerTestData.FundAssetId,
                    100,
                    EntryRole.Transfer,
                    accountId: CoreLedgerTestData.DestinationAccountId));
            context.LotEntryAllocations.AddRange(
                new LotEntryAllocationRow
                {
                    Id = Guid.NewGuid(),
                    AssetLotId = originalLot.LotId,
                    TransactionEntryId = sourceEntryId,
                    QuantityDeltaE8 = -100,
                    CreatedAtUtc = CoreLedgerTestData.CreatedAtUtc
                },
                new LotEntryAllocationRow
                {
                    Id = Guid.NewGuid(),
                    AssetLotId = originalLot.LotId,
                    TransactionEntryId = destinationEntryId,
                    QuantityDeltaE8 = 100,
                    CreatedAtUtc = CoreLedgerTestData.CreatedAtUtc
                });
            await context.SaveChangesAsync();
            await CoreLedgerTestData.PostAsync(context, transferTransactionId);

            context.LedgerTransactions.Add(
                CoreLedgerTestData.CreateDraftTransaction(
                    reversalTransactionId,
                    TransactionType.Reversal,
                    reversalOfTransactionId: originalLot.TransactionId));
            context.TransactionEntries.Add(
                CoreLedgerTestData.CreateEntry(
                    reversalEntryId,
                    reversalTransactionId,
                    0,
                    CoreLedgerTestData.FundAssetId,
                    -100,
                    EntryRole.Principal));
            context.LotEntryAllocations.Add(new LotEntryAllocationRow
            {
                Id = Guid.NewGuid(),
                AssetLotId = originalLot.LotId,
                TransactionEntryId = reversalEntryId,
                QuantityDeltaE8 = -100,
                CreatedAtUtc = CoreLedgerTestData.CreatedAtUtc
            });
            await context.SaveChangesAsync();
        }

        await using var postingContext = database.CreateContext();
        var exception = await Assert.ThrowsAsync<DbUpdateException>(() =>
            CoreLedgerTestData.PostAsync(postingContext, reversalTransactionId));

        Assert.Contains("later posted lot allocations", exception.InnerException?.Message);
    }

    [Fact]
    public async Task AssetLotUpdate_RevalidatesOpeningEntry()
    {
        await using var database = await SqliteTestDatabase.CreateAsync();
        var positiveTransactionId = Guid.NewGuid();
        var positiveEntryId = Guid.NewGuid();
        var negativeTransactionId = Guid.NewGuid();
        var negativeEntryId = Guid.NewGuid();
        var lotId = Guid.NewGuid();

        await using (var context = database.CreateContext())
        {
            await CoreLedgerTestData.SeedMasterDataAsync(context);
            context.LedgerTransactions.AddRange(
                CoreLedgerTestData.CreateDraftTransaction(
                    positiveTransactionId,
                    TransactionType.OpeningBalance),
                CoreLedgerTestData.CreateDraftTransaction(
                    negativeTransactionId,
                    TransactionType.Adjustment));
            context.TransactionEntries.AddRange(
                CoreLedgerTestData.CreateEntry(
                    positiveEntryId,
                    positiveTransactionId,
                    0,
                    CoreLedgerTestData.FundAssetId,
                    100,
                    EntryRole.Principal),
                CoreLedgerTestData.CreateEntry(
                    negativeEntryId,
                    negativeTransactionId,
                    0,
                    CoreLedgerTestData.FundAssetId,
                    -100,
                    EntryRole.Adjustment));
            context.AssetLots.Add(new AssetLotRow
            {
                Id = lotId,
                AssetId = CoreLedgerTestData.FundAssetId,
                OpeningTransactionEntryId = positiveEntryId,
                CostBasisStatus = CostBasisStatus.Unknown,
                CreatedAtUtc = CoreLedgerTestData.CreatedAtUtc
            });
            await context.SaveChangesAsync();
        }

        await AssertSqliteFailureAsync(
            () => database.ExecuteNonQueryAsync(
                "UPDATE AssetLot SET OpeningTransactionEntryId = $entryId WHERE Id = $lotId;",
                new SqliteParameter("$entryId", negativeEntryId.ToString("D")),
                new SqliteParameter("$lotId", lotId.ToString("D"))),
            "positive transaction entry");
    }

    [Fact]
    public async Task CostBasisConstraint_RejectsAmountForUnknownBasis()
    {
        await using var database = await SqliteTestDatabase.CreateAsync();

        var transactionId = Guid.NewGuid();
        var entryId = Guid.NewGuid();

        await using (var context = database.CreateContext())
        {
            await CoreLedgerTestData.SeedMasterDataAsync(context);
            context.LedgerTransactions.Add(
                CoreLedgerTestData.CreateDraftTransaction(
                    transactionId,
                    TransactionType.OpeningBalance));
            context.TransactionEntries.Add(
                CoreLedgerTestData.CreateEntry(
                    entryId,
                    transactionId,
                    0,
                    CoreLedgerTestData.FundAssetId,
                    100,
                    EntryRole.Principal));
            await context.SaveChangesAsync();
        }

        var exception = await Assert.ThrowsAsync<SqliteException>(() =>
            database.ExecuteNonQueryAsync(
                """
                INSERT INTO AssetLot (
                    Id,
                    AssetId,
                    OpeningTransactionEntryId,
                    OriginalCostBasisMinor,
                    CostBasisCurrencyCode,
                    CostBasisStatusCode,
                    CreatedAtUtc)
                VALUES (
                    $id,
                    $assetId,
                    $entryId,
                    0,
                    'TRY',
                    'UNKNOWN',
                    '2026-08-24T08:00:00.0000000Z');
                """,
                new SqliteParameter("$id", Guid.NewGuid().ToString("D")),
                new SqliteParameter("$assetId", CoreLedgerTestData.FundAssetId.ToString("D")),
                new SqliteParameter("$entryId", entryId.ToString("D"))));

        Assert.Equal(19, exception.SqliteErrorCode);
        Assert.Contains("CK_AssetLot_CostBasisShape", exception.Message);
    }

    [Fact]
    public async Task LotTrackedTransfer_PreservesGlobalQuantityAndDerivesCustody()
    {
        await using var database = await SqliteTestDatabase.CreateAsync();
        PostedLot postedLot;
        var transferId = Guid.NewGuid();
        var sourceEntryId = Guid.NewGuid();
        var destinationEntryId = Guid.NewGuid();

        await using (var context = database.CreateContext())
        {
            await CoreLedgerTestData.SeedMasterDataAsync(context);
            postedLot = await CreateAndPostOpeningLotAsync(
                context,
                CoreLedgerTestData.FundAssetId,
                quantityE8: 100);

            context.LedgerTransactions.Add(
                CoreLedgerTestData.CreateDraftTransaction(
                    transferId,
                    TransactionType.Transfer));

            context.TransactionEntries.AddRange(
                CoreLedgerTestData.CreateEntry(
                    sourceEntryId,
                    transferId,
                    0,
                    CoreLedgerTestData.FundAssetId,
                    -40,
                    EntryRole.Transfer),
                CoreLedgerTestData.CreateEntry(
                    destinationEntryId,
                    transferId,
                    1,
                    CoreLedgerTestData.FundAssetId,
                    40,
                    EntryRole.Transfer,
                    accountId: CoreLedgerTestData.DestinationAccountId));

            context.LotEntryAllocations.AddRange(
                new LotEntryAllocationRow
                {
                    Id = Guid.NewGuid(),
                    AssetLotId = postedLot.LotId,
                    TransactionEntryId = sourceEntryId,
                    QuantityDeltaE8 = -40,
                    CreatedAtUtc = CoreLedgerTestData.CreatedAtUtc
                },
                new LotEntryAllocationRow
                {
                    Id = Guid.NewGuid(),
                    AssetLotId = postedLot.LotId,
                    TransactionEntryId = destinationEntryId,
                    QuantityDeltaE8 = 40,
                    CreatedAtUtc = CoreLedgerTestData.CreatedAtUtc
                });

            await context.SaveChangesAsync();
            await CoreLedgerTestData.PostAsync(context, transferId);
        }

        var globalLotQuantity = Convert.ToInt64(
            await database.ExecuteScalarAsync(
                """
                SELECT SUM(allocation.QuantityDeltaE8)
                FROM LotEntryAllocation AS allocation
                JOIN TransactionEntry AS entry ON entry.Id = allocation.TransactionEntryId
                JOIN LedgerTransaction AS tx ON tx.Id = entry.TransactionId
                WHERE allocation.AssetLotId = $lotId
                  AND tx.StatusCode = 'POSTED';
                """,
                new SqliteParameter("$lotId", postedLot.LotId.ToString("D"))));

        var sourceQuantity = Convert.ToInt64(
            await database.ExecuteScalarAsync(
                """
                SELECT SUM(entry.QuantityDeltaE8)
                FROM TransactionEntry AS entry
                JOIN LedgerTransaction AS tx ON tx.Id = entry.TransactionId
                WHERE entry.AccountId = $accountId
                  AND entry.AssetId = $assetId
                  AND tx.StatusCode = 'POSTED';
                """,
                new SqliteParameter("$accountId", CoreLedgerTestData.AccountId.ToString("D")),
                new SqliteParameter("$assetId", CoreLedgerTestData.FundAssetId.ToString("D"))));

        var destinationQuantity = Convert.ToInt64(
            await database.ExecuteScalarAsync(
                """
                SELECT SUM(entry.QuantityDeltaE8)
                FROM TransactionEntry AS entry
                JOIN LedgerTransaction AS tx ON tx.Id = entry.TransactionId
                WHERE entry.AccountId = $accountId
                  AND entry.AssetId = $assetId
                  AND tx.StatusCode = 'POSTED';
                """,
                new SqliteParameter("$accountId", CoreLedgerTestData.DestinationAccountId.ToString("D")),
                new SqliteParameter("$assetId", CoreLedgerTestData.FundAssetId.ToString("D"))));

        Assert.Equal(100, globalLotQuantity);
        Assert.Equal(60, sourceQuantity);
        Assert.Equal(40, destinationQuantity);
    }

    [Fact]
    public async Task Positions_AreDerivedOnlyFromPostedEntryHistory()
    {
        await using var database = await SqliteTestDatabase.CreateAsync();
        var postedTransactionId = Guid.NewGuid();
        var draftTransactionId = Guid.NewGuid();

        await using (var context = database.CreateContext())
        {
            await CoreLedgerTestData.SeedMasterDataAsync(context);

            context.LedgerTransactions.AddRange(
                CoreLedgerTestData.CreateDraftTransaction(
                    postedTransactionId,
                    TransactionType.Adjustment),
                CoreLedgerTestData.CreateDraftTransaction(
                    draftTransactionId,
                    TransactionType.Adjustment));

            context.TransactionEntries.AddRange(
                CoreLedgerTestData.CreateEntry(
                    Guid.NewGuid(),
                    postedTransactionId,
                    0,
                    CoreLedgerTestData.CashAssetId,
                    300,
                    EntryRole.Adjustment),
                CoreLedgerTestData.CreateEntry(
                    Guid.NewGuid(),
                    draftTransactionId,
                    0,
                    CoreLedgerTestData.CashAssetId,
                    50,
                    EntryRole.Adjustment));

            await context.SaveChangesAsync();
            await CoreLedgerTestData.PostAsync(context, postedTransactionId);
        }

        var derivedPosition = Convert.ToInt64(
            await database.ExecuteScalarAsync(
                """
                SELECT SUM(entry.QuantityDeltaE8)
                FROM TransactionEntry AS entry
                JOIN LedgerTransaction AS tx ON tx.Id = entry.TransactionId
                WHERE entry.PortfolioId = $portfolioId
                  AND entry.AccountId = $accountId
                  AND entry.AssetId = $assetId
                  AND tx.StatusCode = 'POSTED';
                """,
                new SqliteParameter("$portfolioId", CoreLedgerTestData.PortfolioId.ToString("D")),
                new SqliteParameter("$accountId", CoreLedgerTestData.AccountId.ToString("D")),
                new SqliteParameter("$assetId", CoreLedgerTestData.CashAssetId.ToString("D"))));

        var forbiddenTableCount = Convert.ToInt32(
            await database.ExecuteScalarAsync(
                """
                SELECT COUNT(*)
                FROM sqlite_master
                WHERE type = 'table'
                  AND name IN (
                    'CurrentPosition',
                    'CurrentBalance',
                    'RemainingLotQuantity',
                    'CurrentPortfolioValue',
                    'AveragePurchasePrice',
                    'CurrentProfit',
                    'CurrentAllocationPercentage'
                  );
                """));

        Assert.Equal(300, derivedPosition);
        Assert.Equal(0, forbiddenTableCount);
    }

    private static async Task<PostedLot> CreateAndPostOpeningLotAsync(
        WealthLedgerDbContext context,
        Guid assetId,
        long quantityE8,
        bool includePhysicalGoldDetail = false)
    {
        var transactionId = Guid.NewGuid();
        var entryId = Guid.NewGuid();
        var lotId = Guid.NewGuid();
        var allocationId = Guid.NewGuid();

        context.LedgerTransactions.Add(
            CoreLedgerTestData.CreateDraftTransaction(
                transactionId,
                TransactionType.OpeningBalance));

        context.TransactionEntries.Add(
            CoreLedgerTestData.CreateEntry(
                entryId,
                transactionId,
                0,
                assetId,
                quantityE8,
                EntryRole.Principal));

        context.AssetLots.Add(new AssetLotRow
        {
            Id = lotId,
            AssetId = assetId,
            OpeningTransactionEntryId = entryId,
            AcquiredOn = CoreLedgerTestData.ExecutionDate,
            CostBasisStatus = CostBasisStatus.Unknown,
            CreatedAtUtc = CoreLedgerTestData.CreatedAtUtc
        });

        context.LotEntryAllocations.Add(new LotEntryAllocationRow
        {
            Id = allocationId,
            AssetLotId = lotId,
            TransactionEntryId = entryId,
            QuantityDeltaE8 = quantityE8,
            CreatedAtUtc = CoreLedgerTestData.CreatedAtUtc
        });

        if (includePhysicalGoldDetail)
        {
            context.PhysicalGoldLotDetails.Add(new PhysicalGoldLotDetailRow
            {
                AssetLotId = lotId,
                ActualFinenessPpm = 916_000,
                PieceCount = 1
            });
        }

        await context.SaveChangesAsync();
        await CoreLedgerTestData.PostAsync(context, transactionId);

        return new PostedLot(transactionId, entryId, lotId, allocationId);
    }

    private static Task<int> InsertAllocationAsync(
        SqliteTestDatabase database,
        Guid lotId,
        Guid entryId,
        long quantityDeltaE8)
        => database.ExecuteNonQueryAsync(
            """
            INSERT INTO LotEntryAllocation (
                Id,
                AssetLotId,
                TransactionEntryId,
                QuantityDeltaE8,
                CreatedAtUtc)
            VALUES (
                $id,
                $lotId,
                $entryId,
                $quantity,
                '2026-08-24T08:00:00.0000000Z');
            """,
            new SqliteParameter("$id", Guid.NewGuid().ToString("D")),
            new SqliteParameter("$lotId", lotId.ToString("D")),
            new SqliteParameter("$entryId", entryId.ToString("D")),
            new SqliteParameter("$quantity", quantityDeltaE8));

    private static async Task AssertSqliteFailureAsync(
        Func<Task> action,
        string expectedMessage)
    {
        var exception = await Assert.ThrowsAsync<SqliteException>(action);

        Assert.Equal(19, exception.SqliteErrorCode);
        Assert.Contains(expectedMessage, exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    private sealed record PostedLot(
        Guid TransactionId,
        Guid EntryId,
        Guid LotId,
        Guid AllocationId);
}
