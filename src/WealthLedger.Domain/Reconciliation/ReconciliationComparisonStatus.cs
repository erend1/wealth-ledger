namespace WealthLedger.Domain.Reconciliation
{
    /// <summary>
    /// Whether an item's difference lies within its recorded tolerance.
    /// </summary>
    /// <remarks>
    /// Matched means only that; it says nothing about cost or valuation
    /// completeness. A resolved difference stays OutOfTolerance.
    /// </remarks>
    public enum ReconciliationComparisonStatus
    {
        Matched,
        OutOfTolerance
    }
}
