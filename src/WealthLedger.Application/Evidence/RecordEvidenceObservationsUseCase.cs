using WealthLedger.Domain.Assets;
using WealthLedger.Domain.Evidence;

namespace WealthLedger.Application.Evidence;

/// <summary>
/// Adds exact observed quantities to an existing evidence record.
/// </summary>
/// <remarks>
/// Each observation names one exact Portfolio/Account/Asset scope of the
/// record's household, inside the record's institution and account context
/// when it names them. Observations are insert-only and naturally idempotent:
/// repeating a stored observation adds nothing, while different facts for a
/// stored scope are a conflict, corrected by superseding the record. A
/// superseded record accepts no new observations. Recording an observation
/// never posts, edits or deletes ledger history.
/// </remarks>
public sealed class RecordEvidenceObservationsUseCase
{
    private readonly IEvidenceStore _store;
    private readonly TimeProvider _timeProvider;

    public RecordEvidenceObservationsUseCase(
        IEvidenceStore store,
        TimeProvider timeProvider)
    {
        _store = store
            ?? throw new ArgumentNullException(nameof(store));
        _timeProvider = timeProvider
            ?? throw new ArgumentNullException(nameof(timeProvider));
    }

    public async Task<RecordEvidenceObservationsResult> ExecuteAsync(
        RecordEvidenceObservationsCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (command.HouseholdId == Guid.Empty || command.EvidenceId == Guid.Empty)
        {
            throw new ArgumentException(
                "Household and evidence identities cannot be empty.",
                nameof(command));
        }

        var inputs = Normalize(command.Observations);

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
                "A superseded evidence record accepts no new observations; observe through its replacement instead.");
        }

        await ValidateScopesAsync(evidence, inputs, cancellationToken);

        var recordedAtUtc = _timeProvider.GetUtcNow();
        var observations = inputs
            .Select(input => EvidenceObservation.Create(
                Guid.NewGuid(),
                command.EvidenceId,
                input.PortfolioId,
                input.AccountId,
                input.AssetId,
                input.ObservedQuantityRawE8,
                input.ToleranceRawE8,
                input.ObservedPieceCount,
                recordedAtUtc))
            .ToArray();

        var existing = await _store.ListObservationsAsync(
            command.EvidenceId,
            cancellationToken);
        var newCount = 0;

        foreach (var observation in observations)
        {
            var stored = existing.SingleOrDefault(observation.HasSameScope);

            if (stored is null)
            {
                newCount++;
            }
            else if (!stored.HasSameFacts(observation))
            {
                throw ObservationConflict();
            }
        }

        if (existing.Count + newCount > EvidenceObservation.MaximumPerEvidenceRecord)
        {
            throw LimitExceeded();
        }

        var added = newCount == 0
            ? 0
            : await _store.AddObservationsAsync(
                command.EvidenceId,
                observations,
                cancellationToken);

        if (added < 0 || added > observations.Length)
        {
            throw new InvalidOperationException(
                "The evidence store reported an impossible observation count.");
        }

        return new RecordEvidenceObservationsResult(
            command.EvidenceId,
            added,
            observations.Length - added);
    }

    internal static EvidenceException ObservationConflict()
        => EvidenceException.Conflict(
            EvidenceErrorCodes.ObservationConflict,
            "The evidence record already holds different observed facts for this scope; supersede the record to correct them.");

    internal static EvidenceException LimitExceeded()
        => EvidenceException.Invalid(
            EvidenceErrorCodes.ObservationLimitExceeded,
            "One evidence record can hold at most 100 observations.");

    private static IReadOnlyList<EvidenceObservationInput> Normalize(
        IReadOnlyList<EvidenceObservationInput>? observations)
    {
        if (observations is null || observations.Count == 0)
        {
            throw EvidenceException.Invalid(
                EvidenceErrorCodes.ObservationInvalid,
                "At least one observation is required.");
        }

        if (observations.Count > EvidenceObservation.MaximumPerEvidenceRecord)
        {
            throw LimitExceeded();
        }

        foreach (var input in observations)
        {
            if (input is null
                || input.PortfolioId == Guid.Empty
                || input.AccountId == Guid.Empty
                || input.AssetId == Guid.Empty)
            {
                throw EvidenceException.Invalid(
                    EvidenceErrorCodes.ObservationInvalid,
                    "Every observation names a non-empty portfolio, account and asset.");
            }

            if (input.ToleranceRawE8 < 0)
            {
                throw EvidenceException.Invalid(
                    EvidenceErrorCodes.ObservationInvalid,
                    "An observation tolerance cannot be negative.");
            }

            if (input.ObservedPieceCount < 0)
            {
                throw EvidenceException.Invalid(
                    EvidenceErrorCodes.ObservationInvalid,
                    "An observed piece count cannot be negative.");
            }
        }

        if (observations
                .Select(x => (x.PortfolioId, x.AccountId, x.AssetId))
                .Distinct()
                .Count() != observations.Count)
        {
            throw EvidenceException.Invalid(
                EvidenceErrorCodes.ObservationInvalid,
                "One command observes each scope at most once.");
        }

        return observations;
    }

    private async Task ValidateScopesAsync(
        EvidenceRecordReference evidence,
        IReadOnlyList<EvidenceObservationInput> inputs,
        CancellationToken cancellationToken)
    {
        var references = await _store.FindObservationReferencesAsync(
            inputs.Select(x => x.PortfolioId).Distinct().ToArray(),
            inputs.Select(x => x.AccountId).Distinct().ToArray(),
            inputs.Select(x => x.AssetId).Distinct().ToArray(),
            cancellationToken);

        foreach (var input in inputs)
        {
            var portfolio = references.Portfolios.SingleOrDefault(
                x => x.PortfolioId == input.PortfolioId);
            var account = references.Accounts.SingleOrDefault(
                x => x.AccountId == input.AccountId);
            var asset = references.Assets.SingleOrDefault(
                x => x.AssetId == input.AssetId);

            if (portfolio is null
                || portfolio.HouseholdId != evidence.HouseholdId
                || account is null
                || account.HouseholdId != evidence.HouseholdId
                || asset is null)
            {
                throw EvidenceException.Missing(
                    EvidenceErrorCodes.ObservationScopeNotFound,
                    "An observed scope does not exist in the evidence household.");
            }

            if ((evidence.AccountId is Guid evidenceAccountId
                 && account.AccountId != evidenceAccountId)
                || (evidence.InstitutionId is Guid evidenceInstitutionId
                    && account.InstitutionId != evidenceInstitutionId))
            {
                throw EvidenceException.Invalid(
                    EvidenceErrorCodes.ObservationScopeMismatch,
                    "An observed account lies outside the evidence record's account or institution.");
            }

            if (input.ObservedPieceCount is not null
                && asset.AssetType != AssetType.PhysicalGold)
            {
                throw EvidenceException.Invalid(
                    EvidenceErrorCodes.ObservationInvalid,
                    "A piece count can be observed only for physical gold.");
            }
        }
    }
}
