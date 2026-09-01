namespace WealthLedger.Api.Contracts;

public sealed record LedgerTransactionResponse(
    Guid TransactionId,
    Guid HouseholdId,
    string TypeCode,
    string StatusCode,
    DateOnly? OrderDate,
    DateOnly? ExecutionDate,
    DateOnly? SettlementDate,
    string? ExternalReference,
    string? Note,
    Guid? ReversalOfTransactionId,
    Guid? ReversedByTransactionId,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? PostedAtUtc,
    IReadOnlyList<LedgerTransactionEntryResponse> Entries,
    LedgerTransactionCashFlowResponse? CashFlow,
    IReadOnlyList<LedgerTransactionCostResponse> Costs,
    IReadOnlyList<LedgerTransactionCreatedLotResponse> CreatedLots,
    IReadOnlyList<LedgerTransactionLotAllocationResponse> LotAllocations);

public sealed record LedgerTransactionEntryResponse(
    Guid EntryId,
    int EntrySequence,
    Guid PortfolioId,
    Guid AccountId,
    Guid AssetId,
    long QuantityDeltaRawE8,
    string RoleCode,
    long? UnitPriceRawE8,
    string? PriceCurrencyCode,
    DateTimeOffset CreatedAtUtc);

public sealed record LedgerTransactionCashFlowResponse(
    string CategoryCode,
    Guid? HouseholdMemberId);

public sealed record LedgerTransactionCostResponse(
    Guid CostId,
    string TypeCode,
    string TreatmentCode,
    long AmountMinorUnits,
    string CurrencyCode,
    string? Note);

public sealed record LedgerTransactionCreatedLotResponse(
    Guid AssetLotId,
    Guid AssetId,
    Guid OpeningTransactionEntryId,
    DateOnly? AcquiredOn,
    long? OriginalCostBasisMinorUnits,
    string? CostBasisCurrencyCode,
    string CostBasisStatusCode,
    DateTimeOffset CreatedAtUtc);

public sealed record LedgerTransactionLotAllocationResponse(
    Guid AllocationId,
    Guid AssetLotId,
    Guid TransactionEntryId,
    long QuantityDeltaRawE8,
    DateTimeOffset CreatedAtUtc);