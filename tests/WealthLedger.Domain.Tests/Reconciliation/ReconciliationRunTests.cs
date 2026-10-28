using WealthLedger.Domain.Evidence;
using WealthLedger.Domain.Reconciliation;

namespace WealthLedger.Domain.Tests.Reconciliation;

public sealed class ReconciliationRunTests
{
    private static readonly Guid RunId =
        Guid.Parse("e2000000-0000-0000-0000-000000000001");

    private static readonly Guid HouseholdId =
        Guid.Parse("e2000000-0000-0000-0000-0000000000a1");

    private static readonly Guid EvidenceId =
        Guid.Parse("e2000000-0000-0000-0000-0000000000e1");

    private static readonly Guid PortfolioId =
        Guid.Parse("e2000000-0000-0000-0000-0000000000b1");

    private static readonly DateOnly EffectiveDate = new(2026, 9, 30);

    private static readonly DateTimeOffset RunAt =
        new(2026, 10, 2, 15, 0, 0, TimeSpan.FromHours(3));

    [Fact]
    public void Create_SnapshotsEachObservationInOrderWithItsComparison()
    {
        var matched = Observation(observed: 1_000, tolerance: 5);
        var mismatched = Observation(observed: 2_000, tolerance: 0);
        var gold = Observation(observed: 500, tolerance: 0, pieces: 2);
        var firstItemId = Guid.NewGuid();

        var run = ReconciliationRun.Create(
            RunId,
            HouseholdId,
            EvidenceId,
            EffectiveDate,
            RunAt,
            [
                new ReconciliationItemInput(firstItemId, matched, 996, null),
                new ReconciliationItemInput(Guid.NewGuid(), mismatched, 2_100, null),
                new ReconciliationItemInput(Guid.NewGuid(), gold, 500, 3)
            ]);

        Assert.Equal(RunId, run.Id);
        Assert.Equal(HouseholdId, run.HouseholdId);
        Assert.Equal(EvidenceId, run.EvidenceId);
        Assert.Equal(EffectiveDate, run.EvidenceEffectiveDate);
        Assert.Equal(TimeSpan.Zero, run.RunAtUtc.Offset);
        Assert.Equal(RunAt.UtcDateTime, run.RunAtUtc.UtcDateTime);
        Assert.Equal(ReconciliationMethod.ExactScopeQuantityAsOf, run.Method);
        Assert.Equal(1, run.MethodVersion);
        Assert.Equal(new[] { 1, 2, 3 }, run.Items.Select(item => item.Sequence));

        var first = run.Items[0];
        Assert.Equal(firstItemId, first.Id);
        Assert.Equal(RunId, first.RunId);
        Assert.Equal(matched.Id, first.ObservationId);
        Assert.Equal(matched.PortfolioId, first.PortfolioId);
        Assert.Equal(matched.AccountId, first.AccountId);
        Assert.Equal(matched.AssetId, first.AssetId);
        Assert.Equal(1_000, first.ObservedQuantityRawE8);
        Assert.Equal(996, first.LedgerQuantityRawE8);
        Assert.Equal(4, first.DeltaRawE8);
        Assert.Equal(5, first.ToleranceRawE8);
        Assert.Equal(ReconciliationComparisonStatus.Matched, first.ComparisonStatus);

        Assert.Equal(-100, run.Items[1].DeltaRawE8);
        Assert.Equal(ReconciliationComparisonStatus.OutOfTolerance, run.Items[1].ComparisonStatus);

        var goldItem = run.Items[2];
        Assert.Equal(2, goldItem.ObservedPieceCount);
        Assert.Equal(3, goldItem.LedgerPieceCount);
        Assert.Equal(-1, goldItem.PieceDelta);
        Assert.Equal(0, goldItem.DeltaRawE8);
        Assert.Equal(ReconciliationComparisonStatus.OutOfTolerance, goldItem.ComparisonStatus);
    }

    [Fact]
    public void Create_RequiresBetweenOneAndOneHundredItems()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Create([]));

        var tooMany = Enumerable.Range(0, ReconciliationRun.MaximumItemCount + 1)
            .Select(_ => new ReconciliationItemInput(
                Guid.NewGuid(),
                Observation(observed: 1, tolerance: 0),
                1,
                null))
            .ToArray();

        Assert.Throws<ArgumentOutOfRangeException>(() => Create(tooMany));
        Assert.Equal(
            ReconciliationRun.MaximumItemCount,
            Create(tooMany[..ReconciliationRun.MaximumItemCount]).Items.Count);
    }

    [Fact]
    public void Create_RejectsObservationOfAnotherEvidenceRecord()
    {
        var foreign = EvidenceObservation.Create(
            Guid.NewGuid(),
            Guid.NewGuid(),
            PortfolioId,
            Guid.NewGuid(),
            Guid.NewGuid(),
            1,
            0,
            null,
            RunAt);

        Assert.Throws<ArgumentException>(
            () => Create([new ReconciliationItemInput(Guid.NewGuid(), foreign, 1, null)]));
    }

    [Fact]
    public void Create_RejectsRepeatedItemObservationOrScope()
    {
        var observation = Observation(observed: 1, tolerance: 0);
        var sameScope = EvidenceObservation.Create(
            Guid.NewGuid(),
            EvidenceId,
            observation.PortfolioId,
            observation.AccountId,
            observation.AssetId,
            1,
            0,
            null,
            RunAt);
        var itemId = Guid.NewGuid();

        Assert.Throws<ArgumentException>(() => Create(
        [
            new ReconciliationItemInput(itemId, observation, 1, null),
            new ReconciliationItemInput(itemId, Observation(observed: 1, tolerance: 0), 1, null)
        ]));
        Assert.Throws<ArgumentException>(() => Create(
        [
            new ReconciliationItemInput(Guid.NewGuid(), observation, 1, null),
            new ReconciliationItemInput(Guid.NewGuid(), observation, 1, null)
        ]));
        Assert.Throws<ArgumentException>(() => Create(
        [
            new ReconciliationItemInput(Guid.NewGuid(), observation, 1, null),
            new ReconciliationItemInput(Guid.NewGuid(), sameScope, 1, null)
        ]));
    }

    [Fact]
    public void Create_RejectsLedgerPiecesThatDoNotMatchTheObservationShape()
    {
        Assert.Throws<ArgumentException>(() => Create(
            [new ReconciliationItemInput(Guid.NewGuid(), Observation(observed: 1, tolerance: 0), 1, 2)]));
        Assert.Throws<ArgumentException>(() => Create(
            [new ReconciliationItemInput(Guid.NewGuid(), Observation(observed: 1, tolerance: 0, pieces: 2), 1, null)]));
    }

    [Fact]
    public void Create_OverflowingDeltaThrowsInsteadOfStoringAWrongValue()
    {
        Assert.Throws<OverflowException>(() => Create(
            [new ReconciliationItemInput(Guid.NewGuid(), Observation(observed: long.MaxValue, tolerance: 0), -1, null)]));
    }

    [Fact]
    public void Create_RejectsEmptyIdentities()
    {
        var input = new ReconciliationItemInput(
            Guid.NewGuid(),
            Observation(observed: 1, tolerance: 0),
            1,
            null);

        Assert.Throws<ArgumentException>(() => ReconciliationRun.Create(
            Guid.Empty, HouseholdId, EvidenceId, EffectiveDate, RunAt, [input]));
        Assert.Throws<ArgumentException>(() => ReconciliationRun.Create(
            RunId, Guid.Empty, EvidenceId, EffectiveDate, RunAt, [input]));
        Assert.Throws<ArgumentException>(() => ReconciliationRun.Create(
            RunId, HouseholdId, Guid.Empty, EffectiveDate, RunAt, [input]));
        Assert.Throws<ArgumentException>(() => Create(
            [new ReconciliationItemInput(Guid.Empty, Observation(observed: 1, tolerance: 0), 1, null)]));
    }

    private static ReconciliationRun Create(
        IReadOnlyList<ReconciliationItemInput> inputs)
        => ReconciliationRun.Create(
            RunId,
            HouseholdId,
            EvidenceId,
            EffectiveDate,
            RunAt,
            inputs);

    private static EvidenceObservation Observation(
        long observed,
        long tolerance,
        int? pieces = null)
        => EvidenceObservation.Create(
            Guid.NewGuid(),
            EvidenceId,
            PortfolioId,
            Guid.NewGuid(),
            Guid.NewGuid(),
            observed,
            tolerance,
            pieces,
            RunAt);
}
