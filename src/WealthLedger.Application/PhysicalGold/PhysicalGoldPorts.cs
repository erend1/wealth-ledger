using WealthLedger.Application.CoreLedger;
using WealthLedger.Domain.Ledger;
using WealthLedger.Domain.Lots;

namespace WealthLedger.Application.PhysicalGold;

/// <summary>
/// Reads current physical-gold custody derived from posted allocations.
/// </summary>
public interface IPhysicalGoldCustodyReadStore
{
    Task<IReadOnlyList<PhysicalGoldCustodyLot>> ListScopedLotsAsync(
        PhysicalGoldCustodyScope scope,
        CancellationToken cancellationToken = default);

    Task<long> DeriveCashPositionRawE8Async(
        Guid householdId,
        Guid portfolioId,
        Guid accountId,
        Guid assetId,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Reads effective lot history for physical-gold realized-cost derivation.
/// </summary>
public interface IPhysicalGoldRealizedCostReadStore
{
    Task<IReadOnlyList<RealizedCostLotHistory>> ListEffectiveLotHistoryAsync(
        Guid householdId,
        Guid saleTransactionId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<RealizedCostLotHistory>> ListLotHistoryAsync(
        Guid householdId,
        IReadOnlyCollection<Guid> assetLotIds,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Commits a complete physical-gold graph and receipt atomically.
/// </summary>
public interface IPhysicalGoldPostingStore
{
    Task<PhysicalGoldCommitResult> TryCommitPurchaseAsync(
        LedgerSubmissionReceipt receipt,
        LedgerTransaction transaction,
        AssetLot newLot,
        PhysicalGoldTradeScope scope,
        bool hasExplanatoryNote,
        CancellationToken cancellationToken = default);

    Task<PhysicalGoldCommitResult> TryCommitSaleAsync(
        LedgerSubmissionReceipt receipt,
        LedgerTransaction transaction,
        PhysicalGoldTradeScope scope,
        IReadOnlyList<PhysicalGoldSelectedLot> reviewedPlan,
        bool hasExplanatoryNote,
        CancellationToken cancellationToken = default);

    Task<PhysicalGoldCommitResult> TryCommitTransferAsync(
        LedgerSubmissionReceipt receipt,
        LedgerTransaction transaction,
        PhysicalGoldTransferScope scope,
        IReadOnlyList<PhysicalGoldSelectedLot> reviewedPlan,
        bool hasExplanatoryNote,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Reads only persisted, posted physical-gold activity facts.
/// </summary>
public interface IPhysicalGoldVerificationReadStore
{
    Task<PhysicalGoldActivityPersistedFacts?> FindPostedActivityAsync(
        Guid householdId,
        Guid transactionId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<PhysicalGoldCustodyPosition>> ListCustodyAsync(
        Guid householdId,
        Guid? transactionId = null,
        CancellationToken cancellationToken = default);
}
