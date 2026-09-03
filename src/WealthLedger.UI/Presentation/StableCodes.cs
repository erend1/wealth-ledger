using WealthLedger.Domain.Assets;
using WealthLedger.Domain.Ledger;
using WealthLedger.Domain.Lots;
using WealthLedger.Domain.Portfolios;

namespace WealthLedger.UI.Presentation;

/// <summary>
/// Families of stable enum-like codes the UI can describe.
/// </summary>
/// <remarks>
/// The family is part of the lookup key so two families may safely share a
/// code. <c>CASH</c> means an asset type in one family and an account type in
/// another, and they carry different human descriptions.
/// </remarks>
public enum StableCodeFamily
{
    AssetType,
    AssetUnit,
    LotTrackingMode,
    InstitutionType,
    AccountType,
    PortfolioStatus,
    TransactionType,
    TransactionStatus,
    EntryRole,
    CashFlowCategory,
    CostType,
    CostTreatment,
    CostBasisStatus
}

/// <summary>
/// Maps Domain enum values to the stable text codes shown to a human.
/// </summary>
/// <remarks>
/// <para>
/// These codes are contract values, not display text. They are never
/// translated, and the UI shows them only as technical detail beside a
/// localized description.
/// </para>
/// <para>
/// Each delivery mechanism owns its own mapping in this repository:
/// persistence maps for storage and the API maps for transport. This mapping
/// exists so the UI assembly does not have to reference Infrastructure or the
/// API contracts. A test pins every value here against the API's codes so the
/// three cannot drift apart silently.
/// </para>
/// </remarks>
public static class StableCodes
{
    public static string ToCode(AssetType value)
        => value switch
        {
            AssetType.Cash => "CASH",
            AssetType.Currency => "CURRENCY",
            AssetType.Fund => "FUND",
            AssetType.Equity => "EQUITY",
            AssetType.PhysicalGold => "PHYSICAL_GOLD",
            AssetType.RealEstate => "REAL_ESTATE",
            AssetType.Land => "LAND",
            AssetType.Vehicle => "VEHICLE",
            AssetType.Other => "OTHER",
            _ => throw Unsupported(value)
        };

    public static string ToCode(AssetUnit value)
        => value switch
        {
            AssetUnit.CurrencyUnit => "CURRENCY_UNIT",
            AssetUnit.FundUnit => "FUND_UNIT",
            AssetUnit.Share => "SHARE",
            AssetUnit.GrossGram => "GROSS_GRAM",
            AssetUnit.Piece => "PIECE",
            AssetUnit.Property => "PROPERTY",
            AssetUnit.LandParcel => "LAND_PARCEL",
            AssetUnit.Vehicle => "VEHICLE",
            AssetUnit.Other => "OTHER",
            _ => throw Unsupported(value)
        };

    public static string ToCode(LotTrackingMode value)
        => value switch
        {
            LotTrackingMode.None => "NONE",
            LotTrackingMode.Optional => "OPTIONAL",
            LotTrackingMode.Required => "REQUIRED",
            _ => throw Unsupported(value)
        };

    public static string ToCode(InstitutionType value)
        => value switch
        {
            InstitutionType.Bank => "BANK",
            InstitutionType.Broker => "BROKER",
            InstitutionType.AssetManager => "ASSET_MANAGER",
            InstitutionType.Jeweler => "JEWELER",
            InstitutionType.Pension => "PENSION",
            InstitutionType.Other => "OTHER",
            _ => throw Unsupported(value)
        };

    public static string ToCode(AccountType value)
        => value switch
        {
            AccountType.Cash => "CASH",
            AccountType.Investment => "INVESTMENT",
            AccountType.PhysicalVault => "PHYSICAL_VAULT",
            AccountType.Pension => "PENSION",
            AccountType.PropertyRegistry => "PROPERTY_REGISTRY",
            AccountType.Other => "OTHER",
            _ => throw Unsupported(value)
        };

    public static string ToCode(PortfolioStatus value)
        => value switch
        {
            PortfolioStatus.Active => "ACTIVE",
            PortfolioStatus.Closed => "CLOSED",
            PortfolioStatus.Archived => "ARCHIVED",
            _ => throw Unsupported(value)
        };

    public static string ToCode(TransactionType value)
        => value switch
        {
            TransactionType.Contribution => "CONTRIBUTION",
            TransactionType.Withdrawal => "WITHDRAWAL",
            TransactionType.Buy => "BUY",
            TransactionType.Sell => "SELL",
            TransactionType.Transfer => "TRANSFER",
            TransactionType.Dividend => "DIVIDEND",
            TransactionType.Income => "INCOME",
            TransactionType.Expense => "EXPENSE",
            TransactionType.Fee => "FEE",
            TransactionType.Tax => "TAX",
            TransactionType.CorporateAction => "CORPORATE_ACTION",
            TransactionType.OpeningBalance => "OPENING_BALANCE",
            TransactionType.Adjustment => "ADJUSTMENT",
            TransactionType.Reversal => "REVERSAL",
            _ => throw Unsupported(value)
        };

    public static string ToCode(TransactionStatus value)
        => value switch
        {
            TransactionStatus.Draft => "DRAFT",
            TransactionStatus.Ordered => "ORDERED",
            TransactionStatus.Posted => "POSTED",
            TransactionStatus.Cancelled => "CANCELLED",
            _ => throw Unsupported(value)
        };

    public static string ToCode(EntryRole value)
        => value switch
        {
            EntryRole.Principal => "PRINCIPAL",
            EntryRole.Consideration => "CONSIDERATION",
            EntryRole.Transfer => "TRANSFER",
            EntryRole.Income => "INCOME",
            EntryRole.Fee => "FEE",
            EntryRole.Tax => "TAX",
            EntryRole.Adjustment => "ADJUSTMENT",
            _ => throw Unsupported(value)
        };

    public static string ToCode(CashFlowCategory value)
        => value switch
        {
            CashFlowCategory.Salary => "SALARY",
            CashFlowCategory.Bonus => "BONUS",
            CashFlowCategory.AcademicIncome => "ACADEMIC_INCOME",
            CashFlowCategory.Gift => "GIFT",
            CashFlowCategory.ExternalSale => "EXTERNAL_SALE",
            CashFlowCategory.Other => "OTHER",
            _ => throw Unsupported(value)
        };

    public static string ToCode(CostType value)
        => value switch
        {
            CostType.Commission => "COMMISSION",
            CostType.WithholdingTax => "WITHHOLDING_TAX",
            CostType.OtherTax => "OTHER_TAX",
            CostType.MakingCharge => "MAKING_CHARGE",
            CostType.Brokerage => "BROKERAGE",
            CostType.TitleDeed => "TITLE_DEED",
            CostType.Expertise => "EXPERTISE",
            CostType.Notary => "NOTARY",
            CostType.Insurance => "INSURANCE",
            CostType.Other => "OTHER",
            _ => throw Unsupported(value)
        };

    public static string ToCode(CostTreatment value)
        => value switch
        {
            CostTreatment.AdditionalCashOutflow => "ADDITIONAL_CASH_OUTFLOW",
            CostTreatment.WithheldFromProceeds => "WITHHELD_FROM_PROCEEDS",
            CostTreatment.IncludedInConsideration => "INCLUDED_IN_CONSIDERATION",
            CostTreatment.InformationalOnly => "INFORMATIONAL_ONLY",
            _ => throw Unsupported(value)
        };

    public static string ToCode(CostBasisStatus value)
        => value switch
        {
            CostBasisStatus.Known => "KNOWN",
            CostBasisStatus.Unknown => "UNKNOWN",
            CostBasisStatus.NotApplicable => "NOT_APPLICABLE",
            _ => throw Unsupported(value)
        };

    private static ArgumentOutOfRangeException Unsupported<T>(T value)
        => new(
            nameof(value),
            value,
            "The value has no stable presentation code.");
}
