using Microsoft.EntityFrameworkCore;

namespace WealthLedger.Infrastructure.Tests.Persistence;

public sealed class EvidencePersistenceMigrationTests
{
    private const string PreviousMigration =
        "20260915082550_008_PhysicalGoldLifecycle";

    private const string CurrentMigration =
        "20261028065838_009_EvidencePersistence";

    [Fact]
    public async Task Migration_008To009To008To009_RestoresExactM009SchemaAndPreservesLedger()
    {
        await using var database = await SqliteTestDatabase.CreateAsync(
            PreviousMigration);
        var fixture = await EvidenceTestData.SeedAsync(database.CreateContext);
        var m009Schema = await ReadSchemaAsync(database);
        var ledgerCount = await CountAsync(database, "LedgerTransaction");

        Assert.DoesNotContain("Evidence", m009Schema, StringComparison.Ordinal);

        await using (var firstUp = database.CreateContext())
        {
            await firstUp.Database.MigrateAsync(CurrentMigration);
        }

        var m010Schema = await ReadSchemaAsync(database);
        Assert.Equal(23L, await CountEvidenceObjectsAsync(database));

        await using (var context = database.CreateContext())
        {
            await EvidenceTestData.RecordUseCase(context).ExecuteAsync(
                "evidence-migration",
                EvidenceTestData.Command(
                    transactionIds: [fixture.FirstPostedId],
                    attachment: EvidenceTestData.PdfUpload()));
        }

        Assert.Equal(1L, await CountAsync(database, "EvidenceAttachment"));

        await using (var down = database.CreateContext())
        {
            await down.Database.MigrateAsync(PreviousMigration);
        }

        Assert.Equal(m009Schema, await ReadSchemaAsync(database));
        Assert.Equal(0L, await CountEvidenceObjectsAsync(database));
        Assert.Equal(ledgerCount, await CountAsync(database, "LedgerTransaction"));

        await using (var secondUp = database.CreateContext())
        {
            await secondUp.Database.MigrateAsync(CurrentMigration);
            Assert.False(secondUp.Database.HasPendingModelChanges());
        }

        Assert.Equal(m010Schema, await ReadSchemaAsync(database));
        Assert.Equal(0L, await CountAsync(database, "EvidenceRecord"));
        Assert.Equal(ledgerCount, await CountAsync(database, "LedgerTransaction"));

        await using (var context = database.CreateContext())
        {
            await EvidenceTestData.RecordUseCase(context).ExecuteAsync(
                "evidence-after-reapply",
                EvidenceTestData.Command(transactionIds: [fixture.SecondPostedId]));
        }

        Assert.Equal(1L, await CountAsync(database, "EvidenceTransactionLink"));
    }

    [Fact]
    public async Task Migration_CreatesNoEvidenceRowsForExistingHistory()
    {
        await using var database = await SqliteTestDatabase.CreateAsync(
            PreviousMigration);
        await EvidenceTestData.SeedAsync(database.CreateContext);

        await using (var context = database.CreateContext())
        {
            await context.Database.MigrateAsync();
        }

        foreach (var table in new[]
                 {
                     "EvidenceRecord",
                     "EvidenceAttachment",
                     "EvidenceTransactionLink",
                     "EvidenceCommandReceipt"
                 })
        {
            Assert.Equal(0L, await CountAsync(database, table));
        }
    }

    // Four tables, their four automatic key indexes, six named indexes and
    // nine triggers.
    private static async Task<long> CountEvidenceObjectsAsync(SqliteTestDatabase database)
        => Convert.ToInt64(await database.ExecuteScalarAsync(
            "SELECT COUNT(*) FROM sqlite_master WHERE name LIKE '%Evidence%';"));

    private static async Task<long> CountAsync(SqliteTestDatabase database, string table)
        => Convert.ToInt64(await database.ExecuteScalarAsync(
            $"SELECT COUNT(*) FROM \"{table}\";"));

    private static async Task<string> ReadSchemaAsync(SqliteTestDatabase database)
        => Convert.ToString(await database.ExecuteScalarAsync(
            """
            SELECT group_concat(type || ':' || name || ':' || COALESCE(sql, ''), char(10))
            FROM (
                SELECT type, name, sql
                FROM sqlite_master
                WHERE name <> '__EFMigrationsHistory'
                  AND name <> '__EFMigrationsLock'
                ORDER BY type, name
            );
            """))!;
}
