using WealthLedger.Api.Contracts;
using WealthLedger.Application.Setup;
using WealthLedger.Domain.Portfolios;
using WealthLedger.Domain.ValueObjects;

namespace WealthLedger.Api.Tests;

internal static class ApiTestData
{
    internal static readonly DateOnly ExecutionDate = new(2026, 8, 24);

    internal static InitializeCoreLedgerRequest CreateSetupRequest()
        => new(
            new InitializeCurrencyRequest(
                "TRY",
                "Synthetic Currency",
                MinorUnitDigits: 2),
            HouseholdName: "Synthetic Household",
            HouseholdMemberDisplayName: "Synthetic Member",
            new InitializeInstitutionRequest(
                "SYNTHETIC_INSTITUTION",
                "Synthetic Institution",
                TypeCode: "BROKER"),
            new InitializePortfolioRequest(
                "CORE",
                "Core Portfolio"),
            new InitializeAccountRequest(
                "PRIMARY",
                "Primary Account",
                TypeCode: "INVESTMENT",
                OpenedOn: new DateOnly(2026, 1, 1)),
            new InitializeAssetRequest(
                "SYNTHETIC_CASH",
                "Synthetic Cash"),
            new InitializeAssetRequest(
                "SYNTHETIC_FUND",
                "Synthetic Fund"));

    internal static InitializeCoreLedgerCommand CreateSetupCommand()
    => new(
        new InitializeCurrencyInput(
            CurrencyCode.TRY,
            "Synthetic Currency",
            MinorUnitDigits: 2),

        HouseholdName:
            "Synthetic Household",

        HouseholdMemberDisplayName:
            "Synthetic Member",

        new InitializeInstitutionInput(
            "SYNTHETIC_INSTITUTION",
            "Synthetic Institution",
            InstitutionType.Broker),

        new InitializePortfolioInput(
            "CORE",
            "Core Portfolio"),

        new InitializeAccountInput(
            "PRIMARY",
            "Primary Account",
            AccountType.Investment,
            new DateOnly(
                2026,
                1,
                1)),

        new InitializeAssetInput(
            "SYNTHETIC_CASH",
            "Synthetic Cash"),

        new InitializeAssetInput(
            "SYNTHETIC_FUND",
            "Synthetic Fund"));
}
