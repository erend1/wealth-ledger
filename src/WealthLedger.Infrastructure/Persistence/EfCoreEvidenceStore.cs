using System.Runtime.InteropServices;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using WealthLedger.Application.CoreLedger;
using WealthLedger.Application.Evidence;
using WealthLedger.Domain.Evidence;
using WealthLedger.Infrastructure.Persistence.Mapping;
using WealthLedger.Infrastructure.Persistence.Rows;

namespace WealthLedger.Infrastructure.Persistence;

/// <summary>
/// Write-side evidence persistence. It inserts evidence, attachment, link,
/// observation and receipt rows only, and never reads or writes ledger rows
/// other than to validate references.
/// </summary>
public sealed class EfCoreEvidenceStore : IEvidenceStore
{
    private const int SqliteConstraint = 19;
    private const int SqliteConstraintPrimaryKey = 1555;
    private const int SqliteConstraintUnique = 2067;

    private readonly WealthLedgerDbContext _dbContext;

    public EfCoreEvidenceStore(WealthLedgerDbContext dbContext)
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

    public Task<bool> InstitutionExistsAsync(
        Guid institutionId,
        CancellationToken cancellationToken = default)
        => _dbContext.Institutions
            .AsNoTracking()
            .AnyAsync(row => row.Id == institutionId, cancellationToken);

    public Task<EvidenceAccountReference?> FindAccountAsync(
        Guid accountId,
        CancellationToken cancellationToken = default)
        => _dbContext.Accounts
            .AsNoTracking()
            .Where(row => row.Id == accountId)
            .Select(row => new EvidenceAccountReference(
                row.Id,
                row.HouseholdId,
                row.InstitutionId))
            .SingleOrDefaultAsync(cancellationToken);

    public async Task<IReadOnlyList<EvidenceTransactionReference>> FindTransactionsAsync(
        IReadOnlyCollection<Guid> transactionIds,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(transactionIds);

        // The Application bounds a command to 100 links, so this parameter
        // list stays small.
        var ids = transactionIds.ToArray();

        return await _dbContext.LedgerTransactions
            .AsNoTracking()
            .Where(row => ids.Contains(row.Id))
            .Select(row => new EvidenceTransactionReference(
                row.Id,
                row.HouseholdId,
                row.Status))
            .ToListAsync(cancellationToken);
    }

    public Task<EvidenceRecordReference?> FindEvidenceAsync(
        Guid evidenceId,
        CancellationToken cancellationToken = default)
        => _dbContext.EvidenceRecords
            .AsNoTracking()
            .Where(row => row.Id == evidenceId)
            .Select(row => new EvidenceRecordReference(
                row.Id,
                row.HouseholdId,
                _dbContext.EvidenceRecords
                    .Where(successor => successor.SupersedesEvidenceId == row.Id)
                    .Select(successor => (Guid?)successor.Id)
                    .FirstOrDefault(),
                row.EffectiveDate,
                row.InstitutionId,
                row.AccountId))
            .SingleOrDefaultAsync(cancellationToken);

    public async Task<EvidenceCommandReceipt?> FindReceiptAsync(
        EvidenceCommandScope scope,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scope);

        var row = await _dbContext.EvidenceCommandReceipts
            .AsNoTracking()
            .SingleOrDefaultAsync(
                x => x.HouseholdId == scope.HouseholdId
                    && x.OperationCode == scope.OperationCode
                    && x.IdempotencyKey == scope.IdempotencyKey,
                cancellationToken);

        return row is null ? null : ToReceipt(row);
    }

    public async Task<EvidenceCommitResult> TryCommitAsync(
        EvidenceCommandReceipt receipt,
        EvidenceRecord record,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(receipt);
        ArgumentNullException.ThrowIfNull(record);

        if (receipt.EvidenceId != record.Id
            || receipt.Scope.HouseholdId != record.HouseholdId
            || receipt.CreatedAtUtc != record.CreatedAtUtc)
        {
            throw new InvalidOperationException(
                "The evidence receipt does not describe the evidence record.");
        }

        var writingReceipt = false;

        try
        {
            await using (var transaction =
                         await _dbContext.Database.BeginTransactionAsync(
                             cancellationToken))
            {
                AddRecordGraph(record);

                await _dbContext.SaveChangesAsync(cancellationToken);

                writingReceipt = true;
                _dbContext.EvidenceCommandReceipts.Add(MapReceipt(receipt));

                await _dbContext.SaveChangesAsync(cancellationToken);

                writingReceipt = false;

                await transaction.CommitAsync(cancellationToken);
            }

            _dbContext.ChangeTracker.Clear();

            return new EvidenceCommitResult(
                WasCommitted: true,
                Receipt: receipt);
        }
        catch (DbUpdateException exception)
            when (writingReceipt && IsKeyCollision(exception))
        {
            _dbContext.ChangeTracker.Clear();

            var winner = await FindReceiptAsync(
                receipt.Scope,
                cancellationToken);

            if (winner is not null)
            {
                return new EvidenceCommitResult(
                    WasCommitted: false,
                    Receipt: winner);
            }

            throw new EvidencePersistenceException(
                "The evidence record collided with another writer but no committed receipt could be recovered.",
                exception);
        }
        catch (Exception exception)
            when (exception is DbUpdateException or SqliteException)
        {
            _dbContext.ChangeTracker.Clear();

            // An equivalent retry can lose earlier than the receipt insert:
            // a superseding record collides on the supersession index first.
            // A committed receipt for the same scope always wins.
            var winner = await FindReceiptAsync(
                receipt.Scope,
                cancellationToken);

            if (winner is not null)
            {
                return new EvidenceCommitResult(
                    WasCommitted: false,
                    Receipt: winner);
            }

            throw TranslateWriteFailure(
                exception,
                "The evidence record could not be persisted atomically.");
        }
    }

    public async Task<int> AddTransactionLinksAsync(
        IReadOnlyCollection<EvidenceTransactionLink> links,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(links);

        if (links.Count == 0)
        {
            return 0;
        }

        try
        {
            await using var transaction =
                await _dbContext.Database.BeginTransactionAsync(
                    cancellationToken);

            var added = 0;

            foreach (var link in links)
            {
                var evidenceId = ToText(link.EvidenceId);
                var transactionId = ToText(link.TransactionId);
                var linkedAtUtc = ToText(link.LinkedAtUtc);

                // An existing pair is left untouched, which keeps linking
                // idempotent under concurrent writers.
                added += await _dbContext.Database.ExecuteSqlAsync(
                    $"""
                     INSERT INTO "EvidenceTransactionLink"
                         ("EvidenceId", "LedgerTransactionId", "LinkedAtUtc")
                     VALUES ({evidenceId}, {transactionId}, {linkedAtUtc})
                     ON CONFLICT ("EvidenceId", "LedgerTransactionId") DO NOTHING;
                     """,
                    cancellationToken);
            }

            await transaction.CommitAsync(cancellationToken);

            return added;
        }
        catch (SqliteException exception)
        {
            throw TranslateWriteFailure(
                exception,
                "The evidence links could not be persisted atomically.");
        }
    }

    public async Task<EvidenceObservationReferences> FindObservationReferencesAsync(
        IReadOnlyCollection<Guid> portfolioIds,
        IReadOnlyCollection<Guid> accountIds,
        IReadOnlyCollection<Guid> assetIds,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(portfolioIds);
        ArgumentNullException.ThrowIfNull(accountIds);
        ArgumentNullException.ThrowIfNull(assetIds);

        // The Application bounds a command to 100 observations, so these
        // parameter lists stay small.
        var portfolios = portfolioIds.ToArray();
        var accounts = accountIds.ToArray();
        var assets = assetIds.ToArray();

        return new EvidenceObservationReferences(
            await _dbContext.Portfolios
                .AsNoTracking()
                .Where(row => portfolios.Contains(row.Id))
                .Select(row => new EvidencePortfolioReference(
                    row.Id,
                    row.HouseholdId))
                .ToListAsync(cancellationToken),
            await _dbContext.Accounts
                .AsNoTracking()
                .Where(row => accounts.Contains(row.Id))
                .Select(row => new EvidenceAccountReference(
                    row.Id,
                    row.HouseholdId,
                    row.InstitutionId))
                .ToListAsync(cancellationToken),
            await _dbContext.Assets
                .AsNoTracking()
                .Where(row => assets.Contains(row.Id))
                .Select(row => new EvidenceAssetReference(
                    row.Id,
                    row.Type))
                .ToListAsync(cancellationToken));
    }

    public async Task<IReadOnlyList<EvidenceObservation>> ListObservationsAsync(
        Guid evidenceId,
        CancellationToken cancellationToken = default)
    {
        var rows = await _dbContext.EvidenceObservations
            .AsNoTracking()
            .Where(row => row.EvidenceId == evidenceId)
            .OrderBy(row => row.RecordedAtUtc)
            .ThenBy(row => row.Id)
            .ToListAsync(cancellationToken);

        return rows.Select(ToObservation).ToArray();
    }

    public async Task<int> AddObservationsAsync(
        Guid evidenceId,
        IReadOnlyCollection<EvidenceObservation> observations,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(observations);

        if (observations.Any(observation => observation.EvidenceId != evidenceId))
        {
            throw new InvalidOperationException(
                "Every observation must belong to the named evidence record.");
        }

        try
        {
            // The write transaction is immediate, so the stored set read here
            // cannot change before the insert.
            await using var transaction =
                await _dbContext.Database.BeginTransactionAsync(
                    cancellationToken);

            var stored = await ListObservationsAsync(
                evidenceId,
                cancellationToken);
            var added = 0;

            foreach (var observation in observations)
            {
                var existing = stored.SingleOrDefault(observation.HasSameScope);

                if (existing is not null)
                {
                    if (!existing.HasSameFacts(observation))
                    {
                        throw ObservationConflict(innerException: null);
                    }

                    continue;
                }

                _dbContext.EvidenceObservations.Add(new EvidenceObservationRow
                {
                    Id = observation.Id,
                    EvidenceId = observation.EvidenceId,
                    PortfolioId = observation.PortfolioId,
                    AccountId = observation.AccountId,
                    AssetId = observation.AssetId,
                    ObservedQuantityE8 = observation.ObservedQuantityRawE8,
                    ToleranceE8 = observation.ToleranceRawE8,
                    ObservedPieceCount = observation.ObservedPieceCount,
                    RecordedAtUtc = observation.RecordedAtUtc.UtcDateTime
                });
                added++;
            }

            if (added > 0)
            {
                await _dbContext.SaveChangesAsync(cancellationToken);
            }

            await transaction.CommitAsync(cancellationToken);
            _dbContext.ChangeTracker.Clear();

            return added;
        }
        catch (Exception exception)
            when (exception is DbUpdateException or SqliteException)
        {
            _dbContext.ChangeTracker.Clear();

            throw TranslateWriteFailure(
                exception,
                "The evidence observations could not be persisted atomically.");
        }
        catch
        {
            _dbContext.ChangeTracker.Clear();
            throw;
        }
    }

    private void AddRecordGraph(EvidenceRecord record)
    {
        var createdAtUtc = record.CreatedAtUtc.UtcDateTime;

        _dbContext.EvidenceRecords.Add(new EvidenceRecordRow
        {
            Id = record.Id,
            HouseholdId = record.HouseholdId,
            SourceKind = record.SourceKind,
            EffectiveDate = record.EffectiveDate,
            InstitutionId = record.InstitutionId,
            AccountId = record.AccountId,
            Label = record.Label,
            Reference = record.Reference,
            Note = record.Note,
            SupersedesEvidenceId = record.SupersedesEvidenceId,
            CreatedAtUtc = createdAtUtc
        });

        if (record.Attachment is { } attachment)
        {
            _dbContext.EvidenceAttachments.Add(new EvidenceAttachmentRow
            {
                EvidenceId = record.Id,
                FileName = attachment.FileName,
                MediaType = attachment.MediaType,
                ByteLength = attachment.ByteLength,
                Sha256 = attachment.Sha256,
                Content = ToArrayWithoutCopy(attachment.Content),
                CreatedAtUtc = createdAtUtc
            });
        }

        foreach (var link in record.TransactionLinks)
        {
            _dbContext.EvidenceTransactionLinks.Add(new EvidenceTransactionLinkRow
            {
                EvidenceId = link.EvidenceId,
                LedgerTransactionId = link.TransactionId,
                LinkedAtUtc = link.LinkedAtUtc.UtcDateTime
            });
        }
    }

    private static EvidenceCommandReceiptRow MapReceipt(
        EvidenceCommandReceipt receipt)
        => new()
        {
            HouseholdId = receipt.Scope.HouseholdId,
            OperationCode = receipt.Scope.OperationCode,
            IdempotencyKey = receipt.Scope.IdempotencyKey,
            FingerprintAlgorithmCode = receipt.Fingerprint.AlgorithmCode,
            FingerprintVersion = receipt.Fingerprint.Version,
            FingerprintValue = receipt.Fingerprint.Value,
            ResultEvidenceId = receipt.EvidenceId,
            CreatedAtUtc = receipt.CreatedAtUtc.UtcDateTime
        };

    private static EvidenceCommandReceipt ToReceipt(
        EvidenceCommandReceiptRow row)
        => new(
            new EvidenceCommandScope(
                row.HouseholdId,
                row.OperationCode,
                row.IdempotencyKey),
            new CommandFingerprint(
                row.FingerprintAlgorithmCode,
                row.FingerprintVersion,
                row.FingerprintValue),
            row.ResultEvidenceId,
            new DateTimeOffset(
                DateTime.SpecifyKind(row.CreatedAtUtc, DateTimeKind.Utc)));

    private static EvidenceObservation ToObservation(EvidenceObservationRow row)
        => EvidenceObservation.Create(
            row.Id,
            row.EvidenceId,
            row.PortfolioId,
            row.AccountId,
            row.AssetId,
            row.ObservedQuantityE8,
            row.ToleranceE8,
            row.ObservedPieceCount,
            new DateTimeOffset(
                DateTime.SpecifyKind(row.RecordedAtUtc, DateTimeKind.Utc)));

    private static EvidenceException ObservationConflict(Exception? innerException)
        => new(
            EvidenceErrorCategory.Conflict,
            EvidenceErrorCodes.ObservationConflict,
            "The evidence record already holds different observed facts for this scope; supersede the record to correct them.",
            innerException);

    private static bool IsKeyCollision(DbUpdateException exception)
        => exception.InnerException is SqliteException
        {
            SqliteErrorCode: SqliteConstraint,
            SqliteExtendedErrorCode:
                SqliteConstraintPrimaryKey or SqliteConstraintUnique
        };

    /// <summary>
    /// Maps a database guard that fired after Application validation, which
    /// only a concurrent writer can cause, to a stable evidence error. Any
    /// other failure becomes a sanitized persistence error.
    /// </summary>
    private static Exception TranslateWriteFailure(
        Exception exception,
        string fallbackMessage)
        => (Exception?)TranslateGuardFailure(exception)
           ?? new EvidencePersistenceException(fallbackMessage, exception);

    private static EvidenceException? TranslateGuardFailure(Exception exception)
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
                    "EvidenceRecord.SupersedesEvidenceId",
                    StringComparison.Ordinal)
                || sqlite.Message.Contains(
                    "WL_M010_EVIDENCE_SUPERSESSION_INVALID",
                    StringComparison.Ordinal))
            {
                return new EvidenceException(
                    EvidenceErrorCategory.Conflict,
                    EvidenceErrorCodes.SupersessionInvalid,
                    "The evidence record has already been superseded; supersede its latest replacement instead.",
                    exception);
            }

            if (sqlite.Message.Contains(
                    "WL_M010_EVIDENCE_SUPERSEDED",
                    StringComparison.Ordinal))
            {
                return new EvidenceException(
                    EvidenceErrorCategory.Conflict,
                    EvidenceErrorCodes.Superseded,
                    "A superseded evidence record accepts no new links or observations; use its replacement instead.",
                    exception);
            }

            if (sqlite.Message.Contains(
                    "EvidenceObservation.EvidenceId",
                    StringComparison.Ordinal))
            {
                return ObservationConflict(exception);
            }

            if (sqlite.Message.Contains(
                    "WL_M010_OBSERVATION_LIMIT",
                    StringComparison.Ordinal))
            {
                return new EvidenceException(
                    EvidenceErrorCategory.Validation,
                    EvidenceErrorCodes.ObservationLimitExceeded,
                    "One evidence record can hold at most 100 observations.",
                    exception);
            }

            if (sqlite.Message.Contains(
                    "WL_M010_OBSERVATION_SCOPE_INVALID",
                    StringComparison.Ordinal))
            {
                return new EvidenceException(
                    EvidenceErrorCategory.Validation,
                    EvidenceErrorCodes.ObservationScopeMismatch,
                    "An observed scope lies outside the evidence household or context.",
                    exception);
            }

            if (sqlite.Message.Contains(
                    "WL_M010_OBSERVATION_PIECES_INVALID",
                    StringComparison.Ordinal))
            {
                return new EvidenceException(
                    EvidenceErrorCategory.Validation,
                    EvidenceErrorCodes.ObservationInvalid,
                    "A piece count can be observed only for physical gold.",
                    exception);
            }

            if (sqlite.Message.Contains(
                    "WL_M010_EVIDENCE_LINK_INVALID",
                    StringComparison.Ordinal))
            {
                return new EvidenceException(
                    EvidenceErrorCategory.Validation,
                    EvidenceErrorCodes.TransactionLinkInvalid,
                    "Evidence can link only Posted transactions of its own household.",
                    exception);
            }

            if (sqlite.Message.Contains(
                    "WL_M010_EVIDENCE_CONTEXT_INVALID",
                    StringComparison.Ordinal))
            {
                return new EvidenceException(
                    EvidenceErrorCategory.Validation,
                    EvidenceErrorCodes.ContextMismatch,
                    "The evidence account does not match the evidence household or institution.",
                    exception);
            }
        }

        return null;
    }

    // The attachment already owns an immutable private copy of up to 10 MiB;
    // the insert only reads it, so a second copy is avoided when possible.
    private static byte[] ToArrayWithoutCopy(ReadOnlyMemory<byte> content)
        => MemoryMarshal.TryGetArray(content, out var segment)
           && segment.Offset == 0
           && segment.Count == segment.Array!.Length
            ? segment.Array
            : content.ToArray();

    private static string ToText(Guid value)
        => (string)SqliteValueConverters.GuidToText.ConvertToProvider(value)!;

    private static string ToText(DateTimeOffset value)
        => (string)SqliteValueConverters.UtcDateTimeToText.ConvertToProvider(
            value.UtcDateTime)!;
}
