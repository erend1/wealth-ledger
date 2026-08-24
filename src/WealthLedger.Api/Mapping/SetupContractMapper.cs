using WealthLedger.Api.Contracts;
using WealthLedger.Application.Setup;
using WealthLedger.Domain.Portfolios;
using WealthLedger.Domain.ValueObjects;

namespace WealthLedger.Api.Mapping;

internal static class SetupContractMapper
{
    internal static InitializeCoreLedgerCommand ToCommand(
        this InitializeCoreLedgerRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.BaseCurrency);
        ArgumentNullException.ThrowIfNull(request.Institution);
        ArgumentNullException.ThrowIfNull(request.Portfolio);
        ArgumentNullException.ThrowIfNull(request.Account);
        ArgumentNullException.ThrowIfNull(request.CashAsset);
        ArgumentNullException.ThrowIfNull(request.FundAsset);

        return new InitializeCoreLedgerCommand(
            new InitializeCurrencyInput(
                new CurrencyCode(request.BaseCurrency.Code),
                request.BaseCurrency.Name,
                request.BaseCurrency.MinorUnitDigits),
            request.HouseholdName,
            request.HouseholdMemberDisplayName,
            new InitializeInstitutionInput(
                request.Institution.Code,
                request.Institution.Name,
                ParseInstitutionType(request.Institution.TypeCode)),
            new InitializePortfolioInput(
                request.Portfolio.Code,
                request.Portfolio.Name),
            new InitializeAccountInput(
                request.Account.Code,
                request.Account.Name,
                ParseAccountType(request.Account.TypeCode),
                request.Account.OpenedOn),
            new InitializeAssetInput(
                request.CashAsset.Code,
                request.CashAsset.Name),
            new InitializeAssetInput(
                request.FundAsset.Code,
                request.FundAsset.Name));
    }

    private static InstitutionType ParseInstitutionType(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);

        return value.Trim().ToUpperInvariant() switch
        {
            "BANK" => InstitutionType.Bank,
            "BROKER" => InstitutionType.Broker,
            "ASSET_MANAGER" => InstitutionType.AssetManager,
            "JEWELER" => InstitutionType.Jeweler,
            "PENSION" => InstitutionType.Pension,
            "OTHER" => InstitutionType.Other,
            _ => throw new ArgumentException(
                "Institution type code must be one of: BANK, BROKER, "
                + "ASSET_MANAGER, JEWELER, PENSION, OTHER.",
                nameof(value))
        };
    }

    private static AccountType ParseAccountType(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);

        return value.Trim().ToUpperInvariant() switch
        {
            "CASH" => AccountType.Cash,
            "INVESTMENT" => AccountType.Investment,
            "PHYSICAL_VAULT" => AccountType.PhysicalVault,
            "PENSION" => AccountType.Pension,
            "PROPERTY_REGISTRY" => AccountType.PropertyRegistry,
            "OTHER" => AccountType.Other,
            _ => throw new ArgumentException(
                "Account type code must be one of: CASH, INVESTMENT, "
                + "PHYSICAL_VAULT, PENSION, PROPERTY_REGISTRY, OTHER.",
                nameof(value))
        };
    }
}
