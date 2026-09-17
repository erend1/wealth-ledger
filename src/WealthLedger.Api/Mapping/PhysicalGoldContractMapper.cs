using WealthLedger.Api.Contracts;
using WealthLedger.Application.PhysicalGold;
using WealthLedger.Domain.Common;
using WealthLedger.Domain.Ledger;
using WealthLedger.Domain.Lots;
using WealthLedger.Domain.ValueObjects;

namespace WealthLedger.Api.Mapping;

internal static class PhysicalGoldContractMapper
{
    internal static PhysicalGoldPurchaseCommand ToCommand(
        this PhysicalGoldPurchaseRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        return new PhysicalGoldPurchaseCommand(
            request.HouseholdId,
            request.PortfolioId,
            request.GoldAccountId,
            request.CashAccountId,
            request.GoldAssetId,
            request.CashAssetId,
            ToQuantity(request.GrossWeightRawE8),
            ToFineness(request.FinenessPpm),
            request.PieceCount,
            ToMoney(
                request.CashConsiderationMinorUnits,
                request.CashConsiderationCurrencyCode),
            request.ExecutionDate,
            ToOptionalUnitPrice(
                request.ExecutedUnitPriceRawE8,
                request.PriceCurrencyCode),
            request.CounterpartyInstitutionId,
            request.OrderDate,
            request.SettlementDate,
            ToCosts(request.Costs),
            request.Hallmark,
            request.CertificateReference,
            request.LotNote,
            request.ExternalReference,
            request.Note);
    }

    internal static PhysicalGoldSaleCommand ToCommand(
        this PhysicalGoldSaleRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        return new PhysicalGoldSaleCommand(
            request.HouseholdId,
            request.PortfolioId,
            request.GoldAccountId,
            request.CashAccountId,
            request.GoldAssetId,
            request.CashAssetId,
            ToQuantity(request.GrossWeightRawE8),
            request.PieceCount,
            ToSelections(request.SelectedLots),
            ToMoney(
                request.CashConsiderationMinorUnits,
                request.CashConsiderationCurrencyCode),
            request.ExecutionDate,
            ToOptionalUnitPrice(
                request.ExecutedUnitPriceRawE8,
                request.PriceCurrencyCode),
            request.CounterpartyInstitutionId,
            request.OrderDate,
            request.SettlementDate,
            ToCosts(request.Costs),
            request.ExternalReference,
            request.Note,
            request.ReviewedPlanFingerprint);
    }

    internal static PhysicalGoldTransferCommand ToCommand(
        this PhysicalGoldTransferRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        return new PhysicalGoldTransferCommand(
            request.HouseholdId,
            request.SourcePortfolioId,
            request.SourceGoldAccountId,
            request.DestinationPortfolioId,
            request.DestinationGoldAccountId,
            request.GoldAssetId,
            ToQuantity(request.GrossWeightRawE8),
            request.PieceCount,
            ToSelections(request.SelectedLots),
            request.ExecutionDate,
            request.CashPortfolioId,
            request.CashAccountId,
            request.CashAssetId,
            ToCosts(request.Costs),
            request.ExternalReference,
            request.Note,
            request.ReviewedPlanFingerprint);
    }

    internal static PhysicalGoldPurchasePreviewResponse ToResponse(
        this PhysicalGoldPurchasePreview preview)
    {
        ArgumentNullException.ThrowIfNull(preview);

        return new PhysicalGoldPurchasePreviewResponse(
            preview.Scope.HouseholdId,
            preview.Scope.PortfolioId,
            preview.PortfolioName,
            preview.Scope.GoldAccountId,
            preview.GoldAccountName,
            preview.Scope.CashAccountId,
            preview.CashAccountName,
            preview.Scope.GoldAssetId,
            preview.GoldAssetCode,
            preview.GoldAssetName,
            preview.Scope.CashAssetId,
            preview.CurrencyCode,
            preview.MinorUnitDigits,
            preview.ExecutionDate,
            preview.OrderDate,
            preview.SettlementDate,
            preview.GrossWeightRawE8,
            preview.FinenessPpm,
            preview.FineWeightGrams,
            preview.PieceCount,
            preview.ExecutedUnitPriceRawE8,
            preview.CounterpartyInstitutionId,
            preview.CounterpartyName,
            preview.Economics.ToResponse(),
            preview.Costs.ToResponse(),
            preview.Hallmark,
            preview.CertificateReference,
            preview.LotNote,
            preview.ExternalReference,
            preview.Note,
            preview.WarningCodes);
    }

    internal static PhysicalGoldSalePreviewResponse ToResponse(
        this PhysicalGoldSalePreview preview)
    {
        ArgumentNullException.ThrowIfNull(preview);

        return new PhysicalGoldSalePreviewResponse(
            preview.Scope.HouseholdId,
            preview.Scope.PortfolioId,
            preview.PortfolioName,
            preview.Scope.GoldAccountId,
            preview.GoldAccountName,
            preview.Scope.CashAccountId,
            preview.CashAccountName,
            preview.Scope.GoldAssetId,
            preview.GoldAssetCode,
            preview.GoldAssetName,
            preview.Scope.CashAssetId,
            preview.CurrencyCode,
            preview.MinorUnitDigits,
            preview.ExecutionDate,
            preview.OrderDate,
            preview.SettlementDate,
            preview.GrossWeightRawE8,
            preview.PieceCount,
            preview.ExecutedUnitPriceRawE8,
            preview.CounterpartyInstitutionId,
            preview.CounterpartyName,
            preview.Economics.ToResponse(),
            preview.Costs.ToResponse(),
            preview.Plan.ToResponse(),
            preview.PlanFingerprint,
            preview.RealizedCost.ToResponse(),
            preview.ExternalReference,
            preview.Note,
            preview.WarningCodes);
    }

    internal static PhysicalGoldTransferPreviewResponse ToResponse(
        this PhysicalGoldTransferPreview preview)
    {
        ArgumentNullException.ThrowIfNull(preview);

        return new PhysicalGoldTransferPreviewResponse(
            preview.Scope.HouseholdId,
            preview.Scope.SourcePortfolioId,
            preview.SourcePortfolioName,
            preview.Scope.SourceGoldAccountId,
            preview.SourceAccountName,
            preview.Scope.DestinationPortfolioId,
            preview.DestinationPortfolioName,
            preview.Scope.DestinationGoldAccountId,
            preview.DestinationAccountName,
            preview.Scope.GoldAssetId,
            preview.GoldAssetCode,
            preview.GoldAssetName,
            preview.CurrencyCode,
            preview.MinorUnitDigits,
            preview.ExecutionDate,
            preview.GrossWeightRawE8,
            preview.PieceCount,
            preview.FineWeightGrams,
            preview.Economics.ToResponse(),
            preview.Costs.ToResponse(),
            preview.Plan.ToResponse(),
            preview.PlanFingerprint,
            preview.ExternalReference,
            preview.Note,
            preview.WarningCodes);
    }

    internal static PhysicalGoldActivityVerificationResponse ToResponse(
        this PhysicalGoldActivityVerification verification)
    {
        ArgumentNullException.ThrowIfNull(verification);
        var facts = verification.Facts;

        return new PhysicalGoldActivityVerificationResponse(
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
            facts.GoldAssetId,
            facts.GoldAssetCode,
            facts.GoldAssetName,
            facts.CashAssetId,
            facts.CurrencyCode,
            verification.MinorUnitDigits,
            facts.CounterpartyInstitutionId,
            facts.CounterpartyName,
            facts.GrossWeight.RawE8,
            facts.PieceCount,
            facts.ExecutedUnitPrice?.RawE8,
            facts.ExecutedUnitPrice?.Currency.Value,
            facts.CashConsideration?.MinorUnits,
            facts.CreatedAssetLotId,
            verification.Economics.ToResponse(),
            facts.Costs.ToResponse(),
            facts.Allocations
                .Select(ToResponse)
                .ToArray(),
            verification.CurrentCustody
                .Select(ToResponse)
                .ToArray(),
            verification.RealizedCost?.ToResponse(),
            verification.WarningCodes);
    }

    internal static PhysicalGoldCustodyInventoryResponse ToResponse(
        this PhysicalGoldCustodyInventory inventory)
    {
        ArgumentNullException.ThrowIfNull(inventory);

        return new PhysicalGoldCustodyInventoryResponse(
            inventory.HouseholdId,
            inventory.Items.Select(ToResponse).ToArray());
    }

    private static PhysicalGoldEconomicsResponse ToResponse(
        this PhysicalGoldEconomics economics)
        => new(
            economics.CashConsideration?.MinorUnits,
            economics.AdditionalCashOutflowTotal.MinorUnits,
            economics.IncludedInConsiderationTotal.MinorUnits,
            economics.WithheldFromProceedsTotal.MinorUnits,
            economics.InformationalOnlyTotal.MinorUnits,
            economics.AdditionalFeeTotal.MinorUnits,
            economics.AdditionalTaxTotal.MinorUnits,
            economics.NetCashEffect.MinorUnits,
            economics.PriceImpliedGross?.MinorUnits,
            economics.PriceImpliedGrossIsRounded,
            economics.ExpectedConsideration?.MinorUnits,
            economics.DiscrepancyMinorUnits,
            ToCode(economics.Discrepancy),
            economics.AcquisitionLotCost?.MinorUnits,
            economics.AdditionalCashOutflowTotal.Currency.Value);

    private static IReadOnlyList<PhysicalGoldCostResponse> ToResponse(
        this IReadOnlyList<PhysicalGoldCostInput> costs)
        => costs.Select(cost => new PhysicalGoldCostResponse(
                PhysicalGoldCodes.ToCostTypeCode(cost.Type),
                PhysicalGoldCodes.ToTreatmentCode(cost.Treatment),
                cost.Amount.MinorUnits,
                cost.Amount.Currency.Value,
                cost.Note))
            .ToArray();

    private static IReadOnlyList<PhysicalGoldPlanLineResponse> ToResponse(
        this IReadOnlyList<PhysicalGoldPlanLine> plan)
        => plan.Select(line => new PhysicalGoldPlanLineResponse(
                line.AssetLotId,
                line.AcquiredOn,
                line.AvailableGrossWeightRawE8,
                line.AvailablePieceCount,
                line.MovedGrossWeightRawE8,
                line.MovedPieceCount,
                line.RemainingGrossWeightRawE8,
                line.RemainingPieceCount,
                line.FinenessPpm,
                line.MovedFineWeightGrams,
                ToCode(line.CostStatus),
                line.LotCostMinorUnits,
                line.LotCostCurrencyCode,
                line.Hallmark,
                line.CertificateReference))
            .ToArray();

    private static PhysicalGoldRealizedCostResponse ToResponse(
        this PhysicalGoldRealizedCostProjection realized)
        => new(
            ToCode(realized.Completeness),
            realized.KnownQuantityRawE8,
            realized.UnknownQuantityRawE8,
            realized.KnownAmounts
                .Select(x => new PhysicalGoldRealizedCostAmountResponse(
                    x.CurrencyCode,
                    x.MinorUnits))
                .ToArray(),
            [],
            realized.MethodCode);

    private static PhysicalGoldRealizedCostResponse ToResponse(
        this PhysicalGoldRealizedCostResult realized)
        => new(
            ToCode(realized.Completeness),
            realized.KnownQuantityRawE8,
            realized.UnknownQuantityRawE8,
            realized.KnownAmounts
                .Select(x => new PhysicalGoldRealizedCostAmountResponse(
                    x.CurrencyCode,
                    x.MinorUnits))
                .ToArray(),
            realized.Lines
                .Select(x => new PhysicalGoldRealizedCostLotLineResponse(
                    x.AssetLotId,
                    x.QuantityRawE8,
                    ToCode(x.CostStatus),
                    x.KnownCostMinorUnits,
                    x.KnownCostCurrencyCode))
                .ToArray(),
            realized.MethodCode,
            realized.DerivedAtUtc,
            realized.SourceSaleIsEffective);

    private static PhysicalGoldAllocationResponse ToResponse(
        PhysicalGoldAllocationFact fact)
        => new(
            fact.AssetLotId,
            fact.AllocationId,
            fact.PortfolioId,
            fact.PortfolioName,
            fact.AccountId,
            fact.AccountName,
            fact.GrossWeightDeltaRawE8,
            fact.PieceDelta,
            fact.AcquiredOn,
            ToCode(fact.CostStatus),
            fact.LotCostBasis?.MinorUnits,
            fact.LotCostBasis?.Currency.Value,
            fact.FinenessPpm,
            fact.OriginalPieceCount,
            fact.Hallmark,
            fact.CertificateReference,
            fact.LotNote,
            fact.FineWeightDeltaGrams);

    private static PhysicalGoldCustodyPositionResponse ToResponse(
        PhysicalGoldCustodyPosition position)
        => new(
            position.PortfolioId,
            position.PortfolioName,
            position.AccountId,
            position.AccountName,
            position.GoldAssetId,
            position.GoldAssetCode,
            position.GoldAssetName,
            position.AssetLotId,
            position.GrossWeightRawE8,
            position.PieceCount,
            position.FinenessPpm,
            position.FineWeightGrams,
            position.AcquiredOn,
            ToCode(position.CostStatus),
            position.CostMinorUnits,
            position.CostCurrencyCode,
            position.Hallmark,
            position.CertificateReference);

    private static IReadOnlyList<PhysicalGoldSelectedLot> ToSelections(
        IReadOnlyList<PhysicalGoldSelectedLotRequest>? selections)
        => selections?.Select(line => new PhysicalGoldSelectedLot(
                line.AssetLotId,
                ToQuantity(line.GrossWeightRawE8),
                line.PieceCount))
            .ToArray()
            ?? [];

    private static IReadOnlyList<PhysicalGoldCostInput>? ToCosts(
        IReadOnlyList<PhysicalGoldCostRequest>? costs)
        => costs?.Select(cost => new PhysicalGoldCostInput(
                PhysicalGoldCodes.ParseCostType(cost.TypeCode),
                PhysicalGoldCodes.ParseTreatment(cost.TreatmentCode),
                ToMoney(cost.AmountMinorUnits, cost.CurrencyCode),
                cost.Note))
            .ToArray();

    private static Quantity ToQuantity(long rawE8)
    {
        try
        {
            return Quantity.FromRaw(rawE8);
        }
        catch (Exception exception)
            when (exception is ArgumentException
                or DomainRuleViolationException
                or OverflowException)
        {
            throw new PhysicalGoldException(
                PhysicalGoldErrorCategory.Validation,
                PhysicalGoldErrorCodes.QuantityInvalid,
                "Physical-gold gross weight must be a supported positive raw-E8 value.",
                innerException: exception);
        }
    }

    private static Fineness ToFineness(int ppm)
    {
        try
        {
            return new Fineness(ppm);
        }
        catch (Exception exception)
            when (exception is ArgumentException
                or DomainRuleViolationException)
        {
            throw new PhysicalGoldException(
                PhysicalGoldErrorCategory.Validation,
                PhysicalGoldErrorCodes.FinenessInvalid,
                "Physical-gold fineness must be a supported integer ppm value.",
                innerException: exception);
        }
    }

    private static Money ToMoney(long minorUnits, string? currencyCode)
        => Money.FromMinorUnits(
            minorUnits,
            ToCurrency(currencyCode));

    private static UnitPrice? ToOptionalUnitPrice(
        long? rawE8,
        string? currencyCode)
    {
        if (rawE8 is null && currencyCode is null)
        {
            return null;
        }

        if (rawE8 is null || currencyCode is null)
        {
            throw new PhysicalGoldException(
                PhysicalGoldErrorCategory.Validation,
                PhysicalGoldErrorCodes.PriceInvalid,
                "Executed price amount and currency must be supplied together.");
        }

        try
        {
            return UnitPrice.FromRaw(
                rawE8.Value,
                ToCurrency(currencyCode));
        }
        catch (Exception exception)
            when (exception is ArgumentException
                or DomainRuleViolationException
                or OverflowException)
        {
            throw new PhysicalGoldException(
                PhysicalGoldErrorCategory.Validation,
                PhysicalGoldErrorCodes.PriceInvalid,
                "Executed price must be a supported positive raw-E8 value.",
                innerException: exception);
        }
    }

    private static CurrencyCode ToCurrency(string? code)
    {
        try
        {
            return new CurrencyCode(code!);
        }
        catch (ArgumentException exception)
        {
            throw new PhysicalGoldException(
                PhysicalGoldErrorCategory.Validation,
                PhysicalGoldErrorCodes.CurrencyMismatch,
                "A valid currency code is required.",
                innerException: exception);
        }
    }

    private static string ToCode(
        PhysicalGoldDiscrepancyClassification value)
        => value switch
        {
            PhysicalGoldDiscrepancyClassification.NotAvailable =>
                "NOT_AVAILABLE",
            PhysicalGoldDiscrepancyClassification.Exact => "EXACT",
            PhysicalGoldDiscrepancyClassification.RoundingConsistent =>
                "ROUNDING_CONSISTENT",
            PhysicalGoldDiscrepancyClassification.Material => "MATERIAL",
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
            TransactionType.Transfer => "TRANSFER",
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
