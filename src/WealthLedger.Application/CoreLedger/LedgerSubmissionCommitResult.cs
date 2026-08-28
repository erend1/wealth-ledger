namespace WealthLedger.Application.CoreLedger
{
    public sealed record LedgerSubmissionCommitResult(
        bool WasCommitted,
        LedgerSubmissionReceipt Receipt);
}
