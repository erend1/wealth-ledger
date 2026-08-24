using WealthLedger.Domain.Ledger;
using WealthLedger.Domain.Lots;

namespace WealthLedger.Application.CoreLedger;

public interface ILedgerPostingStore
{
    Task SavePostedTransactionAsync(
        LedgerTransaction transaction,
        IReadOnlyCollection<AssetLot> newLots,
        CancellationToken cancellationToken = default);
}
