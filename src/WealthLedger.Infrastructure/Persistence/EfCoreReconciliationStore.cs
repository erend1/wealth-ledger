using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using WealthLedger.Application.Reconciliation;
using WealthLedger.Domain.Reconciliation;
using WealthLedger.Infrastructure.Persistence.Rows;

namespace WealthLedger.Infrastructure.Persistence;

/// <summary>
/// Write-side reconciliation persistence. It inserts run, item and
/// resolution rows only, and never writes ledger rows.
/// </summary>
public sealed class EfCoreReconciliationStore : IReconciliationStore
{
    private const int SqliteConstraint = 19;
    private const int SqliteConstraintPrimaryKey = 1555;
    private const int SqliteConstraintUnique = 2067;

    private readonly WealthLedgerDbContext _dbContext;

    public EfCoreReconciliationStore(WealthLedgerDbContext dbContext)
    {
        _dbContext = dbContext
            ?? throw new ArgumentNullException(nameof(dbContext));
    }

    public Task<ReconciliationRunReference?> FindRunByKeyAsync(
        Guid householdId,
        string idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(idempotencyKey);

        return _dbContext.ReconciliationRuns
            .AsNoTracking()
            .Where(row => row.HouseholdId == householdId
                          && row.IdempotencyKey == idempotencyKey)
            .Select(row => new ReconciliationRunReference(
                row.Id,
                row.HouseholdId,
                row.EvidenceId))
            .SingleOrDefaultAsync(cancellationToken);
    }

    public async Task<ReconciliationRunCommitResult> TryCommitRunAsync(
        string idempotencyKey,
        ReconciliationRun run,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(idempotencyKey);
        ArgumentNullException.ThrowIfNull(run);

        try
        {
            await using (var transaction =
                         await _dbContext.Database.BeginTransactionAsync(
                             cancellationToken))
            {
                _dbContext.ReconciliationRuns.Add(new ReconciliationRunRow
                {
                    Id = run.Id,
                    HouseholdId = run.HouseholdId,
                    EvidenceId = run.EvidenceId,
                    EvidenceEffectiveDate = run.EvidenceEffectiveDate,
                    RunAtUtc = run.RunAtUtc.UtcDateTime,
                    Method = run.Method,
                    MethodVersion = run.MethodVersion,
                    ItemCount = run.Items.Count,
                    IdempotencyKey = idempotencyKey
                });

                // The item guard reads the run row, so it is written first.
                await _dbContext.SaveChangesAsync(cancellationToken);

                foreach (var item in run.Items)
                {
                    _dbContext.ReconciliationItems.Add(new ReconciliationItemRow
                    {
                        Id = item.Id,
                        RunId = item.RunId,
                        Sequence = item.Sequence,
                        ObservationId = item.ObservationId,
                        PortfolioId = item.PortfolioId,
                        AccountId = item.AccountId,
                        AssetId = item.AssetId,
                        ObservedQuantityE8 = item.ObservedQuantityRawE8,
                        LedgerQuantityE8 = item.LedgerQuantityRawE8,
                        DeltaE8 = item.DeltaRawE8,
                        ToleranceE8 = item.ToleranceRawE8,
                        ObservedPieceCount = item.ObservedPieceCount,
                        LedgerPieceCount = item.LedgerPieceCount,
                        PieceDelta = item.PieceDelta,
                        ComparisonStatus = item.ComparisonStatus
                    });
                }

                await _dbContext.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
            }

            _dbContext.ChangeTracker.Clear();

            return new ReconciliationRunCommitResult(
                WasCommitted: true,
                new ReconciliationRunReference(run.Id, run.HouseholdId, run.EvidenceId));
        }
        catch (Exception exception)
            when (exception is DbUpdateException or SqliteException)
        {
            _dbContext.ChangeTracker.Clear();

            // A committed run under the same retry key always wins.
            var winner = await FindRunByKeyAsync(
                run.HouseholdId,
                idempotencyKey,
                cancellationToken);

            if (winner is not null)
            {
                return new ReconciliationRunCommitResult(
                    WasCommitted: false,
                    winner);
            }

            throw TranslateWriteFailure(
                exception,
                "The reconciliation run could not be persisted atomically.");
        }
    }

    public async Task<ReconciliationItemReference?> FindItemAsync(
        Guid itemId,
        CancellationToken cancellationToken = default)
    {
        var row = await (
                from item in _dbContext.ReconciliationItems.AsNoTracking()
                join run in _dbContext.ReconciliationRuns.AsNoTracking()
                    on item.RunId equals run.Id
                join resolution in _dbContext.ReconciliationItemResolutions.AsNoTracking()
                    on item.Id equals resolution.ItemId
                    into itemResolutions
                from resolution in itemResolutions.DefaultIfEmpty()
                where item.Id == itemId
                select new
                {
                    item.Id,
                    item.RunId,
                    run.HouseholdId,
                    item.ComparisonStatus,
                    Resolution = resolution
                })
            .SingleOrDefaultAsync(cancellationToken);

        return row is null
            ? null
            : new ReconciliationItemReference(
                row.Id,
                row.RunId,
                row.HouseholdId,
                row.ComparisonStatus,
                row.Resolution is null ? null : ToResolution(row.Resolution));
    }

    public Task<ReconciliationTransactionReference?> FindTransactionAsync(
        Guid transactionId,
        CancellationToken cancellationToken = default)
        => _dbContext.LedgerTransactions
            .AsNoTracking()
            .Where(row => row.Id == transactionId)
            .Select(row => new ReconciliationTransactionReference(
                row.Id,
                row.HouseholdId,
                row.Status))
            .SingleOrDefaultAsync(cancellationToken);

    public async Task<ReconciliationResolutionCommitResult> TryAddResolutionAsync(
        ReconciliationItemResolution resolution,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(resolution);

        try
        {
            _dbContext.ReconciliationItemResolutions.Add(
                new ReconciliationItemResolutionRow
                {
                    ItemId = resolution.ItemId,
                    Kind = resolution.Kind,
                    CorrectiveTransactionId = resolution.CorrectiveTransactionId,
                    Explanation = resolution.Explanation,
                    ResolvedAtUtc = resolution.ResolvedAtUtc.UtcDateTime
                });

            await _dbContext.SaveChangesAsync(cancellationToken);
            _dbContext.ChangeTracker.Clear();

            return new ReconciliationResolutionCommitResult(
                WasCommitted: true,
                resolution);
        }
        catch (DbUpdateException exception)
            when (IsKeyCollision(exception))
        {
            _dbContext.ChangeTracker.Clear();

            var winner = await _dbContext.ReconciliationItemResolutions
                .AsNoTracking()
                .SingleOrDefaultAsync(
                    row => row.ItemId == resolution.ItemId,
                    cancellationToken);

            return winner is not null
                ? new ReconciliationResolutionCommitResult(
                    WasCommitted: false,
                    ToResolution(winner))
                : throw new ReconciliationPersistenceException(
                    "The reconciliation resolution collided with another writer but no stored resolution could be recovered.",
                    exception);
        }
        catch (Exception exception)
            when (exception is DbUpdateException or SqliteException)
        {
            _dbContext.ChangeTracker.Clear();

            throw TranslateWriteFailure(
                exception,
                "The reconciliation resolution could not be persisted.");
        }
    }

    private static ReconciliationItemResolution ToResolution(
        ReconciliationItemResolutionRow row)
    {
        var resolvedAtUtc = new DateTimeOffset(
            DateTime.SpecifyKind(row.ResolvedAtUtc, DateTimeKind.Utc));

        try
        {
            return row switch
            {
                {
                    Kind: ReconciliationResolutionKind.CorrectedByTransaction,
                    CorrectiveTransactionId: Guid transactionId,
                    Explanation: null
                } => ReconciliationItemResolution.CorrectedByTransaction(
                    row.ItemId,
                    transactionId,
                    resolvedAtUtc),
                {
                    Kind: ReconciliationResolutionKind.DocumentedException,
                    CorrectiveTransactionId: null,
                    Explanation: string explanation
                } => ReconciliationItemResolution.DocumentedException(
                    row.ItemId,
                    explanation,
                    resolvedAtUtc),
                _ => throw new ReconciliationPersistenceException(
                    "A persisted reconciliation resolution has an unsupported shape.")
            };
        }
        catch (ArgumentException exception)
        {
            throw new ReconciliationPersistenceException(
                "A persisted reconciliation resolution has an unsupported shape.",
                exception);
        }
    }

    private static bool IsKeyCollision(DbUpdateException exception)
        => exception.InnerException is SqliteException
        {
            SqliteErrorCode: SqliteConstraint,
            SqliteExtendedErrorCode:
                SqliteConstraintPrimaryKey or SqliteConstraintUnique
        };

    /// <summary>
    /// Maps a database guard that fired after Application validation, which
    /// only a concurrent writer can cause, to a stable reconciliation error.
    /// Any other failure becomes a sanitized persistence error.
    /// </summary>
    private static Exception TranslateWriteFailure(
        Exception exception,
        string fallbackMessage)
    {
        for (Exception? current = exception;
             current is not null;
             current = current.InnerException)
        {
            if (current is not SqliteException { SqliteErrorCode: SqliteConstraint } sqlite)
            {
                continue;
            }

            if (sqlite.Message.Contains(
                    "WL_M010_EVIDENCE_SUPERSEDED",
                    StringComparison.Ordinal))
            {
                return new ReconciliationException(
                    ReconciliationErrorCategory.Conflict,
                    ReconciliationErrorCodes.EvidenceSuperseded,
                    "A superseded evidence record cannot be reconciled again; reconcile its replacement instead.",
                    exception);
            }

            if (sqlite.Message.Contains(
                    "WL_M010_RECONCILIATION_CORRECTION_INVALID",
                    StringComparison.Ordinal))
            {
                return new ReconciliationException(
                    ReconciliationErrorCategory.Validation,
                    ReconciliationErrorCodes.CorrectionInvalid,
                    "A corrective transaction must be a Posted transaction of the run's household.",
                    exception);
            }
        }

        return new ReconciliationPersistenceException(fallbackMessage, exception);
    }
}
