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
    private readonly ILedgerPostingStore _postingStore;
    private readonly TimeProvider _timeProvider;

    public RecordContributionUseCase(
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

    public async Task<RecordContributionResult> ExecuteAsync(
        RecordContributionCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(command.Amount);

        EnsureNonEmpty(command.HouseholdId, nameof(command.HouseholdId));
        EnsureNonEmpty(command.PortfolioId, nameof(command.PortfolioId));
        EnsureNonEmpty(command.AccountId, nameof(command.AccountId));
        EnsureNonEmpty(command.CashAssetId, nameof(command.CashAssetId));

        if (command.Amount.MinorUnits <= 0)
        {
            throw new ApplicationRuleViolationException(
                "A contribution amount must be positive.");
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

        var cashAsset = await _referenceData.FindAssetAsync(
            command.CashAssetId,
            cancellationToken);

        ValidateCashAsset(
            cashAsset,
            command.CashAssetId,
            command.Amount.Currency);

        var currency = await _referenceData.FindCurrencyAsync(
            command.Amount.Currency,
            cancellationToken);

        if (currency is null)
        {
            throw new ApplicationRuleViolationException(
                $"Currency '{command.Amount.Currency}' does not exist.");
        }

        if (command.HouseholdMemberId is Guid householdMemberId)
        {
            EnsureNonEmpty(
                householdMemberId,
                nameof(command.HouseholdMemberId));

            var member = await _referenceData.FindHouseholdMemberAsync(
                householdMemberId,
                cancellationToken);

            if (member is null
                || member.Id != householdMemberId
                || member.HouseholdId != command.HouseholdId
                || !member.IsActive)
            {
                throw new ApplicationRuleViolationException(
                    "The contribution member must be active and belong to the transaction household.");
            }
        }

        var quantityRawE8 = CurrencyAmountConverter.ToQuantityRawE8(
            command.Amount,
            currency);

        var recordedAtUtc = _timeProvider.GetUtcNow();
        var transaction = LedgerTransaction.CreateDraft(
            Guid.NewGuid(),
            command.HouseholdId,
            TransactionType.Contribution,
            recordedAtUtc,
            executionDate: command.ExecutionDate,
            externalReference: command.ExternalReference,
            note: command.Note);

        transaction.AddEntry(
            command.PortfolioId,
            command.AccountId,
            command.CashAssetId,
            QuantityDelta.FromRaw(quantityRawE8),
            EntryRole.Principal);

        transaction.AttachCashFlowDetail(
            command.Category,
            command.HouseholdMemberId);

        transaction.Post(recordedAtUtc);

        await _postingStore.SavePostedTransactionAsync(
            transaction,
            Array.Empty<AssetLot>(),
            cancellationToken);

        return new RecordContributionResult(transaction.Id);
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
