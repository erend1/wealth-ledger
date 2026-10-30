using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using WealthLedger.Application.PhysicalGold;
using WealthLedger.Application.Positions;
using WealthLedger.Application.Reconciliation;
using WealthLedger.Domain.Ledger;
using WealthLedger.Domain.Reconciliation;
using WealthLedger.Infrastructure.Persistence;
using WealthLedger.Infrastructure.Persistence.Rows;
using static WealthLedger.Infrastructure.Tests.Persistence.ReconciliationTestData;

namespace WealthLedger.Infrastructure.Tests.Persistence;

/// <summary>
/// M010 as a whole: both of its migrations applied to an M009 database,
/// removed, and applied again, with M009 schema, history, derivations and
/// guards unchanged at every step.
/// </summary>
public sealed class M010MigrationRoundTripTests
{
    private const string M009Migration =
        "20260915082550_008_PhysicalGoldLifecycle";

    private const string M010Migration =
        "20261028082512_010_ReconciliationPersistence";

    private static readonly (Guid AccountId, Guid AssetId)[] PositionScopes =
    [
        (CoreLedgerTestData.AccountId, CoreLedgerTestData.CashAssetId),
        (CoreLedgerTestData.AccountId, CoreLedgerTestData.FundAssetId),
        (CoreLedgerTestData.DestinationAccountId, CoreLedgerTestData.CashAssetId),
        (VaultId, CoreLedgerTestData.GoldAssetId)
    ];

    [Fact]
    public async Task Migration_008To010To008To010_PreservesExactM009SchemaHistoryDerivationsAndGuards()
    {
        await using var database = await SqliteTestDatabase.CreateAsync(
            M009Migration);
        await SeedLedgerAsync(database.CreateContext);
        var m009Schema = await ReadSchemaAsync(database);
        var ledger = await SnapshotLedgerAsync(database);
        var derived = await ReadM009DerivedStateAsync(database);

        Assert.Equal(0L, await CountM010ObjectsAsync(database));
        await AssertM009GuardRefusesPieceLessGoldSaleAsync(database);

        await MigrateAsync(database, M010Migration);
        var m010Schema = await ReadSchemaAsync(database);

        Assert.Equal(60L, await CountM010ObjectsAsync(database));
        Assert.Equal(ledger, await SnapshotLedgerAsync(database));
        Assert.Equal(derived, await ReadM009DerivedStateAsync(database));
        await AssertM009GuardRefusesPieceLessGoldSaleAsync(database);
        await AssertReconciliationComparesTheSameLedgerAsync(database);
        Assert.Equal(ledger, await SnapshotLedgerAsync(database));

        await MigrateAsync(database, M009Migration);

        Assert.Equal(m009Schema, await ReadSchemaAsync(database));
        Assert.Equal(0L, await CountM010ObjectsAsync(database));
        Assert.Equal(ledger, await SnapshotLedgerAsync(database));
        Assert.Equal(derived, await ReadM009DerivedStateAsync(database));
        await AssertM009GuardRefusesPieceLessGoldSaleAsync(database);

        await using (var secondUp = database.CreateContext())
        {
            await secondUp.Database.MigrateAsync(M010Migration);
            Assert.False(secondUp.Database.HasPendingModelChanges());
        }

        Assert.Equal(m010Schema, await ReadSchemaAsync(database));
        Assert.Equal(ledger, await SnapshotLedgerAsync(database));
        Assert.Equal(derived, await ReadM009DerivedStateAsync(database));
        await AssertM009GuardRefusesPieceLessGoldSaleAsync(database);
        await AssertReconciliationComparesTheSameLedgerAsync(database);
        Assert.Equal(ledger, await SnapshotLedgerAsync(database));
    }

    private static async Task MigrateAsync(
        SqliteTestDatabase database,
        string targetMigration)
    {
        await using var context = database.CreateContext();
        await context.Database.MigrateAsync(targetMigration);
    }

    /// <summary>
    /// Records the synthetic statement and runs it. The schema was just
    /// created, so every M010 table starts empty, and the same history always
    /// yields one Matched and two OutOfTolerance items.
    /// </summary>
    private static async Task AssertReconciliationComparesTheSameLedgerAsync(
        SqliteTestDatabase database)
    {
        var evidenceId = await RecordStatementAsync(database.CreateContext);
        ReconciliationRunDetail detail;

        await using (var context = database.CreateContext())
        {
            var runId = (await RunUseCase(context).ExecuteAsync(
                    "m010-round-trip-run",
                    new RunReconciliationCommand(CoreLedgerTestData.HouseholdId, evidenceId)))
                .ReconciliationRunId;
            detail = await GetUseCase(context).ExecuteAsync(
                new GetReconciliationRunQuery(CoreLedgerTestData.HouseholdId, runId));
        }

        Assert.False(detail.IsStale);
        Assert.Equal(3, detail.Items.Count);
        Assert.Single(
            detail.Items,
            item => item.ComparisonStatus == ReconciliationComparisonStatus.Matched);
        Assert.Equal(
            2,
            detail.Items.Count(
                item => item.ComparisonStatus == ReconciliationComparisonStatus.OutOfTolerance));
        Assert.Equal(
            100 * Unit,
            detail.Items.Single(item => item.Scope.AssetId == CoreLedgerTestData.CashAssetId)
                .LedgerQuantityRawE8);
    }

    /// <summary>
    /// The M005 point position and the M009 custody inventory, the derivations
    /// that existed before M010.
    /// </summary>
    private static async Task<string> ReadM009DerivedStateAsync(
        SqliteTestDatabase database)
    {
        await using var context = database.CreateContext();
        var positions = new GetPositionUseCase(
            new EfCorePostedEntrySource(context),
            new EfCoreNavigationScopeReadStore(context));
        var lines = new List<string>();

        foreach (var (accountId, assetId) in PositionScopes)
        {
            var position = await positions.ExecuteAsync(
                new GetPositionQuery(
                    CoreLedgerTestData.HouseholdId,
                    CoreLedgerTestData.PortfolioId,
                    accountId,
                    assetId));
            lines.Add(
                $"{accountId}:{assetId}:{position.Quantity.RawE8}:{position.SourceEntryCount}");
        }

        var custody = await new GetPhysicalGoldCustodyInventoryUseCase(
                new EfCorePhysicalGoldVerificationReadStore(context))
            .ExecuteAsync(CoreLedgerTestData.HouseholdId);

        Assert.Equal(2, custody.Items.Count);
        lines.AddRange(
            custody.Items
                .OrderBy(item => item.AssetLotId)
                .Select(
                    item => string.Join(
                        ':',
                        item.AccountId,
                        item.AssetLotId,
                        item.GrossWeightRawE8,
                        item.PieceCount,
                        item.FinenessPpm,
                        item.FineWeightGrams,
                        item.CostStatus,
                        item.CostMinorUnits)));

        return string.Join(';', lines);
    }

    /// <summary>
    /// Posts a physical-gold sale that consumes a lot without piece evidence
    /// inside a transaction that is rolled back, so the probe leaves no row.
    /// </summary>
    private static async Task AssertM009GuardRefusesPieceLessGoldSaleAsync(
        SqliteTestDatabase database)
    {
        var lotId = Guid.Parse(Convert.ToString(
            await database.ExecuteScalarAsync(
                "SELECT Id FROM AssetLot WHERE AssetId = $gold ORDER BY Id LIMIT 1;",
                new SqliteParameter(
                    "$gold",
                    CoreLedgerTestData.GoldAssetId.ToString("D"))))!);
        var saleId = Guid.NewGuid();
        var principalId = Guid.NewGuid();

        await using var context = database.CreateContext();
        await using var transaction = await context.Database.BeginTransactionAsync();
        var sale = CoreLedgerTestData.CreateDraftTransaction(
            saleId,
            TransactionType.Sell,
            executionDate: new DateOnly(2026, 9, 20));
        sale.ExternalReference = "SYNTHETIC-ROUND-TRIP-GOLD-SALE";
        context.LedgerTransactions.Add(sale);
        context.TransactionEntries.AddRange(
            CoreLedgerTestData.CreateEntry(
                principalId,
                saleId,
                0,
                CoreLedgerTestData.GoldAssetId,
                -1_00000000L,
                EntryRole.Principal,
                accountId: VaultId),
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
            AssetLotId = lotId,
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

        var exception = await Assert.ThrowsAsync<DbUpdateException>(
            () => CoreLedgerTestData.PostAsync(context, saleId));

        Assert.Contains(
            "WL_M009_PHYSICAL_GOLD_PIECE_DETAIL_REQUIRED",
            exception.InnerException?.Message,
            StringComparison.Ordinal);
    }

    // The 26 evidence objects of migration 009 plus the 34 observation and
    // reconciliation objects of migration 010.
    private static async Task<long> CountM010ObjectsAsync(SqliteTestDatabase database)
        => Convert.ToInt64(await database.ExecuteScalarAsync(
            """
            SELECT COUNT(*)
            FROM sqlite_master
            WHERE name LIKE '%Evidence%'
               OR name LIKE '%Reconciliation%';
            """));

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
