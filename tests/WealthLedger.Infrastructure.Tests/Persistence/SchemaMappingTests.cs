using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using WealthLedger.Domain.Ledger;
using WealthLedger.Domain.Lots;
using WealthLedger.Infrastructure.Persistence.Rows;

namespace WealthLedger.Infrastructure.Tests.Persistence;

public sealed class SchemaMappingTests
{
    [Fact]
    public async Task Migration_CreatesCoreSchemaAndEnablesForeignKeys()
    {
        await using var database = await SqliteTestDatabase.CreateAsync();

        var foreignKeys = Convert.ToInt32(
            await database.ExecuteScalarAsync("PRAGMA foreign_keys;"));

        var coreTableCount = Convert.ToInt32(
            await database.ExecuteScalarAsync(
                """
                SELECT COUNT(*)
                FROM sqlite_master
                WHERE type = 'table'
                  AND name IN (
                    'Currency',
                    'Household',
                    'HouseholdMember',
                    'Institution',
                    'Portfolio',
                    'Account',
                    'Asset',
                    'LedgerTransaction',
                    'TransactionEntry',
                    'CashFlowDetail',
                    'TransactionCostComponent',
                    'AssetLot',
                    'LotEntryAllocation',
                    'PhysicalGoldLotDetail'
                  );
                """));

        var migrationId = Convert.ToString(
            await database.ExecuteScalarAsync(
                "SELECT MigrationId FROM __EFMigrationsHistory LIMIT 1;"));

        Assert.Equal(1, foreignKeys);
        Assert.Equal(14, coreTableCount);
        Assert.EndsWith("_001_CoreLedger", migrationId, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ForeignKeyEnforcement_RejectsMissingCurrency()
    {
        await using var database = await SqliteTestDatabase.CreateAsync();

        var exception = await Assert.ThrowsAsync<SqliteException>(() =>
            database.ExecuteNonQueryAsync(
                """
                INSERT INTO Household (Id, Name, BaseCurrencyCode, CreatedAtUtc)
                VALUES ($id, 'Invalid Household', 'ZZZ', '2026-08-24T08:00:00.0000000Z');
                """,
                new SqliteParameter("$id", Guid.NewGuid().ToString("D"))));

        Assert.Equal(19, exception.SqliteErrorCode);
        Assert.Equal(787, exception.SqliteExtendedErrorCode);
    }

    [Fact]
    public async Task FixedPointAndTemporalValues_RoundTripWithoutPrecisionLoss()
    {
        await using var database = await SqliteTestDatabase.CreateAsync();

        var transactionId = Guid.Parse("70000000-0000-0000-0000-000000000001");
        var entryId = Guid.Parse("71000000-0000-0000-0000-000000000001");
        var costId = Guid.Parse("72000000-0000-0000-0000-000000000001");
        var timestamp = CoreLedgerTestData.CreatedAtUtc.AddTicks(1_234_567);

        await using (var context = database.CreateContext())
        {
            await CoreLedgerTestData.SeedMasterDataAsync(context);

            context.LedgerTransactions.Add(new LedgerTransactionRow
            {
                Id = transactionId,
                HouseholdId = CoreLedgerTestData.HouseholdId,
                Type = TransactionType.Adjustment,
                Status = TransactionStatus.Draft,
                ExecutionDate = CoreLedgerTestData.ExecutionDate,
                CreatedAtUtc = timestamp
            });

            context.TransactionEntries.Add(
                CoreLedgerTestData.CreateEntry(
                    entryId,
                    transactionId,
                    0,
                    CoreLedgerTestData.CashAssetId,
                    long.MaxValue,
                    EntryRole.Adjustment,
                    unitPriceE8: long.MaxValue,
                    priceCurrencyCode: "TRY"));

            context.TransactionCostComponents.Add(new TransactionCostComponentRow
            {
                Id = costId,
                TransactionId = transactionId,
                Type = CostType.Other,
                Treatment = CostTreatment.InformationalOnly,
                AmountMinor = long.MaxValue,
                CurrencyCode = "TRY"
            });

            await context.SaveChangesAsync();
        }

        await using (var context = database.CreateContext())
        {
            var entry = await context.TransactionEntries
                .AsNoTracking()
                .SingleAsync(x => x.Id == entryId);

            var cost = await context.TransactionCostComponents
                .AsNoTracking()
                .SingleAsync(x => x.Id == costId);

            var transaction = await context.LedgerTransactions
                .AsNoTracking()
                .SingleAsync(x => x.Id == transactionId);

            Assert.Equal(long.MaxValue, entry.QuantityDeltaE8);
            Assert.Equal(long.MaxValue, entry.UnitPriceE8);
            Assert.Equal(long.MaxValue, cost.AmountMinor);
            Assert.Equal(timestamp, transaction.CreatedAtUtc);
            Assert.Equal(DateTimeKind.Utc, transaction.CreatedAtUtc.Kind);
        }

        var storageTypes = Convert.ToString(
            await database.ExecuteScalarAsync(
                """
                SELECT typeof(entry.QuantityDeltaE8)
                    || ',' || typeof(entry.UnitPriceE8)
                    || ',' || typeof(cost.AmountMinor)
                FROM TransactionEntry AS entry
                JOIN TransactionCostComponent AS cost
                  ON cost.TransactionId = entry.TransactionId
                WHERE entry.Id = $entryId;
                """,
                new SqliteParameter("$entryId", entryId.ToString("D"))));

        var storedTimestamp = Convert.ToString(
            await database.ExecuteScalarAsync(
                "SELECT CreatedAtUtc FROM LedgerTransaction WHERE Id = $id;",
                new SqliteParameter("$id", transactionId.ToString("D"))));

        var storedGuid = Convert.ToString(
            await database.ExecuteScalarAsync(
                "SELECT Id FROM LedgerTransaction WHERE Id = $id;",
                new SqliteParameter("$id", transactionId.ToString("D"))));

        Assert.Equal("integer,integer,integer", storageTypes);
        Assert.Equal(timestamp.ToString("O"), storedTimestamp);
        Assert.Equal(transactionId.ToString("D"), storedGuid);
    }

    [Fact]
    public async Task StableEnumCodes_AreStoredAsExplicitText()
    {
        await using var database = await SqliteTestDatabase.CreateAsync();

        var transactionId = Guid.Parse("70000000-0000-0000-0000-000000000002");
        var entryId = Guid.Parse("71000000-0000-0000-0000-000000000002");
        var lotId = Guid.Parse("73000000-0000-0000-0000-000000000002");

        await using (var context = database.CreateContext())
        {
            await CoreLedgerTestData.SeedMasterDataAsync(context);

            context.LedgerTransactions.Add(
                CoreLedgerTestData.CreateDraftTransaction(transactionId, TransactionType.Buy));

            context.TransactionEntries.Add(
                CoreLedgerTestData.CreateEntry(
                    entryId,
                    transactionId,
                    0,
                    CoreLedgerTestData.GoldAssetId,
                    100,
                    EntryRole.Principal));

            context.AssetLots.Add(new AssetLotRow
            {
                Id = lotId,
                AssetId = CoreLedgerTestData.GoldAssetId,
                OpeningTransactionEntryId = entryId,
                AcquiredOn = CoreLedgerTestData.ExecutionDate,
                OriginalCostBasisMinor = 0,
                CostBasisCurrencyCode = "TRY",
                CostBasisStatus = CostBasisStatus.Known,
                CreatedAtUtc = CoreLedgerTestData.CreatedAtUtc
            });

            context.LotEntryAllocations.Add(new LotEntryAllocationRow
            {
                Id = Guid.NewGuid(),
                AssetLotId = lotId,
                TransactionEntryId = entryId,
                QuantityDeltaE8 = 100,
                CreatedAtUtc = CoreLedgerTestData.CreatedAtUtc
            });

            context.PhysicalGoldLotDetails.Add(new PhysicalGoldLotDetailRow
            {
                AssetLotId = lotId,
                ActualFinenessPpm = 916_000,
                PieceCount = 1
            });

            await context.SaveChangesAsync();
        }

        var assetCodes = Convert.ToString(
            await database.ExecuteScalarAsync(
                """
                SELECT AssetTypeCode || ',' || BaseUnitCode || ',' || LotTrackingModeCode
                FROM Asset
                WHERE Id = $id;
                """,
                new SqliteParameter("$id", CoreLedgerTestData.GoldAssetId.ToString("D"))));

        var transactionCodes = Convert.ToString(
            await database.ExecuteScalarAsync(
                """
                SELECT tx.TransactionTypeCode || ',' || tx.StatusCode || ',' || entry.EntryRoleCode
                FROM LedgerTransaction AS tx
                JOIN TransactionEntry AS entry ON entry.TransactionId = tx.Id
                WHERE tx.Id = $id;
                """,
                new SqliteParameter("$id", transactionId.ToString("D"))));

        var lotCode = Convert.ToString(
            await database.ExecuteScalarAsync(
                "SELECT CostBasisStatusCode FROM AssetLot WHERE Id = $id;",
                new SqliteParameter("$id", lotId.ToString("D"))));

        Assert.Equal("PHYSICAL_GOLD,GROSS_GRAM,REQUIRED", assetCodes);
        Assert.Equal("BUY,DRAFT,PRINCIPAL", transactionCodes);
        Assert.Equal("KNOWN", lotCode);
    }

    [Fact]
    public async Task CostBasis_KnownZeroAndUnknownRemainDistinct()
    {
        await using var database = await SqliteTestDatabase.CreateAsync();

        var transactionId = Guid.NewGuid();
        var knownEntryId = Guid.NewGuid();
        var unknownEntryId = Guid.NewGuid();
        var knownLotId = Guid.NewGuid();
        var unknownLotId = Guid.NewGuid();

        await using (var context = database.CreateContext())
        {
            await CoreLedgerTestData.SeedMasterDataAsync(context);

            context.LedgerTransactions.Add(
                CoreLedgerTestData.CreateDraftTransaction(
                    transactionId,
                    TransactionType.OpeningBalance));

            context.TransactionEntries.AddRange(
                CoreLedgerTestData.CreateEntry(
                    knownEntryId,
                    transactionId,
                    0,
                    CoreLedgerTestData.FundAssetId,
                    100,
                    EntryRole.Principal),
                CoreLedgerTestData.CreateEntry(
                    unknownEntryId,
                    transactionId,
                    1,
                    CoreLedgerTestData.OtherFundAssetId,
                    100,
                    EntryRole.Principal));

            context.AssetLots.AddRange(
                new AssetLotRow
                {
                    Id = knownLotId,
                    AssetId = CoreLedgerTestData.FundAssetId,
                    OpeningTransactionEntryId = knownEntryId,
                    OriginalCostBasisMinor = 0,
                    CostBasisCurrencyCode = "TRY",
                    CostBasisStatus = CostBasisStatus.Known,
                    CreatedAtUtc = CoreLedgerTestData.CreatedAtUtc
                },
                new AssetLotRow
                {
                    Id = unknownLotId,
                    AssetId = CoreLedgerTestData.OtherFundAssetId,
                    OpeningTransactionEntryId = unknownEntryId,
                    CostBasisStatus = CostBasisStatus.Unknown,
                    CreatedAtUtc = CoreLedgerTestData.CreatedAtUtc
                });

            await context.SaveChangesAsync();
        }

        await using var verificationContext = database.CreateContext();
        var known = await verificationContext.AssetLots
            .AsNoTracking()
            .SingleAsync(x => x.Id == knownLotId);
        var unknown = await verificationContext.AssetLots
            .AsNoTracking()
            .SingleAsync(x => x.Id == unknownLotId);

        Assert.Equal(CostBasisStatus.Known, known.CostBasisStatus);
        Assert.Equal(0, known.OriginalCostBasisMinor);
        Assert.Equal("TRY", known.CostBasisCurrencyCode);
        Assert.Equal(CostBasisStatus.Unknown, unknown.CostBasisStatus);
        Assert.Null(unknown.OriginalCostBasisMinor);
        Assert.Null(unknown.CostBasisCurrencyCode);
    }

    [Fact]
    public async Task DateOrderingConstraint_RejectsSettlementBeforeExecution()
    {
        await using var database = await SqliteTestDatabase.CreateAsync();

        await using (var context = database.CreateContext())
        {
            await CoreLedgerTestData.SeedMasterDataAsync(context);
        }

        var exception = await Assert.ThrowsAsync<SqliteException>(() =>
            database.ExecuteNonQueryAsync(
                """
                INSERT INTO LedgerTransaction (
                    Id,
                    HouseholdId,
                    TransactionTypeCode,
                    StatusCode,
                    ExecutionDate,
                    SettlementDate,
                    CreatedAtUtc)
                VALUES (
                    $id,
                    $householdId,
                    'ADJUSTMENT',
                    'DRAFT',
                    '2026-08-24',
                    '2026-08-23',
                    '2026-08-24T08:00:00.0000000Z');
                """,
                new SqliteParameter("$id", Guid.NewGuid().ToString("D")),
                new SqliteParameter("$householdId", CoreLedgerTestData.HouseholdId.ToString("D"))));

        Assert.Equal(19, exception.SqliteErrorCode);
        Assert.Contains("CK_LedgerTransaction_ExecutionSettlementDate", exception.Message);
    }
}
