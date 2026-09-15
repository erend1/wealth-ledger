using WealthLedger.Domain.Ledger;
using WealthLedger.Domain.ValueObjects;

namespace WealthLedger.Application.PhysicalGold;

internal static class PhysicalGoldEconomicsCalculator
{
    internal static PhysicalGoldEconomics ComputeTrade(
        TransactionType tradeType,
        Quantity grossWeight,
        UnitPrice? executedUnitPrice,
        Money cashConsideration,
        IReadOnlyList<PhysicalGoldCostInput> costs,
        int minorUnitDigits)
    {
        if (tradeType is not TransactionType.Buy
            and not TransactionType.Sell)
        {
            throw new ArgumentOutOfRangeException(nameof(tradeType));
        }

        var totals = Sum(costs, cashConsideration.Currency);

        var netCash = tradeType == TransactionType.Buy
            ? cashConsideration.Add(totals.Additional).Negate()
            : cashConsideration.Subtract(totals.Additional);

        Money? priceImplied = null;
        Money? expected = null;
        long? difference = null;
        var rounded = false;
        var classification =
            PhysicalGoldDiscrepancyClassification.NotAvailable;

        if (executedUnitPrice is not null)
        {
            var calculated = PriceImpliedAmountCalculator.Compute(
                grossWeight,
                executedUnitPrice,
                minorUnitDigits);

            priceImplied = calculated.RoundedAmount;
            rounded = calculated.IsRounded;

            expected = tradeType == TransactionType.Buy
                ? priceImplied.Add(totals.Included)
                : priceImplied
                    .Subtract(totals.Withheld)
                    .Subtract(totals.Included);

            difference = checked(
                cashConsideration.MinorUnits
                - expected.MinorUnits);

            classification = difference.Value switch
            {
                0 => PhysicalGoldDiscrepancyClassification.Exact,
                >= -1 and <= 1 =>
                    PhysicalGoldDiscrepancyClassification
                        .RoundingConsistent,
                _ => PhysicalGoldDiscrepancyClassification.Material
            };
        }

        return new PhysicalGoldEconomics(
            cashConsideration,
            totals.Additional,
            totals.Included,
            totals.Withheld,
            totals.Informational,
            totals.AdditionalFee,
            totals.AdditionalTax,
            netCash,
            priceImplied,
            rounded,
            expected,
            difference,
            classification,
            tradeType == TransactionType.Buy
                ? cashConsideration.Add(totals.Additional)
                : null);
    }

    internal static PhysicalGoldEconomics ComputeTransfer(
        CurrencyCode currency,
        IReadOnlyList<PhysicalGoldCostInput> costs)
    {
        var totals = Sum(costs, currency);

        return new PhysicalGoldEconomics(
            CashConsideration: null,
            totals.Additional,
            totals.Included,
            totals.Withheld,
            totals.Informational,
            totals.AdditionalFee,
            totals.AdditionalTax,
            totals.Additional.Negate(),
            PriceImpliedGross: null,
            PriceImpliedGrossIsRounded: false,
            ExpectedConsideration: null,
            DiscrepancyMinorUnits: null,
            PhysicalGoldDiscrepancyClassification.NotAvailable,
            AcquisitionLotCost: null);
    }

    private static CostTotals Sum(
        IReadOnlyList<PhysicalGoldCostInput> costs,
        CurrencyCode currency)
    {
        var additional = Money.Zero(currency);
        var included = Money.Zero(currency);
        var withheld = Money.Zero(currency);
        var informational = Money.Zero(currency);
        var additionalFee = Money.Zero(currency);
        var additionalTax = Money.Zero(currency);

        foreach (var cost in costs)
        {
            switch (cost.Treatment)
            {
                case CostTreatment.AdditionalCashOutflow:
                    additional = additional.Add(cost.Amount);

                    if (PhysicalGoldActivityCostRules
                            .ResolveSupportingEntryRole(cost.Type)
                        == EntryRole.Tax)
                    {
                        additionalTax = additionalTax.Add(cost.Amount);
                    }
                    else
                    {
                        additionalFee = additionalFee.Add(cost.Amount);
                    }

                    break;

                case CostTreatment.IncludedInConsideration:
                    included = included.Add(cost.Amount);
                    break;

                case CostTreatment.WithheldFromProceeds:
                    withheld = withheld.Add(cost.Amount);
                    break;

                case CostTreatment.InformationalOnly:
                    informational = informational.Add(cost.Amount);
                    break;

                default:
                    throw new ArgumentOutOfRangeException(nameof(costs));
            }
        }

        return new CostTotals(
            additional,
            included,
            withheld,
            informational,
            additionalFee,
            additionalTax);
    }

    private sealed record CostTotals(
        Money Additional,
        Money Included,
        Money Withheld,
        Money Informational,
        Money AdditionalFee,
        Money AdditionalTax);
}
