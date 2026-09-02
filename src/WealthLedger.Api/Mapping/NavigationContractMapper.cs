using WealthLedger.Api.Contracts;
using WealthLedger.Application.Navigation;
using WealthLedger.Domain.Assets;
using WealthLedger.Domain.Ledger;
using WealthLedger.Domain.Portfolios;

namespace WealthLedger.Api.Mapping;

internal static class NavigationContractMapper
{
    internal static HouseholdNavigationResponse ToResponse(
        this HouseholdNavigationItem item)
        => new(
            item.HouseholdId,
            item.Name,
            item.BaseCurrency.ToResponse(),
            item.CreatedAtUtc);

    internal static CurrencyNavigationResponse ToResponse(
        this CurrencyNavigationItem item)
        => new(
            item.Code,
            item.Name,
            item.MinorUnitDigits);

    internal static HouseholdMemberNavigationResponse ToResponse(
        this HouseholdMemberNavigationItem item)
        => new(
            item.HouseholdMemberId,
            item.HouseholdId,
            item.DisplayName,
            item.IsActive,
            item.CreatedAtUtc);

    internal static InstitutionNavigationResponse ToResponse(
        this InstitutionNavigationItem item)
        => new(
            item.InstitutionId,
            item.Code,
            item.Name,
            ToCode(item.Type),
            item.IsActive);

    internal static PortfolioNavigationResponse ToResponse(
        this PortfolioNavigationItem item)
        => new(
            item.PortfolioId,
            item.HouseholdId,
            item.Code,
            item.Name,
            ToCode(item.Status),
            item.CreatedAtUtc,
            item.ClosedAtUtc);

    internal static AccountNavigationResponse ToResponse(
        this AccountNavigationItem item)
        => new(
            item.AccountId,
            item.HouseholdId,
            item.Institution is null
                ? null
                : new AccountInstitutionNavigationResponse(
                    item.Institution.InstitutionId,
                    item.Institution.Code,
                    item.Institution.Name,
                    ToCode(item.Institution.Type),
                    item.Institution.IsActive),
            item.Code,
            item.Name,
            ToCode(item.Type),
            item.IsActive,
            item.OpenedOn,
            item.ClosedOn);

    internal static AssetNavigationResponse ToResponse(
        this AssetNavigationItem item)
        => new(
            item.AssetId,
            item.Code,
            item.Name,
            ToCode(item.Type),
            ToCode(item.BaseUnit),
            item.BaseCurrencyCode,
            ToCode(item.LotTrackingMode),
            item.IsActive,
            item.CreatedAtUtc);

    internal static RecentLedgerTransactionNavigationResponse ToResponse(
        this RecentLedgerTransactionNavigationItem item)
        => new(
            item.TransactionId,
            item.HouseholdId,
            ToCode(item.Type),
            ToCode(item.Status),
            item.OrderDate,
            item.ExecutionDate,
            item.SettlementDate,
            item.ExternalReference,
            item.ReversalOfTransactionId,
            item.ReversedByTransactionId,
            item.CreatedAtUtc,
            item.PostedAtUtc,
            item.EntryEffects
                .Select(
                    effect => new RecentLedgerEntryEffectNavigationResponse(
                        effect.EntryId,
                        effect.EntrySequence,
                        effect.PortfolioId,
                        effect.PortfolioCode,
                        effect.PortfolioName,
                        ToCode(effect.PortfolioStatus),
                        effect.AccountId,
                        effect.AccountCode,
                        effect.AccountName,
                        ToCode(effect.AccountType),
                        effect.AccountIsActive,
                        effect.InstitutionId,
                        effect.InstitutionCode,
                        effect.InstitutionName,
                        effect.InstitutionType is null
                            ? null
                            : ToCode(effect.InstitutionType.Value),
                        effect.InstitutionIsActive,
                        effect.AssetId,
                        effect.AssetCode,
                        effect.AssetName,
                        ToCode(effect.AssetType),
                        ToCode(effect.AssetBaseUnit),
                        effect.AssetBaseCurrencyCode,
                        ToCode(effect.AssetLotTrackingMode),
                        effect.AssetIsActive,
                        effect.QuantityDeltaRawE8,
                        ToCode(effect.Role)))
                .ToArray());

    private static string ToCode(InstitutionType value)
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

    private static string ToCode(PortfolioStatus value)
        => value switch
        {
            PortfolioStatus.Active => "ACTIVE",
            PortfolioStatus.Closed => "CLOSED",
            PortfolioStatus.Archived => "ARCHIVED",
            _ => throw Unsupported(value)
        };

    private static string ToCode(AccountType value)
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

    private static string ToCode(AssetType value)
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

    private static string ToCode(AssetUnit value)
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

    private static string ToCode(LotTrackingMode value)
        => value switch
        {
            LotTrackingMode.None => "NONE",
            LotTrackingMode.Optional => "OPTIONAL",
            LotTrackingMode.Required => "REQUIRED",
            _ => throw Unsupported(value)
        };

    private static string ToCode(TransactionType value)
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

    private static string ToCode(TransactionStatus value)
        => value switch
        {
            TransactionStatus.Draft => "DRAFT",
            TransactionStatus.Ordered => "ORDERED",
            TransactionStatus.Posted => "POSTED",
            TransactionStatus.Cancelled => "CANCELLED",
            _ => throw Unsupported(value)
        };

    private static string ToCode(EntryRole value)
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

    private static ArgumentOutOfRangeException Unsupported<T>(T value)
        => new(
            nameof(value),
            value,
            "The value has no stable API code.");
}
