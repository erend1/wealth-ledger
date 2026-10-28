using Microsoft.Data.Sqlite;
using WealthLedger.Application.Evidence;
using WealthLedger.Application.Inventory;
using WealthLedger.Application.LocalData;
using WealthLedger.Application.Reconciliation;
using WealthLedger.Domain.Reconciliation;
using WealthLedger.Infrastructure.LocalData;
using WealthLedger.Infrastructure.Persistence;
using WealthLedger.Infrastructure.Tests.Persistence;
using static WealthLedger.Infrastructure.Tests.Persistence.ReconciliationTestData;

namespace WealthLedger.Infrastructure.Tests.LocalData;

/// <summary>
/// Observations and reconciliation runs live inside the SQLite file, so the
/// normal M004 package must carry them, and a fresh process reading the staged
/// copy must re-derive the same ledger side.
/// </summary>
public sealed class ReconciliationBackupRecoveryTests
{
    [Fact]
    public async Task BackupVerifyAndStage_CarryObservationsRunsAndResolutions()
    {
        await using var harness = await LocalBackupTestHarness.CreateAsync();
        WealthLedgerDbContext Live()
            => SqliteLocalDataConnectionFactory.CreateContext(
                harness.DatabasePath,
                SqliteOpenMode.ReadWrite);
        var history = await SeedAsync(Live);
        Guid runId;
        ReconciliationRunDetail liveDetail;

        await using (var context = Live())
        {
            runId = (await RunUseCase(context).ExecuteAsync(
                    "backup-run",
                    new RunReconciliationCommand(CoreLedgerTestData.HouseholdId, history.EvidenceId)))
                .ReconciliationRunId;
        }

        await using (var context = Live())
        {
            var detail = await GetUseCase(context).ExecuteAsync(
                new GetReconciliationRunQuery(CoreLedgerTestData.HouseholdId, runId));
            await ResolveUseCase(context).ExecuteAsync(
                new ResolveReconciliationItemCommand(
                    CoreLedgerTestData.HouseholdId,
                    runId,
                    detail.Items.Single(item => item.Scope.AssetId == CoreLedgerTestData.GoldAssetId)
                        .ReconciliationItemId,
                    ReconciliationResolutionKind.DocumentedException,
                    Explanation: "Synthetic count note."));
        }

        await using (var context = Live())
        {
            liveDetail = await GetUseCase(context).ExecuteAsync(
                new GetReconciliationRunQuery(CoreLedgerTestData.HouseholdId, runId));
        }

        var backup = await harness.CreateBackupAsync();
        Assert.True(backup.Succeeded, backup.Failure?.Message);

        var verified = await harness.Verifier.VerifyAsync(backup.Value!.FilePath);
        Assert.True(verified.Succeeded, verified.Failure?.Message);
        Assert.Equal(
            LocalDatabaseCompatibility.Compatible,
            verified.Value!.Compatibility);

        var target = Path.Combine(harness.RootPath, "restore-drill", "restored.db");
        var staged = await harness.RestoreStager.StageAsync(
            backup.Value.FilePath,
            target);
        Assert.True(staged.Succeeded, staged.Failure?.Message);

        await using var restored = SqliteLocalDataConnectionFactory.CreateContext(
            target,
            SqliteOpenMode.ReadOnly);
        var observations = await new ListEvidenceObservationsUseCase(
                new EfCoreEvidenceReadStore(restored))
            .ExecuteAsync(new ListEvidenceObservationsQuery(
                CoreLedgerTestData.HouseholdId,
                history.EvidenceId));
        var restoredDetail = await GetUseCase(restored).ExecuteAsync(
            new GetReconciliationRunQuery(CoreLedgerTestData.HouseholdId, runId));
        var list = await ListUseCase(restored).ExecuteAsync(
            new ListReconciliationRunsQuery(CoreLedgerTestData.HouseholdId));

        Assert.Equal(3, observations.Items.Count);
        Assert.Equal(
            observations.Items.Select(item => item.ObservationId).Order(),
            restoredDetail.Items.Select(item => item.ObservationId).Order());
        Assert.False(restoredDetail.IsStale);
        Assert.Equal(Fingerprint(liveDetail), Fingerprint(restoredDetail));
        Assert.Equal(
            ReconciliationResolutionState.DocumentedException,
            restoredDetail.Items.Single(item => item.Scope.AssetId == CoreLedgerTestData.GoldAssetId)
                .ResolutionState);
        Assert.Equal("Synthetic count note.", restoredDetail.Items.Single(item => item.Explanation is not null).Explanation);
        Assert.Equal(runId, Assert.Single(list.Items).ReconciliationRunId);

        // The ledger-derived positions the run compared came across unchanged.
        await using var live = Live();
        Assert.Equal(
            await PositionsAsync(live),
            await PositionsAsync(restored));
    }

    private static string Fingerprint(ReconciliationRunDetail detail)
        => string.Join(
            ';',
            detail.Items.Select(
                item => string.Join(
                    ':',
                    item.Sequence,
                    item.ObservationId,
                    item.ObservedQuantityRawE8,
                    item.LedgerQuantityRawE8,
                    item.DeltaRawE8,
                    item.ToleranceRawE8,
                    item.ObservedPieceCount,
                    item.LedgerPieceCount,
                    item.PieceDelta,
                    item.ComparisonStatus,
                    item.ResolutionState,
                    item.ResolvedAtUtc?.ToString("O"),
                    item.CurrentLedgerQuantityRawE8,
                    item.CurrentLedgerPieceCount)));

    private static async Task<string> PositionsAsync(WealthLedgerDbContext context)
    {
        var inventory = await new ListPositionInventoryUseCase(
                new EfCoreInventoryReadStore(context))
            .ExecuteAsync(new ListPositionInventoryQuery(
                CoreLedgerTestData.HouseholdId,
                AsOf: EvidenceDate));

        return string.Join(
            ';',
            inventory.Items.Select(
                item => $"{item.PortfolioId}:{item.AccountId}:{item.AssetId}:{item.QuantityRawE8}"));
    }
}
