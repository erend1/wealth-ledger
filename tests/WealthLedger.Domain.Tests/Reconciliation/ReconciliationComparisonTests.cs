using WealthLedger.Domain.Reconciliation;

namespace WealthLedger.Domain.Tests.Reconciliation;

public sealed class ReconciliationComparisonTests
{
    [Fact]
    public void Compare_ExactEqualityMatchesWithZeroTolerance()
    {
        var result = ReconciliationComparison.Compare(
            150_000_000, 150_000_000, 0, null, null);

        Assert.Equal(0, result.DeltaRawE8);
        Assert.Null(result.PieceDelta);
        Assert.Equal(ReconciliationComparisonStatus.Matched, result.Status);
    }

    [Theory]
    [InlineData(1_000, 990, 10, ReconciliationComparisonStatus.Matched)]
    [InlineData(1_000, 1_010, 10, ReconciliationComparisonStatus.Matched)]
    [InlineData(1_000, 989, 10, ReconciliationComparisonStatus.OutOfTolerance)]
    [InlineData(1_000, 1_011, 10, ReconciliationComparisonStatus.OutOfTolerance)]
    [InlineData(1_000, 999, 0, ReconciliationComparisonStatus.OutOfTolerance)]
    public void Compare_ToleranceBoundaryIsInclusive(
        long observed,
        long ledger,
        long tolerance,
        ReconciliationComparisonStatus expected)
    {
        var result = ReconciliationComparison.Compare(
            observed, ledger, tolerance, null, null);

        Assert.Equal(observed - ledger, result.DeltaRawE8);
        Assert.Equal(expected, result.Status);
    }

    [Fact]
    public void Compare_DeltaIsObservedMinusLedgerAndKeepsItsSign()
    {
        var shortfall = ReconciliationComparison.Compare(-500, 250, 0, null, null);
        var surplus = ReconciliationComparison.Compare(250, -500, 0, null, null);

        Assert.Equal(-750, shortfall.DeltaRawE8);
        Assert.Equal(750, surplus.DeltaRawE8);
        Assert.Equal(ReconciliationComparisonStatus.OutOfTolerance, shortfall.Status);
        Assert.Equal(ReconciliationComparisonStatus.OutOfTolerance, surplus.Status);
    }

    [Fact]
    public void Compare_MaximumToleranceAndMinimumDeltaDoNotOverflow()
    {
        var result = ReconciliationComparison.Compare(
            long.MinValue + 1, 1, long.MaxValue, null, null);

        Assert.Equal(long.MinValue, result.DeltaRawE8);
        Assert.Equal(ReconciliationComparisonStatus.OutOfTolerance, result.Status);
    }

    [Fact]
    public void Compare_OverflowingDeltaThrows()
    {
        Assert.Throws<OverflowException>(
            () => ReconciliationComparison.Compare(long.MaxValue, -1, 0, null, null));
        Assert.Throws<OverflowException>(
            () => ReconciliationComparison.Compare(long.MinValue, 1, 0, null, null));
        Assert.Throws<OverflowException>(
            () => ReconciliationComparison.Compare(0, 0, 0, int.MaxValue, -1));
    }

    [Fact]
    public void Compare_PiecesMustMatchExactlyEvenWithinQuantityTolerance()
    {
        var pieceMismatch = ReconciliationComparison.Compare(1_000, 1_000, 500, 3, 2);
        var pieceMatch = ReconciliationComparison.Compare(1_000, 1_200, 500, 3, 3);
        var signedLedgerPieces = ReconciliationComparison.Compare(0, 0, 0, 0, -1);

        Assert.Equal(1, pieceMismatch.PieceDelta);
        Assert.Equal(ReconciliationComparisonStatus.OutOfTolerance, pieceMismatch.Status);
        Assert.Equal(0, pieceMatch.PieceDelta);
        Assert.Equal(ReconciliationComparisonStatus.Matched, pieceMatch.Status);
        Assert.Equal(1, signedLedgerPieces.PieceDelta);
        Assert.Equal(ReconciliationComparisonStatus.OutOfTolerance, signedLedgerPieces.Status);
    }

    [Fact]
    public void Compare_RejectsNegativeToleranceAndOneSidedPieces()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => ReconciliationComparison.Compare(0, 0, -1, null, null));
        Assert.Throws<ArgumentException>(
            () => ReconciliationComparison.Compare(0, 0, 0, 1, null));
        Assert.Throws<ArgumentException>(
            () => ReconciliationComparison.Compare(0, 0, 0, null, 1));
    }
}
