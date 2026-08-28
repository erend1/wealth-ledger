using WealthLedger.Domain.Ledger;
using WealthLedger.Domain.Lots;

namespace WealthLedger.Application.CoreLedger
{
    public sealed record LedgerTransactionDetail(
        Guid TransactionId,
        Guid HouseholdId,
        TransactionType Type,
        TransactionStatus Status,
        DateOnly? OrderDate,
        DateOnly? ExecutionDate,
        DateOnly? SettlementDate,
        string? ExternalReference,
        string? Note,
        Guid? ReversalOfTransactionId,
        DateTimeOffset CreatedAtUtc,
        DateTimeOffset? PostedAtUtc,
        IReadOnlyList<LedgerTransactionEntryDetail> Entries,
        LedgerTransactionCashFlowDetail? CashFlow,
        IReadOnlyList<LedgerTransactionCostDetail> Costs,
        IReadOnlyList<LedgerTransactionCreatedLotDetail> CreatedLots);

    public sealed record LedgerTransactionEntryDetail(
        Guid EntryId,
        int EntrySequence,
        Guid PortfolioId,
        Guid AccountId,
        Guid AssetId,
        long QuantityDeltaRawE8,
        EntryRole Role,
        long? UnitPriceRawE8,
        string? PriceCurrencyCode,
        DateTimeOffset CreatedAtUtc);

    public sealed record LedgerTransactionCashFlowDetail(
        CashFlowCategory Category,
        Guid? HouseholdMemberId);

    public sealed record LedgerTransactionCostDetail(
        Guid CostId,
        CostType Type,
        CostTreatment Treatment,
        long AmountMinorUnits,
        string CurrencyCode,
        string? Note);

    public sealed record LedgerTransactionCreatedLotDetail(
        Guid AssetLotId,
        Guid AssetId,
        Guid OpeningTransactionEntryId,
        DateOnly? AcquiredOn,
        long? OriginalCostBasisMinorUnits,
        string? CostBasisCurrencyCode,
        CostBasisStatus CostBasisStatus,
        DateTimeOffset CreatedAtUtc);
}
