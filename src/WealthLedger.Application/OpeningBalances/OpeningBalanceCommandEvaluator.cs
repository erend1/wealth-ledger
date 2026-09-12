using WealthLedger.Domain.Assets;
using WealthLedger.Domain.Lots;
using WealthLedger.Domain.Portfolios;
using WealthLedger.Domain.ValueObjects;

namespace WealthLedger.Application.OpeningBalances;

internal sealed record ValidatedOpeningBalance(
    RecordOpeningBalanceCommand Command,
    OpeningBalanceHouseholdReference Household,
    OpeningBalancePortfolioReference Portfolio,
    OpeningBalanceAccountReference Account,
    OpeningBalanceAssetReference Asset,
    OpeningBalanceCurrencyReference AssetCurrency,
    long AllocationTotalRawE8,
    IReadOnlyList<OpeningBalancePreviewLot> Lots,
    IReadOnlyList<string> WarningCodes,
    decimal? TotalFineWeightGrams)
{
    internal OpeningBalancePreview ToPreview()
        => new(
            Command.HouseholdId,
            Portfolio.PortfolioId,
            Portfolio.Code,
            Portfolio.Name,
            Account.AccountId,
            Account.Code,
            Account.Name,
            Account.Type,
            Asset.AssetId,
            Asset.Code,
            Asset.Name,
            Asset.Type,
            Asset.BaseUnit,
            AssetCurrency.Code.Value,
            Command.AsOfDate,
            Command.Quantity.RawE8,
            AllocationTotalRawE8,
            AllocationsReconcile: true,
            UnlottedCostBasisStatus:
                Asset.Type is AssetType.Cash or AssetType.Currency
                    ? CostBasisStatus.NotApplicable
                    : null,
            TotalFineWeightGrams,
            Command.ExternalReference,
            Command.Note,
            Lots,
            WarningCodes,
            IsSemanticallyEligible: true);
}

internal static class OpeningBalanceCommandEvaluator
{
    internal const string IndependentReconciliationRequiredWarning =
        "OPENING_BALANCE_INDEPENDENT_RECONCILIATION_REQUIRED";

    internal const string KnownZeroCostWarning =
        "OPENING_BALANCE_KNOWN_ZERO_COST_RECORDED";

    internal static async Task<ValidatedOpeningBalance> EvaluateAsync(
        RecordOpeningBalanceCommand normalizedCommand,
        DateTimeOffset evaluatedAtUtc,
        TimeZoneInfo operatingTimeZone,
        IOpeningBalanceReferenceReadStore referenceStore,
        IOpeningBalanceEffectiveHistoryReadStore historyStore,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(normalizedCommand);
        ArgumentNullException.ThrowIfNull(operatingTimeZone);
        ArgumentNullException.ThrowIfNull(referenceStore);
        ArgumentNullException.ThrowIfNull(historyStore);

        EnsureNonEmpty(
            normalizedCommand.HouseholdId,
            nameof(normalizedCommand.HouseholdId));
        EnsureNonEmpty(
            normalizedCommand.PortfolioId,
            nameof(normalizedCommand.PortfolioId));
        EnsureNonEmpty(
            normalizedCommand.AccountId,
            nameof(normalizedCommand.AccountId));
        EnsureNonEmpty(
            normalizedCommand.AssetId,
            nameof(normalizedCommand.AssetId));

        if (normalizedCommand.Quantity.RawE8 == 0)
        {
            throw Invalid(
                OpeningBalanceErrorCodes.QuantityInvalid,
                "Opening-balance quantity must be greater than zero.");
        }

        var currentLocalDate = DateOnly.FromDateTime(
            TimeZoneInfo.ConvertTime(
                    evaluatedAtUtc,
                    operatingTimeZone)
                .DateTime);

        if (normalizedCommand.AsOfDate > currentLocalDate)
        {
            throw Invalid(
                OpeningBalanceErrorCodes.AsOfDateInFuture,
                "Opening-balance as-of date cannot be in the future.");
        }

        var household =
            await referenceStore.FindHouseholdAsync(
                normalizedCommand.HouseholdId,
                cancellationToken)
            ?? throw NotFound("The requested household does not exist.");

        var portfolio =
            await referenceStore.FindPortfolioAsync(
                normalizedCommand.PortfolioId,
                cancellationToken)
            ?? throw NotFound("The requested portfolio does not exist.");

        var account =
            await referenceStore.FindAccountAsync(
                normalizedCommand.AccountId,
                cancellationToken)
            ?? throw NotFound("The requested account does not exist.");

        var asset =
            await referenceStore.FindAssetAsync(
                normalizedCommand.AssetId,
                cancellationToken)
            ?? throw NotFound("The requested asset does not exist.");

        ValidateScopeAndActivity(
            normalizedCommand,
            household,
            portfolio,
            account,
            asset);

        ValidateAccountInstitution(account);

        if (account.InstitutionId is Guid institutionId)
        {
            var institution =
                await referenceStore.FindInstitutionAsync(
                    institutionId,
                    cancellationToken);

            if (institution is null)
            {
                throw NotFound(
                    "The account institution does not exist.");
            }

            if (!institution.IsActive)
            {
                throw Invalid(
                    OpeningBalanceErrorCodes.ReferenceInactive,
                    "The account institution must be active.");
            }
        }

        var assetCurrencyCode = asset.BaseCurrency
            ?? throw Invalid(
                OpeningBalanceErrorCodes.ReferenceShapeInvalid,
                "The selected asset must have a base currency.");

        var assetCurrency =
            await referenceStore.FindCurrencyAsync(
                assetCurrencyCode,
                cancellationToken)
            ?? throw NotFound(
                "The selected asset base currency does not exist.");

        ValidateAssetAndAccountShape(
            household,
            account,
            asset);

        ValidateCurrencyQuantity(asset, assetCurrency, normalizedCommand.Quantity);

        var lotEvaluation =
            await ValidateLotsAsync(
                normalizedCommand,
                asset,
                referenceStore,
                cancellationToken);

        var scope = new OpeningBalanceScope(
            normalizedCommand.HouseholdId,
            normalizedCommand.PortfolioId,
            normalizedCommand.AccountId,
            normalizedCommand.AssetId);

        var history =
            await historyStore.ReadAsync(
                scope,
                cancellationToken);

        if (history.EffectiveOpeningTransactionId is Guid openingId)
        {
            throw new OpeningBalanceException(
                OpeningBalanceErrorCategory.Conflict,
                OpeningBalanceErrorCodes.AlreadyExists,
                "An effective opening balance already exists for this scope.",
                openingId);
        }

        if (history.HasOtherEffectiveHistory)
        {
            throw new OpeningBalanceException(
                OpeningBalanceErrorCategory.Conflict,
                OpeningBalanceErrorCodes.ScopeHasEffectiveHistory,
                "The selected scope already contains effective ledger history.");
        }

        var warnings = new List<string>
        {
            IndependentReconciliationRequiredWarning
        };

        if (normalizedCommand.Lots.Any(
                lot => lot.CostBasis.Status == CostBasisStatus.Known
                    && lot.CostBasis.Amount?.MinorUnits == 0))
        {
            warnings.Add(KnownZeroCostWarning);
        }

        return new ValidatedOpeningBalance(
            normalizedCommand,
            household,
            portfolio,
            account,
            asset,
            assetCurrency,
            lotEvaluation.AllocationTotalRawE8,
            lotEvaluation.Lots,
            warnings,
            lotEvaluation.TotalFineWeightGrams);
    }

    private static void ValidateScopeAndActivity(
        RecordOpeningBalanceCommand command,
        OpeningBalanceHouseholdReference household,
        OpeningBalancePortfolioReference portfolio,
        OpeningBalanceAccountReference account,
        OpeningBalanceAssetReference asset)
    {
        if (household.HouseholdId != command.HouseholdId
            || portfolio.PortfolioId != command.PortfolioId
            || account.AccountId != command.AccountId
            || asset.AssetId != command.AssetId
            || portfolio.HouseholdId != command.HouseholdId
            || account.HouseholdId != command.HouseholdId)
        {
            throw Invalid(
                OpeningBalanceErrorCodes.ReferenceShapeInvalid,
                "The opening-balance references do not share the requested scope.");
        }

        if (portfolio.Status != PortfolioStatus.Active
            || !account.IsActive
            || account.ClosedOn is not null
            || !asset.IsActive)
        {
            throw Invalid(
                OpeningBalanceErrorCodes.ReferenceInactive,
                "The selected portfolio, account, and asset must be active.");
        }

        if (account.OpenedOn is DateOnly openedOn
            && command.AsOfDate < openedOn)
        {
            throw Invalid(
                OpeningBalanceErrorCodes.ReferenceShapeInvalid,
                "Opening-balance as-of date cannot precede the account opening date.");
        }
    }

    private static void ValidateAccountInstitution(
        OpeningBalanceAccountReference account)
    {
        if (account.Type is not AccountType.Cash
            and not AccountType.Investment
            and not AccountType.PhysicalVault
            and not AccountType.Pension)
        {
            throw Invalid(
                OpeningBalanceErrorCodes.AccountAssetMismatch,
                "The selected account type is not supported for opening balances.");
        }

        if (account.Type is AccountType.Investment or AccountType.Pension
            && account.InstitutionId is null)
        {
            throw Invalid(
                OpeningBalanceErrorCodes.ReferenceShapeInvalid,
                "Investment and pension accounts require an institution.");
        }
    }

    private static void ValidateAssetAndAccountShape(
        OpeningBalanceHouseholdReference household,
        OpeningBalanceAccountReference account,
        OpeningBalanceAssetReference asset)
    {
        var shapeIsValid = asset.Type switch
        {
            AssetType.Cash =>
                asset.BaseUnit == AssetUnit.CurrencyUnit
                && asset.LotTrackingMode == LotTrackingMode.None
                && asset.BaseCurrency == household.BaseCurrency
                && account.Type is AccountType.Cash
                    or AccountType.Investment
                    or AccountType.Pension,

            AssetType.Currency =>
                asset.BaseUnit == AssetUnit.CurrencyUnit
                && asset.LotTrackingMode == LotTrackingMode.None
                && asset.BaseCurrency != household.BaseCurrency
                && account.Type is AccountType.Cash
                    or AccountType.Investment
                    or AccountType.Pension,

            AssetType.Fund =>
                asset.BaseUnit == AssetUnit.FundUnit
                && asset.LotTrackingMode is LotTrackingMode.Optional
                    or LotTrackingMode.Required
                && account.Type is AccountType.Investment
                    or AccountType.Pension,

            AssetType.Equity =>
                asset.BaseUnit == AssetUnit.Share
                && asset.LotTrackingMode is LotTrackingMode.Optional
                    or LotTrackingMode.Required
                && account.Type is AccountType.Investment
                    or AccountType.Pension,

            AssetType.PhysicalGold =>
                asset.BaseUnit == AssetUnit.GrossGram
                && asset.LotTrackingMode == LotTrackingMode.Required
                && account.Type == AccountType.PhysicalVault,

            _ => throw new OpeningBalanceException(
                OpeningBalanceErrorCategory.Validation,
                OpeningBalanceErrorCodes.AssetNotSupported,
                "The selected asset type is not supported for opening balances.")
        };

        if (!shapeIsValid)
        {
            throw Invalid(
                OpeningBalanceErrorCodes.AccountAssetMismatch,
                "The selected account and asset shape are incompatible.");
        }
    }

    private static void ValidateCurrencyQuantity(
        OpeningBalanceAssetReference asset,
        OpeningBalanceCurrencyReference currency,
        Quantity quantity)
    {
        if (asset.Type is not AssetType.Cash
            and not AssetType.Currency)
        {
            return;
        }

        if (currency.MinorUnitDigits is < 0 or > 8)
        {
            throw Invalid(
                OpeningBalanceErrorCodes.ReferenceShapeInvalid,
                "The selected currency has invalid minor-unit metadata.");
        }

        long rawE8PerMinorUnit = 1;

        for (var digit = currency.MinorUnitDigits; digit < 8; digit++)
        {
            rawE8PerMinorUnit = checked(rawE8PerMinorUnit * 10);
        }

        if (quantity.RawE8 % rawE8PerMinorUnit != 0)
        {
            throw Invalid(
                OpeningBalanceErrorCodes.QuantityInvalid,
                "Currency opening quantity must use exact minor-unit precision.");
        }
    }

    private static async Task<OpeningBalanceLotEvaluation> ValidateLotsAsync(
        RecordOpeningBalanceCommand command,
        OpeningBalanceAssetReference asset,
        IOpeningBalanceReferenceReadStore referenceStore,
        CancellationToken cancellationToken)
    {
        var isUnlotted = asset.Type is AssetType.Cash or AssetType.Currency;

        if (isUnlotted && command.Lots.Count != 0)
        {
            throw Invalid(
                OpeningBalanceErrorCodes.LotsForbidden,
                "Cash and currency opening balances cannot contain lots.");
        }

        if (!isUnlotted && command.Lots.Count == 0)
        {
            throw Invalid(
                OpeningBalanceErrorCodes.LotsRequired,
                "The selected asset requires at least one opening lot.");
        }

        if (isUnlotted)
        {
            return new OpeningBalanceLotEvaluation(
                AllocationTotalRawE8: 0,
                Lots: Array.Empty<OpeningBalancePreviewLot>(),
                TotalFineWeightGrams: null);
        }

        long allocationTotal = 0;
        decimal totalFineWeight = 0;
        var previews = new OpeningBalancePreviewLot[command.Lots.Count];
        var checkedCurrencies = new HashSet<CurrencyCode>();

        for (var index = 0; index < command.Lots.Count; index++)
        {
            var lot = command.Lots[index];

            if (lot.Quantity.RawE8 == 0)
            {
                throw Invalid(
                    OpeningBalanceErrorCodes.QuantityInvalid,
                    "Opening lot quantity must be greater than zero.");
            }

            try
            {
                allocationTotal = checked(
                    allocationTotal + lot.Quantity.RawE8);
            }
            catch (OverflowException)
            {
                throw Invalid(
                    OpeningBalanceErrorCodes.QuantityInvalid,
                    "Opening lot quantity total exceeds the supported range.");
            }

            if (lot.AcquiredOn is DateOnly acquiredOn
                && acquiredOn > command.AsOfDate)
            {
                throw Invalid(
                    OpeningBalanceErrorCodes.AcquisitionDateAfterAsOf,
                    "Lot acquisition date cannot follow the opening as-of date.");
            }

            if (lot.CostBasis.Status == CostBasisStatus.NotApplicable)
            {
                throw Invalid(
                    OpeningBalanceErrorCodes.CostBasisInvalid,
                    "Tracked opening lots require Known or Unknown cost basis.");
            }

            if (lot.CostBasis.Status == CostBasisStatus.Known)
            {
                var cost = lot.CostBasis.Amount
                    ?? throw Invalid(
                        OpeningBalanceErrorCodes.CostBasisInvalid,
                        "Known opening-lot cost requires an amount and currency.");

                if (checkedCurrencies.Add(cost.Currency)
                    && await referenceStore.FindCurrencyAsync(
                        cost.Currency,
                        cancellationToken) is null)
                {
                    throw NotFound(
                        "An opening-lot cost currency does not exist.");
                }
            }

            var goldDetail = lot.PhysicalGoldDetail;

            if (asset.Type == AssetType.PhysicalGold
                && goldDetail is null)
            {
                throw Invalid(
                    OpeningBalanceErrorCodes.GoldDetailRequired,
                    "Every physical-gold opening lot requires gold details.");
            }

            if (asset.Type != AssetType.PhysicalGold
                && goldDetail is not null)
            {
                throw Invalid(
                    OpeningBalanceErrorCodes.GoldDetailForbidden,
                    "Physical-gold details are valid only for physical-gold lots.");
            }

            decimal? fineWeight = null;

            if (goldDetail is not null)
            {
                fineWeight =
                    goldDetail.CalculateFineWeightGrams(lot.Quantity);
                totalFineWeight = checked(
                    totalFineWeight + fineWeight.Value);
            }

            previews[index] = new OpeningBalancePreviewLot(
                index,
                lot.Quantity.RawE8,
                lot.AcquiredOn,
                lot.CostBasis.Status,
                lot.CostBasis.Amount?.MinorUnits,
                lot.CostBasis.Amount?.Currency.Value,
                goldDetail?.Fineness.Ppm,
                goldDetail?.PieceCount,
                goldDetail?.Hallmark,
                goldDetail?.CertificateReference,
                goldDetail?.Note,
                fineWeight);
        }

        if (allocationTotal != command.Quantity.RawE8)
        {
            throw Invalid(
                OpeningBalanceErrorCodes.LotTotalMismatch,
                "Opening lot quantities must reconcile exactly to the entry quantity.");
        }

        return new OpeningBalanceLotEvaluation(
            allocationTotal,
            previews,
            asset.Type == AssetType.PhysicalGold
                ? totalFineWeight
                : null);
    }

    private static void EnsureNonEmpty(
        Guid value,
        string parameterName)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException(
                $"{parameterName} cannot be empty.",
                parameterName);
        }
    }

    private static OpeningBalanceException NotFound(string message)
        => new(
            OpeningBalanceErrorCategory.NotFound,
            OpeningBalanceErrorCodes.ReferenceNotFound,
            message);

    private static OpeningBalanceException Invalid(
        string errorCode,
        string message)
        => new(
            OpeningBalanceErrorCategory.Validation,
            errorCode,
            message);

    private sealed record OpeningBalanceLotEvaluation(
        long AllocationTotalRawE8,
        IReadOnlyList<OpeningBalancePreviewLot> Lots,
        decimal? TotalFineWeightGrams);
}
