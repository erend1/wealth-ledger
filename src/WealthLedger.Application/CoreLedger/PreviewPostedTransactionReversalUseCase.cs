namespace WealthLedger.Application.CoreLedger
{
    public sealed class
        PreviewPostedTransactionReversalUseCase
    {
        private readonly ILedgerReversalStore
            _reversalStore;

        public PreviewPostedTransactionReversalUseCase(
            ILedgerReversalStore reversalStore)
        {
            _reversalStore =
                reversalStore
                ?? throw new ArgumentNullException(
                    nameof(reversalStore));
        }

        public async Task<ReversalPreviewResult?> ExecuteAsync(
            Guid originalTransactionId,
            CancellationToken cancellationToken = default)
        {
            if (originalTransactionId == Guid.Empty)
            {
                throw new ArgumentException(
                    "Original transaction ID cannot be empty.",
                    nameof(originalTransactionId));
            }

            var candidate =
                await _reversalStore.LoadCandidateAsync(
                    originalTransactionId,
                    cancellationToken);

            if (candidate is null)
            {
                return null;
            }

            if (candidate.TransactionId
                != originalTransactionId)
            {
                throw new InvalidOperationException(
                    "The reversal store returned a candidate for a different transaction.");
            }

            var evaluation =
                ReversalEligibilityEvaluator.Evaluate(
                    candidate);

            return new ReversalPreviewResult(
                originalTransactionId,
                evaluation.CanReverse,
                evaluation.Code,
                evaluation.ExistingReversalTransactionId,
                evaluation.BlockingTransactionIds,
                evaluation.InverseEntries,
                evaluation.InverseLotAllocations);
        }
    }
}
