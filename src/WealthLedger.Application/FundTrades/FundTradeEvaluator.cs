using WealthLedger.Application.OpeningBalances;
using WealthLedger.Domain.Assets;
using WealthLedger.Domain.Ledger;
using WealthLedger.Domain.Portfolios;
using WealthLedger.Domain.ValueObjects;

namespace WealthLedger.Application.FundTrades;

/*
 * The reference snapshot records and the read port are the ones M007
 * introduced. Decision 15 requires reusing that create-only reference
 * behaviour rather than growing a parallel one, and renaming those public
 * types would break M007's verified contract, so they are reused as they
 * stand despite their opening-balance names.
 */

/// <summary>
/// One fund trade whose references, dates, text and economics have all been
/// validated together.
/// </summary>
internal sealed record ValidatedFundTrade(
    TransactionType TradeType,
    FundTradeScope Scope,
    OpeningBalancePortfolioReference Portfolio,
    OpeningBalanceAccountReference FundAccount,
    OpeningBalanceAccountReference CashAccount,
    OpeningBalanceAssetReference FundAsset,
    OpeningBalanceAssetReference CashAsset,
    OpeningBalanceCurrencyReference Currency,
    Quantity FundQuantity,
    UnitPrice ExecutedUnitPrice,
    Money CashConsideration,
    DateOnly ExecutionDate,
    DateOnly? OrderDate,
    DateOnly? SettlementDate,
    IReadOnlyList<FundTradeCostInput> Costs,
    string? ExternalReference,
    string? Note,
    FundTradeEconomics Economics,
    IReadOnlyList<string> WarningCodes);

internal static class FundTradeEvaluator
{
    internal static async Task<ValidatedFundTrade> EvaluateAsync(
        TransactionType tradeType,
        FundTradeScope scope,
        Quantity fundQuantity,
        UnitPrice executedUnitPrice,
        Money cashConsideration,
        DateOnly executionDate,
        DateOnly? orderDate,
        DateOnly? settlementDate,
        IReadOnlyList<FundTradeCostInput>? costs,
        string? externalReference,
        string? note,
        DateTimeOffset evaluatedAtUtc,
        TimeZoneInfo operatingTimeZone,
        IOpeningBalanceReferenceReadStore referenceStore,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(executedUnitPrice);
        ArgumentNullException.ThrowIfNull(cashConsideration);
        ArgumentNullException.ThrowIfNull(operatingTimeZone);
        ArgumentNullException.ThrowIfNull(referenceStore);

        EnsureNonEmpty(scope.HouseholdId, "household");
        EnsureNonEmpty(scope.PortfolioId, "portfolio");
        EnsureNonEmpty(scope.FundAccountId, "fund account");
        EnsureNonEmpty(scope.CashAccountId, "cash account");
        EnsureNonEmpty(scope.FundAssetId, "fund asset");
        EnsureNonEmpty(scope.CashAssetId, "cash asset");

        if (scope.FundAssetId == scope.CashAssetId)
        {
            throw FundTradeException.Invalid(
                FundTradeErrorCodes.ReferenceShapeInvalid,
                "The fund asset and the cash asset must differ.");
        }

        if (fundQuantity.RawE8 <= 0)
        {
            throw FundTradeException.Invalid(
                FundTradeErrorCodes.QuantityInvalid,
                "A fund trade quantity must be greater than zero.");
        }

        /*
         * The executed price is a preserved source fact, never derived by
         * dividing consideration by units, so a zero price would be a missing
         * fact rather than a free trade.
         */
        if (executedUnitPrice.RawE8 <= 0)
        {
            throw FundTradeException.Invalid(
                FundTradeErrorCodes.PriceInvalid,
                "A fund trade requires a positive executed unit price.");
        }

        if (cashConsideration.MinorUnits <= 0)
        {
            throw FundTradeException.Invalid(
                FundTradeErrorCodes.AmountInvalid,
                "Fund trade cash consideration must be greater than zero.");
        }

        var normalizedReference =
            FundTradeCanonicalizer.NormalizeExternalReference(
                externalReference);

        var normalizedNote =
            FundTradeCanonicalizer.NormalizeNote(note);

        FundTradeCanonicalizer.EnsureProvenance(
            normalizedReference,
            normalizedNote);

        ValidateDates(
            executionDate,
            orderDate,
            settlementDate,
            evaluatedAtUtc,
            operatingTimeZone);

        var portfolio =
            await referenceStore.FindPortfolioAsync(
                scope.PortfolioId,
                cancellationToken)
            ?? throw FundTradeException.NotFound(
                "The requested portfolio does not exist.");

        var fundAccount =
            await referenceStore.FindAccountAsync(
                scope.FundAccountId,
                cancellationToken)
            ?? throw FundTradeException.NotFound(
                "The requested fund account does not exist.");

        var cashAccount =
            scope.CashAccountId == scope.FundAccountId
                ? fundAccount
                : await referenceStore.FindAccountAsync(
                      scope.CashAccountId,
                      cancellationToken)
                  ?? throw FundTradeException.NotFound(
                      "The requested cash account does not exist.");

        var fundAsset =
            await referenceStore.FindAssetAsync(
                scope.FundAssetId,
                cancellationToken)
            ?? throw FundTradeException.NotFound(
                "The requested fund asset does not exist.");

        var cashAsset =
            await referenceStore.FindAssetAsync(
                scope.CashAssetId,
                cancellationToken)
            ?? throw FundTradeException.NotFound(
                "The requested cash asset does not exist.");

        ValidateHouseholdAndActivity(
            scope,
            portfolio,
            fundAccount,
            cashAccount,
            fundAsset,
            cashAsset);

        ValidateFundAsset(fundAsset);
        ValidateCashAsset(cashAsset);
        ValidateAccountTypes(fundAccount, cashAccount);

        ValidateCurrencyAgreement(
            fundAsset,
            cashAsset,
            executedUnitPrice,
            cashConsideration);

        ValidateAccountOpening(
            fundAccount,
            cashAccount,
            executionDate);

        var currency =
            await referenceStore.FindCurrencyAsync(
                cashConsideration.Currency,
                cancellationToken)
            ?? throw FundTradeException.NotFound(
                "The trade currency does not exist.");

        var normalizedCosts =
            FundTradeCanonicalizer.NormalizeCosts(
                costs,
                tradeType,
                cashConsideration.Currency);

        var economics =
            FundTradeEconomicsCalculator.Compute(
                tradeType,
                fundQuantity,
                executedUnitPrice,
                cashConsideration,
                normalizedCosts,
                currency.MinorUnitDigits);

        var warnings =
            ValidateEconomics(
                tradeType,
                economics,
                normalizedNote);

        return new ValidatedFundTrade(
            tradeType,
            scope,
            portfolio,
            fundAccount,
            cashAccount,
            fundAsset,
            cashAsset,
            currency,
            fundQuantity,
            executedUnitPrice,
            cashConsideration,
            executionDate,
            orderDate,
            settlementDate,
            normalizedCosts,
            normalizedReference,
            normalizedNote,
            economics,
            warnings);
    }

    private static IReadOnlyList<string> ValidateEconomics(
        TransactionType tradeType,
        FundTradeEconomics economics,
        string? note)
    {
        var warnings = new List<string>();

        /*
         * A sale cannot be expected to hand over more than it grosses. A
         * negative expectation means the entered costs are inconsistent with
         * the entered price, which is a data error rather than a warning.
         */
        if (tradeType == TransactionType.Sell
            && economics.ExpectedConsideration.MinorUnits < 0)
        {
            throw FundTradeException.Invalid(
                FundTradeErrorCodes.NegativeExpectedProceeds,
                "Withheld and included costs cannot exceed the gross sale amount.");
        }

        switch (economics.Discrepancy)
        {
            case DiscrepancyClassification.Exact:
                break;

            case DiscrepancyClassification.RoundingConsistent:
                warnings.Add(
                    FundTradeWarningCodes.RoundingConsistent);
                break;

            case DiscrepancyClassification.Material:
                /*
                 * A material difference is never silently corrected. The
                 * entered facts are posted exactly as given, but only with an
                 * explanation attached, and the receipt keeps showing it.
                 */
                if (string.IsNullOrEmpty(note))
                {
                    throw FundTradeException.Invalid(
                        FundTradeErrorCodes.UnexplainedDiscrepancy,
                        "Explain the difference between entered and price-implied consideration in the note.");
                }

                warnings.Add(
                    FundTradeWarningCodes.ExplainedDiscrepancy);
                break;

            default:
                throw new ArgumentOutOfRangeException(
                    nameof(economics));
        }

        return warnings;
    }

    private static void ValidateDates(
        DateOnly executionDate,
        DateOnly? orderDate,
        DateOnly? settlementDate,
        DateTimeOffset evaluatedAtUtc,
        TimeZoneInfo operatingTimeZone)
    {
        var currentLocalDate =
            DateOnly.FromDateTime(
                TimeZoneInfo.ConvertTime(
                        evaluatedAtUtc,
                        operatingTimeZone)
                    .DateTime);

        if (executionDate > currentLocalDate
            || orderDate > currentLocalDate
            || settlementDate > currentLocalDate)
        {
            throw FundTradeException.Invalid(
                FundTradeErrorCodes.DateInFuture,
                "A completed fund trade cannot carry a future date.");
        }

        if (orderDate is not null
            && orderDate > executionDate)
        {
            throw FundTradeException.Invalid(
                FundTradeErrorCodes.DateOrderInvalid,
                "The order date cannot be later than the execution date.");
        }

        if (settlementDate is not null
            && settlementDate < executionDate)
        {
            throw FundTradeException.Invalid(
                FundTradeErrorCodes.DateOrderInvalid,
                "The settlement date cannot be earlier than the execution date.");
        }
    }

    private static void ValidateHouseholdAndActivity(
        FundTradeScope scope,
        OpeningBalancePortfolioReference portfolio,
        OpeningBalanceAccountReference fundAccount,
        OpeningBalanceAccountReference cashAccount,
        OpeningBalanceAssetReference fundAsset,
        OpeningBalanceAssetReference cashAsset)
    {
        if (portfolio.HouseholdId != scope.HouseholdId
            || fundAccount.HouseholdId != scope.HouseholdId
            || cashAccount.HouseholdId != scope.HouseholdId)
        {
            throw FundTradeException.Invalid(
                FundTradeErrorCodes.HouseholdMismatch,
                "The portfolio and both accounts must belong to the selected household.");
        }

        if (portfolio.Status != PortfolioStatus.Active)
        {
            throw FundTradeException.Invalid(
                FundTradeErrorCodes.ReferenceInactive,
                "The selected portfolio must be active.");
        }

        if (!fundAccount.IsActive
            || !cashAccount.IsActive)
        {
            throw FundTradeException.Invalid(
                FundTradeErrorCodes.ReferenceInactive,
                "Both accounts must be active.");
        }

        if (!fundAsset.IsActive
            || !cashAsset.IsActive)
        {
            throw FundTradeException.Invalid(
                FundTradeErrorCodes.ReferenceInactive,
                "Both assets must be active.");
        }
    }

    private static void ValidateFundAsset(
        OpeningBalanceAssetReference fundAsset)
    {
        if (fundAsset.Type != AssetType.Fund
            || fundAsset.BaseUnit != AssetUnit.FundUnit)
        {
            throw FundTradeException.Invalid(
                FundTradeErrorCodes.ReferenceShapeInvalid,
                "The traded asset must be a fund measured in fund units.");
        }

        if (fundAsset.LotTrackingMode == LotTrackingMode.None)
        {
            throw FundTradeException.Invalid(
                FundTradeErrorCodes.ReferenceShapeInvalid,
                "The traded fund must use lot tracking.");
        }

        /*
         * A fund without a base currency cannot state what its price and cost
         * are denominated in, so its trades cannot be checked for currency
         * agreement at all.
         */
        if (fundAsset.BaseCurrency is null)
        {
            throw FundTradeException.Invalid(
                FundTradeErrorCodes.ReferenceShapeInvalid,
                "The traded fund must declare a base currency.");
        }
    }

    private static void ValidateCashAsset(
        OpeningBalanceAssetReference cashAsset)
    {
        if (cashAsset.Type is not AssetType.Cash
            and not AssetType.Currency)
        {
            throw FundTradeException.Invalid(
                FundTradeErrorCodes.ReferenceShapeInvalid,
                "Fund trade consideration must use a cash or currency asset.");
        }

        if (cashAsset.BaseUnit != AssetUnit.CurrencyUnit
            || cashAsset.LotTrackingMode != LotTrackingMode.None)
        {
            throw FundTradeException.Invalid(
                FundTradeErrorCodes.ReferenceShapeInvalid,
                "The cash asset must use currency units without lot tracking.");
        }

        if (cashAsset.BaseCurrency is null)
        {
            throw FundTradeException.Invalid(
                FundTradeErrorCodes.ReferenceShapeInvalid,
                "The cash asset must declare a base currency.");
        }
    }

    private static void ValidateAccountTypes(
        OpeningBalanceAccountReference fundAccount,
        OpeningBalanceAccountReference cashAccount)
    {
        if (fundAccount.Type is not AccountType.Investment
            and not AccountType.Pension)
        {
            throw FundTradeException.Invalid(
                FundTradeErrorCodes.ReferenceShapeInvalid,
                "A fund position requires an investment or pension account.");
        }

        if (cashAccount.Type is not AccountType.Cash
            and not AccountType.Investment
            and not AccountType.Pension)
        {
            throw FundTradeException.Invalid(
                FundTradeErrorCodes.ReferenceShapeInvalid,
                "The cash account type does not support a cash position.");
        }

        /*
         * M007 already requires an institution for investment and pension
         * accounts, and a fund trade must not be the way that requirement is
         * bypassed.
         */
        if (fundAccount.InstitutionId is null)
        {
            throw FundTradeException.Invalid(
                FundTradeErrorCodes.ReferenceShapeInvalid,
                "An investment or pension account requires an institution.");
        }

        if (cashAccount.Type
                is AccountType.Investment
                or AccountType.Pension
            && cashAccount.InstitutionId is null)
        {
            throw FundTradeException.Invalid(
                FundTradeErrorCodes.ReferenceShapeInvalid,
                "An investment or pension account requires an institution.");
        }
    }

    private static void ValidateCurrencyAgreement(
        OpeningBalanceAssetReference fundAsset,
        OpeningBalanceAssetReference cashAsset,
        UnitPrice executedUnitPrice,
        Money cashConsideration)
    {
        /*
         * M008 has no accepted rate observation, so every currency in the
         * trade must already agree. Cross-currency trades belong to a later
         * milestone with an explicit conversion policy.
         */
        if (fundAsset.BaseCurrency != cashAsset.BaseCurrency
            || executedUnitPrice.Currency != cashConsideration.Currency
            || cashConsideration.Currency != cashAsset.BaseCurrency)
        {
            throw FundTradeException.Invalid(
                FundTradeErrorCodes.CurrencyMismatch,
                "The fund, cash asset, price and consideration must all use one currency.");
        }
    }

    private static void ValidateAccountOpening(
        OpeningBalanceAccountReference fundAccount,
        OpeningBalanceAccountReference cashAccount,
        DateOnly executionDate)
    {
        if (fundAccount.OpenedOn is { } fundOpened
            && executionDate < fundOpened)
        {
            throw FundTradeException.Invalid(
                FundTradeErrorCodes.DateBeforeAccountOpening,
                "The execution date precedes the fund account opening date.");
        }

        if (cashAccount.OpenedOn is { } cashOpened
            && executionDate < cashOpened)
        {
            throw FundTradeException.Invalid(
                FundTradeErrorCodes.DateBeforeAccountOpening,
                "The execution date precedes the cash account opening date.");
        }
    }

    private static void EnsureNonEmpty(
        Guid value,
        string label)
    {
        if (value == Guid.Empty)
        {
            throw FundTradeException.Invalid(
                FundTradeErrorCodes.ReferenceShapeInvalid,
                $"A fund trade requires a {label}.");
        }
    }
}
