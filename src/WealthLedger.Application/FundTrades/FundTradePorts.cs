using WealthLedger.Application.CoreLedger;
using WealthLedger.Domain.Ledger;
using WealthLedger.Domain.Lots;
using WealthLedger.Domain.ValueObjects;

namespace WealthLedger.Application.FundTrades;

/// <summary>
/// The selected household, portfolio, accounts and assets of one fund trade.
/// </summary>
public sealed record FundTradeScope(
    Guid HouseholdId,
    Guid PortfolioId,
    Guid FundAccountId,
    Guid CashAccountId,
    Guid FundAssetId,
    Guid CashAssetId);

/// <summary>
/// Reads derived custody for fund sales.
/// </summary>
/// <remarks>
/// Everything here is derived from posted entries and signed allocations. No
/// current-position, remaining-quantity or available-cash value is stored.
/// </remarks>
public interface IFundLotCustodyReadStore
{
    /// <summary>
    /// Lists the lots that currently hold fund quantity in the selected
    /// portfolio and account, with the exact quantity held in that scope.
    /// </summary>
    Task<IReadOnlyList<ScopedLotCandidate>> ListScopedLotCandidatesAsync(
        FundTradeScope scope,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Derives the current cash position for the selected cash account and
    /// asset, used to warn about a negative projected balance.
    /// </summary>
    Task<long> DeriveCashPositionRawE8Async(
        FundTradeScope scope,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Why a fund-sale commit did not succeed.
/// </summary>
public enum FundSaleCommitStatus
{
    Committed,

    /// <summary>An equivalent receipt already existed.</summary>
    AlreadyRecorded,

    /// <summary>
    /// Effective history changed after the plan was reviewed, so the lots or
    /// quantities about to be consumed are no longer the reviewed ones.
    /// </summary>
    StaleReviewedPlan,

    /// <summary>
    /// The reviewed quantity is no longer available in the selected scope.
    /// </summary>
    InsufficientQuantity,

    /// <summary>
    /// Posting would drive the derived cash position negative and the trade
    /// carries no note explaining the gap.
    /// </summary>
    NegativeCashNoteRequired
}

public sealed record FundSaleCommitResult(
    FundSaleCommitStatus Status,
    LedgerSubmissionReceipt? Receipt);

/// <summary>
/// Posts a fund trade atomically.
/// </summary>
/// <remarks>
/// Plan freshness cannot be enforced by a database trigger, because the
/// database has no knowledge of what a user was shown. It is arbitrated here,
/// inside the same transaction that writes the sale, while the database
/// independently guarantees non-negative global and scoped quantity.
///
/// The negative-cash note requirement is also evaluated here rather than at
/// preview, so neither a direct API caller nor a concurrent write can slip
/// past it.
/// </remarks>
public interface IFundTradePostingStore
{
    /// <summary>
    /// Posts a purchase with its single new acquisition lot.
    /// </summary>
    Task<FundSaleCommitResult> TryCommitPurchaseAsync(
        LedgerSubmissionReceipt receipt,
        LedgerTransaction transaction,
        AssetLot newLot,
        FundTradeScope scope,
        bool hasExplanatoryNote,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Posts a sale, re-checking the reviewed plan and scoped availability
    /// inside the write transaction before writing anything.
    /// </summary>
    Task<FundSaleCommitResult> TryCommitSaleAsync(
        LedgerSubmissionReceipt receipt,
        LedgerTransaction transaction,
        FundTradeScope scope,
        IReadOnlyList<ReviewedLotAllocation> reviewedPlan,
        bool hasExplanatoryNote,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Reads the effective allocation history needed to derive realized cost.
/// </summary>
public interface IFundRealizedCostReadStore
{
    /// <summary>
    /// Loads, for every lot the given sale consumed, the lot's original
    /// quantity, cost basis and complete effective disposal sequence.
    /// </summary>
    /// <remarks>
    /// The full sequence is required, not just this sale's share: cumulative
    /// apportionment depends on where the sale sits among all effective
    /// disposals of the same lot.
    /// </remarks>
    Task<IReadOnlyList<RealizedCostLotHistory>> ListEffectiveLotHistoryAsync(
        Guid householdId,
        Guid saleTransactionId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Loads the same history for lots named directly, so a sale can be
    /// projected before it exists.
    /// </summary>
    /// <remarks>
    /// Review needs exactly what posting will use. Without the existing
    /// disposal sequence a preview could only guess a proportional share,
    /// and would disagree with the receipt for any lot already partly sold.
    /// </remarks>
    Task<IReadOnlyList<RealizedCostLotHistory>> ListLotHistoryAsync(
        Guid householdId,
        IReadOnlyCollection<Guid> assetLotIds,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Reads a posted fund trade back for verification and receipts.
/// </summary>
public interface IFundTradeVerificationReadStore
{
    Task<FundTradePersistedFacts?> FindPostedFundTradeAsync(
        Guid householdId,
        Guid transactionId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Derives the current fund position for the scope a trade used.
    /// </summary>
    Task<long> DeriveFundPositionRawE8Async(
        FundTradeScope scope,
        CancellationToken cancellationToken = default);
}

public sealed record FundTradePersistedFacts(
    Guid TransactionId,
    Guid HouseholdId,
    TransactionType Type,
    TransactionStatus Status,
    DateOnly? OrderDate,
    DateOnly ExecutionDate,
    DateOnly? SettlementDate,
    DateTimeOffset PostedAtUtc,
    string? ExternalReference,
    string? Note,
    Guid? ReversedByTransactionId,
    FundTradeScope Scope,
    Quantity FundQuantity,
    UnitPrice ExecutedUnitPrice,
    Money CashConsideration,
    IReadOnlyList<FundTradeCostInput> Costs,
    IReadOnlyList<FundTradeAllocationFact> Allocations);

public sealed record FundTradeAllocationFact(
    Guid AssetLotId,
    Guid AllocationId,
    DateOnly? AcquiredOn,
    CostBasisStatus CostStatus,
    Money? LotCostBasis,
    Quantity Quantity);
