using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using WealthLedger.Domain.Assets;
using WealthLedger.Domain.Ledger;
using WealthLedger.Domain.Lots;
using WealthLedger.Domain.Portfolios;
using WealthLedger.Infrastructure.Persistence;
using WealthLedger.Infrastructure.Persistence.Rows;

namespace WealthLedger.Infrastructure.Tests.Persistence;

public sealed class OpeningBalanceCutoverGuardTests
{
    private const string PreviousMigration =
        "20260903075104_005_WorkspaceIdentity";

    private const string CurrentMigration =
        "20260910101810_006_OpeningBalanceCutoverGuards";

    [Fact]
    public async Task Migration_UpDownUpChangesOnlyOwnedObjectsAndPreservesData()
    {
        await using var database = await SqliteTestDatabase.CreateAsync(
            PreviousMigration);

        await using (var seedContext = database.CreateContext())
        {
            await CoreLedgerTestData.SeedMasterDataAsync(seedContext);
        }

        Assert.Equal(0L, await CountM007ObjectsAsync(database));

        await using (var upContext = database.CreateContext())
        {
            await upContext.Database.MigrateAsync();
            Assert.False(upContext.Database.HasPendingModelChanges());
        }

        Assert.Equal(2L, await CountM007ObjectsAsync(database));
        Assert.Equal(2L, await CountHouseholdsAsync(database));

        await using (var downContext = database.CreateContext())
        {
            await downContext.Database.MigrateAsync(PreviousMigration);
        }

        Assert.Equal(0L, await CountM007ObjectsAsync(database));
        Assert.Equal(2L, await CountHouseholdsAsync(database));

        await using (var secondUpContext = database.CreateContext())
        {
            await secondUpContext.Database.MigrateAsync();
        }

        Assert.Equal(2L, await CountM007ObjectsAsync(database));
        Assert.Equal(2L, await CountHouseholdsAsync(database));
    }

    [Fact]
    public async Task Migration_IncompatibleLegacyOpeningFailsClosed()
    {
        await using var database = await SqliteTestDatabase.CreateAsync(
            PreviousMigration);
        var transactionId = Guid.NewGuid();

        await using (var seedContext = database.CreateContext())
        {
            await CoreLedgerTestData.SeedMasterDataAsync(seedContext);
            seedContext.LedgerTransactions.Add(new LedgerTransactionRow
            {
                Id = transactionId,
                HouseholdId = CoreLedgerTestData.HouseholdId,
                Type = TransactionType.OpeningBalance,
                Status = TransactionStatus.Draft,
                ExecutionDate = CoreLedgerTestData.ExecutionDate,
                Note = null,
                CreatedAtUtc = CoreLedgerTestData.CreatedAtUtc
            });
            seedContext.TransactionEntries.Add(
                CoreLedgerTestData.CreateEntry(
                    Guid.NewGuid(),
                    transactionId,
                    0,
                    CoreLedgerTestData.CashAssetId,
                    100_000_000,
                    EntryRole.Principal));
            await seedContext.SaveChangesAsync();
            await CoreLedgerTestData.PostAsync(seedContext, transactionId);
        }

        await using (var migrationContext = database.CreateContext())
        {
            var exception = await Assert.ThrowsAsync<SqliteException>(
                () => migrationContext.Database.MigrateAsync());

            Assert.Equal(19, exception.SqliteErrorCode);
            Assert.DoesNotContain(CurrentMigration, exception.Message);
        }

        Assert.Equal(
            PreviousMigration,
            Convert.ToString(await database.ExecuteScalarAsync(
                "SELECT MAX(MigrationId) FROM __EFMigrationsHistory;")));
        Assert.Equal(0L, await CountM007ObjectsAsync(database));
    }

    [Fact]
    public async Task Posting_SemanticDuplicateIsRejectedAndReversalPermitsReplacement()
    {
        await using var database = await CreateSeededDatabaseAsync();
        var firstId = Guid.NewGuid();
        var firstEntryId = Guid.NewGuid();
        var replacementId = Guid.NewGuid();

        await using (var context = database.CreateContext())
        {
            AddCashOpening(context, firstId, firstEntryId);
            AddCashOpening(context, replacementId, Guid.NewGuid());
            await context.SaveChangesAsync();
            await CoreLedgerTestData.PostAsync(context, firstId);
        }

        await using (var duplicateContext = database.CreateContext())
        {
            var exception = await Assert.ThrowsAsync<DbUpdateException>(
                () => CoreLedgerTestData.PostAsync(
                    duplicateContext,
                    replacementId));

            Assert.Contains(
                "WL_M007_ALREADY_EXISTS",
                exception.InnerException?.Message,
                StringComparison.Ordinal);
        }

        var reversalId = Guid.NewGuid();

        await using (var reversalContext = database.CreateContext())
        {
            reversalContext.LedgerTransactions.Add(
                CoreLedgerTestData.CreateDraftTransaction(
                    reversalId,
                    TransactionType.Reversal,
                    reversalOfTransactionId: firstId));
            reversalContext.TransactionEntries.Add(
                CoreLedgerTestData.CreateEntry(
                    Guid.NewGuid(),
                    reversalId,
                    0,
                    CoreLedgerTestData.CashAssetId,
                    -1_234_567_000_000,
                    EntryRole.Principal));
            await reversalContext.SaveChangesAsync();
            await CoreLedgerTestData.PostAsync(reversalContext, reversalId);
        }

        await using (var replacementContext = database.CreateContext())
        {
            await CoreLedgerTestData.PostAsync(
                replacementContext,
                replacementId);
        }

        Assert.Equal(
            3L,
            Convert.ToInt64(await database.ExecuteScalarAsync(
                "SELECT COUNT(*) FROM LedgerTransaction WHERE StatusCode = 'POSTED';")));
    }

    [Fact]
    public async Task Posting_PriorEffectiveHistoryRejectsOpening()
    {
        await using var database = await CreateSeededDatabaseAsync();
        var adjustmentId = Guid.NewGuid();
        var openingId = Guid.NewGuid();

        await using (var context = database.CreateContext())
        {
            context.LedgerTransactions.Add(
                CoreLedgerTestData.CreateDraftTransaction(
                    adjustmentId,
                    TransactionType.Adjustment));
            context.TransactionEntries.Add(
                CoreLedgerTestData.CreateEntry(
                    Guid.NewGuid(),
                    adjustmentId,
                    0,
                    CoreLedgerTestData.CashAssetId,
                    100_000_000,
                    EntryRole.Adjustment));
            AddCashOpening(context, openingId, Guid.NewGuid());
            await context.SaveChangesAsync();
            await CoreLedgerTestData.PostAsync(context, adjustmentId);
        }

        await using var postingContext = database.CreateContext();
        var exception = await Assert.ThrowsAsync<DbUpdateException>(
            () => CoreLedgerTestData.PostAsync(postingContext, openingId));

        Assert.Contains(
            "WL_M007_SCOPE_HAS_EFFECTIVE_HISTORY",
            exception.InnerException?.Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task Posting_OptionalLotModeStillRequiresExactNewLotAllocation()
    {
        await using var database = await CreateSeededDatabaseAsync();
        var assetId = Guid.NewGuid();
        var transactionId = Guid.NewGuid();
        var entryId = Guid.NewGuid();
        var allocationId = Guid.NewGuid();

        await using (var context = database.CreateContext())
        {
            context.Assets.Add(new AssetRow
            {
                Id = assetId,
                Code = "SYNTHETIC_OPTIONAL_FUND",
                Name = "Synthetic Optional Fund",
                Type = AssetType.Fund,
                BaseUnit = AssetUnit.FundUnit,
                BaseCurrencyCode = "TRY",
                LotTrackingMode = LotTrackingMode.Optional,
                IsActive = true,
                CreatedAtUtc = CoreLedgerTestData.CreatedAtUtc
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
                    assetId,
                    123_456_789,
                    EntryRole.Principal));
            var lotId = Guid.NewGuid();
            context.AssetLots.Add(new AssetLotRow
            {
                Id = lotId,
                AssetId = assetId,
                OpeningTransactionEntryId = entryId,
                CostBasisStatus = CostBasisStatus.Unknown,
                CreatedAtUtc = CoreLedgerTestData.CreatedAtUtc
            });
            context.LotEntryAllocations.Add(new LotEntryAllocationRow
            {
                Id = allocationId,
                AssetLotId = lotId,
                TransactionEntryId = entryId,
                QuantityDeltaE8 = 123_456_788,
                CreatedAtUtc = CoreLedgerTestData.CreatedAtUtc
            });
            await context.SaveChangesAsync();
        }

        await using (var invalidContext = database.CreateContext())
        {
            var exception = await Assert.ThrowsAsync<DbUpdateException>(
                () => CoreLedgerTestData.PostAsync(
                    invalidContext,
                    transactionId));
            Assert.Contains(
                "WL_M007_INVALID_OPENING_BALANCE",
                exception.InnerException?.Message,
                StringComparison.Ordinal);
        }

        await using (var correctedContext = database.CreateContext())
        {
            var allocation = await correctedContext.LotEntryAllocations
                .SingleAsync(row => row.Id == allocationId);
            allocation.QuantityDeltaE8 = 123_456_789;
            await correctedContext.SaveChangesAsync();
            await CoreLedgerTestData.PostAsync(
                correctedContext,
                transactionId);
        }
    }

    [Fact]
    public async Task Posting_PhysicalGoldRequiresVaultAndDetail()
    {
        await using var database = await CreateSeededDatabaseAsync();
        var vaultId = Guid.NewGuid();
        var transactionId = Guid.NewGuid();
        var entryId = Guid.NewGuid();
        var lotId = Guid.NewGuid();

        await using (var context = database.CreateContext())
        {
            context.Accounts.Add(new AccountRow
            {
                Id = vaultId,
                HouseholdId = CoreLedgerTestData.HouseholdId,
                Code = "SYNTHETIC_GUARD_VAULT",
                Name = "Synthetic Guard Vault",
                Type = AccountType.PhysicalVault,
                IsActive = true,
                OpenedOn = CoreLedgerTestData.ExecutionDate
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
                    2_525_000_000,
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
            context.LotEntryAllocations.Add(new LotEntryAllocationRow
            {
                Id = Guid.NewGuid(),
                AssetLotId = lotId,
                TransactionEntryId = entryId,
                QuantityDeltaE8 = 2_525_000_000,
                CreatedAtUtc = CoreLedgerTestData.CreatedAtUtc
            });
            await context.SaveChangesAsync();
        }

        await using var postingContext = database.CreateContext();
        var exception = await Assert.ThrowsAsync<DbUpdateException>(
            () => CoreLedgerTestData.PostAsync(
                postingContext,
                transactionId));

        Assert.Contains(
            "WL_M007_INVALID_OPENING_BALANCE",
            exception.InnerException?.Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task SemanticGuardQueryPlanUsesOpeningBalanceScopeIndex()
    {
        await using var database = await CreateSeededDatabaseAsync();
        await using var connection = new SqliteConnection(
            database.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            EXPLAIN QUERY PLAN
            SELECT existingEntry.TransactionId
            FROM TransactionEntry AS candidateEntry
            JOIN TransactionEntry AS existingEntry
              ON existingEntry.PortfolioId = candidateEntry.PortfolioId
             AND existingEntry.AccountId = candidateEntry.AccountId
             AND existingEntry.AssetId = candidateEntry.AssetId
            WHERE candidateEntry.TransactionId = $transactionId;
            """;
        command.Parameters.AddWithValue(
            "$transactionId",
            Guid.NewGuid().ToString("D"));
        var details = new List<string>();
        await using var reader = await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            details.Add(reader.GetString(3));
        }

        Assert.Contains(
            details,
            detail => detail.Contains(
                "IX_TransactionEntry_OpeningBalanceScope",
                StringComparison.Ordinal));
    }

    private static async Task<SqliteTestDatabase> CreateSeededDatabaseAsync()
    {
        var database = await SqliteTestDatabase.CreateAsync();
        await using var context = database.CreateContext();
        await CoreLedgerTestData.SeedMasterDataAsync(context);
        return database;
    }

    private static void AddCashOpening(
        WealthLedgerDbContext context,
        Guid transactionId,
        Guid entryId)
    {
        context.LedgerTransactions.Add(
            CoreLedgerTestData.CreateDraftTransaction(
                transactionId,
                TransactionType.OpeningBalance));
        context.TransactionEntries.Add(
            CoreLedgerTestData.CreateEntry(
                entryId,
                transactionId,
                0,
                CoreLedgerTestData.CashAssetId,
                1_234_567_000_000,
                EntryRole.Principal));
    }

    private static async Task<long> CountM007ObjectsAsync(
        SqliteTestDatabase database)
        => Convert.ToInt64(await database.ExecuteScalarAsync(
            """
            SELECT COUNT(*)
            FROM sqlite_master
            WHERE name IN (
                'TR_LedgerTransaction_ValidateOpeningBalanceBeforePosting',
                'IX_TransactionEntry_OpeningBalanceScope');
            """));

    private static async Task<long> CountHouseholdsAsync(
        SqliteTestDatabase database)
        => Convert.ToInt64(await database.ExecuteScalarAsync(
            "SELECT COUNT(*) FROM Household;"));
}
