using WealthLedger.Domain.ValueObjects;

namespace WealthLedger.Domain.Ledger
{
    public sealed record LedgerTransactionEntrySnapshot(
        Guid Id,
        int Sequence,
        Guid PortfolioId,
        Guid AccountId,
        Guid AssetId,
        QuantityDelta QuantityDelta,
        EntryRole Role,
        UnitPrice? UnitPrice);
}
