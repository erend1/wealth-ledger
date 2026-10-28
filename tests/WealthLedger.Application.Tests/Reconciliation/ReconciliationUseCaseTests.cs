using WealthLedger.Application.CoreLedger;
using WealthLedger.Application.Evidence;
using WealthLedger.Application.Inventory;
using WealthLedger.Application.Reconciliation;
using WealthLedger.Domain.Assets;
using WealthLedger.Domain.Evidence;
using WealthLedger.Domain.Ledger;
using WealthLedger.Domain.Lots;
using WealthLedger.Domain.Portfolios;
using WealthLedger.Domain.Reconciliation;

namespace WealthLedger.Application.Tests.Reconciliation;

public sealed class ReconciliationUseCaseTests
{
    private static readonly Guid HouseholdId = Id("a1");
    private static readonly Guid OtherHouseholdId = Id("a2");
    private static readonly Guid InstitutionId = Id("b1");
    private static readonly Guid PortfolioId = Id("c1");
    private static readonly Guid AccountId = Id("d1");
    private static readonly Guid SecondAccountId = Id("d2");
    private static readonly Guid CashAssetId = Id("e1");
    private static readonly Guid FundAssetId = Id("e2");
    private static readonly Guid GoldAssetId = Id("e3");
    private static readonly Guid EvidenceId = Id("f1");
    private static readonly Guid SupersededEvidenceId = Id("f2");
    private static readonly Guid OtherHouseholdEvidenceId = Id("f3");
    private static readonly Guid UnobservedEvidenceId = Id("f4");
    private static readonly Guid SecondEvidenceId = Id("f5");
    private static readonly Guid PostedTransactionId = Id("71");
    private static readonly Guid DraftTransactionId = Id("72");
    private static readonly Guid OtherHouseholdTransactionId = Id("73");

    private static readonly DateOnly EvidenceDate = new(2026, 9, 30);

    private static readonly DateTimeOffset Now =
        new(2026, 10, 2, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Run_ComparesEachObservedScopeAsOfTheEvidenceDate()
    {
        var fixture = new Fixture();
        fixture.Entry(AccountId, CashAssetId, 1_000, new DateOnly(2026, 9, 1));
        fixture.Entry(AccountId, CashAssetId, 500, new DateOnly(2026, 10, 1));
        fixture.Entry(SecondAccountId, CashAssetId, 700, new DateOnly(2026, 9, 1));
        fixture.Entry(AccountId, FundAssetId, 40, new DateOnly(2026, 9, 15));
        fixture.GoldLot(AccountId, 100, pieces: 3, new DateOnly(2026, 9, 10));
        fixture.Observe(EvidenceId, AccountId, CashAssetId, 1_000);
        fixture.Observe(EvidenceId, AccountId, FundAssetId, 50, tolerance: 5);
        fixture.Observe(EvidenceId, AccountId, GoldAssetId, 100, pieces: 2);

        var result = await fixture.RunAsync("run-1", EvidenceId);

        Assert.False(result.WasReplay);
        var run = Assert.Single(fixture.Store.Runs).Run;
        Assert.Equal(result.ReconciliationRunId, run.Id);
        Assert.Equal(HouseholdId, run.HouseholdId);
        Assert.Equal(EvidenceId, run.EvidenceId);
        Assert.Equal(EvidenceDate, run.EvidenceEffectiveDate);
        Assert.Equal(Now, run.RunAtUtc);
        Assert.Equal(ReconciliationMethod.ExactScopeQuantityAsOf, run.Method);
        Assert.Equal(1, run.MethodVersion);
        Assert.Equal(3, run.Items.Count);

        var cash = run.Items[0];
        Assert.Equal((1_000L, 1_000L, 0L), (cash.ObservedQuantityRawE8, cash.LedgerQuantityRawE8, cash.DeltaRawE8));
        Assert.Equal(ReconciliationComparisonStatus.Matched, cash.ComparisonStatus);

        var fund = run.Items[1];
        Assert.Equal((50L, 40L, 10L, 5L), (fund.ObservedQuantityRawE8, fund.LedgerQuantityRawE8, fund.DeltaRawE8, fund.ToleranceRawE8));
        Assert.Equal(ReconciliationComparisonStatus.OutOfTolerance, fund.ComparisonStatus);

        var gold = run.Items[2];
        Assert.Equal(0, gold.DeltaRawE8);
        Assert.Equal((2, 3, -1), (gold.ObservedPieceCount, gold.LedgerPieceCount, gold.PieceDelta));
        Assert.Equal(ReconciliationComparisonStatus.OutOfTolerance, gold.ComparisonStatus);

        // The second account was not observed, so it has no item at all.
        Assert.DoesNotContain(run.Items, item => item.AccountId == SecondAccountId);
    }

    [Fact]
    public async Task Run_ValidScopeWithoutEffectiveEntriesIsExactlyZero()
    {
        var fixture = new Fixture();
        fixture.Entry(AccountId, CashAssetId, 900, new DateOnly(2026, 10, 1));
        fixture.Observe(EvidenceId, AccountId, CashAssetId, 0);
        fixture.Observe(EvidenceId, SecondAccountId, CashAssetId, 5);

        await fixture.RunAsync("run-zero", EvidenceId);

        var run = Assert.Single(fixture.Store.Runs).Run;
        Assert.Equal(0, run.Items[0].LedgerQuantityRawE8);
        Assert.Equal(ReconciliationComparisonStatus.Matched, run.Items[0].ComparisonStatus);
        Assert.Equal(0, run.Items[1].LedgerQuantityRawE8);
        Assert.Equal(5, run.Items[1].DeltaRawE8);
        Assert.Equal(ReconciliationComparisonStatus.OutOfTolerance, run.Items[1].ComparisonStatus);
    }

    [Fact]
    public async Task Run_SignedAsOfSliceFromBackdatedHistoryIsAVisibleDifference()
    {
        var fixture = new Fixture();
        fixture.Entry(AccountId, FundAssetId, -300, new DateOnly(2026, 9, 10));
        fixture.Entry(AccountId, FundAssetId, 300, new DateOnly(2026, 10, 10));
        var lotId = fixture.GoldLot(AccountId, 100, pieces: 1, new DateOnly(2026, 10, 10));
        fixture.GoldAllocation(lotId, AccountId, -100, pieceDelta: -1, new DateOnly(2026, 9, 10));
        fixture.Observe(EvidenceId, AccountId, FundAssetId, 0);
        fixture.Observe(EvidenceId, AccountId, GoldAssetId, 0, pieces: 0);

        await fixture.RunAsync("run-signed", EvidenceId);

        var run = Assert.Single(fixture.Store.Runs).Run;
        Assert.Equal((-300L, 300L), (run.Items[0].LedgerQuantityRawE8, run.Items[0].DeltaRawE8));
        Assert.Equal(ReconciliationComparisonStatus.OutOfTolerance, run.Items[0].ComparisonStatus);
        Assert.Equal((-100L, 100L), (run.Items[1].LedgerQuantityRawE8, run.Items[1].DeltaRawE8));
        Assert.Equal((-1, 1), (run.Items[1].LedgerPieceCount, run.Items[1].PieceDelta));
    }

    [Fact]
    public async Task Run_NarrowsTheDerivationToSharedScopeDimensions()
    {
        var fixture = new Fixture();
        fixture.Entry(AccountId, CashAssetId, 1_000, new DateOnly(2026, 9, 1));
        fixture.Entry(SecondAccountId, CashAssetId, 700, new DateOnly(2026, 9, 1));
        fixture.Observe(EvidenceId, AccountId, CashAssetId, 1_000);
        fixture.Observe(EvidenceId, AccountId, FundAssetId, 0);
        fixture.Observe(SecondEvidenceId, SecondAccountId, GoldAssetId, 0);

        await fixture.RunAsync("narrow-account", EvidenceId);
        Assert.Equal((PortfolioId, AccountId, (Guid?)null), fixture.Inventory.LastPositionFilter);

        // One exact scope with no effective entries is a valid zero.
        await fixture.RunAsync("narrow-exact", SecondEvidenceId);
        Assert.Equal((PortfolioId, SecondAccountId, GoldAssetId), fixture.Inventory.LastPositionFilter);

        var exact = fixture.Store.Runs.Single(run => run.Key == "narrow-exact").Run;
        Assert.Equal(0, Assert.Single(exact.Items).LedgerQuantityRawE8);
        Assert.Equal(ReconciliationComparisonStatus.Matched, exact.Items[0].ComparisonStatus);
        Assert.Equal(
            (1_000L, ReconciliationComparisonStatus.Matched),
            (fixture.Store.Runs[0].Run.Items[0].LedgerQuantityRawE8, fixture.Store.Runs[0].Run.Items[0].ComparisonStatus));
    }

    [Fact]
    public async Task Run_ReplayReturnsTheOriginalAndAnotherEvidenceIsAConflict()
    {
        var fixture = new Fixture();
        fixture.Observe(EvidenceId, AccountId, CashAssetId, 1);
        fixture.Observe(SecondEvidenceId, AccountId, CashAssetId, 1);

        var first = await fixture.RunAsync("run-key", EvidenceId);
        var replay = await fixture.RunAsync("run-key", EvidenceId);

        Assert.True(replay.WasReplay);
        Assert.Equal(first.ReconciliationRunId, replay.ReconciliationRunId);
        Assert.Single(fixture.Store.Runs);
        await Assert.ThrowsAsync<IdempotencyConflictException>(
            () => fixture.RunAsync("run-key", SecondEvidenceId));

        var second = await fixture.RunAsync("run-key-2", EvidenceId);
        Assert.NotEqual(first.ReconciliationRunId, second.ReconciliationRunId);
        Assert.Equal(2, fixture.Store.Runs.Count);
    }

    [Fact]
    public async Task Run_LostRaceReturnsTheWinnerOrConflicts()
    {
        var fixture = new Fixture();
        fixture.Observe(EvidenceId, AccountId, CashAssetId, 1);
        var winnerId = Guid.NewGuid();
        fixture.Store.OnCommit = _ => new ReconciliationRunReference(winnerId, HouseholdId, EvidenceId);

        var result = await fixture.RunAsync("race", EvidenceId);

        Assert.True(result.WasReplay);
        Assert.Equal(winnerId, result.ReconciliationRunId);

        fixture.Store.OnCommit = _ => new ReconciliationRunReference(winnerId, HouseholdId, SecondEvidenceId);
        await Assert.ThrowsAsync<IdempotencyConflictException>(
            () => fixture.RunAsync("race-2", EvidenceId));
    }

    [Theory]
    [InlineData("unknown", ReconciliationErrorCategory.NotFound, ReconciliationErrorCodes.EvidenceNotFound)]
    [InlineData("other-household", ReconciliationErrorCategory.NotFound, ReconciliationErrorCodes.EvidenceNotFound)]
    [InlineData("superseded", ReconciliationErrorCategory.Conflict, ReconciliationErrorCodes.EvidenceSuperseded)]
    [InlineData("unobserved", ReconciliationErrorCategory.Validation, ReconciliationErrorCodes.NoObservations)]
    public async Task Run_RequiresTheLatestObservedSameHouseholdEvidence(
        string evidence,
        ReconciliationErrorCategory category,
        string errorCode)
    {
        var fixture = new Fixture();
        fixture.Observe(SupersededEvidenceId, AccountId, CashAssetId, 1);
        fixture.Observe(OtherHouseholdEvidenceId, AccountId, CashAssetId, 1);
        var evidenceId = evidence switch
        {
            "unknown" => Guid.NewGuid(),
            "other-household" => OtherHouseholdEvidenceId,
            "superseded" => SupersededEvidenceId,
            _ => UnobservedEvidenceId
        };

        var exception = await Assert.ThrowsAsync<ReconciliationException>(
            () => fixture.RunAsync("run-refused", evidenceId));

        Assert.Equal(category, exception.Category);
        Assert.Equal(errorCode, exception.ErrorCode);
        Assert.Empty(fixture.Store.Runs);
    }

    [Fact]
    public async Task Run_OverflowingDifferenceIsAStableErrorAndCommitsNothing()
    {
        var fixture = new Fixture();
        fixture.Entry(AccountId, CashAssetId, -1, new DateOnly(2026, 9, 1));
        fixture.Observe(EvidenceId, AccountId, CashAssetId, long.MaxValue);

        var exception = await Assert.ThrowsAsync<ReconciliationException>(
            () => fixture.RunAsync("run-overflow", EvidenceId));

        Assert.Equal(ReconciliationErrorCodes.ArithmeticOverflow, exception.ErrorCode);
        Assert.Empty(fixture.Store.Runs);
    }

    [Fact]
    public async Task OverflowingLedgerSideIsAStableErrorForRunsAndReads()
    {
        var fixture = await Fixture.WithMismatchedRunAsync();
        fixture.Entry(AccountId, CashAssetId, long.MaxValue, new DateOnly(2026, 9, 2));

        var run = await Assert.ThrowsAsync<ReconciliationException>(
            () => fixture.RunAsync("run-ledger-overflow", EvidenceId));
        var read = await Assert.ThrowsAsync<ReconciliationException>(
            () => fixture.GetAsync());

        Assert.Equal(ReconciliationErrorCodes.ArithmeticOverflow, run.ErrorCode);
        Assert.Equal(ReconciliationErrorCodes.ArithmeticOverflow, read.ErrorCode);
        Assert.Single(fixture.Store.Runs);
    }

    [Fact]
    public async Task Run_ValidatesTheRetryKeyAndIdentities()
    {
        var fixture = new Fixture();

        await Assert.ThrowsAnyAsync<ArgumentException>(() => fixture.RunAsync(" ", EvidenceId));
        await Assert.ThrowsAsync<ArgumentException>(() => fixture.RunAsync(new string('k', 257), EvidenceId));
        await Assert.ThrowsAsync<ArgumentException>(() => fixture.RunAsync("key", Guid.Empty));
        Assert.Equal(0, fixture.Store.CallCount);
    }

    [Fact]
    public async Task Resolve_DocumentedExceptionAndCorrectionKeepTheComparison()
    {
        var fixture = await Fixture.WithMismatchedRunAsync();
        var (first, second) = (fixture.MismatchedItemId(0), fixture.MismatchedItemId(1));

        var documented = await fixture.ResolveAsync(
            first,
            ReconciliationResolutionKind.DocumentedException,
            explanation: "  Synthetic custodian rounding.  ");
        var corrected = await fixture.ResolveAsync(
            second,
            ReconciliationResolutionKind.CorrectedByTransaction,
            transactionId: PostedTransactionId);

        Assert.False(documented.WasAlreadyResolved);
        Assert.False(corrected.WasAlreadyResolved);
        Assert.Equal("Synthetic custodian rounding.", fixture.Store.Resolutions[first].Explanation);
        Assert.Equal(PostedTransactionId, fixture.Store.Resolutions[second].CorrectiveTransactionId);
        Assert.Equal(Now, fixture.Store.Resolutions[second].ResolvedAtUtc);

        var detail = await fixture.GetAsync();
        Assert.All(
            detail.Items.Where(item => item.ResolutionState is not null),
            item => Assert.Equal(ReconciliationComparisonStatus.OutOfTolerance, item.ComparisonStatus));
        Assert.Equal(
            ReconciliationResolutionState.DocumentedException,
            detail.Items.Single(item => item.ReconciliationItemId == first).ResolutionState);
        Assert.Equal(
            ReconciliationResolutionState.CorrectedByTransaction,
            detail.Items.Single(item => item.ReconciliationItemId == second).ResolutionState);
        Assert.Null(detail.Items.Single(item => item.ComparisonStatus == ReconciliationComparisonStatus.Matched).ResolutionState);
    }

    [Theory]
    [InlineData("draft")]
    [InlineData("other-household")]
    [InlineData("unknown")]
    public async Task Resolve_CorrectionRequiresAPostedSameHouseholdTransaction(string transaction)
    {
        var fixture = await Fixture.WithMismatchedRunAsync();
        var transactionId = transaction switch
        {
            "draft" => DraftTransactionId,
            "other-household" => OtherHouseholdTransactionId,
            _ => Guid.NewGuid()
        };

        var exception = await Assert.ThrowsAsync<ReconciliationException>(
            () => fixture.ResolveAsync(
                fixture.MismatchedItemId(0),
                ReconciliationResolutionKind.CorrectedByTransaction,
                transactionId: transactionId));

        Assert.Equal(ReconciliationErrorCodes.CorrectionInvalid, exception.ErrorCode);
        Assert.Empty(fixture.Store.Resolutions);
    }

    [Fact]
    public async Task Resolve_MatchedItemNeedsNoResolution()
    {
        var fixture = await Fixture.WithMismatchedRunAsync();

        var exception = await Assert.ThrowsAsync<ReconciliationException>(
            () => fixture.ResolveAsync(
                fixture.MatchedItemId(),
                ReconciliationResolutionKind.DocumentedException,
                explanation: "Synthetic reason"));

        Assert.Equal(ReconciliationErrorCodes.ItemMatched, exception.ErrorCode);
    }

    [Fact]
    public async Task Resolve_RepeatIsANoOpAndADifferentResolutionIsAConflict()
    {
        var fixture = await Fixture.WithMismatchedRunAsync();
        var itemId = fixture.MismatchedItemId(0);

        await fixture.ResolveAsync(itemId, ReconciliationResolutionKind.DocumentedException, explanation: "Synthetic reason");
        var repeat = await fixture.ResolveAsync(itemId, ReconciliationResolutionKind.DocumentedException, explanation: "Synthetic reason ");
        var conflict = await Assert.ThrowsAsync<ReconciliationException>(
            () => fixture.ResolveAsync(itemId, ReconciliationResolutionKind.CorrectedByTransaction, transactionId: PostedTransactionId));

        Assert.True(repeat.WasAlreadyResolved);
        Assert.Equal(ReconciliationErrorCategory.Conflict, conflict.Category);
        Assert.Equal(ReconciliationErrorCodes.ItemAlreadyResolved, conflict.ErrorCode);
        Assert.Single(fixture.Store.Resolutions);
    }

    [Fact]
    public async Task Resolve_LostRaceReturnsTheStoredResolutionOrConflicts()
    {
        var fixture = await Fixture.WithMismatchedRunAsync();
        var itemId = fixture.MismatchedItemId(0);
        fixture.Store.OnResolve = resolution => ReconciliationItemResolution.DocumentedException(
            resolution.ItemId,
            "Synthetic reason",
            Now.AddMinutes(-1));

        var same = await fixture.ResolveAsync(itemId, ReconciliationResolutionKind.DocumentedException, explanation: "Synthetic reason");
        var different = await Assert.ThrowsAsync<ReconciliationException>(
            () => fixture.ResolveAsync(itemId, ReconciliationResolutionKind.DocumentedException, explanation: "Another reason"));

        Assert.True(same.WasAlreadyResolved);
        Assert.Equal(ReconciliationErrorCodes.ItemAlreadyResolved, different.ErrorCode);
    }

    [Theory]
    [InlineData("unknown-item")]
    [InlineData("other-run")]
    [InlineData("other-household")]
    public async Task Resolve_UnknownOrForeignItemIsNotFound(string shape)
    {
        var fixture = await Fixture.WithMismatchedRunAsync();
        var command = shape switch
        {
            "unknown-item" => fixture.ResolveCommand(Guid.NewGuid(), explanation: "Synthetic reason"),
            "other-run" => fixture.ResolveCommand(fixture.MismatchedItemId(0), explanation: "Synthetic reason") with
            {
                ReconciliationRunId = Guid.NewGuid()
            },
            _ => fixture.ResolveCommand(fixture.MismatchedItemId(0), explanation: "Synthetic reason") with
            {
                HouseholdId = OtherHouseholdId
            }
        };

        var exception = await Assert.ThrowsAsync<ReconciliationException>(
            () => fixture.ResolveUseCase().ExecuteAsync(command));

        Assert.Equal(ReconciliationErrorCategory.NotFound, exception.Category);
        Assert.Equal(ReconciliationErrorCodes.ItemNotFound, exception.ErrorCode);
    }

    [Theory]
    [InlineData("corrected-without-transaction")]
    [InlineData("corrected-with-explanation")]
    [InlineData("documented-with-transaction")]
    [InlineData("documented-blank")]
    [InlineData("documented-control")]
    [InlineData("documented-too-long")]
    [InlineData("undefined-kind")]
    public async Task Resolve_RejectsMalformedResolutionsBeforeAnyStoreAccess(string shape)
    {
        var fixture = await Fixture.WithMismatchedRunAsync();
        var itemId = fixture.MismatchedItemId(0);
        var callsBefore = fixture.Store.CallCount;
        var command = shape switch
        {
            "corrected-without-transaction" => fixture.ResolveCommand(itemId, ReconciliationResolutionKind.CorrectedByTransaction),
            "corrected-with-explanation" => fixture.ResolveCommand(
                itemId,
                ReconciliationResolutionKind.CorrectedByTransaction,
                PostedTransactionId,
                "Synthetic reason"),
            "documented-with-transaction" => fixture.ResolveCommand(
                itemId,
                ReconciliationResolutionKind.DocumentedException,
                PostedTransactionId,
                "Synthetic reason"),
            "documented-blank" => fixture.ResolveCommand(itemId, explanation: "   "),
            "documented-control" => fixture.ResolveCommand(itemId, explanation: "Synthetic\u0007"),
            "documented-too-long" => fixture.ResolveCommand(itemId, explanation: new string('x', 2_001)),
            _ => fixture.ResolveCommand(itemId, (ReconciliationResolutionKind)99, explanation: "Synthetic reason")
        };

        var exception = await Assert.ThrowsAsync<ReconciliationException>(
            () => fixture.ResolveUseCase().ExecuteAsync(command));

        Assert.Equal(ReconciliationErrorCodes.ResolutionInvalid, exception.ErrorCode);
        Assert.Equal(callsBefore, fixture.Store.CallCount);
    }

    [Fact]
    public async Task Get_LaterEffectiveHistoryMakesTheRunStaleWithoutRewritingIt()
    {
        var fixture = await Fixture.WithMismatchedRunAsync();
        var before = await fixture.GetAsync();

        Assert.False(before.IsStale);
        Assert.All(before.Items, item => Assert.False(item.IsStale));

        // Later than the evidence date: the as-of comparison is unchanged.
        fixture.Entry(AccountId, CashAssetId, 77, new DateOnly(2026, 10, 1));
        Assert.False((await fixture.GetAsync()).IsStale);

        // A reversal keeps its original's effective date, on or before the
        // evidence date, so it changes the as-of quantity.
        fixture.Entry(AccountId, FundAssetId, -40, new DateOnly(2026, 9, 15));
        var after = await fixture.GetAsync();

        Assert.True(after.IsStale);
        var fund = after.Items.Single(item => item.Scope.AssetId == FundAssetId);
        Assert.True(fund.IsStale);
        Assert.Equal(40, fund.LedgerQuantityRawE8);
        Assert.Equal(10, fund.DeltaRawE8);
        Assert.Equal(0, fund.CurrentLedgerQuantityRawE8);
        Assert.False(after.Items.Single(item => item.Scope.AssetId == CashAssetId).IsStale);
        Assert.Equal(before.Items.Select(item => item.LedgerQuantityRawE8), after.Items.Select(item => item.LedgerQuantityRawE8));
    }

    [Fact]
    public async Task Get_PieceCountChangesAlsoMakeTheRunStale()
    {
        var fixture = await Fixture.WithMismatchedRunAsync();

        // Pieces move independently of gross weight.
        fixture.GoldAllocation(fixture.GoldLotId, AccountId, 0, pieceDelta: -1, new DateOnly(2026, 9, 20));
        var detail = await fixture.GetAsync();

        var gold = detail.Items.Single(item => item.Scope.AssetId == GoldAssetId);
        Assert.True(detail.IsStale);
        Assert.True(gold.IsStale);
        Assert.Equal(gold.LedgerQuantityRawE8, gold.CurrentLedgerQuantityRawE8);
        Assert.Equal(gold.LedgerPieceCount - 1, gold.CurrentLedgerPieceCount);
    }

    [Fact]
    public async Task Get_UnknownOrForeignRunIsNotFound()
    {
        var fixture = await Fixture.WithMismatchedRunAsync();
        var useCase = fixture.GetUseCase();

        var unknown = await Assert.ThrowsAsync<ReconciliationException>(
            () => useCase.ExecuteAsync(new GetReconciliationRunQuery(HouseholdId, Guid.NewGuid())));
        var foreign = await Assert.ThrowsAsync<ReconciliationException>(
            () => useCase.ExecuteAsync(new GetReconciliationRunQuery(OtherHouseholdId, fixture.RunId)));

        Assert.Equal(ReconciliationErrorCodes.RunNotFound, unknown.ErrorCode);
        Assert.Equal(ReconciliationErrorCodes.RunNotFound, foreign.ErrorCode);
    }

    [Fact]
    public async Task List_PagesWithAHouseholdAndFilterBoundCursor()
    {
        var fixture = new Fixture();
        fixture.Observe(EvidenceId, AccountId, CashAssetId, 0);
        fixture.Observe(SecondEvidenceId, AccountId, CashAssetId, 0);

        for (var index = 0; index < 3; index++)
        {
            fixture.Now = Now.AddMinutes(index);
            await fixture.RunAsync($"list-{index}", EvidenceId);
        }

        await fixture.RunAsync("list-other", SecondEvidenceId);
        var useCase = fixture.ListUseCase();

        var first = await useCase.ExecuteAsync(new ListReconciliationRunsQuery(HouseholdId, EvidenceId, PageSize: 2));
        var second = await useCase.ExecuteAsync(
            new ListReconciliationRunsQuery(HouseholdId, EvidenceId, PageSize: 2, Cursor: first.NextCursor));
        var all = await useCase.ExecuteAsync(new ListReconciliationRunsQuery(HouseholdId));

        Assert.Equal(2, first.Items.Count);
        Assert.NotNull(first.NextCursor);
        Assert.Single(second.Items);
        Assert.Null(second.NextCursor);
        Assert.Equal(
            first.Items.Concat(second.Items).Select(item => item.RunAtUtc),
            first.Items.Concat(second.Items).Select(item => item.RunAtUtc).OrderDescending());
        Assert.Equal(4, all.Items.Count);
        Assert.All(first.Items, item => Assert.Equal((1, 1, 0), (item.ItemCount, item.MatchedCount, item.OpenCount)));

        var mismatch = await Assert.ThrowsAsync<ReconciliationException>(
            () => useCase.ExecuteAsync(new ListReconciliationRunsQuery(HouseholdId, Cursor: first.NextCursor)));
        var foreign = await Assert.ThrowsAsync<ReconciliationException>(
            () => useCase.ExecuteAsync(new ListReconciliationRunsQuery(OtherHouseholdId, EvidenceId, Cursor: first.NextCursor)));
        var malformed = await Assert.ThrowsAsync<ReconciliationException>(
            () => useCase.ExecuteAsync(new ListReconciliationRunsQuery(HouseholdId, Cursor: "not a cursor")));

        Assert.Equal(ReconciliationErrorCodes.CursorScopeMismatch, mismatch.ErrorCode);
        Assert.Equal(ReconciliationErrorCodes.CursorScopeMismatch, foreign.ErrorCode);
        Assert.Equal(ReconciliationErrorCodes.CursorInvalid, malformed.ErrorCode);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(101)]
    public async Task List_RejectsPageSizeOutsideBounds(int pageSize)
    {
        var exception = await Assert.ThrowsAsync<ReconciliationException>(
            () => new Fixture().ListUseCase().ExecuteAsync(
                new ListReconciliationRunsQuery(HouseholdId, PageSize: pageSize)));

        Assert.Equal(ReconciliationErrorCodes.PageSizeInvalid, exception.ErrorCode);
    }

    [Fact]
    public async Task List_UnknownHouseholdOrEvidenceFilterIsNotFound()
    {
        var useCase = new Fixture().ListUseCase();

        var household = await Assert.ThrowsAsync<ReconciliationException>(
            () => useCase.ExecuteAsync(new ListReconciliationRunsQuery(Guid.NewGuid())));
        var evidence = await Assert.ThrowsAsync<ReconciliationException>(
            () => useCase.ExecuteAsync(new ListReconciliationRunsQuery(HouseholdId, OtherHouseholdEvidenceId)));

        Assert.Equal(ReconciliationErrorCodes.HouseholdNotFound, household.ErrorCode);
        Assert.Equal(ReconciliationErrorCodes.EvidenceNotFound, evidence.ErrorCode);
    }

    [Fact]
    public async Task FailuresAndModelsNeverEchoQuantitiesOrExplanations()
    {
        var fixture = await Fixture.WithMismatchedRunAsync();
        await fixture.ResolveAsync(
            fixture.MismatchedItemId(0),
            ReconciliationResolutionKind.DocumentedException,
            explanation: "Synthetic private explanation");
        var detail = await fixture.GetAsync();
        var conflict = await Assert.ThrowsAsync<ReconciliationException>(
            () => fixture.ResolveAsync(
                fixture.MismatchedItemId(0),
                ReconciliationResolutionKind.DocumentedException,
                explanation: "Another private explanation"));

        foreach (var text in new[]
                 {
                     conflict.Message,
                     detail.ToString(),
                     detail.Items[1].ToString(),
                     fixture.ResolveCommand(fixture.MismatchedItemId(0), explanation: "Synthetic private explanation").ToString()
                 })
        {
            Assert.DoesNotContain("private explanation", text, StringComparison.Ordinal);
            Assert.DoesNotContain("1234567", text, StringComparison.Ordinal);
        }
    }

    private static Guid Id(string suffix)
        => Guid.Parse($"e5000000-0000-0000-0000-0000000000{suffix}");

    private static InventoryScopeContext ScopeContext(Guid accountId, Guid assetId)
        => new(
            PortfolioId,
            "CORE",
            "Synthetic portfolio",
            PortfolioStatus.Active,
            accountId,
            accountId == AccountId ? "PRIMARY" : "SECONDARY",
            "Synthetic account",
            AccountType.Investment,
            true,
            InstitutionId,
            "SYN_BROKER",
            "Synthetic broker",
            InstitutionType.Broker,
            true,
            assetId,
            assetId == CashAssetId ? "TRY" : assetId == FundAssetId ? "FUND" : "GOLD",
            "Synthetic asset",
            assetId == CashAssetId
                ? AssetType.Cash
                : assetId == FundAssetId ? AssetType.Fund : AssetType.PhysicalGold,
            assetId == CashAssetId
                ? AssetUnit.CurrencyUnit
                : assetId == FundAssetId ? AssetUnit.FundUnit : AssetUnit.GrossGram,
            assetId == CashAssetId ? "TRY" : null,
            assetId == CashAssetId ? LotTrackingMode.None : LotTrackingMode.Required,
            true);

    private static EvidenceObservationScope ObservationScope(Guid accountId, Guid assetId)
    {
        var context = ScopeContext(accountId, assetId);

        return new EvidenceObservationScope(
            context.PortfolioId,
            context.PortfolioCode,
            context.PortfolioName,
            context.AccountId,
            context.AccountCode,
            context.AccountName,
            context.InstitutionId,
            context.InstitutionCode,
            context.InstitutionName,
            context.AssetId,
            context.AssetCode,
            context.AssetName,
            context.AssetType,
            context.AssetBaseUnit);
    }

    private sealed class Fixture
    {
        public Fixture()
        {
            Store = new FakeReconciliationStore();
            Evidence = new FakeEvidenceStore();
            Inventory = new FakeInventoryReadStore();
        }

        public FakeReconciliationStore Store { get; }

        public FakeEvidenceStore Evidence { get; }

        public FakeInventoryReadStore Inventory { get; }

        public DateTimeOffset Now { get; set; } = ReconciliationUseCaseTests.Now;

        public Guid RunId { get; private set; }

        public Guid GoldLotId { get; private set; }

        public static async Task<Fixture> WithMismatchedRunAsync()
        {
            var fixture = new Fixture();
            fixture.Entry(AccountId, CashAssetId, 1_000, new DateOnly(2026, 9, 1));
            fixture.Entry(AccountId, FundAssetId, 40, new DateOnly(2026, 9, 15));
            fixture.GoldLotId = fixture.GoldLot(AccountId, 100, pieces: 3, new DateOnly(2026, 9, 10));
            fixture.Observe(EvidenceId, AccountId, CashAssetId, 1_000);
            fixture.Observe(EvidenceId, AccountId, FundAssetId, 50, tolerance: 5);
            fixture.Observe(EvidenceId, AccountId, GoldAssetId, 100, pieces: 2);
            fixture.RunId = (await fixture.RunAsync("mismatched", EvidenceId)).ReconciliationRunId;
            return fixture;
        }

        public void Entry(Guid accountId, Guid assetId, long quantity, DateOnly date)
            => Inventory.Entries.Add(
                new InventoryEntryFact(
                    Guid.NewGuid(),
                    Guid.NewGuid(),
                    date,
                    Now,
                    1,
                    ScopeContext(accountId, assetId),
                    quantity));

        public Guid GoldLot(Guid accountId, long gross, int pieces, DateOnly date)
        {
            var lotId = Guid.NewGuid();
            var openingEntryId = Guid.NewGuid();
            Inventory.Lots.Add(
                new InventoryLotDescriptor(
                    lotId,
                    Guid.NewGuid(),
                    openingEntryId,
                    GoldAssetId,
                    "GOLD",
                    "Synthetic gold",
                    AssetType.PhysicalGold,
                    AssetUnit.GrossGram,
                    null,
                    LotTrackingMode.Required,
                    true,
                    date,
                    CostBasisStatus.Unknown,
                    null,
                    null,
                    gross,
                    995_000,
                    pieces,
                    null,
                    null,
                    null));
            Inventory.Allocations.Add(Allocation(lotId, openingEntryId, accountId, gross, pieces, date));
            Entry(accountId, GoldAssetId, gross, date);
            return lotId;
        }

        public void GoldAllocation(Guid lotId, Guid accountId, long gross, int pieceDelta, DateOnly date)
        {
            Inventory.Allocations.Add(Allocation(lotId, Guid.NewGuid(), accountId, gross, pieceDelta, date));

            if (gross != 0)
            {
                Entry(accountId, GoldAssetId, gross, date);
            }
        }

        public void Observe(
            Guid evidenceId,
            Guid accountId,
            Guid assetId,
            long observed,
            long tolerance = 0,
            int? pieces = null)
            => Evidence.Observations.Add(
                EvidenceObservation.Create(
                    Guid.NewGuid(),
                    evidenceId,
                    PortfolioId,
                    accountId,
                    assetId,
                    observed,
                    tolerance,
                    pieces,
                    Now.AddHours(-1).AddTicks(Evidence.Observations.Count)));

        public Task<RunReconciliationResult> RunAsync(string key, Guid evidenceId)
            => new RunReconciliationUseCase(
                    Evidence,
                    Store,
                    Inventory,
                    new FixedTimeProvider(Now))
                .ExecuteAsync(key, new RunReconciliationCommand(HouseholdId, evidenceId));

        public Guid MatchedItemId()
            => Store.Runs.Single(run => run.Run.Id == RunId).Run.Items
                .Single(item => item.ComparisonStatus == ReconciliationComparisonStatus.Matched).Id;

        public Guid MismatchedItemId(int index)
            => Store.Runs.Single(run => run.Run.Id == RunId).Run.Items
                .Where(item => item.ComparisonStatus == ReconciliationComparisonStatus.OutOfTolerance)
                .ElementAt(index).Id;

        public ResolveReconciliationItemCommand ResolveCommand(
            Guid itemId,
            ReconciliationResolutionKind kind = ReconciliationResolutionKind.DocumentedException,
            Guid? transactionId = null,
            string? explanation = null)
            => new(HouseholdId, RunId, itemId, kind, transactionId, explanation);

        public ResolveReconciliationItemUseCase ResolveUseCase()
            => new(Store, new FixedTimeProvider(Now));

        public Task<ResolveReconciliationItemResult> ResolveAsync(
            Guid itemId,
            ReconciliationResolutionKind kind,
            Guid? transactionId = null,
            string? explanation = null)
            => ResolveUseCase().ExecuteAsync(ResolveCommand(itemId, kind, transactionId, explanation));

        public GetReconciliationRunUseCase GetUseCase()
            => new(new FakeReconciliationReadStore(Store), Inventory);

        public Task<ReconciliationRunDetail> GetAsync()
            => GetUseCase().ExecuteAsync(new GetReconciliationRunQuery(HouseholdId, RunId));

        public ListReconciliationRunsUseCase ListUseCase()
            => new(new FakeReconciliationReadStore(Store));

        private InventoryLotAllocationFact Allocation(
            Guid lotId,
            Guid entryId,
            Guid accountId,
            long gross,
            int pieces,
            DateOnly date)
            => new(
                lotId,
                Guid.NewGuid(),
                entryId,
                Guid.NewGuid(),
                TransactionType.OpeningBalance,
                date,
                Now,
                1,
                ScopeContext(accountId, GoldAssetId),
                gross,
                pieces);
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class FakeInventoryReadStore : IInventoryReadStore
    {
        public List<InventoryEntryFact> Entries { get; } = [];

        public List<InventoryLotDescriptor> Lots { get; } = [];

        public List<InventoryLotAllocationFact> Allocations { get; } = [];

        public Task<bool> HouseholdExistsAsync(Guid householdId, CancellationToken cancellationToken = default)
            => Task.FromResult(householdId == HouseholdId);

        public Task<bool> FilterScopeExistsAsync(
            Guid householdId,
            Guid? portfolioId,
            Guid? accountId,
            Guid? institutionId,
            Guid? assetId,
            CancellationToken cancellationToken = default)
            => Task.FromResult(householdId == HouseholdId);

        public (Guid? PortfolioId, Guid? AccountId, Guid? AssetId) LastPositionFilter { get; private set; }

        public Task<InventoryScopeContext?> FindExactPositionScopeAsync(
            Guid householdId,
            Guid portfolioId,
            Guid accountId,
            Guid assetId,
            CancellationToken cancellationToken = default)
            => Task.FromResult<InventoryScopeContext?>(
                householdId == HouseholdId && portfolioId == PortfolioId
                    ? ScopeContext(accountId, assetId)
                    : null);

        public Task<IReadOnlyList<InventoryEntryFact>> ListPositionEntryFactsAsync(
            Guid householdId,
            Guid? portfolioId,
            Guid? accountId,
            Guid? institutionId,
            Guid? assetId,
            DateOnly? asOf,
            CancellationToken cancellationToken = default)
        {
            LastPositionFilter = (portfolioId, accountId, assetId);
            IReadOnlyList<InventoryEntryFact> facts = Entries
                .Where(fact => (portfolioId is null || fact.Scope.PortfolioId == portfolioId)
                               && (accountId is null || fact.Scope.AccountId == accountId)
                               && (assetId is null || fact.Scope.AssetId == assetId)
                               && (asOf is null || fact.ExecutionDate <= asOf))
                .ToArray();
            return Task.FromResult(facts);
        }

        public Task<InventoryLotFactSet> ListLotFactsAsync(
            Guid householdId,
            Guid? assetId,
            DateOnly? asOf,
            CancellationToken cancellationToken = default)
            => Task.FromResult(
                new InventoryLotFactSet(
                    Lots.Where(lot => assetId is null || lot.AssetId == assetId).ToArray(),
                    Allocations.Where(allocation => asOf is null || allocation.ExecutionDate <= asOf).ToArray()));
    }

    private sealed class FakeEvidenceStore : IEvidenceStore
    {
        public List<EvidenceObservation> Observations { get; } = [];

        public Task<EvidenceRecordReference?> FindEvidenceAsync(
            Guid evidenceId,
            CancellationToken cancellationToken = default)
        {
            EvidenceRecordReference? evidence = evidenceId switch
            {
                _ when evidenceId == EvidenceId || evidenceId == SecondEvidenceId || evidenceId == UnobservedEvidenceId =>
                    new(evidenceId, HouseholdId, null, EvidenceDate, null, null),
                _ when evidenceId == SupersededEvidenceId =>
                    new(evidenceId, HouseholdId, EvidenceId, EvidenceDate, null, null),
                _ when evidenceId == OtherHouseholdEvidenceId =>
                    new(evidenceId, OtherHouseholdId, null, EvidenceDate, null, null),
                _ => null
            };
            return Task.FromResult(evidence);
        }

        public Task<IReadOnlyList<EvidenceObservation>> ListObservationsAsync(
            Guid evidenceId,
            CancellationToken cancellationToken = default)
        {
            IReadOnlyList<EvidenceObservation> observations = Observations
                .Where(observation => observation.EvidenceId == evidenceId)
                .OrderBy(observation => observation.RecordedAtUtc)
                .ToArray();
            return Task.FromResult(observations);
        }

        public Task<bool> HouseholdExistsAsync(Guid householdId, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<bool> InstitutionExistsAsync(Guid institutionId, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<EvidenceAccountReference?> FindAccountAsync(Guid accountId, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<IReadOnlyList<EvidenceTransactionReference>> FindTransactionsAsync(
            IReadOnlyCollection<Guid> transactionIds,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<EvidenceCommandReceipt?> FindReceiptAsync(
            EvidenceCommandScope scope,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<EvidenceCommitResult> TryCommitAsync(
            EvidenceCommandReceipt receipt,
            EvidenceRecord record,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<int> AddTransactionLinksAsync(
            IReadOnlyCollection<EvidenceTransactionLink> links,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<EvidenceObservationReferences> FindObservationReferencesAsync(
            IReadOnlyCollection<Guid> portfolioIds,
            IReadOnlyCollection<Guid> accountIds,
            IReadOnlyCollection<Guid> assetIds,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<int> AddObservationsAsync(
            Guid evidenceId,
            IReadOnlyCollection<EvidenceObservation> observations,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }

    private sealed class FakeReconciliationStore : IReconciliationStore
    {
        public List<(string Key, ReconciliationRun Run)> Runs { get; } = [];

        public Dictionary<Guid, ReconciliationItemResolution> Resolutions { get; } = [];

        public Func<ReconciliationRun, ReconciliationRunReference>? OnCommit { get; set; }

        public Func<ReconciliationItemResolution, ReconciliationItemResolution>? OnResolve { get; set; }

        public int CallCount { get; private set; }

        public Task<ReconciliationRunReference?> FindRunByKeyAsync(
            Guid householdId,
            string idempotencyKey,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            var stored = Runs.SingleOrDefault(
                run => run.Run.HouseholdId == householdId && run.Key == idempotencyKey);
            return Task.FromResult(
                stored.Run is null
                    ? null
                    : new ReconciliationRunReference(stored.Run.Id, stored.Run.HouseholdId, stored.Run.EvidenceId));
        }

        public Task<ReconciliationRunCommitResult> TryCommitRunAsync(
            string idempotencyKey,
            ReconciliationRun run,
            CancellationToken cancellationToken = default)
        {
            CallCount++;

            if (OnCommit is not null)
            {
                return Task.FromResult(new ReconciliationRunCommitResult(false, OnCommit(run)));
            }

            Runs.Add((idempotencyKey, run));
            return Task.FromResult(
                new ReconciliationRunCommitResult(
                    true,
                    new ReconciliationRunReference(run.Id, run.HouseholdId, run.EvidenceId)));
        }

        public Task<ReconciliationItemReference?> FindItemAsync(
            Guid itemId,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            var run = Runs.Select(x => x.Run).SingleOrDefault(x => x.Items.Any(item => item.Id == itemId));
            var item = run?.Items.Single(x => x.Id == itemId);
            return Task.FromResult(
                item is null
                    ? null
                    : new ReconciliationItemReference(
                        item.Id,
                        item.RunId,
                        run!.HouseholdId,
                        item.ComparisonStatus,
                        Resolutions.GetValueOrDefault(item.Id)));
        }

        public Task<ReconciliationTransactionReference?> FindTransactionAsync(
            Guid transactionId,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            ReconciliationTransactionReference? transaction = transactionId switch
            {
                _ when transactionId == PostedTransactionId =>
                    new(transactionId, HouseholdId, TransactionStatus.Posted),
                _ when transactionId == DraftTransactionId =>
                    new(transactionId, HouseholdId, TransactionStatus.Draft),
                _ when transactionId == OtherHouseholdTransactionId =>
                    new(transactionId, OtherHouseholdId, TransactionStatus.Posted),
                _ => null
            };
            return Task.FromResult(transaction);
        }

        public Task<ReconciliationResolutionCommitResult> TryAddResolutionAsync(
            ReconciliationItemResolution resolution,
            CancellationToken cancellationToken = default)
        {
            CallCount++;

            if (OnResolve is not null)
            {
                return Task.FromResult(new ReconciliationResolutionCommitResult(false, OnResolve(resolution)));
            }

            Resolutions.Add(resolution.ItemId, resolution);
            return Task.FromResult(new ReconciliationResolutionCommitResult(true, resolution));
        }
    }

    private sealed class FakeReconciliationReadStore(FakeReconciliationStore store)
        : IReconciliationReadStore
    {
        public Task<bool> HouseholdExistsAsync(Guid householdId, CancellationToken cancellationToken = default)
            => Task.FromResult(householdId == HouseholdId || householdId == OtherHouseholdId);

        public Task<bool> EvidenceExistsAsync(
            Guid householdId,
            Guid evidenceId,
            CancellationToken cancellationToken = default)
            => Task.FromResult(householdId == HouseholdId && evidenceId != OtherHouseholdEvidenceId);

        public Task<IReadOnlyList<ReconciliationRunSummary>> ListRunsAsync(
            Guid householdId,
            Guid? evidenceId,
            int take,
            ReconciliationRunCursorKey? after,
            CancellationToken cancellationToken = default)
        {
            IReadOnlyList<ReconciliationRunSummary> page = store.Runs
                .Select(x => x.Run)
                .Where(run => run.HouseholdId == householdId
                              && (evidenceId is null || run.EvidenceId == evidenceId)
                              && (after is null || run.RunAtUtc < after.RunAtUtc))
                .OrderByDescending(run => run.RunAtUtc)
                .Take(take)
                .Select(run =>
                {
                    var matched = run.Items.Count(item => item.ComparisonStatus == ReconciliationComparisonStatus.Matched);
                    var mismatched = run.Items.Count - matched;
                    var resolved = run.Items.Count(item => store.Resolutions.ContainsKey(item.Id));
                    return new ReconciliationRunSummary(
                        run.Id,
                        run.HouseholdId,
                        run.EvidenceId,
                        run.EvidenceEffectiveDate,
                        run.RunAtUtc,
                        run.Method,
                        run.MethodVersion,
                        run.Items.Count,
                        matched,
                        mismatched,
                        mismatched - resolved,
                        0,
                        resolved);
                })
                .ToArray();
            return Task.FromResult(page);
        }

        public Task<StoredReconciliationRun?> GetRunAsync(
            Guid householdId,
            Guid reconciliationRunId,
            CancellationToken cancellationToken = default)
        {
            var run = store.Runs
                .Select(x => x.Run)
                .SingleOrDefault(x => x.Id == reconciliationRunId && x.HouseholdId == householdId);

            return Task.FromResult(
                run is null
                    ? null
                    : new StoredReconciliationRun(
                        run.Id,
                        run.HouseholdId,
                        run.EvidenceId,
                        run.EvidenceEffectiveDate,
                        run.RunAtUtc,
                        run.Method,
                        run.MethodVersion,
                        run.Items
                            .Select(item =>
                            {
                                var resolution = store.Resolutions.GetValueOrDefault(item.Id);
                                return new StoredReconciliationItem(
                                    item.Id,
                                    item.Sequence,
                                    item.ObservationId,
                                    ObservationScope(item.AccountId, item.AssetId),
                                    item.ObservedQuantityRawE8,
                                    item.LedgerQuantityRawE8,
                                    item.DeltaRawE8,
                                    item.ToleranceRawE8,
                                    item.ObservedPieceCount,
                                    item.LedgerPieceCount,
                                    item.PieceDelta,
                                    item.ComparisonStatus,
                                    resolution?.Kind,
                                    resolution?.CorrectiveTransactionId,
                                    resolution?.Explanation,
                                    resolution?.ResolvedAtUtc);
                            })
                            .ToArray()));
        }
    }
}
