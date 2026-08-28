using WealthLedger.Application.Common;
using WealthLedger.Domain.Assets;
using WealthLedger.Domain.Ledger;
using WealthLedger.Domain.Lots;
using WealthLedger.Domain.Portfolios;
using WealthLedger.Domain.ValueObjects;

namespace WealthLedger.Application.CoreLedger;

public sealed record RecordFundPurchaseCommand(
    Guid HouseholdId,
    Guid PortfolioId,
    Guid AccountId,
    Guid FundAssetId,
    Guid CashAssetId,
    Quantity FundQuantity,
    UnitPrice ExecutedUnitPrice,
    Money CashConsideration,
    DateOnly ExecutionDate,
    string? ExternalReference = null,
    string? Note = null);

public sealed record RecordFundPurchaseResult(
    Guid TransactionId,
    Guid AssetLotId);

public sealed class RecordFundPurchaseUseCase
{
    private readonly ILedgerReferenceData _referenceData;
    private readonly ILedgerSubmissionStore _submissionStore;
    private readonly TimeProvider _timeProvider;

    public RecordFundPurchaseUseCase(
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

    public async Task<RecordFundPurchaseResult> ExecuteAsync(
        string idempotencyKey,
        RecordFundPurchaseCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(idempotencyKey);
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(command.ExecutedUnitPrice);
        ArgumentNullException.ThrowIfNull(command.CashConsideration);

        EnsureNonEmpty(command.HouseholdId, nameof(command.HouseholdId));
        EnsureNonEmpty(command.PortfolioId, nameof(command.PortfolioId));
        EnsureNonEmpty(command.AccountId, nameof(command.AccountId));
        EnsureNonEmpty(command.FundAssetId, nameof(command.FundAssetId));
        EnsureNonEmpty(command.CashAssetId, nameof(command.CashAssetId));

        if (command.FundAssetId == command.CashAssetId)
        {
            throw new ApplicationRuleViolationException(
                "The purchased fund and cash consideration assets must differ.");
        }

        if (command.FundQuantity.RawE8 == 0)
        {
            throw new ApplicationRuleViolationException(
                "A fund purchase quantity must be positive.");
        }

        if (command.CashConsideration.MinorUnits <= 0)
        {
            throw new ApplicationRuleViolationException(
                "Fund purchase cash consideration must be positive.");
        }

        if (command.ExecutedUnitPrice.Currency
            != command.CashConsideration.Currency)
        {
            throw new ApplicationRuleViolationException(
                "Executed unit price and cash consideration must use the same currency.");
        }

        var normalizedCommand = RecordFundPurchaseCommandCanonicalizer
            .Normalize(command);

        var scope =
            new LedgerSubmissionScope(
                normalizedCommand.HouseholdId,
                LedgerOperationCodes.RecordFundPurchase,
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

        var location = await _referenceData.FindLocationAsync(
            normalizedCommand.PortfolioId,
            normalizedCommand.AccountId,
            cancellationToken);

        ValidateLocation(
            location,
            normalizedCommand.HouseholdId,
            normalizedCommand.PortfolioId,
            normalizedCommand.AccountId);

        var fundAsset = await _referenceData.FindAssetAsync(
            normalizedCommand.FundAssetId,
            cancellationToken);

        ValidateFundAsset(fundAsset, normalizedCommand.FundAssetId);

        var cashAsset = await _referenceData.FindAssetAsync(
            normalizedCommand.CashAssetId,
            cancellationToken);

        ValidateCashAsset(
            cashAsset,
            normalizedCommand.CashAssetId,
            normalizedCommand.CashConsideration.Currency);

        var currency = await _referenceData.FindCurrencyAsync(
            normalizedCommand.CashConsideration.Currency,
            cancellationToken);

        if (currency is null)
        {
            throw new ApplicationRuleViolationException(
                $"Currency '{normalizedCommand.CashConsideration.Currency}' does not exist.");
        }

        var considerationRawE8 = CurrencyAmountConverter.ToQuantityRawE8(
            normalizedCommand.CashConsideration,
            currency);

        var fingerprint = RecordFundPurchaseCommandFingerprint
            .ComputeCurrent(normalizedCommand);

        var recordedAtUtc = _timeProvider.GetUtcNow();

        var transaction =
            LedgerTransaction.CreateDraft(
                Guid.NewGuid(),
                normalizedCommand.HouseholdId,
                TransactionType.Buy,
                recordedAtUtc,
                executionDate:
                    normalizedCommand.ExecutionDate,
                externalReference:
                    normalizedCommand.ExternalReference,
                note:
                    normalizedCommand.Note);

        var principalEntry =
            transaction.AddEntry(
                normalizedCommand.PortfolioId,
                normalizedCommand.AccountId,
                normalizedCommand.FundAssetId,
                QuantityDelta.FromRaw(
                    normalizedCommand
                        .FundQuantity.RawE8),
                EntryRole.Principal,
                normalizedCommand.ExecutedUnitPrice);

        transaction.AddEntry(
            normalizedCommand.PortfolioId,
            normalizedCommand.AccountId,
            normalizedCommand.CashAssetId,
            QuantityDelta.FromRaw(
                checked(-considerationRawE8)),
            EntryRole.Consideration);

        var assetLot =
            AssetLot.Create(
                Guid.NewGuid(),
                fundAsset!,
                principalEntry,
                normalizedCommand.FundQuantity,
                normalizedCommand.ExecutionDate,
                CostBasis.Known(
                    normalizedCommand
                        .CashConsideration),
                recordedAtUtc);

        transaction.Post(recordedAtUtc);

        var receipt =
            new LedgerSubmissionReceipt(
                scope,
                fingerprint,
                transaction.Id,
                AssetLotId:
                    assetLot.Id,
                CreatedAtUtc:
                    recordedAtUtc);

        var commitResult =
            await _submissionStore.TryCommitAsync(
                receipt,
                transaction,
                [assetLot],
                cancellationToken);

        if (commitResult.WasCommitted)
        {
            if (commitResult.Receipt != receipt)
            {
                throw new InvalidOperationException(
                    "The submission store returned an inconsistent committed receipt.");
            }

            return new RecordFundPurchaseResult(
                transaction.Id,
                assetLot.Id);
        }

        return ResolveReceipt(
            scope,
            normalizedCommand,
            commitResult.Receipt);
    }

    private static RecordFundPurchaseResult ResolveReceipt(
        LedgerSubmissionScope expectedScope,
        RecordFundPurchaseCommand normalizedCommand,
        LedgerSubmissionReceipt receipt)
    {
        if (receipt.Scope != expectedScope)
        {
            throw new InvalidOperationException(
                "The submission store returned a receipt for a different scope.");
        }

        if (receipt.AssetLotId is not Guid assetLotId
            || assetLotId == Guid.Empty)
        {
            throw new InvalidOperationException(
                "A fund-purchase receipt must contain an asset-lot result.");
        }

        var replayFingerprint =
            RecordFundPurchaseCommandFingerprint.Compute(
                normalizedCommand,
                receipt.Fingerprint.AlgorithmCode,
                receipt.Fingerprint.Version);

        if (replayFingerprint
            != receipt.Fingerprint)
        {
            throw new IdempotencyConflictException();
        }

        return new RecordFundPurchaseResult(
            receipt.TransactionId,
            assetLotId);
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

    private static void ValidateFundAsset(
        Asset? fundAsset,
        Guid fundAssetId)
    {
        if (fundAsset is null || !fundAsset.IsActive)
        {
            throw new ApplicationRuleViolationException(
                "The fund asset must exist and be active.");
        }

        if (fundAsset.Id != fundAssetId)
        {
            throw new ApplicationRuleViolationException(
                "The resolved fund asset does not match the request.");
        }

        if (fundAsset.Type != AssetType.Fund)
        {
            throw new ApplicationRuleViolationException(
                "The purchase principal asset must be a fund.");
        }

        if (fundAsset.LotTrackingMode == LotTrackingMode.None)
        {
            throw new ApplicationRuleViolationException(
                "The purchased fund must use lot tracking.");
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
                "Fund purchase consideration must use a cash or currency asset.");
        }

        if (cashAsset.LotTrackingMode != LotTrackingMode.None)
        {
            throw new ApplicationRuleViolationException(
                "The purchase cash asset cannot use lot tracking.");
        }

        if (cashAsset.BaseCurrency != currency)
        {
            throw new ApplicationRuleViolationException(
                "Cash consideration currency must match the cash asset currency.");
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
