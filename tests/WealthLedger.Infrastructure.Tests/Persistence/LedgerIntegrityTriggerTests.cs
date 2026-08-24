using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using WealthLedger.Domain.Ledger;
using WealthLedger.Infrastructure.Persistence.Rows;

namespace WealthLedger.Infrastructure.Tests.Persistence;

public sealed class LedgerIntegrityTriggerTests
{
    [Fact]
    public async Task HouseholdConsistency_RejectsCrossHouseholdEntry()
    {
        await using var database = await SqliteTestDatabase.CreateAsync();
        var transactionId = Guid.NewGuid();

        await using (var context = database.CreateContext())
        {
            await CoreLedgerTestData.SeedMasterDataAsync(context);
            context.LedgerTransactions.Add(
                CoreLedgerTestData.CreateDraftTransaction(
                    transactionId,
                    TransactionType.Adjustment));
            await context.SaveChangesAsync();
        }

        var exception = await Assert.ThrowsAsync<SqliteException>(() =>
            database.ExecuteNonQueryAsync(
                """
                INSERT INTO TransactionEntry (
                    Id,
                    TransactionId,
                    EntrySequence,
                    PortfolioId,
                    AccountId,
                    AssetId,
                    QuantityDeltaE8,
                    EntryRoleCode,
                    CreatedAtUtc)
                VALUES (
                    $id,
                    $transactionId,
                    0,
                    $portfolioId,
                    $accountId,
                    $assetId,
                    100,
                    'ADJUSTMENT',
                    '2026-08-24T08:00:00.0000000Z');
                """,
                new SqliteParameter("$id", Guid.NewGuid().ToString("D")),
                new SqliteParameter("$transactionId", transactionId.ToString("D")),
                new SqliteParameter("$portfolioId", CoreLedgerTestData.PortfolioId.ToString("D")),
                new SqliteParameter("$accountId", CoreLedgerTestData.OtherAccountId.ToString("D")),
                new SqliteParameter("$assetId", CoreLedgerTestData.CashAssetId.ToString("D"))));

        Assert.Equal(19, exception.SqliteErrorCode);
        Assert.Contains("same household", exception.Message);
    }

    [Fact]
    public async Task PostedTransactionGraph_RejectsMutationDeletionAndAppend()
    {
        await using var database = await SqliteTestDatabase.CreateAsync();

        var transactionId = Guid.NewGuid();
        var entryId = Guid.NewGuid();
        var costId = Guid.NewGuid();

        await using (var context = database.CreateContext())
        {
            await CoreLedgerTestData.SeedMasterDataAsync(context);

            context.LedgerTransactions.Add(
                CoreLedgerTestData.CreateDraftTransaction(
                    transactionId,
                    TransactionType.Contribution));

            context.TransactionEntries.Add(
                CoreLedgerTestData.CreateEntry(
                    entryId,
                    transactionId,
                    0,
                    CoreLedgerTestData.CashAssetId,
                    100,
                    EntryRole.Principal));

            context.CashFlowDetails.Add(new CashFlowDetailRow
            {
                TransactionId = transactionId,
                Category = CashFlowCategory.Salary,
                HouseholdMemberId = CoreLedgerTestData.HouseholdMemberId
            });

            context.TransactionCostComponents.Add(new TransactionCostComponentRow
            {
                Id = costId,
                TransactionId = transactionId,
                Type = CostType.Commission,
                Treatment = CostTreatment.InformationalOnly,
                AmountMinor = 0,
                CurrencyCode = "TRY"
            });

            await context.SaveChangesAsync();
            await CoreLedgerTestData.PostAsync(context, transactionId);
        }

        await AssertSqliteFailureAsync(
            () => database.ExecuteNonQueryAsync(
                "UPDATE LedgerTransaction SET Note = 'changed' WHERE Id = $id;",
                new SqliteParameter("$id", transactionId.ToString("D"))),
            "immutable");

        await AssertSqliteFailureAsync(
            () => database.ExecuteNonQueryAsync(
                "DELETE FROM LedgerTransaction WHERE Id = $id;",
                new SqliteParameter("$id", transactionId.ToString("D"))),
            "cannot be deleted");

        await AssertSqliteFailureAsync(
            () => database.ExecuteNonQueryAsync(
                "UPDATE TransactionEntry SET QuantityDeltaE8 = 200 WHERE Id = $id;",
                new SqliteParameter("$id", entryId.ToString("D"))),
            "immutable");

        await AssertSqliteFailureAsync(
            () => database.ExecuteNonQueryAsync(
                "DELETE FROM TransactionEntry WHERE Id = $id;",
                new SqliteParameter("$id", entryId.ToString("D"))),
            "cannot be deleted");

        await AssertSqliteFailureAsync(
            () => database.ExecuteNonQueryAsync(
                """
                INSERT INTO TransactionEntry (
                    Id, TransactionId, EntrySequence, PortfolioId, AccountId,
                    AssetId, QuantityDeltaE8, EntryRoleCode, CreatedAtUtc)
                VALUES (
                    $id, $transactionId, 1, $portfolioId, $accountId,
                    $assetId, 1, 'PRINCIPAL', '2026-08-24T08:00:00.0000000Z');
                """,
                new SqliteParameter("$id", Guid.NewGuid().ToString("D")),
                new SqliteParameter("$transactionId", transactionId.ToString("D")),
                new SqliteParameter("$portfolioId", CoreLedgerTestData.PortfolioId.ToString("D")),
                new SqliteParameter("$accountId", CoreLedgerTestData.AccountId.ToString("D")),
                new SqliteParameter("$assetId", CoreLedgerTestData.CashAssetId.ToString("D"))),
            "Cannot append entries");

        await AssertSqliteFailureAsync(
            () => database.ExecuteNonQueryAsync(
                "UPDATE TransactionCostComponent SET AmountMinor = 1 WHERE Id = $id;",
                new SqliteParameter("$id", costId.ToString("D"))),
            "immutable");

        await AssertSqliteFailureAsync(
            () => database.ExecuteNonQueryAsync(
                "DELETE FROM TransactionCostComponent WHERE Id = $id;",
                new SqliteParameter("$id", costId.ToString("D"))),
            "cannot be deleted");

        await AssertSqliteFailureAsync(
            () => database.ExecuteNonQueryAsync(
                "UPDATE CashFlowDetail SET CashFlowCategoryCode = 'BONUS' WHERE TransactionId = $id;",
                new SqliteParameter("$id", transactionId.ToString("D"))),
            "immutable");

        await AssertSqliteFailureAsync(
            () => database.ExecuteNonQueryAsync(
                "DELETE FROM CashFlowDetail WHERE TransactionId = $id;",
                new SqliteParameter("$id", transactionId.ToString("D"))),
            "cannot be deleted");

        var transactionCount = Convert.ToInt32(
            await database.ExecuteScalarAsync(
                "SELECT COUNT(*) FROM LedgerTransaction WHERE Id = $id;",
                new SqliteParameter("$id", transactionId.ToString("D"))));
        var entryQuantity = Convert.ToInt64(
            await database.ExecuteScalarAsync(
                "SELECT QuantityDeltaE8 FROM TransactionEntry WHERE Id = $id;",
                new SqliteParameter("$id", entryId.ToString("D"))));

        Assert.Equal(1, transactionCount);
        Assert.Equal(100, entryQuantity);
    }

    [Fact]
    public async Task Posting_RequiresEntriesAndExecutionDate()
    {
        await using var database = await SqliteTestDatabase.CreateAsync();
        var emptyTransactionId = Guid.NewGuid();
        var noDateTransactionId = Guid.NewGuid();

        await using (var context = database.CreateContext())
        {
            await CoreLedgerTestData.SeedMasterDataAsync(context);

            context.LedgerTransactions.Add(
                CoreLedgerTestData.CreateDraftTransaction(
                    emptyTransactionId,
                    TransactionType.Adjustment));

            context.LedgerTransactions.Add(new LedgerTransactionRow
            {
                Id = noDateTransactionId,
                HouseholdId = CoreLedgerTestData.HouseholdId,
                Type = TransactionType.Adjustment,
                Status = TransactionStatus.Draft,
                CreatedAtUtc = CoreLedgerTestData.CreatedAtUtc
            });

            context.TransactionEntries.Add(
                CoreLedgerTestData.CreateEntry(
                    Guid.NewGuid(),
                    noDateTransactionId,
                    0,
                    CoreLedgerTestData.CashAssetId,
                    1,
                    EntryRole.Adjustment));

            await context.SaveChangesAsync();
        }

        await using (var context = database.CreateContext())
        {
            var exception = await Assert.ThrowsAsync<DbUpdateException>(() =>
                CoreLedgerTestData.PostAsync(context, emptyTransactionId));

            Assert.Contains("without entries", exception.InnerException?.Message);
        }

        await using (var context = database.CreateContext())
        {
            var exception = await Assert.ThrowsAsync<DbUpdateException>(() =>
                CoreLedgerTestData.PostAsync(context, noDateTransactionId));

            Assert.Contains("execution date", exception.InnerException?.Message);
        }
    }

    [Fact]
    public async Task Reversal_RequiresExactInverseAndIsUnique()
    {
        await using var database = await SqliteTestDatabase.CreateAsync();

        var originalId = Guid.NewGuid();
        var reversalId = Guid.NewGuid();
        var originalPrincipalId = Guid.NewGuid();
        var originalConsiderationId = Guid.NewGuid();
        var reversalPrincipalId = Guid.NewGuid();

        await using (var context = database.CreateContext())
        {
            await CoreLedgerTestData.SeedMasterDataAsync(context);

            context.LedgerTransactions.Add(
                CoreLedgerTestData.CreateDraftTransaction(
                    originalId,
                    TransactionType.Buy));

            context.TransactionEntries.AddRange(
                CoreLedgerTestData.CreateEntry(
                    originalPrincipalId,
                    originalId,
                    0,
                    CoreLedgerTestData.CashAssetId,
                    100,
                    EntryRole.Principal,
                    unitPriceE8: 50,
                    priceCurrencyCode: "TRY"),
                CoreLedgerTestData.CreateEntry(
                    originalConsiderationId,
                    originalId,
                    1,
                    CoreLedgerTestData.CashAssetId,
                    -500,
                    EntryRole.Consideration));

            await context.SaveChangesAsync();
            await CoreLedgerTestData.PostAsync(context, originalId);

            context.LedgerTransactions.Add(
                CoreLedgerTestData.CreateDraftTransaction(
                    reversalId,
                    TransactionType.Reversal,
                    reversalOfTransactionId: originalId));

            context.TransactionEntries.AddRange(
                CoreLedgerTestData.CreateEntry(
                    reversalPrincipalId,
                    reversalId,
                    0,
                    CoreLedgerTestData.CashAssetId,
                    -99,
                    EntryRole.Principal,
                    unitPriceE8: 50,
                    priceCurrencyCode: "TRY"),
                CoreLedgerTestData.CreateEntry(
                    Guid.NewGuid(),
                    reversalId,
                    1,
                    CoreLedgerTestData.CashAssetId,
                    500,
                    EntryRole.Consideration));

            await context.SaveChangesAsync();
        }

        await using (var context = database.CreateContext())
        {
            var exception = await Assert.ThrowsAsync<DbUpdateException>(() =>
                CoreLedgerTestData.PostAsync(context, reversalId));

            Assert.Contains("exact opposite quantity", exception.InnerException?.Message);
        }

        await using (var context = database.CreateContext())
        {
            var reversalPrincipal = await context.TransactionEntries
                .SingleAsync(x => x.Id == reversalPrincipalId);
            reversalPrincipal.QuantityDeltaE8 = -100;
            await context.SaveChangesAsync();

            await CoreLedgerTestData.PostAsync(context, reversalId);
        }

        await using (var context = database.CreateContext())
        {
            context.LedgerTransactions.Add(
                CoreLedgerTestData.CreateDraftTransaction(
                    Guid.NewGuid(),
                    TransactionType.Reversal,
                    reversalOfTransactionId: originalId));

            var exception = await Assert.ThrowsAsync<DbUpdateException>(
                () => context.SaveChangesAsync());

            Assert.Contains("UNIQUE constraint failed", exception.InnerException?.Message);
        }

        var netQuantity = Convert.ToInt64(
            await database.ExecuteScalarAsync(
                """
                SELECT SUM(entry.QuantityDeltaE8)
                FROM TransactionEntry AS entry
                JOIN LedgerTransaction AS tx ON tx.Id = entry.TransactionId
                WHERE tx.Id IN ($originalId, $reversalId)
                  AND tx.StatusCode = 'POSTED';
                """,
                new SqliteParameter("$originalId", originalId.ToString("D")),
                new SqliteParameter("$reversalId", reversalId.ToString("D"))));

        var originalStatus = Convert.ToString(
            await database.ExecuteScalarAsync(
                "SELECT StatusCode FROM LedgerTransaction WHERE Id = $id;",
                new SqliteParameter("$id", originalId.ToString("D"))));

        Assert.Equal(0, netQuantity);
        Assert.Equal("POSTED", originalStatus);
    }

    [Fact]
    public async Task CashFlowDetail_RejectsNonContributionTransaction()
    {
        await using var database = await SqliteTestDatabase.CreateAsync();
        var transactionId = Guid.NewGuid();

        await using (var context = database.CreateContext())
        {
            await CoreLedgerTestData.SeedMasterDataAsync(context);
            context.LedgerTransactions.Add(
                CoreLedgerTestData.CreateDraftTransaction(
                    transactionId,
                    TransactionType.Buy));
            await context.SaveChangesAsync();
        }

        var exception = await Assert.ThrowsAsync<SqliteException>(() =>
            database.ExecuteNonQueryAsync(
                """
                INSERT INTO CashFlowDetail (
                    TransactionId,
                    CashFlowCategoryCode,
                    HouseholdMemberId)
                VALUES ($transactionId, 'SALARY', $memberId);
                """,
                new SqliteParameter("$transactionId", transactionId.ToString("D")),
                new SqliteParameter("$memberId", CoreLedgerTestData.HouseholdMemberId.ToString("D"))));

        Assert.Equal(19, exception.SqliteErrorCode);
        Assert.Contains("contribution", exception.Message);
    }

    [Fact]
    public async Task CashFlowDetail_RemainsConsistentWhenParentTransactionChanges()
    {
        await using var database = await SqliteTestDatabase.CreateAsync();
        var transactionId = Guid.NewGuid();

        await using (var context = database.CreateContext())
        {
            await CoreLedgerTestData.SeedMasterDataAsync(context);
            context.LedgerTransactions.Add(
                CoreLedgerTestData.CreateDraftTransaction(
                    transactionId,
                    TransactionType.Contribution));
            context.CashFlowDetails.Add(new CashFlowDetailRow
            {
                TransactionId = transactionId,
                Category = CashFlowCategory.Salary,
                HouseholdMemberId = CoreLedgerTestData.HouseholdMemberId
            });
            await context.SaveChangesAsync();
        }

        await AssertSqliteFailureAsync(
            () => database.ExecuteNonQueryAsync(
                "UPDATE LedgerTransaction SET TransactionTypeCode = 'BUY' WHERE Id = $id;",
                new SqliteParameter("$id", transactionId.ToString("D"))),
            "contribution");

        await AssertSqliteFailureAsync(
            () => database.ExecuteNonQueryAsync(
                "UPDATE LedgerTransaction SET HouseholdId = $householdId WHERE Id = $id;",
                new SqliteParameter("$householdId", CoreLedgerTestData.OtherHouseholdId.ToString("D")),
                new SqliteParameter("$id", transactionId.ToString("D"))),
            "same household");
    }

    private static async Task AssertSqliteFailureAsync(
        Func<Task> action,
        string expectedMessage)
    {
        var exception = await Assert.ThrowsAsync<SqliteException>(action);

        Assert.Equal(19, exception.SqliteErrorCode);
        Assert.Contains(expectedMessage, exception.Message, StringComparison.OrdinalIgnoreCase);
    }
}
