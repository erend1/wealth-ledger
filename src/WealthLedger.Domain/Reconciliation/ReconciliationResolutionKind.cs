namespace WealthLedger.Domain.Reconciliation
{
    /// <summary>
    /// The explicit resolution of an out-of-tolerance item. An item without a
    /// resolution is Open.
    /// </summary>
    public enum ReconciliationResolutionKind
    {
        CorrectedByTransaction,
        DocumentedException
    }
}
