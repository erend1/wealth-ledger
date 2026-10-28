namespace WealthLedger.Domain.Reconciliation
{
    /// <summary>
    /// The deterministic quantity comparison of one observed scope.
    /// </summary>
    /// <remarks>
    /// <c>delta = observed - ledger</c>, and an item matches only when
    /// <c>abs(delta) &lt;= tolerance</c>. Physical-gold pieces, when observed,
    /// must match exactly: their tolerance is always zero. Arithmetic is
    /// checked, so an overflow throws instead of producing a wrong delta.
    /// </remarks>
    public static class ReconciliationComparison
    {
        public static ReconciliationComparisonResult Compare(
            long observedQuantityRawE8,
            long ledgerQuantityRawE8,
            long toleranceRawE8,
            int? observedPieceCount,
            int? ledgerPieceCount)
        {
            if (toleranceRawE8 < 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(toleranceRawE8),
                    "A comparison tolerance cannot be negative.");
            }

            if (observedPieceCount is null != ledgerPieceCount is null)
            {
                throw new ArgumentException(
                    "Pieces are compared only when both sides carry a piece count.",
                    nameof(ledgerPieceCount));
            }

            var deltaRawE8 = checked(observedQuantityRawE8 - ledgerQuantityRawE8);
            int? pieceDelta = observedPieceCount is int observedPieces
                ? checked(observedPieces - ledgerPieceCount!.Value)
                : null;

            // Comparing against both bounds avoids abs(long.MinValue).
            var matched = deltaRawE8 >= -toleranceRawE8
                          && deltaRawE8 <= toleranceRawE8
                          && pieceDelta is null or 0;

            return new ReconciliationComparisonResult(
                deltaRawE8,
                pieceDelta,
                matched
                    ? ReconciliationComparisonStatus.Matched
                    : ReconciliationComparisonStatus.OutOfTolerance);
        }
    }

    public sealed record ReconciliationComparisonResult(
        long DeltaRawE8,
        int? PieceDelta,
        ReconciliationComparisonStatus Status)
    {
        public override string ToString() => nameof(ReconciliationComparisonResult);
    }
}
