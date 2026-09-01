using WealthLedger.Domain.ValueObjects;

namespace WealthLedger.Domain.Ledger
{
    public sealed record LedgerTransactionCostSnapshot(
        Guid Id,
        CostType Type,
        CostTreatment Treatment,
        Money Amount,
        string? Note);
}
