using Microsoft.EntityFrameworkCore;
using WealthLedger.Application.CoreLedger;
using WealthLedger.Application.FundTrades;
using WealthLedger.Domain.Ledger;
using WealthLedger.Domain.Lots;
using WealthLedger.Domain.ValueObjects;
using WealthLedger.Infrastructure.Persistence.Rows;

namespace WealthLedger.Infrastructure.Persistence;

/// <summary>
/// Fund-trade posting.
/// </summary>
/// <remarks>
/// These writes live on the existing posting store so they reuse one set of
/// mapping helpers and one transaction discipline, rather than growing a
/// second store that could drift from it.
///
/// Two checks run inside the write transaction rather than before it, because
/// both answer questions that another writer could change in between:
/// whether the reviewed FIFO plan is still the plan, and whether the resulting
/// cash position goes negative without an explanation.
/// </remarks>
public sealed partial class EfCoreLedgerPostingStore
{
    private static readonly LotAllocationService AllocationService = new();

    public async Task<FundSaleCommitResult> TryCommitPurchaseAsync(
        LedgerSubmissionReceipt receipt,
        LedgerTransaction transaction,
        AssetLot newLot,
        FundTradeScope scope,
        bool hasExplanatoryNote,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(receipt);
        ArgumentNullException.ThrowIfNull(transaction);
        ArgumentNullException.ThrowIfNull(newLot);
        ArgumentNullException.ThrowIfNull(scope);

        return await CommitFundTradeAsync(
            receipt,
            transaction,
            [newLot],
            additionalAllocations: [],
            scope,
            hasExplanatoryNote,
            recheckPlanAsync: null,
            cancellationToken);
    }

    public async Task<FundSaleCommitResult> TryCommitSaleAsync(
        LedgerSubmissionReceipt receipt,
        LedgerTransaction transaction,
        FundTradeScope scope,
        IReadOnlyList<ReviewedLotAllocation> reviewedPlan,
        bool hasExplanatoryNote,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(receipt);
        ArgumentNullException.ThrowIfNull(transaction);
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(reviewedPlan);

        var principal =
            transaction.Entries.Single(
                x => x.Role == EntryRole.Principal);

        var allocations =
            reviewedPlan
                .Select(line =>
                    new LotEntryAllocationRow
                    {
                        Id = Guid.NewGuid(),
                        AssetLotId = line.AssetLotId,
                        TransactionEntryId = principal.Id,
                        QuantityDeltaE8 =
                            checked(-line.Quantity.RawE8),
                        CreatedAtUtc =
                            transaction.CreatedAtUtc.UtcDateTime
                    })
                .ToArray();

        return await CommitFundTradeAsync(
            receipt,
            transaction,
            newLots: [],
            allocations,
            scope,
            hasExplanatoryNote,
            recheckPlanAsync:
                ct => RecheckPlanAsync(
                    scope,
                    reviewedPlan,
                    ct),
            cancellationToken);
    }

    private async Task<FundSaleCommitResult> CommitFundTradeAsync(
        LedgerSubmissionReceipt receipt,
        LedgerTransaction transaction,
        IReadOnlyCollection<AssetLot> newLots,
        IReadOnlyCollection<LotEntryAllocationRow> additionalAllocations,
        FundTradeScope scope,
        bool hasExplanatoryNote,
        Func<CancellationToken, Task<FundSaleCommitStatus>>? recheckPlanAsync,
        CancellationToken cancellationToken)
    {
        var graph =
            PrepareLedgerGraph(transaction, newLots);

        var receiptRow = MapReceipt(receipt);

        var writingReceipt = false;

        try
        {
            await using var databaseTransaction =
                await _dbContext.Database.BeginTransactionAsync(
                    cancellationToken);

            if (recheckPlanAsync is not null)
            {
                var planStatus =
                    await recheckPlanAsync(cancellationToken);

                if (planStatus != FundSaleCommitStatus.Committed)
                {
                    /*
                     * Nothing has been written yet, so abandoning the
                     * transaction here leaves no partial trade behind.
                     */
                    await databaseTransaction.RollbackAsync(
                        cancellationToken);

                    _dbContext.ChangeTracker.Clear();

                    return new FundSaleCommitResult(planStatus, null);
                }
            }

            if (!hasExplanatoryNote
                && await WouldOverdrawCashAsync(
                    scope,
                    transaction,
                    cancellationToken))
            {
                await databaseTransaction.RollbackAsync(
                    cancellationToken);

                _dbContext.ChangeTracker.Clear();

                return new FundSaleCommitResult(
                    FundSaleCommitStatus.NegativeCashNoteRequired,
                    null);
            }

            AddGraph(graph);

            _dbContext.LotEntryAllocations.AddRange(
                additionalAllocations);

            await _dbContext.SaveChangesAsync(cancellationToken);

            writingReceipt = true;

            _dbContext.CommandReceipts.Add(receiptRow);

            await _dbContext.SaveChangesAsync(cancellationToken);

            writingReceipt = false;

            MarkPosted(graph, transaction);

            await _dbContext.SaveChangesAsync(cancellationToken);

            await databaseTransaction.CommitAsync(cancellationToken);

            return new FundSaleCommitResult(
                FundSaleCommitStatus.Committed,
                receipt);
        }
        catch (DbUpdateException exception)
            when (writingReceipt
                  && IsUniqueConstraintViolation(exception))
        {
            /*
             * Another writer used the same key first. Its receipt is the
             * authoritative result, so this attempt reports the winner rather
             * than failing.
             */
            _dbContext.ChangeTracker.Clear();

            var winner =
                await FindReceiptAsync(
                    receipt.Scope,
                    cancellationToken);

            if (winner is not null)
            {
                return new FundSaleCommitResult(
                    FundSaleCommitStatus.AlreadyRecorded,
                    winner);
            }

            throw new CoreLedgerPersistenceException(
                "The fund trade collided with another writer but no committed receipt could be recovered.",
                exception);
        }
        catch (DbUpdateException exception)
            when (IsFundTradeGuardViolation(exception))
        {
            /*
             * A database guard refused the write. That means application
             * state and database state disagreed, which in practice means
             * another writer moved the holdings underneath this one.
             */
            _dbContext.ChangeTracker.Clear();

            return new FundSaleCommitResult(
                FundSaleCommitStatus.InsufficientQuantity,
                null);
        }
    }

    /// <summary>
    /// Recomputes the FIFO plan against current scoped custody and compares
    /// it with the plan the user reviewed.
    /// </summary>
    private async Task<FundSaleCommitStatus> RecheckPlanAsync(
        FundTradeScope scope,
        IReadOnlyList<ReviewedLotAllocation> reviewedPlan,
        CancellationToken cancellationToken)
    {
        var requested =
            reviewedPlan.Aggregate(
                0L,
                (total, line) =>
                    checked(total + line.Quantity.RawE8));

        var candidates =
            await ReadScopedLotCandidatesAsync(
                scope,
                cancellationToken);

        IReadOnlyList<LotAllocationPlanItem> recomputed;

        try
        {
            recomputed =
                AllocationService.PlanScopedFifo(
                    scope.FundAssetId,
                    Quantity.FromRaw(requested),
                    candidates);
        }
        catch (Domain.Common.DomainRuleViolationException)
        {
            return FundSaleCommitStatus.InsufficientQuantity;
        }

        if (recomputed.Count != reviewedPlan.Count)
        {
            return FundSaleCommitStatus.StaleReviewedPlan;
        }

        for (var index = 0; index < recomputed.Count; index++)
        {
            if (recomputed[index].AssetLotId
                    != reviewedPlan[index].AssetLotId
                || recomputed[index].Quantity.RawE8
                    != reviewedPlan[index].Quantity.RawE8)
            {
                return FundSaleCommitStatus.StaleReviewedPlan;
            }
        }

        return FundSaleCommitStatus.Committed;
    }

    /// <summary>
    /// States whether posting this trade drives the derived cash position
    /// below zero.
    /// </summary>
    private async Task<bool> WouldOverdrawCashAsync(
        FundTradeScope scope,
        LedgerTransaction transaction,
        CancellationToken cancellationToken)
    {
        var current =
            await ReadCashPositionRawE8Async(
                scope,
                cancellationToken);

        long effect = 0;

        foreach (var entry in transaction.Entries)
        {
            if (entry.AssetId == scope.CashAssetId
                && entry.AccountId == scope.CashAccountId)
            {
                effect = checked(
                    effect + entry.QuantityDelta.RawE8);
            }
        }

        return checked(current + effect) < 0;
    }

    internal async Task<IReadOnlyList<ScopedLotCandidate>>
        ReadScopedLotCandidatesAsync(
            FundTradeScope scope,
            CancellationToken cancellationToken)
    {
        /*
         * Availability is derived from allocations whose entries sit inside
         * the selected portfolio and account. A lot opened in another account
         * therefore contributes nothing here, which is what stops a sale from
         * consuming custody it does not hold.
         */
        var rows =
            await (
                from allocation in _dbContext.LotEntryAllocations
                join lot in _dbContext.AssetLots
                    on allocation.AssetLotId equals lot.Id
                join entry in _dbContext.TransactionEntries
                    on allocation.TransactionEntryId equals entry.Id
                join tx in _dbContext.LedgerTransactions
                    on entry.TransactionId equals tx.Id
                where lot.AssetId == scope.FundAssetId
                      && tx.HouseholdId == scope.HouseholdId
                      && tx.Status == TransactionStatus.Posted
                      && entry.PortfolioId == scope.PortfolioId
                      && entry.AccountId == scope.FundAccountId
                      && entry.AssetId == scope.FundAssetId
                group new { allocation, lot } by new
                {
                    lot.Id,
                    lot.AssetId,
                    lot.AcquiredOn,
                    lot.OriginalCostBasisMinor,
                    lot.CostBasisCurrencyCode,
                    lot.CostBasisStatus,
                    lot.CreatedAtUtc
                }
                into grouped
                select new
                {
                    grouped.Key,
                    Available =
                        grouped.Sum(x =>
                            x.allocation.QuantityDeltaE8)
                })
                .ToListAsync(cancellationToken);

        return rows
            .Where(row => row.Available > 0)
            .Select(row =>
                new ScopedLotCandidate(
                    row.Key.Id,
                    row.Key.AssetId,
                    row.Key.AcquiredOn,
                    new DateTimeOffset(
                        DateTime.SpecifyKind(
                            row.Key.CreatedAtUtc,
                            DateTimeKind.Utc)),
                    Quantity.FromRaw(row.Available),
                    MapCostBasis(
                        row.Key.CostBasisStatus,
                        row.Key.OriginalCostBasisMinor,
                        row.Key.CostBasisCurrencyCode)))
            .ToList();
    }

    internal async Task<long> ReadCashPositionRawE8Async(
        FundTradeScope scope,
        CancellationToken cancellationToken)
    {
        var entries =
            await (
                from entry in _dbContext.TransactionEntries
                join tx in _dbContext.LedgerTransactions
                    on entry.TransactionId equals tx.Id
                where tx.HouseholdId == scope.HouseholdId
                      && tx.Status == TransactionStatus.Posted
                      && entry.PortfolioId == scope.PortfolioId
                      && entry.AccountId == scope.CashAccountId
                      && entry.AssetId == scope.CashAssetId
                select entry.QuantityDeltaE8)
                .ToListAsync(cancellationToken);

        long total = 0;

        foreach (var delta in entries)
        {
            total = checked(total + delta);
        }

        return total;
    }

    internal static CostBasis MapCostBasis(
        CostBasisStatus status,
        long? amountMinor,
        string? currencyCode)
        => status switch
        {
            CostBasisStatus.Known =>
                CostBasis.Known(
                    Money.FromMinorUnits(
                        amountMinor
                        ?? throw new CoreLedgerPersistenceException(
                            "A known cost basis must persist an amount."),
                        new CurrencyCode(
                            currencyCode
                            ?? throw new CoreLedgerPersistenceException(
                                "A known cost basis must persist a currency.")))),

            CostBasisStatus.Unknown => CostBasis.Unknown(),

            _ => CostBasis.NotApplicable()
        };

    /// <summary>
    /// Recognizes a refusal raised by the M008 database guards.
    /// </summary>
    private static bool IsFundTradeGuardViolation(
        Exception exception)
    {
        for (var current = exception;
             current is not null;
             current = current.InnerException)
        {
            if (current.Message.Contains(
                    "FUND_TRADE_",
                    StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }
}
