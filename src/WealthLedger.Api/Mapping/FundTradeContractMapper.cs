using WealthLedger.Api.Contracts;
using WealthLedger.Application.CoreLedger;
using WealthLedger.Application.FundTrades;
using WealthLedger.Domain.Ledger;
using WealthLedger.Domain.Lots;
using WealthLedger.Domain.ValueObjects;

namespace WealthLedger.Api.Mapping;

/// <summary>
/// Translates fund-trade transport contracts to and from application types.
/// </summary>
/// <remarks>
/// Vocabulary crosses the boundary as stable explicit codes rather than CLR
/// enum names or ordinals, so renaming a member cannot silently change the
/// wire format.
/// </remarks>
internal static class FundTradeContractMapper
{
    internal static FundPurchaseCommand ToCommand(
        this FundPurchaseRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        return new FundPurchaseCommand(
            request.HouseholdId,
            request.PortfolioId,
            request.FundAccountId,
            request.CashAccountId,
            request.FundAssetId,
            request.CashAssetId,
            Quantity.FromRaw(request.FundQuantityRawE8),
            UnitPrice.FromRaw(
                request.ExecutedUnitPriceRawE8,
                new CurrencyCode(request.PriceCurrencyCode)),
            Money.FromMinorUnits(
                request.CashConsiderationMinorUnits,
                new CurrencyCode(
                    request.CashConsiderationCurrencyCode)),
            request.ExecutionDate,
            request.OrderDate,
            request.SettlementDate,
            ToCosts(request.Costs),
            request.ExternalReference,
            request.Note);
    }

    internal static FundSaleCommand ToCommand(
        this FundSaleRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        return new FundSaleCommand(
            request.HouseholdId,
            request.PortfolioId,
            request.FundAccountId,
            request.CashAccountId,
            request.FundAssetId,
            request.CashAssetId,
            Quantity.FromRaw(request.FundQuantityRawE8),
            UnitPrice.FromRaw(
                request.ExecutedUnitPriceRawE8,
                new CurrencyCode(request.PriceCurrencyCode)),
            Money.FromMinorUnits(
                request.CashConsiderationMinorUnits,
                new CurrencyCode(
                    request.CashConsiderationCurrencyCode)),
            request.ExecutionDate,
            request.OrderDate,
            request.SettlementDate,
            ToCosts(request.Costs),
            request.ExternalReference,
            request.Note,
            request.ReviewedPlan
                ?.Select(line =>
                    new ReviewedLotAllocation(
                        line.AssetLotId,
                        Quantity.FromRaw(line.QuantityRawE8)))
                .ToList(),
            request.ReviewedPlanFingerprint);
    }

    internal static IReadOnlyList<FundTradeCostInput>? ToCosts(
        IReadOnlyList<FundTradeCostRequest>? costs)
        => costs
            ?.Select(cost =>
                new FundTradeCostInput(
                    ParseCostType(cost.TypeCode),
                    ParseTreatment(cost.TreatmentCode),
                    Money.FromMinorUnits(
                        cost.AmountMinorUnits,
                        new CurrencyCode(cost.CurrencyCode)),
                    cost.Note))
            .ToList();

    internal static FundPurchasePreviewResponse ToResponse(
        this FundPurchasePreview preview)
    {
        ArgumentNullException.ThrowIfNull(preview);

        return new FundPurchasePreviewResponse(
            preview.Scope.HouseholdId,
            preview.Scope.PortfolioId,
            preview.PortfolioName,
            preview.Scope.FundAccountId,
            preview.FundAccountName,
            preview.Scope.CashAccountId,
            preview.CashAccountName,
            preview.Scope.FundAssetId,
            preview.FundAssetCode,
            preview.FundAssetName,
            preview.Scope.CashAssetId,
            preview.CurrencyCode,
            preview.MinorUnitDigits,
            preview.ExecutionDate,
            preview.OrderDate,
            preview.SettlementDate,
            preview.FundQuantityRawE8,
            preview.ExecutedUnitPriceRawE8,
            preview.Economics.ToResponse(),
            preview.Costs.ToResponse(),
            preview.ExternalReference,
            preview.Note,
            preview.WarningCodes);
    }

    internal static FundSalePreviewResponse ToResponse(
        this FundSalePreview preview)
    {
        ArgumentNullException.ThrowIfNull(preview);

        return new FundSalePreviewResponse(
            preview.Scope.HouseholdId,
            preview.Scope.PortfolioId,
            preview.PortfolioName,
            preview.Scope.FundAccountId,
            preview.FundAccountName,
            preview.Scope.CashAccountId,
            preview.CashAccountName,
            preview.Scope.FundAssetId,
            preview.FundAssetCode,
            preview.FundAssetName,
            preview.Scope.CashAssetId,
            preview.CurrencyCode,
            preview.MinorUnitDigits,
            preview.ExecutionDate,
            preview.OrderDate,
            preview.SettlementDate,
            preview.FundQuantityRawE8,
            preview.ExecutedUnitPriceRawE8,
            preview.AvailableQuantityRawE8,
            preview.Economics.ToResponse(),
            preview.Costs.ToResponse(),
            preview.Plan
                .Select(line =>
                    new FundSalePlanLineResponse(
                        line.AssetLotId,
                        line.AcquiredOn,
                        line.AcquiredOn is null,
                        line.AvailableQuantityRawE8,
                        line.ConsumedQuantityRawE8,
                        ToCode(line.CostStatus),
                        line.LotCostMinorUnits,
                        line.LotCostCurrencyCode))
                .ToList(),
            preview.PlanFingerprint,
            new RealizedCostResponse(
                ToCode(preview.RealizedCost.Completeness),
                preview.RealizedCost.KnownQuantityRawE8,
                preview.RealizedCost.UnknownQuantityRawE8,
                preview.RealizedCost.KnownAmounts
                    .Select(x =>
                        new RealizedCostAmountResponse(
                            x.CurrencyCode,
                            x.MinorUnits))
                    .ToList(),
                preview.RealizedCost.MethodCode),
            preview.ExternalReference,
            preview.Note,
            preview.WarningCodes);
    }

    internal static FundTradeVerificationResponse ToResponse(
        this FundTradeVerification verification)
    {
        ArgumentNullException.ThrowIfNull(verification);

        var facts = verification.Facts;

        return new FundTradeVerificationResponse(
            facts.TransactionId,
            facts.HouseholdId,
            ToCode(facts.Type),
            ToCode(facts.Status),
            facts.OrderDate,
            facts.ExecutionDate,
            facts.SettlementDate,
            facts.PostedAtUtc,
            facts.ExternalReference,
            facts.Note,
            facts.ReversedByTransactionId,
            facts.Scope.PortfolioId,
            facts.Scope.FundAccountId,
            facts.Scope.CashAccountId,
            facts.Scope.FundAssetId,
            facts.Scope.CashAssetId,
            facts.FundQuantity.RawE8,
            facts.ExecutedUnitPrice.RawE8,
            verification.Economics.ToResponse(),
            facts.Costs.ToResponse(),
            facts.Allocations
                .Select(x =>
                    new FundTradeAllocationResponse(
                        x.AssetLotId,
                        x.AllocationId,
                        x.AcquiredOn,
                        ToCode(x.CostStatus),
                        x.LotCostBasis?.MinorUnits,
                        x.LotCostBasis?.Currency.Value,
                        x.Quantity.RawE8))
                .ToList(),
            verification.CurrentFundPositionRawE8,
            verification.RealizedCost is null
                ? null
                : new RealizedCostResponse(
                    ToCode(verification.RealizedCost.Completeness),
                    verification.RealizedCost.KnownQuantityRawE8,
                    verification.RealizedCost.UnknownQuantityRawE8,
                    verification.RealizedCost.KnownAmounts
                        .Select(x =>
                            new RealizedCostAmountResponse(
                                x.CurrencyCode,
                                x.MinorUnits))
                        .ToList(),
                    verification.RealizedCost.MethodCode,
                    verification.RealizedCost.DerivedAtUtc,
                    verification.RealizedCost.SourceSaleIsEffective),
            verification.WarningCodes);
    }

    private static FundTradeEconomicsResponse ToResponse(
        this FundTradeEconomics economics)
        => new(
            economics.CashConsideration.MinorUnits,
            economics.AdditionalCashOutflowTotal.MinorUnits,
            economics.IncludedInConsiderationTotal.MinorUnits,
            economics.WithheldFromProceedsTotal.MinorUnits,
            economics.InformationalOnlyTotal.MinorUnits,
            economics.AdditionalFeeTotal.MinorUnits,
            economics.AdditionalTaxTotal.MinorUnits,
            economics.NetCashEffect.MinorUnits,
            economics.PriceImpliedGross.MinorUnits,
            economics.PriceImpliedGrossIsRounded,
            economics.ExpectedConsideration.MinorUnits,
            economics.DiscrepancyMinorUnits,
            ToCode(economics.Discrepancy),
            economics.AcquisitionLotCost?.MinorUnits,
            economics.CashConsideration.Currency.Value);

    private static IReadOnlyList<FundTradeCostResponse> ToResponse(
        this IReadOnlyList<FundTradeCostInput> costs)
        => costs
            .Select(cost =>
                new FundTradeCostResponse(
                    ToCode(cost.Type),
                    ToCode(cost.Treatment),
                    cost.Amount.MinorUnits,
                    cost.Amount.Currency.Value,
                    cost.Note))
            .ToList();

    internal static string ToCode(CostType type)
        => FundTradeCodes.ToCostTypeCode(type);

    internal static string ToCode(CostTreatment treatment)
        => FundTradeCodes.ToTreatmentCode(treatment);

    internal static CostType ParseCostType(string code)
        => FundTradeCodes.ParseCostType(code);

    internal static CostTreatment ParseTreatment(string code)
        => FundTradeCodes.ParseTreatment(code);

    private static string ToCode(DiscrepancyClassification value)
        => value switch
        {
            DiscrepancyClassification.Exact => "EXACT",
            DiscrepancyClassification.RoundingConsistent =>
                "ROUNDING_CONSISTENT",
            DiscrepancyClassification.Material => "MATERIAL",
            _ => throw new ArgumentOutOfRangeException(nameof(value))
        };

    private static string ToCode(RealizedCostCompleteness value)
        => value switch
        {
            RealizedCostCompleteness.CompleteKnown => "COMPLETE_KNOWN",
            RealizedCostCompleteness.PartiallyKnown => "PARTIALLY_KNOWN",
            RealizedCostCompleteness.Unknown => "UNKNOWN",
            _ => throw new ArgumentOutOfRangeException(nameof(value))
        };

    private static string ToCode(CostBasisStatus value)
        => value switch
        {
            CostBasisStatus.Known => "KNOWN",
            CostBasisStatus.Unknown => "UNKNOWN",
            CostBasisStatus.NotApplicable => "NOT_APPLICABLE",
            _ => throw new ArgumentOutOfRangeException(nameof(value))
        };

    private static string ToCode(TransactionType value)
        => value switch
        {
            TransactionType.Buy => "BUY",
            TransactionType.Sell => "SELL",
            _ => throw new ArgumentOutOfRangeException(nameof(value))
        };

    private static string ToCode(TransactionStatus value)
        => value switch
        {
            TransactionStatus.Draft => "DRAFT",
            TransactionStatus.Ordered => "ORDERED",
            TransactionStatus.Posted => "POSTED",
            TransactionStatus.Cancelled => "CANCELLED",
            _ => throw new ArgumentOutOfRangeException(nameof(value))
        };
}
