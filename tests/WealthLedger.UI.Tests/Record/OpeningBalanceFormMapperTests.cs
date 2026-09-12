using WealthLedger.Application.Navigation;
using WealthLedger.Application.OpeningBalances;
using WealthLedger.Domain.Assets;
using WealthLedger.Domain.Lots;
using WealthLedger.Domain.Portfolios;
using WealthLedger.UI.Pages.Record;

namespace WealthLedger.UI.Tests.Record;

public sealed class OpeningBalanceFormMapperTests
{
    private static readonly Guid HouseholdId = Guid.Parse(
        "11111111-1111-1111-1111-111111111111");
    private static readonly Guid PortfolioId = Guid.Parse(
        "22222222-2222-2222-2222-222222222222");
    private static readonly Guid InvestmentAccountId = Guid.Parse(
        "33333333-3333-3333-3333-333333333333");
    private static readonly Guid CashAccountId = Guid.Parse(
        "44444444-4444-4444-4444-444444444444");
    private static readonly Guid VaultAccountId = Guid.Parse(
        "55555555-5555-5555-5555-555555555555");
    private static readonly Guid CashAssetId = Guid.Parse(
        "66666666-6666-6666-6666-666666666666");
    private static readonly Guid FundAssetId = Guid.Parse(
        "77777777-7777-7777-7777-777777777777");
    private static readonly Guid GoldAssetId = Guid.Parse(
        "88888888-8888-8888-8888-888888888888");

    [Fact]
    public void Map_CashUsesCurrencyPrecisionAndCreatesNoLot()
    {
        var form = CreateBaseForm(CashAccountId, CashAssetId, "12345,67");

        var result = OpeningBalanceFormMapper.Map(
            form,
            HouseholdId,
            CreateChoices());

        Assert.True(result.Succeeded);
        Assert.Equal(1_234_567_000_000, result.Command!.Quantity.RawE8);
        Assert.Empty(result.Command.Lots);

        form.Quantity = "12345,671";
        var invalid = OpeningBalanceFormMapper.Map(
            form,
            HouseholdId,
            CreateChoices());

        Assert.False(invalid.Succeeded);
        Assert.Contains(
            invalid.Errors,
            error => error.FieldName == nameof(form.Quantity)
                     && error.ResourceKey
                     == "Opening_Validation_CurrencyPrecision");
    }

    [Fact]
    public void Map_FundPreservesExactKnownAndUnknownLots()
    {
        var form = CreateBaseForm(
            InvestmentAccountId,
            FundAssetId,
            "123.456789");
        form.Lots =
        [
            new OpeningBalanceLotForm
            {
                Quantity = "100,000001",
                AcquiredOn = "2025-01-12",
                CostBasisStatusCode = "KNOWN",
                CostAmount = "2500,00",
                CostCurrencyCode = "TRY"
            },
            new OpeningBalanceLotForm
            {
                Quantity = "23.456788",
                CostBasisStatusCode = "UNKNOWN"
            }
        ];

        var result = OpeningBalanceFormMapper.Map(
            form,
            HouseholdId,
            CreateChoices());

        Assert.True(result.Succeeded);
        Assert.Equal(12_345_678_900, result.Command!.Quantity.RawE8);
        Assert.Equal(2, result.Command.Lots.Count);
        Assert.Equal(10_000_000_100, result.Command.Lots[0].Quantity.RawE8);
        Assert.Equal(250_000, result.Command.Lots[0].CostBasis.Amount!.MinorUnits);
        Assert.Equal(CostBasisStatus.Unknown, result.Command.Lots[1].CostBasis.Status);
        Assert.Null(result.Command.Lots[1].CostBasis.Amount);
    }

    [Fact]
    public void Map_UnknownCostRejectsPlausibleButUnsupportedAmount()
    {
        var form = CreateBaseForm(
            InvestmentAccountId,
            FundAssetId,
            "17");
        form.Lots =
        [
            new OpeningBalanceLotForm
            {
                Quantity = "17",
                CostBasisStatusCode = "UNKNOWN",
                CostAmount = "0",
                CostCurrencyCode = "TRY"
            }
        ];

        var result = OpeningBalanceFormMapper.Map(
            form,
            HouseholdId,
            CreateChoices());

        Assert.False(result.Succeeded);
        Assert.Contains(
            result.Errors,
            error => error.ResourceKey
                     == "Opening_Validation_UnknownCostHasAmount");
    }

    [Fact]
    public void Map_GoldPresetAndPreciseFinenessAreDeterministic()
    {
        var form = CreateBaseForm(VaultAccountId, GoldAssetId, "24.5");
        form.Lots =
        [
            new OpeningBalanceLotForm
            {
                Quantity = "10",
                CostBasisStatusCode = "UNKNOWN",
                FinenessChoiceCode = "22K_916",
                PieceCount = "2",
                Hallmark = "SYNTHETIC-916"
            },
            new OpeningBalanceLotForm
            {
                Quantity = "14,5",
                CostBasisStatusCode = "KNOWN",
                CostAmount = "0",
                CostCurrencyCode = "TRY",
                FinenessChoiceCode = FinenessChoice.PreciseCode,
                PreciseFinenessPerMille = "999.999",
                PieceCount = "1"
            }
        ];

        var result = OpeningBalanceFormMapper.Map(
            form,
            HouseholdId,
            CreateChoices());

        Assert.True(result.Succeeded);
        Assert.Equal(
            916_000,
            result.Command!.Lots[0].PhysicalGoldDetail!.Fineness.Ppm);
        Assert.Equal(
            999_999,
            result.Command.Lots[1].PhysicalGoldDetail!.Fineness.Ppm);
        Assert.Equal(0, result.Command.Lots[1].CostBasis.Amount!.MinorUnits);

        form.Lots[1].PreciseFinenessPerMille = "999.9999";
        var invalid = OpeningBalanceFormMapper.Map(
            form,
            HouseholdId,
            CreateChoices());

        Assert.False(invalid.Succeeded);
        Assert.Contains(
            invalid.Errors,
            error => error.ResourceKey
                     == "Opening_Validation_PreciseFineness");
    }

    [Fact]
    public void Map_AcquisitionAfterAsOfAndGoldFieldsOnFundAreRejected()
    {
        var form = CreateBaseForm(
            InvestmentAccountId,
            FundAssetId,
            "17");
        form.Lots =
        [
            new OpeningBalanceLotForm
            {
                Quantity = "17",
                AcquiredOn = "2026-02-01",
                CostBasisStatusCode = "UNKNOWN",
                FinenessChoiceCode = "22K_916"
            }
        ];

        var result = OpeningBalanceFormMapper.Map(
            form,
            HouseholdId,
            CreateChoices());

        Assert.False(result.Succeeded);
        Assert.Contains(
            result.Errors,
            error => error.ResourceKey
                     == "Opening_Validation_AcquiredAfterAsOf");
        Assert.Contains(
            result.Errors,
            error => error.ResourceKey
                     == "Opening_Validation_GoldFieldsForbidden");
    }

    [Theory]
    [InlineData("synthetic-key", true)]
    [InlineData("", false)]
    [InlineData(" ", false)]
    public void TryParseIdempotencyKey_EnforcesBoundedOpaqueValue(
        string value,
        bool expected)
    {
        Assert.Equal(
            expected,
            OpeningBalanceFormMapper.TryParseIdempotencyKey(
                value,
                out _));
    }

    private static OpeningBalanceForm CreateBaseForm(
        Guid accountId,
        Guid assetId,
        string quantity)
        => new()
        {
            PortfolioId = PortfolioId.ToString("D"),
            AccountId = accountId.ToString("D"),
            AssetId = assetId.ToString("D"),
            AsOfDate = "2026-01-31",
            Quantity = quantity,
            ExternalReference = "SYNTHETIC-STATEMENT-001",
            Note = "Synthetic opening balance from an anonymous statement.",
            IdempotencyKey = "synthetic-idempotency-key"
        };

    private static OpeningBalanceChoices CreateChoices()
    {
        var createdAt = new DateTimeOffset(
            2026,
            1,
            1,
            0,
            0,
            0,
            TimeSpan.Zero);
        var currency = new CurrencyNavigationItem("TRY", "Synthetic Lira", 2);

        return new OpeningBalanceChoices(
            new HouseholdNavigationItem(
                HouseholdId,
                "Synthetic Household",
                currency,
                createdAt),
            new NavigationPage<PortfolioNavigationItem>(
                [
                    new PortfolioNavigationItem(
                        PortfolioId,
                        HouseholdId,
                        "CORE",
                        "Synthetic Portfolio",
                        PortfolioStatus.Active,
                        createdAt,
                        null)
                ],
                null),
            new NavigationPage<AccountNavigationItem>(
                [
                    Account(InvestmentAccountId, AccountType.Investment),
                    Account(CashAccountId, AccountType.Cash),
                    Account(VaultAccountId, AccountType.PhysicalVault)
                ],
                null),
            new NavigationPage<InstitutionNavigationItem>([], null),
            new NavigationPage<CurrencyNavigationItem>([currency], null),
            new NavigationPage<AssetNavigationItem>(
                [
                    Asset(
                        CashAssetId,
                        "SYNTHETIC_CASH",
                        AssetType.Cash,
                        AssetUnit.CurrencyUnit,
                        LotTrackingMode.None),
                    Asset(
                        FundAssetId,
                        "SYNTHETIC_FUND",
                        AssetType.Fund,
                        AssetUnit.FundUnit,
                        LotTrackingMode.Required),
                    Asset(
                        GoldAssetId,
                        "SYNTHETIC_GOLD",
                        AssetType.PhysicalGold,
                        AssetUnit.GrossGram,
                        LotTrackingMode.Required)
                ],
                null));

        AccountNavigationItem Account(Guid id, AccountType type)
            => new(
                id,
                HouseholdId,
                type == AccountType.Investment
                    ? new AccountInstitutionNavigationItem(
                        Guid.Parse("99999999-9999-9999-9999-999999999999"),
                        "SYNTHETIC_BROKER",
                        "Synthetic Broker",
                        InstitutionType.Broker,
                        true)
                    : null,
                $"{type}_ACCOUNT",
                $"Synthetic {type} Account",
                type,
                true,
                null,
                null);

        AssetNavigationItem Asset(
            Guid id,
            string code,
            AssetType type,
            AssetUnit unit,
            LotTrackingMode lotTrackingMode)
            => new(
                id,
                code,
                $"Synthetic {type}",
                type,
                unit,
                "TRY",
                lotTrackingMode,
                true,
                createdAt);
    }
}
