namespace WealthLedger.Application.CoreLedger
{
    public enum ReversalEligibilityCode
    {
        Eligible,
        NotPosted,
        TargetIsReversal,
        AlreadyReversed,
        BlockedByDependencies,
        UnsupportedPersistedShape
    }
}
