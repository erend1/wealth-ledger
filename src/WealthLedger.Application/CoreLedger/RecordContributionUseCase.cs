using WealthLedger.Application.Common;
using WealthLedger.Domain.Assets;
using WealthLedger.Domain.Ledger;
using WealthLedger.Domain.Lots;
using WealthLedger.Domain.Portfolios;
using WealthLedger.Domain.ValueObjects;

namespace WealthLedger.Application.CoreLedger;

public sealed record RecordContributionCommand(
    Guid HouseholdId,
    Guid PortfolioId,
    Guid AccountId,
    Guid CashAssetId,
    Money Amount,
    CashFlowCategory Category,
    DateOnly ExecutionDate,
    Guid? HouseholdMemberId = null,
    string? ExternalReference = null,
    string? Note = null);

public sealed record RecordContributionResult(Guid TransactionId);

public sealed class RecordContributionUseCase
{
    private readonly ILedgerReferenceData _referenceData;
    private readonly ILedgerSubmissionStore _submissionStore;
    private readonly TimeProvider _timeProvider;

    public RecordContributionUseCase(
        ILedgerReferenceData referenceData,
        ILedgerSubmissionStore submissionStore,
        TimeProvider timeProvider)
    {
        _referenceData = referenceData
            ?? throw new ArgumentNullException(nameof(referenceData));
        _submissionStore = submissionStore
            ?? throw new ArgumentNullException(nameof(submissionStore));
        _timeProvider = timeProvider
            ?? throw new ArgumentNullException(nameof(timeProvider));
    }

    public async Task<RecordContributionResult> ExecuteAsync(
        string idempotencyKey,
        RecordContributionCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(idempotencyKey);
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(command.Amount);

        EnsureNonEmpty(
            command.HouseholdId,
            nameof(command.HouseholdId));

        var normalizedCommand =
            RecordContributionCommandCanonicalizer.Normalize(command);

        var scope = new LedgerSubmissionScope(
            normalizedCommand.HouseholdId,
            LedgerOperationCodes.RecordContribution,
            idempotencyKey);

        var existingReceipt =
            await _submissionStore.FindReceiptAsync(
                scope,
                cancellationToken);

        if (existingReceipt is not null)
        {
            return ResolveReceipt(
                scope,
                normalizedCommand,
                existingReceipt);
        }

        EnsureNonEmpty(
            normalizedCommand.PortfolioId,
            nameof(normalizedCommand.PortfolioId));

        EnsureNonEmpty(
            normalizedCommand.AccountId,
            nameof(normalizedCommand.AccountId));

        EnsureNonEmpty(
            normalizedCommand.CashAssetId,
            nameof(normalizedCommand.CashAssetId));

        if (normalizedCommand.Amount.MinorUnits <= 0)
        {
            throw new ApplicationRuleViolationException(
                "A contribution amount must be positive.");
        }

        var location =
            await _referenceData.FindLocationAsync(
                normalizedCommand.PortfolioId,
                normalizedCommand.AccountId,
                cancellationToken);

        ValidateLocation(
            location,
            normalizedCommand.HouseholdId,
            normalizedCommand.PortfolioId,
            normalizedCommand.AccountId);

        var cashAsset =
            await _referenceData.FindAssetAsync(
                normalizedCommand.CashAssetId,
                cancellationToken);

        ValidateCashAsset(
            cashAsset,
            normalizedCommand.CashAssetId,
            normalizedCommand.Amount.Currency);

        var currency =
            await _referenceData.FindCurrencyAsync(
                normalizedCommand.Amount.Currency,
                cancellationToken);

        if (currency is null)
        {
            throw new ApplicationRuleViolationException(
                $"Currency '{normalizedCommand.Amount.Currency}' does not exist.");
        }

        if (normalizedCommand.HouseholdMemberId
            is Guid householdMemberId)
        {
            EnsureNonEmpty(
                householdMemberId,
                nameof(normalizedCommand.HouseholdMemberId));

            var member =
                await _referenceData.FindHouseholdMemberAsync(
                    householdMemberId,
                    cancellationToken);

            if (member is null
                || member.Id != householdMemberId
                || member.HouseholdId
                    != normalizedCommand.HouseholdId
                || !member.IsActive)
            {
                throw new ApplicationRuleViolationException(
                    "The contribution member must be active and belong to the transaction household.");
            }
        }

        var quantityRawE8 =
            CurrencyAmountConverter.ToQuantityRawE8(
                normalizedCommand.Amount,
                currency);

        var fingerprint =
            RecordContributionCommandFingerprint.ComputeCurrent(
                normalizedCommand);

        var recordedAtUtc =
            _timeProvider.GetUtcNow();

        var transaction =
            LedgerTransaction.CreateDraft(
                Guid.NewGuid(),
                normalizedCommand.HouseholdId,
                TransactionType.Contribution,
                recordedAtUtc,
                executionDate:
                    normalizedCommand.ExecutionDate,
                externalReference:
                    normalizedCommand.ExternalReference,
                note:
                    normalizedCommand.Note);

        transaction.AddEntry(
            normalizedCommand.PortfolioId,
            normalizedCommand.AccountId,
            normalizedCommand.CashAssetId,
            QuantityDelta.FromRaw(quantityRawE8),
            EntryRole.Principal);

        transaction.AttachCashFlowDetail(
            normalizedCommand.Category,
            normalizedCommand.HouseholdMemberId);

        transaction.Post(recordedAtUtc);

        var receipt =
            new LedgerSubmissionReceipt(
                scope,
                fingerprint,
                transaction.Id,
                AssetLotId: null,
                CreatedAtUtc: recordedAtUtc);

        var commitResult =
            await _submissionStore.TryCommitAsync(
                receipt,
                transaction,
                Array.Empty<AssetLot>(),
                cancellationToken);

        if (commitResult.WasCommitted)
        {
            if (commitResult.Receipt != receipt)
            {
                throw new InvalidOperationException(
                    "The submission store returned an inconsistent committed receipt.");
            }

            return new RecordContributionResult(
                transaction.Id);
        }

        return ResolveReceipt(
            scope,
            normalizedCommand,
            commitResult.Receipt);
    }

    private static RecordContributionResult ResolveReceipt(
        LedgerSubmissionScope expectedScope,
        RecordContributionCommand normalizedCommand,
        LedgerSubmissionReceipt receipt)
    {
        if (receipt.Scope != expectedScope)
        {
            throw new InvalidOperationException(
                "The submission store returned a receipt for a different scope.");
        }

        if (receipt.AssetLotId is not null)
        {
            throw new InvalidOperationException(
                "A contribution receipt cannot contain an asset-lot result.");
        }

        var replayFingerprint =
            RecordContributionCommandFingerprint.Compute(
                normalizedCommand,
                receipt.Fingerprint.AlgorithmCode,
                receipt.Fingerprint.Version);

        if (replayFingerprint != receipt.Fingerprint)
        {
            throw new IdempotencyConflictException();
        }

        return new RecordContributionResult(
            receipt.TransactionId);
    }

    private static void ValidateLocation(
        LedgerLocationReference? location,
        Guid householdId,
        Guid portfolioId,
        Guid accountId)
    {
        if (location is null)
        {
            throw new ApplicationRuleViolationException(
                "The portfolio/account location does not exist.");
        }

        if (location.PortfolioId != portfolioId
            || location.AccountId != accountId)
        {
            throw new ApplicationRuleViolationException(
                "The resolved portfolio/account location does not match the request.");
        }

        if (location.PortfolioHouseholdId != householdId
            || location.AccountHouseholdId != householdId)
        {
            throw new ApplicationRuleViolationException(
                "The portfolio, account and transaction must belong to the same household.");
        }

        if (location.PortfolioStatus != PortfolioStatus.Active
            || !location.AccountIsActive)
        {
            throw new ApplicationRuleViolationException(
                "The portfolio and account must be active.");
        }
    }

    private static void ValidateCashAsset(
        Asset? cashAsset,
        Guid cashAssetId,
        CurrencyCode currency)
    {
        if (cashAsset is null || !cashAsset.IsActive)
        {
            throw new ApplicationRuleViolationException(
                "The cash asset must exist and be active.");
        }

        if (cashAsset.Id != cashAssetId)
        {
            throw new ApplicationRuleViolationException(
                "The resolved cash asset does not match the request.");
        }

        if (cashAsset.Type is not AssetType.Cash
            and not AssetType.Currency)
        {
            throw new ApplicationRuleViolationException(
                "A contribution must use a cash or currency asset.");
        }

        if (cashAsset.LotTrackingMode != LotTrackingMode.None)
        {
            throw new ApplicationRuleViolationException(
                "The contribution cash asset cannot use lot tracking.");
        }

        if (cashAsset.BaseCurrency != currency)
        {
            throw new ApplicationRuleViolationException(
                "The contribution amount currency must match the cash asset currency.");
        }
    }

    private static void EnsureNonEmpty(Guid value, string parameterName)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException(
                $"{parameterName} cannot be empty.",
                parameterName);
        }
    }
}
