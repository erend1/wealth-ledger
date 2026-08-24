using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using WealthLedger.Domain.Assets;
using WealthLedger.Domain.Ledger;
using WealthLedger.Domain.Lots;
using WealthLedger.Domain.Portfolios;

namespace WealthLedger.Infrastructure.Persistence.Mapping;

internal static class StableCodeMappings
{
    internal static readonly ValueConverter<AssetType, string> AssetTypeConverter = new(
        value => AssetTypeToCode(value),
        value => AssetTypeFromCode(value));

    internal static readonly ValueConverter<AssetUnit, string> AssetUnitConverter = new(
        value => AssetUnitToCode(value),
        value => AssetUnitFromCode(value));

    internal static readonly ValueConverter<LotTrackingMode, string> LotTrackingModeConverter = new(
        value => LotTrackingModeToCode(value),
        value => LotTrackingModeFromCode(value));

    internal static readonly ValueConverter<InstitutionType, string> InstitutionTypeConverter = new(
        value => InstitutionTypeToCode(value),
        value => InstitutionTypeFromCode(value));

    internal static readonly ValueConverter<PortfolioStatus, string> PortfolioStatusConverter = new(
        value => PortfolioStatusToCode(value),
        value => PortfolioStatusFromCode(value));

    internal static readonly ValueConverter<AccountType, string> AccountTypeConverter = new(
        value => AccountTypeToCode(value),
        value => AccountTypeFromCode(value));

    internal static readonly ValueConverter<TransactionType, string> TransactionTypeConverter = new(
        value => TransactionTypeToCode(value),
        value => TransactionTypeFromCode(value));

    internal static readonly ValueConverter<TransactionStatus, string> TransactionStatusConverter = new(
        value => TransactionStatusToCode(value),
        value => TransactionStatusFromCode(value));

    internal static readonly ValueConverter<EntryRole, string> EntryRoleConverter = new(
        value => EntryRoleToCode(value),
        value => EntryRoleFromCode(value));

    internal static readonly ValueConverter<CashFlowCategory, string> CashFlowCategoryConverter = new(
        value => CashFlowCategoryToCode(value),
        value => CashFlowCategoryFromCode(value));

    internal static readonly ValueConverter<CostType, string> CostTypeConverter = new(
        value => CostTypeToCode(value),
        value => CostTypeFromCode(value));

    internal static readonly ValueConverter<CostTreatment, string> CostTreatmentConverter = new(
        value => CostTreatmentToCode(value),
        value => CostTreatmentFromCode(value));

    internal static readonly ValueConverter<CostBasisStatus, string> CostBasisStatusConverter = new(
        value => CostBasisStatusToCode(value),
        value => CostBasisStatusFromCode(value));

    internal static string AssetTypeToCode(AssetType value)
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
            _ => ThrowUnknownValue<AssetType, string>(value)
        };

    internal static AssetType AssetTypeFromCode(string value)
        => value switch
        {
            "CASH" => AssetType.Cash,
            "CURRENCY" => AssetType.Currency,
            "FUND" => AssetType.Fund,
            "EQUITY" => AssetType.Equity,
            "PHYSICAL_GOLD" => AssetType.PhysicalGold,
            "REAL_ESTATE" => AssetType.RealEstate,
            "LAND" => AssetType.Land,
            "VEHICLE" => AssetType.Vehicle,
            "OTHER" => AssetType.Other,
            _ => ThrowUnknownCode<AssetType>(value)
        };

    internal static string AssetUnitToCode(AssetUnit value)
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
            _ => ThrowUnknownValue<AssetUnit, string>(value)
        };

    internal static AssetUnit AssetUnitFromCode(string value)
        => value switch
        {
            "CURRENCY_UNIT" => AssetUnit.CurrencyUnit,
            "FUND_UNIT" => AssetUnit.FundUnit,
            "SHARE" => AssetUnit.Share,
            "GROSS_GRAM" => AssetUnit.GrossGram,
            "PIECE" => AssetUnit.Piece,
            "PROPERTY" => AssetUnit.Property,
            "LAND_PARCEL" => AssetUnit.LandParcel,
            "VEHICLE" => AssetUnit.Vehicle,
            "OTHER" => AssetUnit.Other,
            _ => ThrowUnknownCode<AssetUnit>(value)
        };

    internal static string LotTrackingModeToCode(LotTrackingMode value)
        => value switch
        {
            LotTrackingMode.None => "NONE",
            LotTrackingMode.Optional => "OPTIONAL",
            LotTrackingMode.Required => "REQUIRED",
            _ => ThrowUnknownValue<LotTrackingMode, string>(value)
        };

    internal static LotTrackingMode LotTrackingModeFromCode(string value)
        => value switch
        {
            "NONE" => LotTrackingMode.None,
            "OPTIONAL" => LotTrackingMode.Optional,
            "REQUIRED" => LotTrackingMode.Required,
            _ => ThrowUnknownCode<LotTrackingMode>(value)
        };

    internal static string InstitutionTypeToCode(InstitutionType value)
        => value switch
        {
            InstitutionType.Bank => "BANK",
            InstitutionType.Broker => "BROKER",
            InstitutionType.AssetManager => "ASSET_MANAGER",
            InstitutionType.Jeweler => "JEWELER",
            InstitutionType.Pension => "PENSION",
            InstitutionType.Other => "OTHER",
            _ => ThrowUnknownValue<InstitutionType, string>(value)
        };

    internal static InstitutionType InstitutionTypeFromCode(string value)
        => value switch
        {
            "BANK" => InstitutionType.Bank,
            "BROKER" => InstitutionType.Broker,
            "ASSET_MANAGER" => InstitutionType.AssetManager,
            "JEWELER" => InstitutionType.Jeweler,
            "PENSION" => InstitutionType.Pension,
            "OTHER" => InstitutionType.Other,
            _ => ThrowUnknownCode<InstitutionType>(value)
        };

    internal static string PortfolioStatusToCode(PortfolioStatus value)
        => value switch
        {
            PortfolioStatus.Active => "ACTIVE",
            PortfolioStatus.Closed => "CLOSED",
            PortfolioStatus.Archived => "ARCHIVED",
            _ => ThrowUnknownValue<PortfolioStatus, string>(value)
        };

    internal static PortfolioStatus PortfolioStatusFromCode(string value)
        => value switch
        {
            "ACTIVE" => PortfolioStatus.Active,
            "CLOSED" => PortfolioStatus.Closed,
            "ARCHIVED" => PortfolioStatus.Archived,
            _ => ThrowUnknownCode<PortfolioStatus>(value)
        };

    internal static string AccountTypeToCode(AccountType value)
        => value switch
        {
            AccountType.Cash => "CASH",
            AccountType.Investment => "INVESTMENT",
            AccountType.PhysicalVault => "PHYSICAL_VAULT",
            AccountType.Pension => "PENSION",
            AccountType.PropertyRegistry => "PROPERTY_REGISTRY",
            AccountType.Other => "OTHER",
            _ => ThrowUnknownValue<AccountType, string>(value)
        };

    internal static AccountType AccountTypeFromCode(string value)
        => value switch
        {
            "CASH" => AccountType.Cash,
            "INVESTMENT" => AccountType.Investment,
            "PHYSICAL_VAULT" => AccountType.PhysicalVault,
            "PENSION" => AccountType.Pension,
            "PROPERTY_REGISTRY" => AccountType.PropertyRegistry,
            "OTHER" => AccountType.Other,
            _ => ThrowUnknownCode<AccountType>(value)
        };

    internal static string TransactionTypeToCode(TransactionType value)
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
            _ => ThrowUnknownValue<TransactionType, string>(value)
        };

    internal static TransactionType TransactionTypeFromCode(string value)
        => value switch
        {
            "CONTRIBUTION" => TransactionType.Contribution,
            "WITHDRAWAL" => TransactionType.Withdrawal,
            "BUY" => TransactionType.Buy,
            "SELL" => TransactionType.Sell,
            "TRANSFER" => TransactionType.Transfer,
            "DIVIDEND" => TransactionType.Dividend,
            "INCOME" => TransactionType.Income,
            "EXPENSE" => TransactionType.Expense,
            "FEE" => TransactionType.Fee,
            "TAX" => TransactionType.Tax,
            "CORPORATE_ACTION" => TransactionType.CorporateAction,
            "OPENING_BALANCE" => TransactionType.OpeningBalance,
            "ADJUSTMENT" => TransactionType.Adjustment,
            "REVERSAL" => TransactionType.Reversal,
            _ => ThrowUnknownCode<TransactionType>(value)
        };

    internal static string TransactionStatusToCode(TransactionStatus value)
        => value switch
        {
            TransactionStatus.Draft => "DRAFT",
            TransactionStatus.Ordered => "ORDERED",
            TransactionStatus.Posted => "POSTED",
            TransactionStatus.Cancelled => "CANCELLED",
            _ => ThrowUnknownValue<TransactionStatus, string>(value)
        };

    internal static TransactionStatus TransactionStatusFromCode(string value)
        => value switch
        {
            "DRAFT" => TransactionStatus.Draft,
            "ORDERED" => TransactionStatus.Ordered,
            "POSTED" => TransactionStatus.Posted,
            "CANCELLED" => TransactionStatus.Cancelled,
            _ => ThrowUnknownCode<TransactionStatus>(value)
        };

    internal static string EntryRoleToCode(EntryRole value)
        => value switch
        {
            EntryRole.Principal => "PRINCIPAL",
            EntryRole.Consideration => "CONSIDERATION",
            EntryRole.Transfer => "TRANSFER",
            EntryRole.Income => "INCOME",
            EntryRole.Fee => "FEE",
            EntryRole.Tax => "TAX",
            EntryRole.Adjustment => "ADJUSTMENT",
            _ => ThrowUnknownValue<EntryRole, string>(value)
        };

    internal static EntryRole EntryRoleFromCode(string value)
        => value switch
        {
            "PRINCIPAL" => EntryRole.Principal,
            "CONSIDERATION" => EntryRole.Consideration,
            "TRANSFER" => EntryRole.Transfer,
            "INCOME" => EntryRole.Income,
            "FEE" => EntryRole.Fee,
            "TAX" => EntryRole.Tax,
            "ADJUSTMENT" => EntryRole.Adjustment,
            _ => ThrowUnknownCode<EntryRole>(value)
        };

    internal static string CashFlowCategoryToCode(CashFlowCategory value)
        => value switch
        {
            CashFlowCategory.Salary => "SALARY",
            CashFlowCategory.Bonus => "BONUS",
            CashFlowCategory.AcademicIncome => "ACADEMIC_INCOME",
            CashFlowCategory.Gift => "GIFT",
            CashFlowCategory.ExternalSale => "EXTERNAL_SALE",
            CashFlowCategory.Other => "OTHER",
            _ => ThrowUnknownValue<CashFlowCategory, string>(value)
        };

    internal static CashFlowCategory CashFlowCategoryFromCode(string value)
        => value switch
        {
            "SALARY" => CashFlowCategory.Salary,
            "BONUS" => CashFlowCategory.Bonus,
            "ACADEMIC_INCOME" => CashFlowCategory.AcademicIncome,
            "GIFT" => CashFlowCategory.Gift,
            "EXTERNAL_SALE" => CashFlowCategory.ExternalSale,
            "OTHER" => CashFlowCategory.Other,
            _ => ThrowUnknownCode<CashFlowCategory>(value)
        };

    internal static string CostTypeToCode(CostType value)
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
            _ => ThrowUnknownValue<CostType, string>(value)
        };

    internal static CostType CostTypeFromCode(string value)
        => value switch
        {
            "COMMISSION" => CostType.Commission,
            "WITHHOLDING_TAX" => CostType.WithholdingTax,
            "OTHER_TAX" => CostType.OtherTax,
            "MAKING_CHARGE" => CostType.MakingCharge,
            "BROKERAGE" => CostType.Brokerage,
            "TITLE_DEED" => CostType.TitleDeed,
            "EXPERTISE" => CostType.Expertise,
            "NOTARY" => CostType.Notary,
            "INSURANCE" => CostType.Insurance,
            "OTHER" => CostType.Other,
            _ => ThrowUnknownCode<CostType>(value)
        };

    internal static string CostTreatmentToCode(CostTreatment value)
        => value switch
        {
            CostTreatment.AdditionalCashOutflow => "ADDITIONAL_CASH_OUTFLOW",
            CostTreatment.WithheldFromProceeds => "WITHHELD_FROM_PROCEEDS",
            CostTreatment.IncludedInConsideration => "INCLUDED_IN_CONSIDERATION",
            CostTreatment.InformationalOnly => "INFORMATIONAL_ONLY",
            _ => ThrowUnknownValue<CostTreatment, string>(value)
        };

    internal static CostTreatment CostTreatmentFromCode(string value)
        => value switch
        {
            "ADDITIONAL_CASH_OUTFLOW" => CostTreatment.AdditionalCashOutflow,
            "WITHHELD_FROM_PROCEEDS" => CostTreatment.WithheldFromProceeds,
            "INCLUDED_IN_CONSIDERATION" => CostTreatment.IncludedInConsideration,
            "INFORMATIONAL_ONLY" => CostTreatment.InformationalOnly,
            _ => ThrowUnknownCode<CostTreatment>(value)
        };

    internal static string CostBasisStatusToCode(CostBasisStatus value)
        => value switch
        {
            CostBasisStatus.Known => "KNOWN",
            CostBasisStatus.Unknown => "UNKNOWN",
            CostBasisStatus.NotApplicable => "NOT_APPLICABLE",
            _ => ThrowUnknownValue<CostBasisStatus, string>(value)
        };

    internal static CostBasisStatus CostBasisStatusFromCode(string value)
        => value switch
        {
            "KNOWN" => CostBasisStatus.Known,
            "UNKNOWN" => CostBasisStatus.Unknown,
            "NOT_APPLICABLE" => CostBasisStatus.NotApplicable,
            _ => ThrowUnknownCode<CostBasisStatus>(value)
        };

    private static TResult ThrowUnknownValue<TValue, TResult>(TValue value)
        => throw new InvalidOperationException(
            $"Value '{value}' has no stable persistence code for {typeof(TValue).Name}.");

    private static TValue ThrowUnknownCode<TValue>(string value)
        => throw new InvalidOperationException(
            $"Code '{value}' is not a recognized persistence code for {typeof(TValue).Name}.");
}
