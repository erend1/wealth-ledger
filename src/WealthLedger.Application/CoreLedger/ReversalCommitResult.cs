namespace WealthLedger.Application.CoreLedger
{
    public abstract record ReversalCommitResult
    {
        private ReversalCommitResult()
        {
        }

        public sealed record Committed(
            LedgerSubmissionReceipt Receipt)
            : ReversalCommitResult;

        public sealed record ReceiptWinner(
            LedgerSubmissionReceipt Receipt)
            : ReversalCommitResult;

        public sealed record AlreadyReversed(
            Guid ExistingReversalTransactionId)
            : ReversalCommitResult;

        public sealed record DependencyConflict(
            IReadOnlyList<Guid> BlockingTransactionIds)
            : ReversalCommitResult;
    }
}
