using WealthLedger.Api.Contracts;
using WealthLedger.Application.CoreLedger;
using WealthLedger.Domain.Ledger;
using WealthLedger.Domain.Lots;
using WealthLedger.Domain.ValueObjects;

namespace WealthLedger.Api.Mapping;

internal static class ApiContractMapper
{
    internal static RecordContributionCommand ToCommand(
        this RecordContributionRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        return new RecordContributionCommand(
            request.HouseholdId,
            request.PortfolioId,
            request.AccountId,
            request.CashAssetId,
            Money.FromMinorUnits(
                request.AmountMinorUnits,
                new CurrencyCode(request.CurrencyCode)),
            ParseCashFlowCategory(request.CashFlowCategoryCode),
            EnsureExecutionDate(request.ExecutionDate),
            request.HouseholdMemberId,
            request.ExternalReference,
            request.Note);
    }

    internal static RecordFundPurchaseCommand ToCommand(
        this RecordFundPurchaseRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        return new RecordFundPurchaseCommand(
            request.HouseholdId,
            request.PortfolioId,
            request.AccountId,
            request.FundAssetId,
            request.CashAssetId,
            Quantity.FromRaw(request.FundQuantityRawE8),
            UnitPrice.FromRaw(
                request.ExecutedUnitPriceRawE8,
                new CurrencyCode(request.PriceCurrencyCode)),
            Money.FromMinorUnits(
                request.CashConsiderationMinorUnits,
                new CurrencyCode(request.CashConsiderationCurrencyCode)),
            EnsureExecutionDate(request.ExecutionDate),
            request.ExternalReference,
            request.Note);
    }

    internal static LedgerTransactionResponse ToResponse(
        this LedgerTransactionDetail detail)
    {
        ArgumentNullException.ThrowIfNull(detail);

        return new LedgerTransactionResponse(
            detail.TransactionId,
            detail.HouseholdId,
            ToCode(detail.Type),
            ToCode(detail.Status),
            detail.OrderDate,
            detail.ExecutionDate,
            detail.SettlementDate,
            detail.ExternalReference,
            detail.Note,
            detail.ReversalOfTransactionId,
            detail.CreatedAtUtc,
            detail.PostedAtUtc,

            detail.Entries
                .Select(
                    entry =>
                        new LedgerTransactionEntryResponse(
                            entry.EntryId,
                            entry.EntrySequence,
                            entry.PortfolioId,
                            entry.AccountId,
                            entry.AssetId,
                            entry.QuantityDeltaRawE8,
                            ToCode(entry.Role),
                            entry.UnitPriceRawE8,
                            entry.PriceCurrencyCode,
                            entry.CreatedAtUtc))
                .ToArray(),

            detail.CashFlow is null
                ? null
                : new LedgerTransactionCashFlowResponse(
                    ToCode(detail.CashFlow.Category),
                    detail.CashFlow.HouseholdMemberId),

            detail.Costs
                .Select(
                    cost =>
                        new LedgerTransactionCostResponse(
                            cost.CostId,
                            ToCode(cost.Type),
                            ToCode(cost.Treatment),
                            cost.AmountMinorUnits,
                            cost.CurrencyCode,
                            cost.Note))
                .ToArray(),

            detail.CreatedLots
                .Select(
                    lot =>
                        new LedgerTransactionCreatedLotResponse(
                            lot.AssetLotId,
                            lot.AssetId,
                            lot.OpeningTransactionEntryId,
                            lot.AcquiredOn,
                            lot.OriginalCostBasisMinorUnits,
                            lot.CostBasisCurrencyCode,
                            ToCode(lot.CostBasisStatus),
                            lot.CreatedAtUtc))
                .ToArray());
    }

    internal static ReversalPreviewResponse ToResponse(
        this ReversalPreviewResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        return new ReversalPreviewResponse(
            result.OriginalTransactionId,
            result.CanReverse,
            ToCode(result.EligibilityCode),
            result.ExistingReversalTransactionId,
            result.BlockingTransactionIds,
            result.InverseEntries
                .OrderBy(x => x.Sequence)
                .Select(
                    entry =>
                        new ReversalPreviewEntryResponse(
                            entry.Sequence,
                            entry.PortfolioId,
                            entry.AccountId,
                            entry.AssetId,
                            entry.QuantityDelta.RawE8,
                            ToCode(entry.Role),
                            entry.UnitPrice?.RawE8,
                            entry.UnitPrice?.Currency.Value))
                .ToArray(),
            result.InverseLotAllocations
                .OrderBy(x => x.EntrySequence)
                .ThenBy(x => x.AssetLotId)
                .Select(
                    allocation =>
                        new ReversalPreviewLotAllocationResponse(
                            allocation.AssetLotId,
                            allocation.OriginalTransactionEntryId,
                            allocation.EntrySequence,
                            allocation.QuantityDelta.RawE8))
                .ToArray());
    }

    private static string ToCode(
        TransactionType value)
    {
        return value switch
        {
            TransactionType.Contribution =>
                "CONTRIBUTION",

            TransactionType.Withdrawal =>
                "WITHDRAWAL",

            TransactionType.Buy =>
                "BUY",

            TransactionType.Sell =>
                "SELL",

            TransactionType.Transfer =>
                "TRANSFER",

            TransactionType.Dividend =>
                "DIVIDEND",

            TransactionType.Income =>
                "INCOME",

            TransactionType.Expense =>
                "EXPENSE",

            TransactionType.Fee =>
                "FEE",

            TransactionType.Tax =>
                "TAX",

            TransactionType.CorporateAction =>
                "CORPORATE_ACTION",

            TransactionType.OpeningBalance =>
                "OPENING_BALANCE",

            TransactionType.Adjustment =>
                "ADJUSTMENT",

            TransactionType.Reversal =>
                "REVERSAL",

            _ => throw new ArgumentOutOfRangeException(
                nameof(value),
                value,
                "Unsupported transaction type.")
        };
    }

    private static string ToCode(
        TransactionStatus value)
    {
        return value switch
        {
            TransactionStatus.Draft =>
                "DRAFT",

            TransactionStatus.Ordered =>
                "ORDERED",

            TransactionStatus.Posted =>
                "POSTED",

            TransactionStatus.Cancelled =>
                "CANCELLED",

            _ => throw new ArgumentOutOfRangeException(
                nameof(value),
                value,
                "Unsupported transaction status.")
        };
    }

    private static string ToCode(
        EntryRole value)
    {
        return value switch
        {
            EntryRole.Principal =>
                "PRINCIPAL",

            EntryRole.Consideration =>
                "CONSIDERATION",

            EntryRole.Transfer =>
                "TRANSFER",

            EntryRole.Income =>
                "INCOME",

            EntryRole.Fee =>
                "FEE",

            EntryRole.Tax =>
                "TAX",

            EntryRole.Adjustment =>
                "ADJUSTMENT",

            _ => throw new ArgumentOutOfRangeException(
                nameof(value),
                value,
                "Unsupported entry role.")
        };
    }

    private static string ToCode(
        CashFlowCategory value)
    {
        return value switch
        {
            CashFlowCategory.Salary =>
                "SALARY",

            CashFlowCategory.Bonus =>
                "BONUS",

            CashFlowCategory.AcademicIncome =>
                "ACADEMIC_INCOME",

            CashFlowCategory.Gift =>
                "GIFT",

            CashFlowCategory.ExternalSale =>
                "EXTERNAL_SALE",

            CashFlowCategory.Other =>
                "OTHER",

            _ => throw new ArgumentOutOfRangeException(
                nameof(value),
                value,
                "Unsupported cash-flow category.")
        };
    }

    private static string ToCode(
        CostType value)
    {
        return value switch
        {
            CostType.Commission =>
                "COMMISSION",

            CostType.WithholdingTax =>
                "WITHHOLDING_TAX",

            CostType.OtherTax =>
                "OTHER_TAX",

            CostType.MakingCharge =>
                "MAKING_CHARGE",

            CostType.Brokerage =>
                "BROKERAGE",

            CostType.TitleDeed =>
                "TITLE_DEED",

            CostType.Expertise =>
                "EXPERTISE",

            CostType.Notary =>
                "NOTARY",

            CostType.Insurance =>
                "INSURANCE",

            CostType.Other =>
                "OTHER",

            _ => throw new ArgumentOutOfRangeException(
                nameof(value),
                value,
                "Unsupported cost type.")
        };
    }

    private static string ToCode(
        CostTreatment value)
    {
        return value switch
        {
            CostTreatment.AdditionalCashOutflow =>
                "ADDITIONAL_CASH_OUTFLOW",

            CostTreatment.WithheldFromProceeds =>
                "WITHHELD_FROM_PROCEEDS",

            CostTreatment.IncludedInConsideration =>
                "INCLUDED_IN_CONSIDERATION",

            CostTreatment.InformationalOnly =>
                "INFORMATIONAL_ONLY",

            _ => throw new ArgumentOutOfRangeException(
                nameof(value),
                value,
                "Unsupported cost treatment.")
        };
    }

    private static string ToCode(
        CostBasisStatus value)
    {
        return value switch
        {
            CostBasisStatus.Known =>
                "KNOWN",

            CostBasisStatus.Unknown =>
                "UNKNOWN",

            CostBasisStatus.NotApplicable =>
                "NOT_APPLICABLE",

            _ => throw new ArgumentOutOfRangeException(
                nameof(value),
                value,
                "Unsupported cost-basis status.")
        };
    }

    private static string ToCode(
        ReversalEligibilityCode value)
    {
        return value switch
        {
            ReversalEligibilityCode.Eligible =>
                "ELIGIBLE",

            ReversalEligibilityCode.NotPosted =>
                "NOT_POSTED",

            ReversalEligibilityCode.TargetIsReversal =>
                "TARGET_IS_REVERSAL",

            ReversalEligibilityCode.AlreadyReversed =>
                "ALREADY_REVERSED",

            ReversalEligibilityCode.BlockedByDependencies =>
                "BLOCKED_BY_DEPENDENCIES",

            ReversalEligibilityCode.UnsupportedPersistedShape =>
                "UNSUPPORTED_PERSISTED_SHAPE",

            _ =>
                throw new ArgumentOutOfRangeException(
                    nameof(value),
                    value,
                    "Unsupported reversal eligibility code.")
        };
    }

    private static CashFlowCategory ParseCashFlowCategory(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);

        return value.Trim().ToUpperInvariant() switch
        {
            "SALARY" => CashFlowCategory.Salary,
            "BONUS" => CashFlowCategory.Bonus,
            "ACADEMIC_INCOME" => CashFlowCategory.AcademicIncome,
            "GIFT" => CashFlowCategory.Gift,
            "EXTERNAL_SALE" => CashFlowCategory.ExternalSale,
            "OTHER" => CashFlowCategory.Other,
            _ => throw new ArgumentException(
                "Cash flow category code must be one of: "
                + "SALARY, BONUS, ACADEMIC_INCOME, GIFT, "
                + "EXTERNAL_SALE, OTHER.",
                nameof(value))
        };
    }

    private static DateOnly EnsureExecutionDate(DateOnly value)
    {
        if (value == default)
        {
            throw new ArgumentException(
                "Execution date is required.",
                nameof(value));
        }

        return value;
    }
}
