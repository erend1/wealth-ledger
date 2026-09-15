using WealthLedger.Domain.Ledger;
using WealthLedger.Domain.ValueObjects;

namespace WealthLedger.Application.FundTrades;

/// <summary>
/// How entered consideration compares with the price-implied expectation.
/// </summary>
public enum DiscrepancyClassification
{
    /// <summary>Entered consideration matches the expectation exactly.</summary>
    Exact,

    /// <summary>
    /// The difference is at most one minor unit, which ordinary rounding can
    /// explain on its own.
    /// </summary>
    RoundingConsistent,

    /// <summary>
    /// The difference is larger than rounding explains. It is never silently
    /// corrected; it requires a note and stays visible on the receipt.
    /// </summary>
    Material
}

/// <summary>
/// The complete derived economic picture of one fund trade.
/// </summary>
/// <remarks>
/// Every figure here is derived for review. The posted facts remain the
/// entered quantity, price, consideration and costs.
/// </remarks>
public sealed record FundTradeEconomics(
    Money CashConsideration,
    Money AdditionalCashOutflowTotal,
    Money IncludedInConsiderationTotal,
    Money WithheldFromProceedsTotal,
    Money InformationalOnlyTotal,
    Money AdditionalFeeTotal,
    Money AdditionalTaxTotal,
    Money NetCashEffect,
    Money PriceImpliedGross,
    bool PriceImpliedGrossIsRounded,
    Money ExpectedConsideration,
    long DiscrepancyMinorUnits,
    DiscrepancyClassification Discrepancy,
    Money? AcquisitionLotCost);

/// <summary>
/// Computes the accepted cash, cost and price equations for a fund trade.
/// </summary>
/// <remarks>
/// The central accounting risk in M008 is counting a cost twice: once inside
/// the consideration the user entered and again as a separate cash entry.
/// These equations exist so that risk is resolved in exactly one place.
/// </remarks>
internal static class FundTradeEconomicsCalculator
{
    /// <summary>
    /// The largest absolute difference between entered and expected
    /// consideration that ordinary rounding can account for.
    /// </summary>
    private const long RoundingToleranceMinorUnits = 1;

    internal static FundTradeEconomics Compute(
        TransactionType tradeType,
        Quantity quantity,
        UnitPrice executedUnitPrice,
        Money cashConsideration,
        IReadOnlyList<FundTradeCostInput> costs,
        int minorUnitDigits)
    {
        ArgumentNullException.ThrowIfNull(executedUnitPrice);
        ArgumentNullException.ThrowIfNull(cashConsideration);
        ArgumentNullException.ThrowIfNull(costs);

        var currency = cashConsideration.Currency;

        var additionalTotal =
            SumTreatment(
                costs,
                CostTreatment.AdditionalCashOutflow,
                currency);

        var includedTotal =
            SumTreatment(
                costs,
                CostTreatment.IncludedInConsideration,
                currency);

        var withheldTotal =
            SumTreatment(
                costs,
                CostTreatment.WithheldFromProceeds,
                currency);

        var informationalTotal =
            SumTreatment(
                costs,
                CostTreatment.InformationalOnly,
                currency);

        var additionalFeeTotal =
            SumAdditionalByRole(
                costs,
                EntryRole.Fee,
                currency);

        var additionalTaxTotal =
            SumAdditionalByRole(
                costs,
                EntryRole.Tax,
                currency);

        /*
         * Decision 5. Only an additional outflow moves cash a second time.
         * A cost included in the consideration, or withheld from proceeds, is
         * already inside the consideration figure the user entered.
         */
        var netCashEffect =
            tradeType == TransactionType.Buy
                ? cashConsideration
                    .Add(additionalTotal)
                    .Negate()
                : cashConsideration
                    .Subtract(additionalTotal);

        var priceImplied =
            PriceImpliedAmountCalculator.Compute(
                quantity,
                executedUnitPrice,
                minorUnitDigits);

        /*
         * Decision 7. A purchase pays the gross price plus whatever was
         * folded into it; a sale receives the gross price less what was
         * withheld or folded in. An additional outflow is settled separately
         * and an informational cost never moves cash, so neither belongs in
         * this comparison.
         */
        var expectedConsideration =
            tradeType == TransactionType.Buy
                ? priceImplied.RoundedAmount
                    .Add(includedTotal)
                : priceImplied.RoundedAmount
                    .Subtract(withheldTotal)
                    .Subtract(includedTotal);

        var discrepancyMinorUnits =
            checked(
                cashConsideration.MinorUnits
                - expectedConsideration.MinorUnits);

        var classification =
            Classify(discrepancyMinorUnits);

        /*
         * Decision 8. The lot records what the acquisition actually cost in
         * cash: the consideration plus any separately settled outflow.
         * Included costs are already inside the consideration and must not be
         * added a second time.
         */
        var lotCost =
            tradeType == TransactionType.Buy
                ? cashConsideration.Add(additionalTotal)
                : null;

        return new FundTradeEconomics(
            cashConsideration,
            additionalTotal,
            includedTotal,
            withheldTotal,
            informationalTotal,
            additionalFeeTotal,
            additionalTaxTotal,
            netCashEffect,
            priceImplied.RoundedAmount,
            priceImplied.IsRounded,
            expectedConsideration,
            discrepancyMinorUnits,
            classification,
            lotCost);
    }

    private static DiscrepancyClassification Classify(
        long discrepancyMinorUnits)
    {
        if (discrepancyMinorUnits == 0)
        {
            return DiscrepancyClassification.Exact;
        }

        return Math.Abs(discrepancyMinorUnits)
            <= RoundingToleranceMinorUnits
            ? DiscrepancyClassification.RoundingConsistent
            : DiscrepancyClassification.Material;
    }

    private static Money SumTreatment(
        IReadOnlyList<FundTradeCostInput> costs,
        CostTreatment treatment,
        CurrencyCode currency)
    {
        var total = Money.Zero(currency);

        foreach (var cost in costs)
        {
            if (cost.Treatment == treatment)
            {
                total = total.Add(cost.Amount);
            }
        }

        return total;
    }

    private static Money SumAdditionalByRole(
        IReadOnlyList<FundTradeCostInput> costs,
        EntryRole role,
        CurrencyCode currency)
    {
        var total = Money.Zero(currency);

        foreach (var cost in costs)
        {
            if (!FundTradeCostRules.CreatesAdditionalCashEntry(
                    cost.Treatment))
            {
                continue;
            }

            if (FundTradeCostRules.ResolveSupportingEntryRole(cost.Type)
                == role)
            {
                total = total.Add(cost.Amount);
            }
        }

        return total;
    }
}
