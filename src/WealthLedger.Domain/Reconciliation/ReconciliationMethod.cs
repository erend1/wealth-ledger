namespace WealthLedger.Domain.Reconciliation
{
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
