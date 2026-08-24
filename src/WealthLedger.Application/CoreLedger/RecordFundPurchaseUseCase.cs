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
    private readonly ILedgerPostingStore _postingStore;
    private readonly TimeProvider _timeProvider;

    public RecordFundPurchaseUseCase(
        ILedgerReferenceData referenceData,
        ILedgerPostingStore postingStore,
        TimeProvider timeProvider)
    {
        _referenceData = referenceData
            ?? throw new ArgumentNullException(nameof(referenceData));
        _postingStore = postingStore
            ?? throw new ArgumentNullException(nameof(postingStore));
        _timeProvider = timeProvider
            ?? throw new ArgumentNullException(nameof(timeProvider));
    }

    public async Task<RecordFundPurchaseResult> ExecuteAsync(
        RecordFundPurchaseCommand command,
        CancellationToken cancellationToken = default)
    {
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

        var location = await _referenceData.FindLocationAsync(
            command.PortfolioId,
            command.AccountId,
            cancellationToken);

        ValidateLocation(
            location,
            command.HouseholdId,
            command.PortfolioId,
            command.AccountId);

        var fundAsset = await _referenceData.FindAssetAsync(
            command.FundAssetId,
            cancellationToken);

        ValidateFundAsset(fundAsset, command.FundAssetId);

        var cashAsset = await _referenceData.FindAssetAsync(
            command.CashAssetId,
            cancellationToken);

        ValidateCashAsset(
            cashAsset,
            command.CashAssetId,
            command.CashConsideration.Currency);

        var currency = await _referenceData.FindCurrencyAsync(
            command.CashConsideration.Currency,
            cancellationToken);

        if (currency is null)
        {
            throw new ApplicationRuleViolationException(
                $"Currency '{command.CashConsideration.Currency}' does not exist.");
        }

        var considerationRawE8 = CurrencyAmountConverter.ToQuantityRawE8(
            command.CashConsideration,
            currency);

        var recordedAtUtc = _timeProvider.GetUtcNow();
        var transaction = LedgerTransaction.CreateDraft(
            Guid.NewGuid(),
            command.HouseholdId,
            TransactionType.Buy,
            recordedAtUtc,
            executionDate: command.ExecutionDate,
            externalReference: command.ExternalReference,
            note: command.Note);

        var principalEntry = transaction.AddEntry(
            command.PortfolioId,
            command.AccountId,
            command.FundAssetId,
            QuantityDelta.FromRaw(command.FundQuantity.RawE8),
            EntryRole.Principal,
            command.ExecutedUnitPrice);

        transaction.AddEntry(
            command.PortfolioId,
            command.AccountId,
            command.CashAssetId,
            QuantityDelta.FromRaw(checked(-considerationRawE8)),
            EntryRole.Consideration);

        var assetLot = AssetLot.Create(
            Guid.NewGuid(),
            fundAsset!,
            principalEntry,
            command.FundQuantity,
            command.ExecutionDate,
            CostBasis.Known(command.CashConsideration),
            recordedAtUtc);

        transaction.Post(recordedAtUtc);

        await _postingStore.SavePostedTransactionAsync(
            transaction,
            [assetLot],
            cancellationToken);

        return new RecordFundPurchaseResult(
            transaction.Id,
            assetLot.Id);
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
