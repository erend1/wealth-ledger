using WealthLedger.Domain.Ledger;
using WealthLedger.Domain.Lots;

namespace WealthLedger.Application.CoreLedger
{
    public interface ILedgerSubmissionStore
    {
        Task<LedgerSubmissionReceipt?> FindReceiptAsync(
            LedgerSubmissionScope scope,
            CancellationToken cancellationToken = default);

        Task<LedgerSubmissionCommitResult> TryCommitAsync(
            LedgerSubmissionReceipt receipt,
            LedgerTransaction transaction,
            IReadOnlyCollection<AssetLot> newLots,
            CancellationToken cancellationToken = default);
    }
}
