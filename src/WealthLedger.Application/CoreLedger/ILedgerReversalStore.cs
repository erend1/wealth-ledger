using WealthLedger.Domain.Ledger;
using WealthLedger.Domain.Lots;

namespace WealthLedger.Application.CoreLedger
{
    public interface ILedgerReversalStore
    {
        Task<ReversalTargetIdentity?> FindTargetIdentityAsync(
            Guid transactionId,
            CancellationToken cancellationToken = default);

        Task<LedgerSubmissionReceipt?> FindReceiptAsync(
            LedgerSubmissionScope scope,
            CancellationToken cancellationToken = default);

        Task<ReversalCandidate?> LoadCandidateAsync(
            Guid transactionId,
            CancellationToken cancellationToken = default);

        Task<ReversalCommitResult> TryCommitAsync(
            LedgerSubmissionReceipt receipt,
            LedgerTransaction reversal,
            IReadOnlyCollection<AssetLot> affectedLots,
            CancellationToken cancellationToken = default);
    }
}
