using WealthLedger.Application.CoreLedger;
using WealthLedger.Domain.Evidence;
using WealthLedger.Domain.Ledger;

namespace WealthLedger.Application.Evidence;

/// <summary>
/// Records one evidence record, optionally with a retained attachment,
/// a supersession link and initial transaction links.
/// </summary>
/// <remarks>
/// The command is retry-safe under ADR-006: an equivalent replay of the same
/// idempotency key returns the original record and retains nothing new.
/// Evidence never posts or edits a ledger transaction.
/// </remarks>
public sealed class RecordEvidenceUseCase
{
    private const int MaximumIdempotencyKeyLength = 256;

    private readonly IEvidenceStore _store;
    private readonly TimeProvider _timeProvider;

    public RecordEvidenceUseCase(
        IEvidenceStore store,
        TimeProvider timeProvider)
    {
        _store = store
            ?? throw new ArgumentNullException(nameof(store));
        _timeProvider = timeProvider
            ?? throw new ArgumentNullException(nameof(timeProvider));
    }

    public async Task<RecordEvidenceResult> ExecuteAsync(
        string idempotencyKey,
        RecordEvidenceCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(idempotencyKey);
        ArgumentNullException.ThrowIfNull(command);

        if (idempotencyKey.Length > MaximumIdempotencyKeyLength)
        {
            throw new ArgumentException(
                "The idempotency key cannot exceed 256 characters.",
                nameof(idempotencyKey));
        }

        if (command.HouseholdId == Guid.Empty)
        {
            throw new ArgumentException(
                "HouseholdId cannot be empty.",
                nameof(command));
        }

        var normalized =
            RecordEvidenceCommandCanonicalizer.Normalize(command);

        var scope = new EvidenceCommandScope(
            normalized.HouseholdId,
            EvidenceOperationCodes.RecordEvidence,
            idempotencyKey);

        var existingReceipt = await _store.FindReceiptAsync(
            scope,
            cancellationToken);

        if (existingReceipt is not null)
        {
            return ResolveReceipt(scope, normalized, existingReceipt);
        }

        var recordedAtUtc = _timeProvider.GetUtcNow();

        EnsureNotInFuture(normalized.EffectiveDate, recordedAtUtc);
        await ValidateReferencesAsync(normalized, cancellationToken);

        var record = EvidenceRecord.Create(
            Guid.NewGuid(),
            normalized.HouseholdId,
            normalized.SourceKind,
            normalized.EffectiveDate,
            normalized.Label,
            recordedAtUtc,
            normalized.InstitutionId,
            normalized.AccountId,
            normalized.Reference,
            normalized.Note,
            normalized.SupersedesEvidenceId,
            normalized.Attachment,
            normalized.TransactionIds);

        var receipt = new EvidenceCommandReceipt(
            scope,
            RecordEvidenceCommandFingerprint.ComputeCurrent(normalized),
            record.Id,
            record.CreatedAtUtc);

        var commitResult = await _store.TryCommitAsync(
            receipt,
            record,
            cancellationToken);

        if (commitResult.WasCommitted)
        {
            if (commitResult.Receipt != receipt)
            {
                throw new InvalidOperationException(
                    "The evidence store returned an inconsistent committed receipt.");
            }

            return new RecordEvidenceResult(record.Id);
        }

        return ResolveReceipt(scope, normalized, commitResult.Receipt);
    }

    private static RecordEvidenceResult ResolveReceipt(
        EvidenceCommandScope expectedScope,
        NormalizedEvidenceCommand normalized,
        EvidenceCommandReceipt receipt)
    {
        if (receipt.Scope != expectedScope)
        {
            throw new InvalidOperationException(
                "The evidence store returned a receipt for a different scope.");
        }

        var replayFingerprint = RecordEvidenceCommandFingerprint.Compute(
            normalized,
            receipt.Fingerprint.AlgorithmCode,
            receipt.Fingerprint.Version);

        if (replayFingerprint != receipt.Fingerprint)
        {
            throw new IdempotencyConflictException();
        }

        return new RecordEvidenceResult(receipt.EvidenceId);
    }

    private void EnsureNotInFuture(
        DateOnly effectiveDate,
        DateTimeOffset recordedAtUtc)
    {
        var localDate = DateOnly.FromDateTime(
            TimeZoneInfo.ConvertTime(
                    recordedAtUtc,
                    _timeProvider.LocalTimeZone)
                .DateTime);

        if (effectiveDate > localDate)
        {
            throw EvidenceException.Invalid(
                EvidenceErrorCodes.EffectiveDateInFuture,
                "An evidence effective date cannot be in the future.");
        }
    }

    private async Task ValidateReferencesAsync(
        NormalizedEvidenceCommand normalized,
        CancellationToken cancellationToken)
    {
        if (!await _store.HouseholdExistsAsync(
                normalized.HouseholdId,
                cancellationToken))
        {
            throw EvidenceException.Missing(
                EvidenceErrorCodes.HouseholdNotFound,
                "The evidence household does not exist.");
        }

        if (normalized.InstitutionId is Guid institutionId
            && !await _store.InstitutionExistsAsync(
                institutionId,
                cancellationToken))
        {
            throw EvidenceException.Missing(
                EvidenceErrorCodes.ContextNotFound,
                "The evidence institution does not exist.");
        }

        if (normalized.AccountId is Guid accountId)
        {
            var account = await _store.FindAccountAsync(
                accountId,
                cancellationToken);

            if (account is null
                || account.AccountId != accountId
                || account.HouseholdId != normalized.HouseholdId)
            {
                throw EvidenceException.Missing(
                    EvidenceErrorCodes.ContextNotFound,
                    "The evidence account does not exist in this household.");
            }

            if (normalized.InstitutionId is Guid expectedInstitutionId
                && account.InstitutionId != expectedInstitutionId)
            {
                throw EvidenceException.Invalid(
                    EvidenceErrorCodes.ContextMismatch,
                    "The evidence account does not belong to the evidence institution.");
            }
        }

        await EvidenceTransactionValidation.EnsureLinkableAsync(
            _store,
            normalized.HouseholdId,
            normalized.TransactionIds,
            cancellationToken);

        if (normalized.SupersedesEvidenceId is Guid supersededId)
        {
            var superseded = await _store.FindEvidenceAsync(
                supersededId,
                cancellationToken);

            if (superseded is null
                || superseded.EvidenceId != supersededId
                || superseded.HouseholdId != normalized.HouseholdId)
            {
                throw EvidenceException.Invalid(
                    EvidenceErrorCodes.SupersessionInvalid,
                    "The superseded evidence does not exist in this household.");
            }

            if (superseded.SupersededByEvidenceId is not null)
            {
                throw EvidenceException.Conflict(
                    EvidenceErrorCodes.SupersessionInvalid,
                    "The evidence record has already been superseded; supersede its latest replacement instead.");
            }
        }
    }
}

/// <summary>
/// Adds links from existing evidence to existing Posted transactions.
/// </summary>
/// <remarks>
/// Linking is naturally idempotent: an existing link is left untouched and
/// reported as already linked. A superseded record accepts no new links.
/// </remarks>
public sealed class LinkEvidenceTransactionsUseCase
{
    private readonly IEvidenceStore _store;
    private readonly TimeProvider _timeProvider;

    public LinkEvidenceTransactionsUseCase(
        IEvidenceStore store,
        TimeProvider timeProvider)
    {
        _store = store
            ?? throw new ArgumentNullException(nameof(store));
        _timeProvider = timeProvider
            ?? throw new ArgumentNullException(nameof(timeProvider));
    }

    public async Task<LinkEvidenceTransactionsResult> ExecuteAsync(
        LinkEvidenceTransactionsCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (command.HouseholdId == Guid.Empty || command.EvidenceId == Guid.Empty)
        {
            throw new ArgumentException(
                "Household and evidence identities cannot be empty.",
                nameof(command));
        }

        var transactionIds =
            RecordEvidenceCommandCanonicalizer.NormalizeTransactionIds(
                command.TransactionIds,
                requireAtLeastOne: true);

        var evidence = await _store.FindEvidenceAsync(
            command.EvidenceId,
            cancellationToken);

        if (evidence is null
            || evidence.EvidenceId != command.EvidenceId
            || evidence.HouseholdId != command.HouseholdId)
        {
            throw EvidenceException.Missing(
                EvidenceErrorCodes.NotFound,
                "The evidence record does not exist in this household.");
        }

        if (evidence.SupersededByEvidenceId is not null)
        {
            throw EvidenceException.Conflict(
                EvidenceErrorCodes.Superseded,
                "A superseded evidence record accepts no new links; link its replacement instead.");
        }

        await EvidenceTransactionValidation.EnsureLinkableAsync(
            _store,
            command.HouseholdId,
            transactionIds,
            cancellationToken);

        var linkedAtUtc = _timeProvider.GetUtcNow();
        var links = transactionIds
            .Select(transactionId => EvidenceTransactionLink.Create(
                command.EvidenceId,
                transactionId,
                linkedAtUtc))
            .ToArray();

        var added = await _store.AddTransactionLinksAsync(
            links,
            cancellationToken);

        if (added < 0 || added > links.Length)
        {
            throw new InvalidOperationException(
                "The evidence store reported an impossible link count.");
        }

        return new LinkEvidenceTransactionsResult(
            command.EvidenceId,
            added,
            links.Length - added);
    }
}

internal static class EvidenceTransactionValidation
{
    internal static async Task EnsureLinkableAsync(
        IEvidenceStore store,
        Guid householdId,
        IReadOnlyList<Guid> transactionIds,
        CancellationToken cancellationToken)
    {
        if (transactionIds.Count == 0)
        {
            return;
        }

        var found = await store.FindTransactionsAsync(
            transactionIds,
            cancellationToken);

        foreach (var transactionId in transactionIds)
        {
            var transaction = found.SingleOrDefault(
                x => x.TransactionId == transactionId);

            if (transaction is null
                || transaction.HouseholdId != householdId
                || transaction.Status != TransactionStatus.Posted)
            {
                throw EvidenceException.Invalid(
                    EvidenceErrorCodes.TransactionLinkInvalid,
                    "Evidence can link only Posted transactions of its own household.");
            }
        }
    }
}
