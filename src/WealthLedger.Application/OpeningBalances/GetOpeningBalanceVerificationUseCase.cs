using WealthLedger.Application.CoreLedger;
using WealthLedger.Application.Positions;
using WealthLedger.Domain.Ledger;

namespace WealthLedger.Application.OpeningBalances;

public sealed record GetOpeningBalanceVerificationQuery(
    Guid HouseholdId,
    Guid TransactionId);

public sealed record OpeningBalanceVerification(
    LedgerTransactionDetail Transaction,
    long PersistedQuantityRawE8,
    long AllocationTotalRawE8,
    bool AllocationsReconcile,
    long CurrentPositionRawE8,
    int PositionSourceEntryCount,
    bool CurrentPositionEqualsOpeningQuantity,
    bool HasAdditionalEffectiveHistory,
    bool IsIndependentlyReconciled,
    IReadOnlyList<string> WarningCodes);

public sealed class GetOpeningBalanceVerificationUseCase
{
    internal const string PositionChangedWarning =
        "OPENING_BALANCE_POSITION_CHANGED_SINCE_POSTING";

    private readonly ILedgerTransactionReadStore _transactionReadStore;
    private readonly GetPositionUseCase _getPosition;

    public GetOpeningBalanceVerificationUseCase(
        ILedgerTransactionReadStore transactionReadStore,
        GetPositionUseCase getPosition)
    {
        _transactionReadStore = transactionReadStore
            ?? throw new ArgumentNullException(nameof(transactionReadStore));
        _getPosition = getPosition
            ?? throw new ArgumentNullException(nameof(getPosition));
    }

    public async Task<OpeningBalanceVerification> ExecuteAsync(
        GetOpeningBalanceVerificationQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        if (query.HouseholdId == Guid.Empty
            || query.TransactionId == Guid.Empty)
        {
            throw NotFound();
        }

        var transaction = await _transactionReadStore.FindByIdAsync(
            query.TransactionId,
            cancellationToken);

        if (transaction is null
            || transaction.HouseholdId != query.HouseholdId
            || transaction.Type != TransactionType.OpeningBalance
            || transaction.Status != TransactionStatus.Posted)
        {
            throw NotFound();
        }

        var entry = transaction.Entries.SingleOrDefault()
            ?? throw new InvalidOperationException(
                "The persisted opening balance does not contain exactly one entry.");

        long allocationTotalRawE8 = 0;

        foreach (var allocation in transaction.LotAllocations)
        {
            if (allocation.TransactionEntryId != entry.EntryId)
            {
                throw new InvalidOperationException(
                    "The persisted opening balance contains an unrelated allocation.");
            }

            allocationTotalRawE8 = checked(
                allocationTotalRawE8 + allocation.QuantityDeltaRawE8);
        }

        var hasLots = transaction.CreatedLots.Count > 0;
        var allocationsReconcile = hasLots
            ? transaction.CreatedLots.Count == transaction.LotAllocations.Count
              && allocationTotalRawE8 == entry.QuantityDeltaRawE8
            : transaction.LotAllocations.Count == 0;

        if (!allocationsReconcile)
        {
            throw new InvalidOperationException(
                "The persisted opening-balance allocations do not reconcile.");
        }

        var position = await _getPosition.ExecuteAsync(
            new GetPositionQuery(
                transaction.HouseholdId,
                entry.PortfolioId,
                entry.AccountId,
                entry.AssetId),
            cancellationToken);
        var hasAdditionalHistory = position.SourceEntryCount > 1;
        var warnings = new List<string>
        {
            OpeningBalanceCommandEvaluator
                .IndependentReconciliationRequiredWarning
        };

        if (hasAdditionalHistory)
        {
            warnings.Add(PositionChangedWarning);
        }

        return new OpeningBalanceVerification(
            transaction,
            entry.QuantityDeltaRawE8,
            allocationTotalRawE8,
            allocationsReconcile,
            position.Quantity.RawE8,
            position.SourceEntryCount,
            position.Quantity.RawE8 == entry.QuantityDeltaRawE8,
            hasAdditionalHistory,
            IsIndependentlyReconciled: false,
            warnings);
    }

    private static OpeningBalanceException NotFound()
        => new(
            OpeningBalanceErrorCategory.NotFound,
            OpeningBalanceErrorCodes.NotFound,
            "The requested opening balance does not exist.");
}
