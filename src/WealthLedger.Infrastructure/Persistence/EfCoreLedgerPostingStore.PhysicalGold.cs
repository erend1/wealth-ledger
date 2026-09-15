using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using WealthLedger.Application.CoreLedger;
using WealthLedger.Application.PhysicalGold;
using WealthLedger.Domain.Ledger;
using WealthLedger.Domain.Lots;
using WealthLedger.Domain.ValueObjects;
using WealthLedger.Infrastructure.Persistence.Rows;

namespace WealthLedger.Infrastructure.Persistence;

public sealed partial class EfCoreLedgerPostingStore
{
    public async Task<PhysicalGoldCommitResult> TryCommitPurchaseAsync(
        LedgerSubmissionReceipt receipt,
        LedgerTransaction transaction,
        AssetLot newLot,
        PhysicalGoldTradeScope scope,
        bool hasExplanatoryNote,
        CancellationToken cancellationToken = default)
        => await CommitPhysicalGoldAsync(
            receipt,
            transaction,
            [newLot],
            [],
            [],
            scope.HouseholdId,
            scope.PortfolioId,
            scope.CashAccountId,
            scope.CashAssetId,
            hasExplanatoryNote,
            recheckPlanAsync: null,
            cancellationToken);

    public async Task<PhysicalGoldCommitResult> TryCommitSaleAsync(
        LedgerSubmissionReceipt receipt,
        LedgerTransaction transaction,
        PhysicalGoldTradeScope scope,
        IReadOnlyList<PhysicalGoldSelectedLot> reviewedPlan,
        bool hasExplanatoryNote,
        CancellationToken cancellationToken = default)
    {
        var principal = transaction.Entries.Single(
            x => x.Role == EntryRole.Principal
                 && x.AssetId == scope.GoldAssetId);
        var (allocations, details) = BuildDisposalAllocations(
            principal,
            reviewedPlan,
            transaction.CreatedAtUtc);

        return await CommitPhysicalGoldAsync(
            receipt,
            transaction,
            [],
            allocations,
            details,
            scope.HouseholdId,
            scope.PortfolioId,
            scope.CashAccountId,
            scope.CashAssetId,
            hasExplanatoryNote,
            ct => RecheckPhysicalPlanAsync(
                new PhysicalGoldCustodyScope(
                    scope.HouseholdId,
                    scope.PortfolioId,
                    scope.GoldAccountId,
                    scope.GoldAssetId),
                reviewedPlan,
                ct),
            cancellationToken);
    }

    public async Task<PhysicalGoldCommitResult> TryCommitTransferAsync(
        LedgerSubmissionReceipt receipt,
        LedgerTransaction transaction,
        PhysicalGoldTransferScope scope,
        IReadOnlyList<PhysicalGoldSelectedLot> reviewedPlan,
        bool hasExplanatoryNote,
        CancellationToken cancellationToken = default)
    {
        var source = transaction.Entries.Single(
            x => x.AssetId == scope.GoldAssetId
                 && x.QuantityDelta.IsNegative);
        var destination = transaction.Entries.Single(
            x => x.AssetId == scope.GoldAssetId
                 && x.QuantityDelta.IsPositive);
        var allocations = new List<LotEntryAllocationRow>(
            reviewedPlan.Count * 2);
        var details = new List<PhysicalGoldLotAllocationDetailRow>(
            reviewedPlan.Count * 2);

        foreach (var line in reviewedPlan)
        {
            AddPhysicalAllocation(
                allocations,
                details,
                line.AssetLotId,
                source.Id,
                checked(-line.GrossWeight.RawE8),
                checked(-line.PieceCount),
                transaction.CreatedAtUtc);
            AddPhysicalAllocation(
                allocations,
                details,
                line.AssetLotId,
                destination.Id,
                line.GrossWeight.RawE8,
                line.PieceCount,
                transaction.CreatedAtUtc);
        }

        return await CommitPhysicalGoldAsync(
            receipt,
            transaction,
            [],
            allocations,
            details,
            scope.HouseholdId,
            scope.CashPortfolioId,
            scope.CashAccountId,
            scope.CashAssetId,
            hasExplanatoryNote,
            ct => RecheckPhysicalPlanAsync(
                new PhysicalGoldCustodyScope(
                    scope.HouseholdId,
                    scope.SourcePortfolioId,
                    scope.SourceGoldAccountId,
                    scope.GoldAssetId),
                reviewedPlan,
                ct),
            cancellationToken);
    }

    private async Task<PhysicalGoldCommitResult> CommitPhysicalGoldAsync(
        LedgerSubmissionReceipt receipt,
        LedgerTransaction transaction,
        IReadOnlyCollection<AssetLot> newLots,
        IReadOnlyCollection<LotEntryAllocationRow> additionalAllocations,
        IReadOnlyCollection<PhysicalGoldLotAllocationDetailRow>
            additionalPieceDetails,
        Guid householdId,
        Guid? cashPortfolioId,
        Guid? cashAccountId,
        Guid? cashAssetId,
        bool hasExplanatoryNote,
        Func<CancellationToken, Task<PhysicalGoldCommitStatus>>?
            recheckPlanAsync,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(receipt);
        ArgumentNullException.ThrowIfNull(transaction);
        ValidateReceipt(receipt, transaction, newLots);
        var graph = PrepareLedgerGraph(transaction, newLots);
        var receiptRow = MapReceipt(receipt);
        var writingReceipt = false;

        try
        {
            await using var databaseTransaction =
                await _dbContext.Database.BeginTransactionAsync(
                    cancellationToken);

            if (recheckPlanAsync is not null)
            {
                var planStatus = await recheckPlanAsync(cancellationToken);
                if (planStatus != PhysicalGoldCommitStatus.Committed)
                {
                    await databaseTransaction.RollbackAsync(cancellationToken);
                    _dbContext.ChangeTracker.Clear();
                    return new PhysicalGoldCommitResult(planStatus, null);
                }
            }

            if (!hasExplanatoryNote
                && cashPortfolioId is Guid portfolioId
                && cashAccountId is Guid accountId
                && cashAssetId is Guid assetId
                && await WouldOverdrawPhysicalCashAsync(
                    householdId,
                    portfolioId,
                    accountId,
                    assetId,
                    transaction,
                    cancellationToken))
            {
                await databaseTransaction.RollbackAsync(cancellationToken);
                _dbContext.ChangeTracker.Clear();
                return new PhysicalGoldCommitResult(
                    PhysicalGoldCommitStatus.NegativeCashNoteRequired,
                    null);
            }

            AddGraph(graph);
            _dbContext.LotEntryAllocations.AddRange(additionalAllocations);
            _dbContext.PhysicalGoldLotAllocationDetails.AddRange(
                additionalPieceDetails);
            await _dbContext.SaveChangesAsync(cancellationToken);
            writingReceipt = true;
            _dbContext.CommandReceipts.Add(receiptRow);
            await _dbContext.SaveChangesAsync(cancellationToken);
            writingReceipt = false;
            MarkPosted(graph, transaction);
            await _dbContext.SaveChangesAsync(cancellationToken);
            await databaseTransaction.CommitAsync(cancellationToken);
            return new PhysicalGoldCommitResult(
                PhysicalGoldCommitStatus.Committed,
                receipt);
        }
        catch (DbUpdateException exception)
            when (writingReceipt && IsUniqueConstraintViolation(exception))
        {
            _dbContext.ChangeTracker.Clear();
            var winner = await FindReceiptAsync(
                receipt.Scope, cancellationToken);
            if (winner is not null)
            {
                return new PhysicalGoldCommitResult(
                    PhysicalGoldCommitStatus.AlreadyRecorded,
                    winner);
            }

            throw new CoreLedgerPersistenceException(
                "The physical-gold submission collided with another writer but no receipt could be recovered.",
                exception);
        }
        catch (Exception exception)
            when (IsPhysicalGoldGuardViolation(exception))
        {
            _dbContext.ChangeTracker.Clear();
            var status = ContainsCode(exception, "PIECE")
                ? PhysicalGoldCommitStatus.InsufficientPieces
                : ContainsCode(exception, "QUANTITY")
                    || ContainsCode(exception, "GROSS")
                    ? PhysicalGoldCommitStatus.InsufficientGrossWeight
                    : PhysicalGoldCommitStatus.PersistenceConflict;
            return new PhysicalGoldCommitResult(status, null);
        }
        catch (Exception exception)
            when (exception is DbUpdateException or SqliteException)
        {
            _dbContext.ChangeTracker.Clear();
            return new PhysicalGoldCommitResult(
                PhysicalGoldCommitStatus.PersistenceConflict,
                null);
        }
    }

    private async Task<PhysicalGoldCommitStatus> RecheckPhysicalPlanAsync(
        PhysicalGoldCustodyScope scope,
        IReadOnlyList<PhysicalGoldSelectedLot> reviewedPlan,
        CancellationToken cancellationToken)
    {
        var current = await ReadPhysicalGoldScopedLotsAsync(
            scope, cancellationToken);
        var byId = current.ToDictionary(x => x.AssetLotId);
        foreach (var line in reviewedPlan)
        {
            if (!byId.TryGetValue(line.AssetLotId, out var candidate)
                || line.GrossWeight.RawE8
                    > candidate.ScopedGrossWeight.RawE8)
            {
                return PhysicalGoldCommitStatus.InsufficientGrossWeight;
            }

            if (line.PieceCount > candidate.ScopedPieceCount)
            {
                return PhysicalGoldCommitStatus.InsufficientPieces;
            }
        }

        return PhysicalGoldCommitStatus.Committed;
    }

    internal async Task<IReadOnlyList<PhysicalGoldCustodyLot>>
        ReadPhysicalGoldScopedLotsAsync(
            PhysicalGoldCustodyScope scope,
            CancellationToken cancellationToken)
    {
        var facts = await (
            from allocation in _dbContext.LotEntryAllocations.AsNoTracking()
            join pieceDetail in _dbContext
                    .PhysicalGoldLotAllocationDetails.AsNoTracking()
                on allocation.Id equals pieceDetail.LotEntryAllocationId
                into pieceDetails
            from pieceDetail in pieceDetails.DefaultIfEmpty()
            join lot in _dbContext.AssetLots.AsNoTracking()
                on allocation.AssetLotId equals lot.Id
            join goldDetail in _dbContext.PhysicalGoldLotDetails.AsNoTracking()
                on lot.Id equals goldDetail.AssetLotId
            join entry in _dbContext.TransactionEntries.AsNoTracking()
                on allocation.TransactionEntryId equals entry.Id
            join transaction in _dbContext.LedgerTransactions.AsNoTracking()
                on entry.TransactionId equals transaction.Id
            where lot.AssetId == scope.GoldAssetId
                  && transaction.HouseholdId == scope.HouseholdId
                  && transaction.Status == TransactionStatus.Posted
                  && entry.AssetId == scope.GoldAssetId
            select new
            {
                lot.Id,
                lot.AssetId,
                lot.AcquiredOn,
                lot.CreatedAtUtc,
                lot.CostBasisStatus,
                lot.OriginalCostBasisMinor,
                lot.CostBasisCurrencyCode,
                goldDetail.ActualFinenessPpm,
                goldDetail.PieceCount,
                goldDetail.Hallmark,
                goldDetail.CertificateReference,
                goldDetail.Note,
                allocation.QuantityDeltaE8,
                PieceDelta = pieceDetail == null
                    ? (int?)null
                    : pieceDetail.PieceDelta,
                IsScoped = entry.PortfolioId == scope.PortfolioId
                           && entry.AccountId == scope.GoldAccountId
            }).ToListAsync(cancellationToken);

        if (facts.Any(x => x.PieceDelta is null))
        {
            throw new CoreLedgerPersistenceException(
                "Persisted physical-gold allocation history lacks piece movement.");
        }

        var result = new List<PhysicalGoldCustodyLot>();
        foreach (var group in facts.GroupBy(x => new
                 {
                     x.Id,
                     x.AssetId,
                     x.AcquiredOn,
                     x.CreatedAtUtc,
                     x.CostBasisStatus,
                     x.OriginalCostBasisMinor,
                     x.CostBasisCurrencyCode,
                     x.ActualFinenessPpm,
                     x.PieceCount,
                     x.Hallmark,
                     x.CertificateReference,
                     x.Note
                 }))
        {
            long globalGross = 0;
            var globalPieces = 0;
            long scopedGross = 0;
            var scopedPieces = 0;
            foreach (var row in group)
            {
                globalGross = checked(globalGross + row.QuantityDeltaE8);
                globalPieces = checked(globalPieces + row.PieceDelta!.Value);
                if (row.IsScoped)
                {
                    scopedGross = checked(scopedGross + row.QuantityDeltaE8);
                    scopedPieces = checked(
                        scopedPieces + row.PieceDelta.Value);
                }
            }

            if (globalGross < 0 || globalPieces < 0
                || scopedGross < 0 || scopedPieces < 0)
            {
                throw new CoreLedgerPersistenceException(
                    "Persisted physical-gold custody has a negative balance.");
            }

            if (scopedGross == 0 && scopedPieces == 0)
            {
                continue;
            }

            result.Add(new PhysicalGoldCustodyLot(
                group.Key.Id,
                group.Key.AssetId,
                group.Key.AcquiredOn,
                new DateTimeOffset(DateTime.SpecifyKind(
                    group.Key.CreatedAtUtc, DateTimeKind.Utc)),
                MapCostBasis(
                    group.Key.CostBasisStatus,
                    group.Key.OriginalCostBasisMinor,
                    group.Key.CostBasisCurrencyCode),
                new Fineness(group.Key.ActualFinenessPpm),
                group.Key.PieceCount,
                group.Key.Hallmark,
                group.Key.CertificateReference,
                group.Key.Note,
                Quantity.FromRaw(scopedGross),
                scopedPieces,
                Quantity.FromRaw(globalGross),
                globalPieces));
        }

        return result.OrderBy(x => x.AssetLotId).ToArray();
    }

    internal async Task<long> ReadCashPositionRawE8Async(
        Guid householdId,
        Guid portfolioId,
        Guid accountId,
        Guid assetId,
        CancellationToken cancellationToken)
    {
        var deltas = await (
            from entry in _dbContext.TransactionEntries.AsNoTracking()
            join transaction in _dbContext.LedgerTransactions.AsNoTracking()
                on entry.TransactionId equals transaction.Id
            where transaction.HouseholdId == householdId
                  && transaction.Status == TransactionStatus.Posted
                  && entry.PortfolioId == portfolioId
                  && entry.AccountId == accountId
                  && entry.AssetId == assetId
            select entry.QuantityDeltaE8).ToListAsync(cancellationToken);
        long total = 0;
        foreach (var delta in deltas)
        {
            total = checked(total + delta);
        }

        return total;
    }

    private async Task<bool> WouldOverdrawPhysicalCashAsync(
        Guid householdId,
        Guid portfolioId,
        Guid accountId,
        Guid assetId,
        LedgerTransaction transaction,
        CancellationToken cancellationToken)
    {
        var current = await ReadCashPositionRawE8Async(
            householdId,
            portfolioId,
            accountId,
            assetId,
            cancellationToken);
        long effect = 0;
        foreach (var entry in transaction.Entries.Where(
                     x => x.PortfolioId == portfolioId
                          && x.AccountId == accountId
                          && x.AssetId == assetId))
        {
            effect = checked(effect + entry.QuantityDelta.RawE8);
        }

        return checked(current + effect) < 0;
    }

    private static (
        LotEntryAllocationRow[] Allocations,
        PhysicalGoldLotAllocationDetailRow[] Details)
        BuildDisposalAllocations(
            TransactionEntry principal,
            IReadOnlyList<PhysicalGoldSelectedLot> plan,
            DateTimeOffset createdAtUtc)
    {
        var allocations = new List<LotEntryAllocationRow>(plan.Count);
        var details = new List<PhysicalGoldLotAllocationDetailRow>(plan.Count);
        foreach (var line in plan)
        {
            AddPhysicalAllocation(
                allocations,
                details,
                line.AssetLotId,
                principal.Id,
                checked(-line.GrossWeight.RawE8),
                checked(-line.PieceCount),
                createdAtUtc);
        }

        return (allocations.ToArray(), details.ToArray());
    }

    private static void AddPhysicalAllocation(
        ICollection<LotEntryAllocationRow> allocations,
        ICollection<PhysicalGoldLotAllocationDetailRow> details,
        Guid lotId,
        Guid entryId,
        long quantityDelta,
        int pieceDelta,
        DateTimeOffset createdAtUtc)
    {
        var allocationId = Guid.NewGuid();
        allocations.Add(new LotEntryAllocationRow
        {
            Id = allocationId,
            AssetLotId = lotId,
            TransactionEntryId = entryId,
            QuantityDeltaE8 = quantityDelta,
            CreatedAtUtc = createdAtUtc.UtcDateTime
        });
        details.Add(new PhysicalGoldLotAllocationDetailRow
        {
            LotEntryAllocationId = allocationId,
            PieceDelta = pieceDelta
        });
    }

    private static bool IsPhysicalGoldGuardViolation(Exception exception)
        => ContainsCode(exception, "PHYSICAL_GOLD_")
           || ContainsCode(exception, "GOLD_");

    private static bool ContainsCode(Exception exception, string value)
    {
        for (Exception? current = exception;
             current is not null;
             current = current.InnerException)
        {
            if (current.Message.Contains(value, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }
}
