using Microsoft.Data.Sqlite;
using WealthLedger.Application.LedgerSearch;
using WealthLedger.Domain.Ledger;
using WealthLedger.Infrastructure.Persistence;

namespace WealthLedger.Infrastructure.Tests.Persistence;

public sealed class LedgerSearchReadStoreTests
{
    private static readonly Guid OriginalId =
        Guid.Parse("72000000-0000-0000-0000-000000000001");
    private static readonly Guid OriginalEntryId =
        Guid.Parse("72100000-0000-0000-0000-000000000001");
    private static readonly Guid ReversalId =
        Guid.Parse("72000000-0000-0000-0000-000000000002");
    private static readonly Guid ReversalEntryId =
        Guid.Parse("72100000-0000-0000-0000-000000000002");
    private static readonly Guid MultiEntryId =
        Guid.Parse("72000000-0000-0000-0000-000000000003");
    private static readonly Guid MultiEntryFirstId =
        Guid.Parse("72100000-0000-0000-0000-000000000003");
    private static readonly Guid MultiEntrySecondId =
        Guid.Parse("72100000-0000-0000-0000-000000000004");
    private static readonly Guid LatestId =
        Guid.Parse("72000000-0000-0000-0000-000000000004");
    private static readonly Guid LatestEntryId =
        Guid.Parse("72100000-0000-0000-0000-000000000005");
    private static readonly Guid DraftId =
        Guid.Parse("72000000-0000-0000-0000-000000000005");
    private static readonly Guid DraftEntryId =
        Guid.Parse("72100000-0000-0000-0000-000000000006");

    [Fact]
    public async Task Search_IsPostedOrderedRestartSafeAndUsesCurrentContext()
    {
        await using var database = await SqliteTestDatabase.CreateAsync();
        await SeedAsync(database);

        string cursor;

        await using (var context = database.CreateContext())
        {
            var first = await new SearchLedgerTransactionsUseCase(
                    new EfCoreLedgerSearchReadStore(context))
                .ExecuteAsync(
                    new SearchLedgerTransactionsQuery(
                        CoreLedgerTestData.HouseholdId,
                        PageSize: 2));

            Assert.Equal([LatestId, MultiEntryId], first.Items.Select(x => x.TransactionId));
            Assert.NotNull(first.NextCursor);
            cursor = first.NextCursor!;
            Assert.All(
                first.Items,
                item => Assert.Equal(TransactionStatus.Posted, item.Status));
            var multiEntry = Assert.Single(
                first.Items,
                item => item.TransactionId == MultiEntryId);
            Assert.Equal(2, multiEntry.EntryEffects.Count);
            Assert.All(
                multiEntry.EntryEffects,
                effect => Assert.Equal(
                    "Current Renamed Account",
                    effect.AccountName));
        }

        await using (var restarted = database.CreateContext())
        {
            var second = await new SearchLedgerTransactionsUseCase(
                    new EfCoreLedgerSearchReadStore(restarted))
                .ExecuteAsync(
                    new SearchLedgerTransactionsQuery(
                        CoreLedgerTestData.HouseholdId,
                        PageSize: 2,
                        Cursor: cursor));

            Assert.Equal([ReversalId, OriginalId], second.Items.Select(x => x.TransactionId));
            Assert.Null(second.NextCursor);
            Assert.DoesNotContain(
                second.Items,
                item => item.TransactionId == DraftId);
            var original = Assert.Single(
                second.Items,
                item => item.TransactionId == OriginalId);
            Assert.Equal(ReversalId, original.ReversedByTransactionId);
            var reversal = Assert.Single(
                second.Items,
                item => item.TransactionId == ReversalId);
            Assert.Equal(OriginalId, reversal.ReversalOfTransactionId);
        }
    }

    [Fact]
    public async Task Search_FiltersAreAndedAndEntryMatchesDoNotDuplicateTransactions()
    {
        await using var database = await SqliteTestDatabase.CreateAsync();
        await SeedAsync(database);
        await using var context = database.CreateContext();
        var useCase = new SearchLedgerTransactionsUseCase(
            new EfCoreLedgerSearchReadStore(context));

        var result = await useCase.ExecuteAsync(
            new SearchLedgerTransactionsQuery(
                CoreLedgerTestData.HouseholdId,
                ExecutedFrom: new DateOnly(2026, 9, 2),
                ExecutedTo: new DateOnly(2026, 9, 2),
                Types: [TransactionType.Adjustment],
                AssetId: CoreLedgerTestData.CashAssetId,
                InstitutionId: CoreLedgerTestData.InstitutionId,
                PortfolioId: CoreLedgerTestData.PortfolioId,
                ExternalReferenceContains: "mixed-ref"));

        var item = Assert.Single(result.Items);
        Assert.Equal(MultiEntryId, item.TransactionId);
        Assert.Equal(2, item.EntryEffects.Count);
    }

    [Fact]
    public async Task Search_ReversalRelationshipFiltersUseEffectivePostedHistory()
    {
        await using var database = await SqliteTestDatabase.CreateAsync();
        await SeedAsync(database);
        await using var context = database.CreateContext();
        var useCase = new SearchLedgerTransactionsUseCase(
            new EfCoreLedgerSearchReadStore(context));

        var reversedOriginals = await useCase.ExecuteAsync(
            new SearchLedgerTransactionsQuery(
                CoreLedgerTestData.HouseholdId,
                ReversalRelationship:
                    LedgerSearchReversalRelationship.ReversedOriginal));
        var reversals = await useCase.ExecuteAsync(
            new SearchLedgerTransactionsQuery(
                CoreLedgerTestData.HouseholdId,
                ReversalRelationship:
                    LedgerSearchReversalRelationship.ReversalOnly));
        var originals = await useCase.ExecuteAsync(
            new SearchLedgerTransactionsQuery(
                CoreLedgerTestData.HouseholdId,
                ReversalRelationship:
                    LedgerSearchReversalRelationship.OriginalOnly));

        Assert.Equal(
            [OriginalId],
            reversedOriginals.Items.Select(x => x.TransactionId));
        Assert.Equal(
            [ReversalId],
            reversals.Items.Select(x => x.TransactionId));
        Assert.DoesNotContain(
            originals.Items,
            item => item.TransactionId == ReversalId);
        Assert.Contains(
            originals.Items,
            item => item.TransactionId == OriginalId);
    }

    [Fact]
    public async Task Search_UnknownOrCrossHouseholdExactScopeFailsClosed()
    {
        await using var database = await SqliteTestDatabase.CreateAsync();
        await SeedAsync(database);
        await using var context = database.CreateContext();
        var useCase = new SearchLedgerTransactionsUseCase(
            new EfCoreLedgerSearchReadStore(context));

        await Assert.ThrowsAsync<LedgerSearchFilterScopeNotFoundException>(
            () => useCase.ExecuteAsync(
                new SearchLedgerTransactionsQuery(
                    CoreLedgerTestData.HouseholdId,
                    AccountId: CoreLedgerTestData.OtherAccountId)));
        await Assert.ThrowsAsync<LedgerSearchFilterScopeNotFoundException>(
            () => useCase.ExecuteAsync(
                new SearchLedgerTransactionsQuery(
                    CoreLedgerTestData.HouseholdId,
                    AssetId: Guid.NewGuid())));
    }

    [Fact]
    public async Task Search_RepresentativePlanUsesExistingHouseholdStatusIndex()
    {
        await using var database = await SqliteTestDatabase.CreateAsync();
        await SeedAsync(database);
        await using var connection = new SqliteConnection(database.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            EXPLAIN QUERY PLAN
            SELECT Id
            FROM LedgerTransaction
            WHERE HouseholdId = $household
              AND Status = 'POSTED'
              AND ExecutionDate IS NOT NULL
              AND PostedAtUtc IS NOT NULL
            ORDER BY ExecutionDate DESC, PostedAtUtc DESC, Id DESC
            LIMIT 51;
            """;
        command.Parameters.AddWithValue(
            "$household",
            CoreLedgerTestData.HouseholdId.ToString("D"));
        var details = new List<string>();
        await using var reader = await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            details.Add(reader.GetString(3));
        }

        Assert.Contains(
            details,
            detail => detail.Contains(
                "IX_LedgerTransaction_Household_Status_Posted_Id",
                StringComparison.Ordinal));
    }

    private static async Task SeedAsync(SqliteTestDatabase database)
    {
        await using var context = database.CreateContext();
        await CoreLedgerTestData.SeedMasterDataAsync(context);

        var original = CoreLedgerTestData.CreateDraftTransaction(
            OriginalId,
            TransactionType.Adjustment,
            executionDate: new DateOnly(2026, 9, 1));
        original.ExternalReference = "ORIGINAL-REF";
        context.LedgerTransactions.Add(original);
        context.TransactionEntries.Add(
            CoreLedgerTestData.CreateEntry(
                OriginalEntryId,
                OriginalId,
                sequence: 0,
                CoreLedgerTestData.CashAssetId,
                quantityDeltaE8: 500,
                EntryRole.Adjustment));
        await context.SaveChangesAsync();
        await CoreLedgerTestData.PostAsync(
            context,
            OriginalId,
            new DateTime(2026, 9, 1, 9, 0, 0, DateTimeKind.Utc));

        context.LedgerTransactions.Add(
            CoreLedgerTestData.CreateDraftTransaction(
                ReversalId,
                TransactionType.Reversal,
                executionDate: new DateOnly(2026, 9, 1),
                reversalOfTransactionId: OriginalId));
        context.TransactionEntries.Add(
            CoreLedgerTestData.CreateEntry(
                ReversalEntryId,
                ReversalId,
                sequence: 0,
                CoreLedgerTestData.CashAssetId,
                quantityDeltaE8: -500,
                EntryRole.Adjustment));
        await context.SaveChangesAsync();
        await CoreLedgerTestData.PostAsync(
            context,
            ReversalId,
            new DateTime(2026, 9, 1, 10, 0, 0, DateTimeKind.Utc));

        var multi = CoreLedgerTestData.CreateDraftTransaction(
            MultiEntryId,
            TransactionType.Adjustment,
            executionDate: new DateOnly(2026, 9, 2));
        multi.ExternalReference = "MiXeD-ReF-2026";
        context.LedgerTransactions.Add(multi);
        context.TransactionEntries.AddRange(
            CoreLedgerTestData.CreateEntry(
                MultiEntryFirstId,
                MultiEntryId,
                sequence: 0,
                CoreLedgerTestData.CashAssetId,
                quantityDeltaE8: 111,
                EntryRole.Adjustment,
                accountId: CoreLedgerTestData.AccountId),
            CoreLedgerTestData.CreateEntry(
                MultiEntrySecondId,
                MultiEntryId,
                sequence: 1,
                CoreLedgerTestData.CashAssetId,
                quantityDeltaE8: 222,
                EntryRole.Adjustment,
                accountId: CoreLedgerTestData.DestinationAccountId));
        await context.SaveChangesAsync();
        await CoreLedgerTestData.PostAsync(
            context,
            MultiEntryId,
            new DateTime(2026, 9, 2, 11, 0, 0, DateTimeKind.Utc));

        var latest = CoreLedgerTestData.CreateDraftTransaction(
            LatestId,
            TransactionType.Adjustment,
            executionDate: new DateOnly(2026, 9, 3));
        latest.ExternalReference = "LATEST-REF";
        context.LedgerTransactions.Add(latest);
        context.TransactionEntries.Add(
            CoreLedgerTestData.CreateEntry(
                LatestEntryId,
                LatestId,
                sequence: 0,
                CoreLedgerTestData.CashAssetId,
                quantityDeltaE8: 333,
                EntryRole.Adjustment));
        await context.SaveChangesAsync();
        await CoreLedgerTestData.PostAsync(
            context,
            LatestId,
            new DateTime(2026, 9, 3, 8, 0, 0, DateTimeKind.Utc));

        context.LedgerTransactions.Add(
            CoreLedgerTestData.CreateDraftTransaction(
                DraftId,
                TransactionType.Adjustment,
                executionDate: new DateOnly(2026, 9, 4)));
        context.TransactionEntries.Add(
            CoreLedgerTestData.CreateEntry(
                DraftEntryId,
                DraftId,
                sequence: 0,
                CoreLedgerTestData.CashAssetId,
                quantityDeltaE8: 444,
                EntryRole.Adjustment));
        await context.SaveChangesAsync();

        var account = await context.Accounts.FindAsync(CoreLedgerTestData.AccountId);
        Assert.NotNull(account);
        account!.Name = "Current Renamed Account";
        var destination = await context.Accounts.FindAsync(
            CoreLedgerTestData.DestinationAccountId);
        Assert.NotNull(destination);
        destination!.Name = "Current Renamed Account";
        await context.SaveChangesAsync();
    }
}
