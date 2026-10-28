using Microsoft.EntityFrameworkCore;
using WealthLedger.Application.Evidence;
using WealthLedger.Application.Reconciliation;
using WealthLedger.Domain.Reconciliation;

namespace WealthLedger.Infrastructure.Persistence;

/// <summary>
/// Household-scoped reconciliation reads. They return stored snapshots only;
/// the Application re-derives the ledger side to detect staleness.
/// </summary>
public sealed class EfCoreReconciliationReadStore : IReconciliationReadStore
{
    private readonly WealthLedgerDbContext _dbContext;

    public EfCoreReconciliationReadStore(WealthLedgerDbContext dbContext)
    {
        _dbContext = dbContext
            ?? throw new ArgumentNullException(nameof(dbContext));
    }

    public Task<bool> HouseholdExistsAsync(
        Guid householdId,
        CancellationToken cancellationToken = default)
        => _dbContext.Households
            .AsNoTracking()
            .AnyAsync(row => row.Id == householdId, cancellationToken);

    public Task<bool> EvidenceExistsAsync(
        Guid householdId,
        Guid evidenceId,
        CancellationToken cancellationToken = default)
        => _dbContext.EvidenceRecords
            .AsNoTracking()
            .AnyAsync(
                row => row.Id == evidenceId && row.HouseholdId == householdId,
                cancellationToken);

    public async Task<IReadOnlyList<ReconciliationRunSummary>> ListRunsAsync(
        Guid householdId,
        Guid? evidenceId,
        int take,
        ReconciliationRunCursorKey? after,
        CancellationToken cancellationToken = default)
    {
        if (take < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(take));
        }

        // A run carries its evidence's household (a migration 010 guard), so
        // the evidence filter seeks its own index; the household of every row
        // is still checked below.
        var runs = evidenceId is Guid evidence
            ? _dbContext.ReconciliationRuns
                .AsNoTracking()
                .Where(run => run.EvidenceId == evidence)
            : _dbContext.ReconciliationRuns
                .AsNoTracking()
                .Where(run => run.HouseholdId == householdId);

        if (after is not null)
        {
            var afterRunAtUtc = after.RunAtUtc.UtcDateTime;

            runs = runs.Where(
                run => run.RunAtUtc < afterRunAtUtc
                       || (run.RunAtUtc == afterRunAtUtc
                           && run.Id.CompareTo(after.ReconciliationRunId) < 0));
        }

        var rows = await runs
            .OrderByDescending(run => run.RunAtUtc)
            .ThenByDescending(run => run.Id)
            .Take(take)
            .Select(run => new
            {
                run.Id,
                run.HouseholdId,
                run.EvidenceId,
                run.EvidenceEffectiveDate,
                run.RunAtUtc,
                run.Method,
                run.MethodVersion,
                run.ItemCount,
                MatchedCount = _dbContext.ReconciliationItems.Count(
                    item => item.RunId == run.Id
                            && item.ComparisonStatus == ReconciliationComparisonStatus.Matched),
                OutOfToleranceCount = _dbContext.ReconciliationItems.Count(
                    item => item.RunId == run.Id
                            && item.ComparisonStatus == ReconciliationComparisonStatus.OutOfTolerance),
                // Counted through the run's items, so each lookup stays on
                // the run index and the resolution key.
                CorrectedCount = _dbContext.ReconciliationItems.Count(
                    item => item.RunId == run.Id
                            && _dbContext.ReconciliationItemResolutions.Any(
                                resolution => resolution.ItemId == item.Id
                                              && resolution.Kind == ReconciliationResolutionKind.CorrectedByTransaction)),
                DocumentedCount = _dbContext.ReconciliationItems.Count(
                    item => item.RunId == run.Id
                            && _dbContext.ReconciliationItemResolutions.Any(
                                resolution => resolution.ItemId == item.Id
                                              && resolution.Kind == ReconciliationResolutionKind.DocumentedException))
            })
            .ToListAsync(cancellationToken);

        return rows
            .Select(row =>
            {
                var openCount = row.OutOfToleranceCount - row.CorrectedCount - row.DocumentedCount;

                if (row.HouseholdId != householdId
                    || row.MatchedCount + row.OutOfToleranceCount != row.ItemCount
                    || openCount < 0)
                {
                    throw new ReconciliationPersistenceException(
                        "A persisted reconciliation run disagrees with its items.");
                }

                return new ReconciliationRunSummary(
                    row.Id,
                    row.HouseholdId,
                    row.EvidenceId,
                    row.EvidenceEffectiveDate,
                    ToUtc(row.RunAtUtc),
                    row.Method,
                    row.MethodVersion,
                    row.ItemCount,
                    row.MatchedCount,
                    row.OutOfToleranceCount,
                    openCount,
                    row.CorrectedCount,
                    row.DocumentedCount);
            })
            .ToArray();
    }

    public async Task<StoredReconciliationRun?> GetRunAsync(
        Guid householdId,
        Guid reconciliationRunId,
        CancellationToken cancellationToken = default)
    {
        var run = await _dbContext.ReconciliationRuns
            .AsNoTracking()
            .SingleOrDefaultAsync(
                row => row.Id == reconciliationRunId
                       && row.HouseholdId == householdId,
                cancellationToken);

        if (run is null)
        {
            return null;
        }

        var rows = await (
                from item in _dbContext.ReconciliationItems.AsNoTracking()
                join portfolio in _dbContext.Portfolios.AsNoTracking()
                    on item.PortfolioId equals portfolio.Id
                join account in _dbContext.Accounts.AsNoTracking()
                    on item.AccountId equals account.Id
                join asset in _dbContext.Assets.AsNoTracking()
                    on item.AssetId equals asset.Id
                join institution in _dbContext.Institutions.AsNoTracking()
                    on account.InstitutionId equals (Guid?)institution.Id
                    into accountInstitutions
                from institution in accountInstitutions.DefaultIfEmpty()
                join resolution in _dbContext.ReconciliationItemResolutions.AsNoTracking()
                    on item.Id equals resolution.ItemId
                    into itemResolutions
                from resolution in itemResolutions.DefaultIfEmpty()
                where item.RunId == reconciliationRunId
                orderby item.Sequence
                select new
                {
                    Item = item,
                    Scope = new EvidenceObservationScope(
                        portfolio.Id,
                        portfolio.Code,
                        portfolio.Name,
                        account.Id,
                        account.Code,
                        account.Name,
                        institution == null ? null : (Guid?)institution.Id,
                        institution == null ? null : institution.Code,
                        institution == null ? null : institution.Name,
                        asset.Id,
                        asset.Code,
                        asset.Name,
                        asset.Type,
                        asset.BaseUnit),
                    Resolution = resolution
                })
            .ToListAsync(cancellationToken);

        if (rows.Count != run.ItemCount)
        {
            throw new ReconciliationPersistenceException(
                "A persisted reconciliation run disagrees with its items.");
        }

        return new StoredReconciliationRun(
            run.Id,
            run.HouseholdId,
            run.EvidenceId,
            run.EvidenceEffectiveDate,
            ToUtc(run.RunAtUtc),
            run.Method,
            run.MethodVersion,
            rows
                .Select(row => new StoredReconciliationItem(
                    row.Item.Id,
                    row.Item.Sequence,
                    row.Item.ObservationId,
                    row.Scope,
                    row.Item.ObservedQuantityE8,
                    row.Item.LedgerQuantityE8,
                    row.Item.DeltaE8,
                    row.Item.ToleranceE8,
                    row.Item.ObservedPieceCount,
                    row.Item.LedgerPieceCount,
                    row.Item.PieceDelta,
                    row.Item.ComparisonStatus,
                    row.Resolution?.Kind,
                    row.Resolution?.CorrectiveTransactionId,
                    row.Resolution?.Explanation,
                    row.Resolution is null ? null : ToUtc(row.Resolution.ResolvedAtUtc)))
                .ToArray());
    }

    private static DateTimeOffset ToUtc(DateTime value)
        => new(DateTime.SpecifyKind(value, DateTimeKind.Utc));
}
