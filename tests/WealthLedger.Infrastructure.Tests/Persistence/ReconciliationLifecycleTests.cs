using WealthLedger.Application.Evidence;
using WealthLedger.Application.Reconciliation;
using WealthLedger.Domain.Assets;
using WealthLedger.Domain.Reconciliation;
using WealthLedger.Infrastructure.Persistence;
using static WealthLedger.Infrastructure.Tests.Persistence.ReconciliationTestData;

namespace WealthLedger.Infrastructure.Tests.Persistence;

/// <summary>
/// Reconciles a synthetic statement against history posted through the real
/// ledger use cases, then resolves, corrects, reruns and reverses, reading
/// every step back through fresh contexts on the same SQLite file.
/// </summary>
public sealed class ReconciliationLifecycleTests
{
    [Fact]
    public async Task Lifecycle_MatchMismatchResolveCorrectRerunAndReverse()
    {
        await using var database = await SqliteTestDatabase.CreateAsync();
        var history = await SeedAsync(database.CreateContext);
        var ledgerBeforeRun = await SnapshotLedgerAsync(database);

        var firstRunId = await RunAsync(database, "statement-run-1", history.EvidenceId, Now);

        Assert.Equal(ledgerBeforeRun, await SnapshotLedgerAsync(database));

        var first = await GetAsync(database, firstRunId);
        Assert.False(first.IsStale);
        Assert.Equal(history.EvidenceId, first.EvidenceId);
        Assert.Equal(EvidenceDate, first.EvidenceEffectiveDate);
        Assert.Equal(Now, first.RunAtUtc);
        Assert.Equal(ReconciliationMethod.ExactScopeQuantityAsOf, first.Method);
        Assert.Equal(1, first.MethodVersion);
        Assert.Equal(new[] { 1, 2, 3 }, first.Items.Select(item => item.Sequence));

        var cash = Item(first, CoreLedgerTestData.CashAssetId);
        Assert.Equal((150 * Unit, 100 * Unit, 50 * Unit, 0L), (cash.ObservedQuantityRawE8, cash.LedgerQuantityRawE8, cash.DeltaRawE8, cash.ToleranceRawE8));
        Assert.Equal(ReconciliationComparisonStatus.OutOfTolerance, cash.ComparisonStatus);
        Assert.Equal(ReconciliationResolutionState.Open, cash.ResolutionState);
        Assert.Equal("PRIMARY", cash.Scope.AccountCode);
        Assert.Equal(AssetType.Cash, cash.Scope.AssetType);

        var fund = Item(first, CoreLedgerTestData.FundAssetId);
        Assert.Equal((10 * Unit, 0L), (fund.LedgerQuantityRawE8, fund.DeltaRawE8));
        Assert.Equal(ReconciliationComparisonStatus.Matched, fund.ComparisonStatus);
        Assert.Null(fund.ResolutionState);

        var gold = Item(first, CoreLedgerTestData.GoldAssetId);
        Assert.Equal((30 * Unit, 0L), (gold.LedgerQuantityRawE8, gold.DeltaRawE8));
        Assert.Equal((2, 3, -1), (gold.ObservedPieceCount, gold.LedgerPieceCount, gold.PieceDelta));
        Assert.Equal(ReconciliationComparisonStatus.OutOfTolerance, gold.ComparisonStatus);
        Assert.Equal(VaultId, gold.Scope.AccountId);
        Assert.Null(gold.Scope.InstitutionId);

        // A matched item needs no resolution; an explained count stays visibly
        // out of tolerance.
        var matched = await ResolveFailsAsync(
            database,
            firstRunId,
            fund.ReconciliationItemId,
            ReconciliationResolutionKind.DocumentedException,
            explanation: "Synthetic reason");
        Assert.Equal(ReconciliationErrorCodes.ItemMatched, matched.ErrorCode);

        await ResolveAsync(
            database,
            firstRunId,
            gold.ReconciliationItemId,
            ReconciliationResolutionKind.DocumentedException,
            explanation: "Synthetic count: one piece is out for appraisal.");

        // The missing contribution is posted through the normal workflow,
        // dated on or before the statement, and linked as the correction.
        var correctionId = await RecordContributionAsync(
            database.CreateContext,
            "statement-correction",
            5_000,
            CorrectionDate);
        var afterCorrection = await GetAsync(database, firstRunId);

        Assert.True(afterCorrection.IsStale);
        Assert.True(Item(afterCorrection, CoreLedgerTestData.CashAssetId).IsStale);
        Assert.Equal(150 * Unit, Item(afterCorrection, CoreLedgerTestData.CashAssetId).CurrentLedgerQuantityRawE8);
        Assert.Equal(100 * Unit, Item(afterCorrection, CoreLedgerTestData.CashAssetId).LedgerQuantityRawE8);
        Assert.False(Item(afterCorrection, CoreLedgerTestData.GoldAssetId).IsStale);

        var ledgerAfterCorrection = await SnapshotLedgerAsync(database);

        await ResolveAsync(
            database,
            firstRunId,
            cash.ReconciliationItemId,
            ReconciliationResolutionKind.CorrectedByTransaction,
            transactionId: correctionId);
        var secondRunId = await RunAsync(database, "statement-run-2", history.EvidenceId, Now.AddHours(1));

        Assert.Equal(ledgerAfterCorrection, await SnapshotLedgerAsync(database));

        var resolvedFirst = await GetAsync(database, firstRunId);
        var resolvedCash = Item(resolvedFirst, CoreLedgerTestData.CashAssetId);
        var resolvedGold = Item(resolvedFirst, CoreLedgerTestData.GoldAssetId);
        Assert.Equal(ReconciliationComparisonStatus.OutOfTolerance, resolvedCash.ComparisonStatus);
        Assert.Equal(50 * Unit, resolvedCash.DeltaRawE8);
        Assert.Equal(ReconciliationResolutionState.CorrectedByTransaction, resolvedCash.ResolutionState);
        Assert.Equal(correctionId, resolvedCash.CorrectiveTransactionId);
        Assert.Equal(ReconciliationComparisonStatus.OutOfTolerance, resolvedGold.ComparisonStatus);
        Assert.Equal(ReconciliationResolutionState.DocumentedException, resolvedGold.ResolutionState);
        Assert.Equal("Synthetic count: one piece is out for appraisal.", resolvedGold.Explanation);

        var second = await GetAsync(database, secondRunId);
        Assert.False(second.IsStale);
        Assert.Equal(ReconciliationComparisonStatus.Matched, Item(second, CoreLedgerTestData.CashAssetId).ComparisonStatus);
        Assert.Equal(ReconciliationComparisonStatus.Matched, Item(second, CoreLedgerTestData.FundAssetId).ComparisonStatus);
        Assert.Equal(ReconciliationResolutionState.Open, Item(second, CoreLedgerTestData.GoldAssetId).ResolutionState);

        // The reversal keeps the correction's effective date, so it changes
        // history as of the statement: the rerun becomes stale, while the
        // first run's snapshot is reproduced again.
        await ReverseAsync(database.CreateContext, "statement-correction-reversal", correctionId);

        var reversedSecond = await GetAsync(database, secondRunId);
        var reversedFirst = await GetAsync(database, firstRunId);
        Assert.True(reversedSecond.IsStale);
        Assert.Equal(150 * Unit, Item(reversedSecond, CoreLedgerTestData.CashAssetId).LedgerQuantityRawE8);
        Assert.Equal(100 * Unit, Item(reversedSecond, CoreLedgerTestData.CashAssetId).CurrentLedgerQuantityRawE8);
        Assert.False(reversedFirst.IsStale);

        await using var listContext = database.CreateContext();
        var list = await ListUseCase(listContext).ExecuteAsync(
            new ListReconciliationRunsQuery(CoreLedgerTestData.HouseholdId));

        Assert.Equal(new[] { secondRunId, firstRunId }, list.Items.Select(item => item.ReconciliationRunId));
        Assert.Equal((3, 2, 1, 1, 0, 0), Counts(list.Items[0]));
        Assert.Equal((3, 1, 2, 0, 1, 1), Counts(list.Items[1]));
        Assert.Equal(2L, await CountAsync(database, "ReconciliationRun"));
        Assert.Equal(6L, await CountAsync(database, "ReconciliationItem"));
        Assert.Equal(2L, await CountAsync(database, "ReconciliationItemResolution"));
    }

    [Fact]
    public async Task Run_StoresStableCodesAndExactRawValues()
    {
        await using var database = await SqliteTestDatabase.CreateAsync();
        var history = await SeedAsync(database.CreateContext);
        var runId = await RunAsync(database, "raw-run", history.EvidenceId, Now);
        var detail = await GetAsync(database, runId);
        await ResolveAsync(
            database,
            runId,
            Item(detail, CoreLedgerTestData.GoldAssetId).ReconciliationItemId,
            ReconciliationResolutionKind.DocumentedException,
            explanation: "Synthetic reason");

        Assert.Equal(
            "EXACT_SCOPE_QUANTITY_AS_OF|1|3|2026-09-30|2026-10-02T09:00:00.0000000Z|raw-run",
            Convert.ToString(await database.ExecuteScalarAsync(
                "SELECT MethodCode || '|' || MethodVersion || '|' || ItemCount || '|' || EvidenceEffectiveDate || '|' || RunAtUtc || '|' || IdempotencyKey FROM ReconciliationRun;")));
        Assert.Equal(
            "OUT_OF_TOLERANCE:15000000000:10000000000:5000000000:0:-",
            await ItemRowAsync(database, CoreLedgerTestData.CashAssetId));
        Assert.Equal(
            "MATCHED:1000000000:1000000000:0:0:-",
            await ItemRowAsync(database, CoreLedgerTestData.FundAssetId));
        Assert.Equal(
            "OUT_OF_TOLERANCE:3000000000:3000000000:0:0:2/3/-1",
            await ItemRowAsync(database, CoreLedgerTestData.GoldAssetId));
        Assert.Equal(
            "1,2,3",
            Convert.ToString(await database.ExecuteScalarAsync(
                "SELECT group_concat(Sequence) FROM (SELECT Sequence FROM ReconciliationItem ORDER BY Sequence);")));
        Assert.Equal(
            "DOCUMENTED_EXCEPTION|Synthetic reason|2026-10-02T09:00:00.0000000Z",
            Convert.ToString(await database.ExecuteScalarAsync(
                "SELECT ResolutionCode || '|' || Explanation || '|' || ResolvedAtUtc FROM ReconciliationItemResolution;")));
    }

    [Fact]
    public async Task Run_RetryKeyIsSafeAcrossConcurrentWriters()
    {
        await using var database = await SqliteTestDatabase.CreateAsync();
        var history = await SeedAsync(database.CreateContext);
        await using var firstContext = database.CreateContext();
        await using var secondContext = database.CreateContext();
        using var start = new SemaphoreSlim(0, 2);

        async Task<RunReconciliationResult> RunRaceAsync(WealthLedgerDbContext context)
        {
            await start.WaitAsync();
            return await RunUseCase(context).ExecuteAsync(
                "statement-race",
                new RunReconciliationCommand(CoreLedgerTestData.HouseholdId, history.EvidenceId));
        }

        var firstTask = Task.Run(() => RunRaceAsync(firstContext));
        var secondTask = Task.Run(() => RunRaceAsync(secondContext));
        start.Release(2);
        var results = await Task.WhenAll(firstTask, secondTask);

        Assert.Equal(results[0].ReconciliationRunId, results[1].ReconciliationRunId);
        Assert.Single(results, result => result.WasReplay);
        Assert.Equal(1L, await CountAsync(database, "ReconciliationRun"));
        Assert.Equal(3L, await CountAsync(database, "ReconciliationItem"));

        await using var context = database.CreateContext();
        var replay = await RunUseCase(context).ExecuteAsync(
            "statement-race",
            new RunReconciliationCommand(CoreLedgerTestData.HouseholdId, history.EvidenceId));
        Assert.True(replay.WasReplay);
        Assert.Equal(results[0].ReconciliationRunId, replay.ReconciliationRunId);
    }

    [Fact]
    public async Task CrossHouseholdAndInvalidCorrectionsAreRefused()
    {
        await using var database = await SqliteTestDatabase.CreateAsync();
        var history = await SeedAsync(database.CreateContext);
        var runId = await RunAsync(database, "foreign-run", history.EvidenceId, Now);
        var cashItemId = Item(await GetAsync(database, runId), CoreLedgerTestData.CashAssetId).ReconciliationItemId;

        await using (var context = database.CreateContext())
        {
            var foreignRun = await Assert.ThrowsAsync<ReconciliationException>(
                () => RunUseCase(context).ExecuteAsync(
                    "foreign-household",
                    new RunReconciliationCommand(CoreLedgerTestData.OtherHouseholdId, history.EvidenceId)));
            var foreignRead = await Assert.ThrowsAsync<ReconciliationException>(
                () => GetUseCase(context).ExecuteAsync(
                    new GetReconciliationRunQuery(CoreLedgerTestData.OtherHouseholdId, runId)));
            var foreignList = await ListUseCase(context).ExecuteAsync(
                new ListReconciliationRunsQuery(CoreLedgerTestData.OtherHouseholdId));

            Assert.Equal(ReconciliationErrorCodes.EvidenceNotFound, foreignRun.ErrorCode);
            Assert.Equal(ReconciliationErrorCodes.RunNotFound, foreignRead.ErrorCode);
            Assert.Empty(foreignList.Items);
        }

        var draft = await ResolveFailsAsync(
            database,
            runId,
            cashItemId,
            ReconciliationResolutionKind.CorrectedByTransaction,
            transactionId: history.DraftId);
        var otherHousehold = await ResolveFailsAsync(
            database,
            runId,
            cashItemId,
            ReconciliationResolutionKind.CorrectedByTransaction,
            transactionId: history.OtherHouseholdPostedId);

        await using (var context = database.CreateContext())
        {
            var foreignItem = await Assert.ThrowsAsync<ReconciliationException>(
                () => ResolveUseCase(context).ExecuteAsync(
                    new ResolveReconciliationItemCommand(
                        CoreLedgerTestData.OtherHouseholdId,
                        runId,
                        cashItemId,
                        ReconciliationResolutionKind.DocumentedException,
                        Explanation: "Synthetic reason")));
            Assert.Equal(ReconciliationErrorCodes.ItemNotFound, foreignItem.ErrorCode);
        }

        Assert.Equal(ReconciliationErrorCodes.CorrectionInvalid, draft.ErrorCode);
        Assert.Equal(ReconciliationErrorCodes.CorrectionInvalid, otherHousehold.ErrorCode);
        Assert.Equal(0L, await CountAsync(database, "ReconciliationItemResolution"));
    }

    [Fact]
    public async Task SupersededEvidence_KeepsItsRunsButAcceptsNoNewRunOrObservation()
    {
        await using var database = await SqliteTestDatabase.CreateAsync();
        var history = await SeedAsync(database.CreateContext);
        var runId = await RunAsync(database, "superseded-run", history.EvidenceId, Now);
        Guid replacementId;

        await using (var context = database.CreateContext())
        {
            replacementId = (await EvidenceTestData.RecordUseCase(context, Now.AddHours(1))
                .ExecuteAsync(
                    "statement-replacement",
                    EvidenceTestData.Command(
                        label: "Synthetic corrected statement",
                        effectiveDate: EvidenceDate,
                        supersedesEvidenceId: history.EvidenceId)))
                .EvidenceId;
        }

        await using (var context = database.CreateContext())
        {
            var rerun = await Assert.ThrowsAsync<ReconciliationException>(
                () => RunUseCase(context).ExecuteAsync(
                    "superseded-rerun",
                    new RunReconciliationCommand(CoreLedgerTestData.HouseholdId, history.EvidenceId)));
            var observation = await Assert.ThrowsAsync<EvidenceException>(
                () => EvidenceTestData.ObservationUseCase(context).ExecuteAsync(
                    new RecordEvidenceObservationsCommand(
                        CoreLedgerTestData.HouseholdId,
                        history.EvidenceId,
                        [FundObservation with { AccountId = CoreLedgerTestData.DestinationAccountId }])));
            var withoutObservations = await Assert.ThrowsAsync<ReconciliationException>(
                () => RunUseCase(context).ExecuteAsync(
                    "replacement-empty",
                    new RunReconciliationCommand(CoreLedgerTestData.HouseholdId, replacementId)));

            Assert.Equal(ReconciliationErrorCodes.EvidenceSuperseded, rerun.ErrorCode);
            Assert.Equal(EvidenceErrorCodes.Superseded, observation.ErrorCode);
            Assert.Equal(ReconciliationErrorCodes.NoObservations, withoutObservations.ErrorCode);
        }

        Assert.Equal(3, (await GetAsync(database, runId)).Items.Count);

        await using (var context = database.CreateContext())
        {
            await EvidenceTestData.ObservationUseCase(context).ExecuteAsync(
                new RecordEvidenceObservationsCommand(
                    CoreLedgerTestData.HouseholdId,
                    replacementId,
                    [CashObservation with { ObservedQuantityRawE8 = 100 * Unit }]));
        }

        var replacementRun = await GetAsync(
            database,
            await RunAsync(database, "replacement-run", replacementId, Now.AddHours(2)));
        Assert.Equal(ReconciliationComparisonStatus.Matched, Assert.Single(replacementRun.Items).ComparisonStatus);
    }

    [Fact]
    public async Task Store_MapsGuardsThatFireAfterValidationToStableErrors()
    {
        await using var database = await SqliteTestDatabase.CreateAsync();
        var history = await SeedAsync(database.CreateContext);
        var runId = await RunAsync(database, "guard-run", history.EvidenceId, Now);
        var cashItemId = Item(await GetAsync(database, runId), CoreLedgerTestData.CashAssetId).ReconciliationItemId;

        // As if a draft had been linked by a writer that skipped validation.
        await using (var context = database.CreateContext())
        {
            var correction = await Assert.ThrowsAsync<ReconciliationException>(
                () => new EfCoreReconciliationStore(context).TryAddResolutionAsync(
                    ReconciliationItemResolution.CorrectedByTransaction(
                        cashItemId,
                        history.DraftId,
                        Now)));
            Assert.Equal(ReconciliationErrorCodes.CorrectionInvalid, correction.ErrorCode);
        }

        IReadOnlyList<Domain.Evidence.EvidenceObservation> observations;

        await using (var context = database.CreateContext())
        {
            observations = await new EfCoreEvidenceStore(context)
                .ListObservationsAsync(history.EvidenceId);
            await EvidenceTestData.RecordUseCase(context, Now.AddHours(1)).ExecuteAsync(
                "guard-replacement",
                EvidenceTestData.Command(
                    effectiveDate: EvidenceDate,
                    supersedesEvidenceId: history.EvidenceId));
        }

        // As if the record had been superseded after the run was validated.
        await using (var context = database.CreateContext())
        {
            var run = ReconciliationRun.Create(
                Guid.NewGuid(),
                CoreLedgerTestData.HouseholdId,
                history.EvidenceId,
                EvidenceDate,
                Now.AddHours(2),
                observations
                    .Select(observation => new ReconciliationItemInput(
                        Guid.NewGuid(),
                        observation,
                        observation.ObservedQuantityRawE8,
                        observation.ObservedPieceCount))
                    .ToArray());
            var superseded = await Assert.ThrowsAsync<ReconciliationException>(
                () => new EfCoreReconciliationStore(context).TryCommitRunAsync("guard-late", run));
            Assert.Equal(ReconciliationErrorCodes.EvidenceSuperseded, superseded.ErrorCode);
        }

        Assert.Equal(1L, await CountAsync(database, "ReconciliationRun"));
        Assert.Equal(0L, await CountAsync(database, "ReconciliationItemResolution"));
    }

    [Fact]
    public async Task List_PagesDeterministicallyAndBindsTheEvidenceFilter()
    {
        await using var database = await SqliteTestDatabase.CreateAsync();
        var history = await SeedAsync(database.CreateContext);
        var runIds = new List<Guid>();

        // Two runs share one timestamp, so the identity breaks the tie.
        foreach (var (key, offset) in new[] { ("page-a", 0), ("page-b", 1), ("page-c", 1), ("page-d", 2) })
        {
            runIds.Add(await RunAsync(database, key, history.EvidenceId, Now.AddMinutes(offset)));
        }

        await using var context = database.CreateContext();
        var useCase = ListUseCase(context);
        var seen = new List<Guid>();
        string? cursor = null;

        do
        {
            var page = await useCase.ExecuteAsync(
                new ListReconciliationRunsQuery(
                    CoreLedgerTestData.HouseholdId,
                    history.EvidenceId,
                    PageSize: 1,
                    Cursor: cursor));
            seen.AddRange(page.Items.Select(item => item.ReconciliationRunId));
            cursor = page.NextCursor;
        }
        while (cursor is not null);

        Assert.Equal(4, seen.Count);
        Assert.Equal(runIds.Distinct().Count(), seen.Distinct().Count());
        Assert.Equal(runIds[3], seen[0]);
        Assert.Equal(runIds[0], seen[3]);
        Assert.Equal(
            new[] { runIds[1], runIds[2] }.OrderByDescending(id => id.ToString("D"), StringComparer.Ordinal),
            seen.Skip(1).Take(2));

        var unfiltered = await useCase.ExecuteAsync(
            new ListReconciliationRunsQuery(CoreLedgerTestData.HouseholdId, PageSize: 2));
        var mismatch = await Assert.ThrowsAsync<ReconciliationException>(
            () => useCase.ExecuteAsync(
                new ListReconciliationRunsQuery(
                    CoreLedgerTestData.HouseholdId,
                    history.EvidenceId,
                    Cursor: unfiltered.NextCursor)));
        Assert.Equal(ReconciliationErrorCodes.CursorScopeMismatch, mismatch.ErrorCode);
    }

    private static ReconciliationItemDetail Item(ReconciliationRunDetail run, Guid assetId)
        => run.Items.Single(item => item.Scope.AssetId == assetId);

    private static (int, int, int, int, int, int) Counts(ReconciliationRunSummary summary)
        => (summary.ItemCount,
            summary.MatchedCount,
            summary.OutOfToleranceCount,
            summary.OpenCount,
            summary.CorrectedByTransactionCount,
            summary.DocumentedExceptionCount);

    private static async Task<Guid> RunAsync(
        SqliteTestDatabase database,
        string key,
        Guid evidenceId,
        DateTimeOffset now)
    {
        await using var context = database.CreateContext();

        return (await RunUseCase(context, now).ExecuteAsync(
                key,
                new RunReconciliationCommand(CoreLedgerTestData.HouseholdId, evidenceId)))
            .ReconciliationRunId;
    }

    private static async Task<ReconciliationRunDetail> GetAsync(
        SqliteTestDatabase database,
        Guid runId)
    {
        await using var context = database.CreateContext();

        return await GetUseCase(context).ExecuteAsync(
            new GetReconciliationRunQuery(CoreLedgerTestData.HouseholdId, runId));
    }

    private static async Task ResolveAsync(
        SqliteTestDatabase database,
        Guid runId,
        Guid itemId,
        ReconciliationResolutionKind kind,
        Guid? transactionId = null,
        string? explanation = null)
    {
        await using var context = database.CreateContext();

        var result = await ResolveUseCase(context).ExecuteAsync(
            new ResolveReconciliationItemCommand(
                CoreLedgerTestData.HouseholdId,
                runId,
                itemId,
                kind,
                transactionId,
                explanation));

        Assert.False(result.WasAlreadyResolved);
    }

    private static async Task<ReconciliationException> ResolveFailsAsync(
        SqliteTestDatabase database,
        Guid runId,
        Guid itemId,
        ReconciliationResolutionKind kind,
        Guid? transactionId = null,
        string? explanation = null)
    {
        await using var context = database.CreateContext();

        return await Assert.ThrowsAsync<ReconciliationException>(
            () => ResolveUseCase(context).ExecuteAsync(
                new ResolveReconciliationItemCommand(
                    CoreLedgerTestData.HouseholdId,
                    runId,
                    itemId,
                    kind,
                    transactionId,
                    explanation)));
    }

    private static async Task<string> ItemRowAsync(SqliteTestDatabase database, Guid assetId)
        => Convert.ToString(await database.ExecuteScalarAsync(
            """
            SELECT ComparisonStatusCode || ':' || ObservedQuantityE8 || ':' || LedgerQuantityE8
                   || ':' || DeltaE8 || ':' || ToleranceE8 || ':'
                   || COALESCE(ObservedPieceCount || '/' || LedgerPieceCount || '/' || PieceDelta, '-')
            FROM ReconciliationItem
            WHERE AssetId = $asset;
            """,
            new Microsoft.Data.Sqlite.SqliteParameter("$asset", assetId.ToString("D"))))!;

    private static async Task<long> CountAsync(SqliteTestDatabase database, string table)
        => Convert.ToInt64(await database.ExecuteScalarAsync(
            $"SELECT COUNT(*) FROM \"{table}\";"));
}
