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

    /// <summary>
    /// The explicit resolution of an out-of-tolerance item. An item without a
    /// resolution is Open.
    /// </summary>
    public enum ReconciliationResolutionKind
    {
        CorrectedByTransaction,
        DocumentedException
    }

    /// <summary>
    /// The deterministic comparison method a run applied.
    /// </summary>
    public enum ReconciliationMethod
    {
        /// <summary>
        /// Observed quantity against the effective Posted quantity of the exact
        /// Portfolio/Account/Asset scope on the evidence effective date, plus
        /// whole pieces for physical gold when they were observed.
        /// </summary>
        ExactScopeQuantityAsOf
    }
}
