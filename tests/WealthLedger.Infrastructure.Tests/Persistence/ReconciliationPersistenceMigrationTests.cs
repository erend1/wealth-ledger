using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace WealthLedger.Infrastructure.Tests.Persistence;

public sealed class ReconciliationPersistenceMigrationTests
{
    private const string PreviousMigration =
        "20261028065838_009_EvidencePersistence";

    private const string CurrentMigration =
        "20261028082512_010_ReconciliationPersistence";

    [Fact]
    public async Task Migration_009To010To009To010_RestoresExactM009SchemaAndPreservesEvidenceAndLedger()
    {
        await using var database = await SqliteTestDatabase.CreateAsync(
            PreviousMigration);
        var fixture = await EvidenceTestData.SeedAsync(database.CreateContext);
        Guid evidenceId;

        await using (var context = database.CreateContext())
        {
            evidenceId = (await EvidenceTestData.RecordUseCase(context).ExecuteAsync(
                    "reconciliation-migration",
                    EvidenceTestData.Command(
                        transactionIds: [fixture.FirstPostedId],
                        attachment: EvidenceTestData.PdfUpload())))
                .EvidenceId;
        }

        var m009Schema = await ReadSchemaAsync(database);
        var ledgerCount = await CountAsync(database, "LedgerTransaction");

        Assert.Equal(0L, await CountReconciliationObjectsAsync(database));

        await using (var firstUp = database.CreateContext())
        {
            await firstUp.Database.MigrateAsync(CurrentMigration);
        }

        var m010Schema = await ReadSchemaAsync(database);
        Assert.Equal(34L, await CountReconciliationObjectsAsync(database));

        await InsertObservationRunAndItemAsync(database, evidenceId);
        Assert.Equal(1L, await CountAsync(database, "ReconciliationItem"));

        await using (var down = database.CreateContext())
        {
            await down.Database.MigrateAsync(PreviousMigration);
        }

        Assert.Equal(m009Schema, await ReadSchemaAsync(database));
        Assert.Equal(0L, await CountReconciliationObjectsAsync(database));
        Assert.Equal(1L, await CountAsync(database, "EvidenceRecord"));
        Assert.Equal(1L, await CountAsync(database, "EvidenceAttachment"));
        Assert.Equal(1L, await CountAsync(database, "EvidenceTransactionLink"));
        Assert.Equal(ledgerCount, await CountAsync(database, "LedgerTransaction"));

        await using (var secondUp = database.CreateContext())
        {
            await secondUp.Database.MigrateAsync(CurrentMigration);
            Assert.False(secondUp.Database.HasPendingModelChanges());
        }

        Assert.Equal(m010Schema, await ReadSchemaAsync(database));
        Assert.Equal(0L, await CountAsync(database, "EvidenceObservation"));
        Assert.Equal(0L, await CountAsync(database, "ReconciliationRun"));
        Assert.Equal(ledgerCount, await CountAsync(database, "LedgerTransaction"));

        await InsertObservationRunAndItemAsync(database, evidenceId);
        Assert.Equal(1L, await CountAsync(database, "ReconciliationItem"));
    }

    [Fact]
    public async Task Migration_CreatesNoObservationOrReconciliationRowsForExistingHistory()
    {
        await using var database = await SqliteTestDatabase.CreateAsync(
            PreviousMigration);
        var fixture = await EvidenceTestData.SeedAsync(database.CreateContext);

        await using (var context = database.CreateContext())
        {
            await EvidenceTestData.RecordUseCase(context).ExecuteAsync(
                "reconciliation-existing-evidence",
                EvidenceTestData.Command(transactionIds: [fixture.SecondPostedId]));
        }

        await using (var context = database.CreateContext())
        {
            await context.Database.MigrateAsync();
        }

        foreach (var table in new[]
                 {
                     "EvidenceObservation",
                     "ReconciliationRun",
                     "ReconciliationItem",
                     "ReconciliationItemResolution"
                 })
        {
            Assert.Equal(0L, await CountAsync(database, table));
        }

        Assert.Equal(1L, await CountAsync(database, "EvidenceRecord"));
    }

    private static async Task InsertObservationRunAndItemAsync(
        SqliteTestDatabase database,
        Guid evidenceId)
    {
        var observationId = Guid.NewGuid().ToString("D");
        var runId = Guid.NewGuid().ToString("D");

        await database.ExecuteNonQueryAsync(
            """
            INSERT INTO EvidenceObservation (
                Id, EvidenceId, PortfolioId, AccountId, AssetId, ObservedQuantityE8,
                ToleranceE8, ObservedPieceCount, RecordedAtUtc)
            VALUES ($observation, $evidence, $portfolio, $account, $asset, 100, 0, NULL,
                    '2026-10-01T10:00:00.0000000Z');

            INSERT INTO ReconciliationRun (
                Id, HouseholdId, EvidenceId, EvidenceEffectiveDate, RunAtUtc,
                MethodCode, MethodVersion, ItemCount, IdempotencyKey)
            VALUES ($run, $household, $evidence, '2026-09-30', '2026-10-01T11:00:00.0000000Z',
                    'EXACT_SCOPE_QUANTITY_AS_OF', 1, 1, $run);

            INSERT INTO ReconciliationItem (
                Id, RunId, Sequence, ObservationId, PortfolioId, AccountId, AssetId,
                ObservedQuantityE8, LedgerQuantityE8, DeltaE8, ToleranceE8,
                ObservedPieceCount, LedgerPieceCount, PieceDelta, ComparisonStatusCode)
            VALUES ($item, $run, 1, $observation, $portfolio, $account, $asset,
                    100, 100, 0, 0, NULL, NULL, NULL, 'MATCHED');
            """,
            new SqliteParameter("$observation", observationId),
            new SqliteParameter("$run", runId),
            new SqliteParameter("$item", Guid.NewGuid().ToString("D")),
            new SqliteParameter("$evidence", evidenceId.ToString("D")),
            new SqliteParameter("$household", CoreLedgerTestData.HouseholdId.ToString("D")),
            new SqliteParameter("$portfolio", CoreLedgerTestData.PortfolioId.ToString("D")),
            new SqliteParameter("$account", CoreLedgerTestData.AccountId.ToString("D")),
            new SqliteParameter("$asset", CoreLedgerTestData.CashAssetId.ToString("D")));
    }

    // Four tables, their four automatic key indexes, fourteen named indexes
    // and twelve triggers.
    private static async Task<long> CountReconciliationObjectsAsync(SqliteTestDatabase database)
        => Convert.ToInt64(await database.ExecuteScalarAsync(
            """
            SELECT COUNT(*)
            FROM sqlite_master
            WHERE name LIKE '%Reconciliation%'
               OR name LIKE '%EvidenceObservation%';
            """));

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
