using WealthLedger.Domain.Evidence;

namespace WealthLedger.Domain.Tests.Evidence;

public sealed class EvidenceObservationTests
{
    private static readonly Guid EvidenceId =
        Guid.Parse("e1000000-0000-0000-0000-000000000001");

    private static readonly Guid PortfolioId =
        Guid.Parse("e1000000-0000-0000-0000-0000000000b1");

    private static readonly Guid AccountId =
        Guid.Parse("e1000000-0000-0000-0000-0000000000c1");

    private static readonly Guid AssetId =
        Guid.Parse("e1000000-0000-0000-0000-0000000000d1");

    private static readonly DateTimeOffset RecordedAt =
        new(2026, 10, 2, 12, 0, 0, TimeSpan.FromHours(3));

    [Fact]
    public void Create_KeepsExactSignedFactsAndUsesUtc()
    {
        var observation = Create(
            observedQuantityRawE8: -12_345_678_901,
            toleranceRawE8: 5,
            observedPieceCount: 3);

        Assert.Equal(EvidenceId, observation.EvidenceId);
        Assert.Equal(PortfolioId, observation.PortfolioId);
        Assert.Equal(AccountId, observation.AccountId);
        Assert.Equal(AssetId, observation.AssetId);
        Assert.Equal(-12_345_678_901, observation.ObservedQuantityRawE8);
        Assert.Equal(5, observation.ToleranceRawE8);
        Assert.Equal(3, observation.ObservedPieceCount);
        Assert.Equal(TimeSpan.Zero, observation.RecordedAtUtc.Offset);
        Assert.Equal(RecordedAt.UtcDateTime, observation.RecordedAtUtc.UtcDateTime);
    }

    [Fact]
    public void Create_AllowsZeroToleranceAndNoPieces()
    {
        var observation = Create(toleranceRawE8: 0, observedPieceCount: null);

        Assert.Equal(0, observation.ToleranceRawE8);
        Assert.Null(observation.ObservedPieceCount);
    }

    [Fact]
    public void Create_RejectsNegativeToleranceAndPieces()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => Create(toleranceRawE8: -1));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => Create(observedPieceCount: -1));
    }

    [Fact]
    public void Create_RejectsEmptyIdentities()
    {
        Assert.Throws<ArgumentException>(
            () => EvidenceObservation.Create(
                Guid.Empty, EvidenceId, PortfolioId, AccountId, AssetId, 1, 0, null, RecordedAt));
        Assert.Throws<ArgumentException>(
            () => EvidenceObservation.Create(
                Guid.NewGuid(), Guid.Empty, PortfolioId, AccountId, AssetId, 1, 0, null, RecordedAt));
        Assert.Throws<ArgumentException>(
            () => EvidenceObservation.Create(
                Guid.NewGuid(), EvidenceId, Guid.Empty, AccountId, AssetId, 1, 0, null, RecordedAt));
        Assert.Throws<ArgumentException>(
            () => EvidenceObservation.Create(
                Guid.NewGuid(), EvidenceId, PortfolioId, Guid.Empty, AssetId, 1, 0, null, RecordedAt));
        Assert.Throws<ArgumentException>(
            () => EvidenceObservation.Create(
                Guid.NewGuid(), EvidenceId, PortfolioId, AccountId, Guid.Empty, 1, 0, null, RecordedAt));
    }

    [Fact]
    public void HasSameFacts_IgnoresIdentityAndTimeButNotValues()
    {
        var original = Create(observedQuantityRawE8: 100, toleranceRawE8: 2, observedPieceCount: 1);
        var replay = EvidenceObservation.Create(
            Guid.NewGuid(),
            EvidenceId,
            PortfolioId,
            AccountId,
            AssetId,
            100,
            2,
            1,
            RecordedAt.AddHours(1));

        Assert.True(original.HasSameScope(replay));
        Assert.True(original.HasSameFacts(replay));
        Assert.False(original.HasSameFacts(Create(observedQuantityRawE8: 101, toleranceRawE8: 2, observedPieceCount: 1)));
        Assert.False(original.HasSameFacts(Create(observedQuantityRawE8: 100, toleranceRawE8: 3, observedPieceCount: 1)));
        Assert.False(original.HasSameFacts(Create(observedQuantityRawE8: 100, toleranceRawE8: 2, observedPieceCount: null)));

        var otherScope = EvidenceObservation.Create(
            Guid.NewGuid(), EvidenceId, PortfolioId, Guid.NewGuid(), AssetId, 100, 2, 1, RecordedAt);
        Assert.False(original.HasSameScope(otherScope));
        Assert.False(original.HasSameFacts(otherScope));
    }

    [Fact]
    public void ToString_NeverPrintsQuantities()
    {
        var text = Create(observedQuantityRawE8: 987_654_321).ToString();

        Assert.Equal(nameof(EvidenceObservation), text);
        Assert.DoesNotContain("987654321", text, StringComparison.Ordinal);
    }

    private static EvidenceObservation Create(
        long observedQuantityRawE8 = 1_000_000_000,
        long toleranceRawE8 = 0,
        int? observedPieceCount = null)
        => EvidenceObservation.Create(
            Guid.NewGuid(),
            EvidenceId,
            PortfolioId,
            AccountId,
            AssetId,
            observedQuantityRawE8,
            toleranceRawE8,
            observedPieceCount,
            RecordedAt);
}
