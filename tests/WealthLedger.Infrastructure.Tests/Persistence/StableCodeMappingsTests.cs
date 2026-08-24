using WealthLedger.Domain.Assets;
using WealthLedger.Domain.Ledger;
using WealthLedger.Domain.Lots;
using WealthLedger.Domain.Portfolios;
using WealthLedger.Infrastructure.Persistence.Mapping;

namespace WealthLedger.Infrastructure.Tests.Persistence;

public sealed class StableCodeMappingsTests
{
    [Fact]
    public void EveryPersistedEnumValue_HasAnExplicitRoundTripCode()
    {
        AssertRoundTrip(
            new Dictionary<AssetType, string>
            {
                [AssetType.Cash] = "CASH",
                [AssetType.Currency] = "CURRENCY",
                [AssetType.Fund] = "FUND",
                [AssetType.Equity] = "EQUITY",
                [AssetType.PhysicalGold] = "PHYSICAL_GOLD",
                [AssetType.RealEstate] = "REAL_ESTATE",
                [AssetType.Land] = "LAND",
                [AssetType.Vehicle] = "VEHICLE",
                [AssetType.Other] = "OTHER"
            },
            StableCodeMappings.AssetTypeToCode,
            StableCodeMappings.AssetTypeFromCode);

        AssertRoundTrip(
            new Dictionary<AssetUnit, string>
            {
                [AssetUnit.CurrencyUnit] = "CURRENCY_UNIT",
                [AssetUnit.FundUnit] = "FUND_UNIT",
                [AssetUnit.Share] = "SHARE",
                [AssetUnit.GrossGram] = "GROSS_GRAM",
                [AssetUnit.Piece] = "PIECE",
                [AssetUnit.Property] = "PROPERTY",
                [AssetUnit.LandParcel] = "LAND_PARCEL",
                [AssetUnit.Vehicle] = "VEHICLE",
                [AssetUnit.Other] = "OTHER"
            },
            StableCodeMappings.AssetUnitToCode,
            StableCodeMappings.AssetUnitFromCode);

        AssertRoundTrip(
            new Dictionary<LotTrackingMode, string>
            {
                [LotTrackingMode.None] = "NONE",
                [LotTrackingMode.Optional] = "OPTIONAL",
                [LotTrackingMode.Required] = "REQUIRED"
            },
            StableCodeMappings.LotTrackingModeToCode,
            StableCodeMappings.LotTrackingModeFromCode);

        AssertRoundTrip(
            new Dictionary<InstitutionType, string>
            {
                [InstitutionType.Bank] = "BANK",
                [InstitutionType.Broker] = "BROKER",
                [InstitutionType.AssetManager] = "ASSET_MANAGER",
                [InstitutionType.Jeweler] = "JEWELER",
                [InstitutionType.Pension] = "PENSION",
                [InstitutionType.Other] = "OTHER"
            },
            StableCodeMappings.InstitutionTypeToCode,
            StableCodeMappings.InstitutionTypeFromCode);

        AssertRoundTrip(
            new Dictionary<PortfolioStatus, string>
            {
                [PortfolioStatus.Active] = "ACTIVE",
                [PortfolioStatus.Closed] = "CLOSED",
                [PortfolioStatus.Archived] = "ARCHIVED"
            },
            StableCodeMappings.PortfolioStatusToCode,
            StableCodeMappings.PortfolioStatusFromCode);

        AssertRoundTrip(
            new Dictionary<AccountType, string>
            {
                [AccountType.Cash] = "CASH",
                [AccountType.Investment] = "INVESTMENT",
                [AccountType.PhysicalVault] = "PHYSICAL_VAULT",
                [AccountType.Pension] = "PENSION",
                [AccountType.PropertyRegistry] = "PROPERTY_REGISTRY",
                [AccountType.Other] = "OTHER"
            },
            StableCodeMappings.AccountTypeToCode,
            StableCodeMappings.AccountTypeFromCode);

        AssertRoundTrip(
            new Dictionary<TransactionType, string>
            {
                [TransactionType.Contribution] = "CONTRIBUTION",
                [TransactionType.Withdrawal] = "WITHDRAWAL",
                [TransactionType.Buy] = "BUY",
                [TransactionType.Sell] = "SELL",
                [TransactionType.Transfer] = "TRANSFER",
                [TransactionType.Dividend] = "DIVIDEND",
                [TransactionType.Income] = "INCOME",
                [TransactionType.Expense] = "EXPENSE",
                [TransactionType.Fee] = "FEE",
                [TransactionType.Tax] = "TAX",
                [TransactionType.CorporateAction] = "CORPORATE_ACTION",
                [TransactionType.OpeningBalance] = "OPENING_BALANCE",
                [TransactionType.Adjustment] = "ADJUSTMENT",
                [TransactionType.Reversal] = "REVERSAL"
            },
            StableCodeMappings.TransactionTypeToCode,
            StableCodeMappings.TransactionTypeFromCode);

        AssertRoundTrip(
            new Dictionary<TransactionStatus, string>
            {
                [TransactionStatus.Draft] = "DRAFT",
                [TransactionStatus.Ordered] = "ORDERED",
                [TransactionStatus.Posted] = "POSTED",
                [TransactionStatus.Cancelled] = "CANCELLED"
            },
            StableCodeMappings.TransactionStatusToCode,
            StableCodeMappings.TransactionStatusFromCode);

        AssertRoundTrip(
            new Dictionary<EntryRole, string>
            {
                [EntryRole.Principal] = "PRINCIPAL",
                [EntryRole.Consideration] = "CONSIDERATION",
                [EntryRole.Transfer] = "TRANSFER",
                [EntryRole.Income] = "INCOME",
                [EntryRole.Fee] = "FEE",
                [EntryRole.Tax] = "TAX",
                [EntryRole.Adjustment] = "ADJUSTMENT"
            },
            StableCodeMappings.EntryRoleToCode,
            StableCodeMappings.EntryRoleFromCode);

        AssertRoundTrip(
            new Dictionary<CashFlowCategory, string>
            {
                [CashFlowCategory.Salary] = "SALARY",
                [CashFlowCategory.Bonus] = "BONUS",
                [CashFlowCategory.AcademicIncome] = "ACADEMIC_INCOME",
                [CashFlowCategory.Gift] = "GIFT",
                [CashFlowCategory.ExternalSale] = "EXTERNAL_SALE",
                [CashFlowCategory.Other] = "OTHER"
            },
            StableCodeMappings.CashFlowCategoryToCode,
            StableCodeMappings.CashFlowCategoryFromCode);

        AssertRoundTrip(
            new Dictionary<CostType, string>
            {
                [CostType.Commission] = "COMMISSION",
                [CostType.WithholdingTax] = "WITHHOLDING_TAX",
                [CostType.OtherTax] = "OTHER_TAX",
                [CostType.MakingCharge] = "MAKING_CHARGE",
                [CostType.Brokerage] = "BROKERAGE",
                [CostType.TitleDeed] = "TITLE_DEED",
                [CostType.Expertise] = "EXPERTISE",
                [CostType.Notary] = "NOTARY",
                [CostType.Insurance] = "INSURANCE",
                [CostType.Other] = "OTHER"
            },
            StableCodeMappings.CostTypeToCode,
            StableCodeMappings.CostTypeFromCode);

        AssertRoundTrip(
            new Dictionary<CostTreatment, string>
            {
                [CostTreatment.AdditionalCashOutflow] = "ADDITIONAL_CASH_OUTFLOW",
                [CostTreatment.WithheldFromProceeds] = "WITHHELD_FROM_PROCEEDS",
                [CostTreatment.IncludedInConsideration] = "INCLUDED_IN_CONSIDERATION",
                [CostTreatment.InformationalOnly] = "INFORMATIONAL_ONLY"
            },
            StableCodeMappings.CostTreatmentToCode,
            StableCodeMappings.CostTreatmentFromCode);

        AssertRoundTrip(
            new Dictionary<CostBasisStatus, string>
            {
                [CostBasisStatus.Known] = "KNOWN",
                [CostBasisStatus.Unknown] = "UNKNOWN",
                [CostBasisStatus.NotApplicable] = "NOT_APPLICABLE"
            },
            StableCodeMappings.CostBasisStatusToCode,
            StableCodeMappings.CostBasisStatusFromCode);
    }

    private static void AssertRoundTrip<T>(
        IReadOnlyDictionary<T, string> expected,
        Func<T, string> toCode,
        Func<string, T> fromCode)
        where T : struct, Enum
    {
        Assert.Equal(Enum.GetValues<T>().Length, expected.Count);

        foreach (var (value, code) in expected)
        {
            Assert.Equal(code, toCode(value));
            Assert.Equal(value, fromCode(code));
        }
    }
}
