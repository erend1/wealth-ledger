using WealthLedger.Api.Contracts;
using WealthLedger.Application.CoreLedger;
using WealthLedger.Domain.Ledger;
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
