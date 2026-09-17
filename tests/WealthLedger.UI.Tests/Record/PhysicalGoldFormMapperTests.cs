using WealthLedger.Application.Navigation;
using WealthLedger.Application.PhysicalGold;
using WealthLedger.Domain.Assets;
using WealthLedger.Domain.Lots;
using WealthLedger.Domain.Portfolios;
using WealthLedger.UI.Pages.Record;

namespace WealthLedger.UI.Tests.Record;

public sealed class PhysicalGoldFormMapperTests
{
    private static readonly Guid HouseholdId = Guid.Parse(
        "11111111-1111-1111-1111-111111111111");
    private static readonly Guid PortfolioId = Guid.Parse(
        "22222222-2222-2222-2222-222222222222");
    private static readonly Guid SourceVaultId = Guid.Parse(
        "33333333-3333-3333-3333-333333333333");
    private static readonly Guid DestinationVaultId = Guid.Parse(
        "44444444-4444-4444-4444-444444444444");
    private static readonly Guid CashAccountId = Guid.Parse(
        "55555555-5555-5555-5555-555555555555");
    private static readonly Guid GoldAssetId = Guid.Parse(
        "66666666-6666-6666-6666-666666666666");
    private static readonly Guid CashAssetId = Guid.Parse(
        "77777777-7777-7777-7777-777777777777");
    private static readonly Guid CounterpartyId = Guid.Parse(
        "88888888-8888-8888-8888-888888888888");
    private static readonly Guid FirstLotId = Guid.Parse(
        "99999999-9999-9999-9999-999999999999");
    private static readonly Guid SecondLotId = Guid.Parse(
        "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");

    [Fact]
    public void Purchase_ParsesTurkishExactValuesWithoutBinaryAuthority()
    {
        var form = TradeForm();
        form.GrossWeight = "20,12345678";
        form.FinenessChoice = "CUSTOM";
        form.CustomFinenessPermille = "916,123";
        form.PieceCount = "2";
        form.UnitPrice = "100,12345678";
        form.CashConsideration = "2000,50";
        form.Costs =
        [
            new PhysicalGoldCostForm
            {
                TypeCode = "MAKING_CHARGE",
                TreatmentCode = "INCLUDED_IN_CONSIDERATION",
                Amount = "50,25"
            }
        ];

        var result = PhysicalGoldFormMapper.MapPurchase(
            form,
            HouseholdId,
            Choices());

        Assert.True(result.Succeeded);
        var command = result.Purchase!;
        Assert.Equal(2_012_345_678L, command.GrossWeight.RawE8);
        Assert.Equal(916_123, command.Fineness.Ppm);
        Assert.Equal(10_012_345_678L, command.ExecutedUnitPrice!.RawE8);
        Assert.Equal(200_050, command.CashConsideration.MinorUnits);
        Assert.Equal(5_025, Assert.Single(command.Costs!).Amount.MinorUnits);
    }

    [Theory]
    [InlineData("916,1234", "2000", "Gold_Validation_Fineness")]
    [InlineData("916", "2000,001", "Gold_Validation_Amount")]
    [InlineData("0", "2000", "Gold_Validation_Fineness")]
    public void Purchase_RejectsUnsupportedFinenessAndMoneyPrecision(
        string fineness,
        string amount,
        string expectedResource)
    {
        var form = TradeForm();
        form.GrossWeight = "20";
        form.FinenessChoice = "CUSTOM";
        form.CustomFinenessPermille = fineness;
        form.PieceCount = "2";
        form.CashConsideration = amount;

        var result = PhysicalGoldFormMapper.MapPurchase(
            form,
            HouseholdId,
            Choices());

        Assert.False(result.Succeeded);
        Assert.Contains(result.Errors, x => x.ResourceKey == expectedResource);
    }

    [Fact]
    public void Sale_SumsOnlyExplicitSelectedGrossAndPieces()
    {
        var form = TradeForm();
        form.CashConsideration = "1000";
        form.SelectedLots =
        [
            new PhysicalGoldSelectedLotForm
            {
                AssetLotId = FirstLotId.ToString("D"),
                GrossWeight = "4,25",
                PieceCount = "1"
            },
            new PhysicalGoldSelectedLotForm
            {
                AssetLotId = SecondLotId.ToString("D"),
                GrossWeight = "3,75",
                PieceCount = "2"
            }
        ];

        var result = PhysicalGoldFormMapper.MapSale(
            form,
            HouseholdId,
            Choices(),
            Custody());

        Assert.True(result.Succeeded);
        var command = result.Sale!;
        Assert.Equal(8_00000000L, command.GrossWeight.RawE8);
        Assert.Equal(3, command.PieceCount);
        Assert.Equal([FirstLotId, SecondLotId],
            command.SelectedLots.Select(x => x.AssetLotId).ToArray());
    }

    [Fact]
    public void Transfer_MapsCashScopeOnlyForAdditionalOutflow()
    {
        var form = TransferForm();
        form.SelectedLots =
        [
            new PhysicalGoldSelectedLotForm
            {
                AssetLotId = FirstLotId.ToString("D"),
                GrossWeight = "5",
                PieceCount = "1"
            }
        ];
        form.Costs =
        [
            new PhysicalGoldCostForm
            {
                TypeCode = "INSURANCE",
                TreatmentCode = "INFORMATIONAL_ONLY",
                Amount = "10"
            }
        ];

        var informational = PhysicalGoldFormMapper.MapTransfer(
            form,
            HouseholdId,
            Choices(),
            Custody());

        Assert.True(informational.Succeeded);
        Assert.Null(informational.Transfer!.CashPortfolioId);
        Assert.Null(informational.Transfer.CashAccountId);
        Assert.Null(informational.Transfer.CashAssetId);

        form.Costs[0].TreatmentCode = "ADDITIONAL_CASH_OUTFLOW";
        var cash = PhysicalGoldFormMapper.MapTransfer(
            form,
            HouseholdId,
            Choices(),
            Custody());

        Assert.True(cash.Succeeded);
        Assert.Equal(PortfolioId, cash.Transfer!.CashPortfolioId);
        Assert.Equal(CashAccountId, cash.Transfer.CashAccountId);
        Assert.Equal(CashAssetId, cash.Transfer.CashAssetId);
    }

    [Fact]
    public void SelectedLot_MustBelongToCurrentScopeUntilReviewed()
    {
        var form = TradeForm();
        form.CashConsideration = "1000";
        form.SelectedLots =
        [
            new PhysicalGoldSelectedLotForm
            {
                AssetLotId = Guid.NewGuid().ToString("D"),
                GrossWeight = "1",
                PieceCount = "1"
            }
        ];

        var initial = PhysicalGoldFormMapper.MapSale(
            form,
            HouseholdId,
            Choices(),
            Custody());
        Assert.False(initial.Succeeded);
        Assert.Contains(initial.Errors, x =>
            x.ResourceKey == "Gold_Validation_LotUnavailable");

        form.ReviewedPlanFingerprint = "sha256-1-synthetic";
        var reviewed = PhysicalGoldFormMapper.MapSale(
            form,
            HouseholdId,
            Choices(),
            Custody());
        Assert.True(reviewed.Succeeded);
    }

    [Theory]
    [InlineData("11111111-1111-1111-1111-111111111111", true)]
    [InlineData("00000000-0000-0000-0000-000000000000", false)]
    [InlineData("not-a-key", false)]
    public void IdempotencyKey_UsesOneNonEmptyCanonicalGuid(
        string value,
        bool expected)
        => Assert.Equal(
            expected,
            PhysicalGoldFormMapper.TryParseIdempotencyKey(value, out _));

    private static PhysicalGoldForm TradeForm()
        => new()
        {
            PortfolioId = PortfolioId.ToString("D"),
            GoldAccountId = SourceVaultId.ToString("D"),
            CashAccountId = CashAccountId.ToString("D"),
            GoldAssetId = GoldAssetId.ToString("D"),
            CashAssetId = CashAssetId.ToString("D"),
            CounterpartyInstitutionId = CounterpartyId.ToString("D"),
            ExecutionDate = "2026-09-15",
            ExternalReference = "SYNTHETIC-UI-MAPPER",
            Note = "Synthetic UI mapper evidence."
        };

    private static PhysicalGoldForm TransferForm()
        => new()
        {
            SourcePortfolioId = PortfolioId.ToString("D"),
            SourceGoldAccountId = SourceVaultId.ToString("D"),
            DestinationPortfolioId = PortfolioId.ToString("D"),
            DestinationGoldAccountId = DestinationVaultId.ToString("D"),
            CashPortfolioId = PortfolioId.ToString("D"),
            CashAccountId = CashAccountId.ToString("D"),
            CashAssetId = CashAssetId.ToString("D"),
            GoldAssetId = GoldAssetId.ToString("D"),
            ExecutionDate = "2026-09-15",
            ExternalReference = "SYNTHETIC-UI-TRANSFER",
            Note = "Synthetic transfer mapper evidence."
        };

    private static PhysicalGoldChoices Choices()
    {
        var currency = new CurrencyNavigationItem("TRY", "Turkish lira", 2);
        var now = new DateTimeOffset(2026, 9, 15, 8, 0, 0, TimeSpan.Zero);
        return new PhysicalGoldChoices(
            new HouseholdNavigationItem(
                HouseholdId,
                "Synthetic Household",
                currency,
                now),
            Page(new PortfolioNavigationItem(
                PortfolioId,
                HouseholdId,
                "SYNTHETIC_PORTFOLIO",
                "Synthetic Portfolio",
                PortfolioStatus.Active,
                now,
                null)),
            Page(
                Account(SourceVaultId, "SOURCE", AccountType.PhysicalVault),
                Account(DestinationVaultId, "DESTINATION", AccountType.PhysicalVault)),
            Page(Account(CashAccountId, "CASH", AccountType.Investment)),
            Page(new InstitutionNavigationItem(
                CounterpartyId,
                "SYNTHETIC_JEWELER",
                "Synthetic Jeweler",
                InstitutionType.Jeweler,
                true)),
            Page(currency),
            Page(new AssetNavigationItem(
                GoldAssetId,
                "SYNTHETIC_GOLD",
                "Synthetic Gold",
                AssetType.PhysicalGold,
                AssetUnit.GrossGram,
                "TRY",
                LotTrackingMode.Required,
                true,
                now)),
            Page(new AssetNavigationItem(
                CashAssetId,
                "TRY",
                "Turkish lira",
                AssetType.Cash,
                AssetUnit.CurrencyUnit,
                "TRY",
                LotTrackingMode.None,
                true,
                now)));
    }

    private static AccountNavigationItem Account(
        Guid id,
        string code,
        AccountType type)
        => new(
            id,
            HouseholdId,
            type == AccountType.Investment
                ? new AccountInstitutionNavigationItem(
                    CounterpartyId,
                    "SYNTHETIC_BANK",
                    "Synthetic Bank",
                    InstitutionType.Bank,
                    true)
                : null,
            code,
            $"Synthetic {code}",
            type,
            true,
            new DateOnly(2026, 1, 1),
            null);

    private static IReadOnlyList<PhysicalGoldCustodyPosition> Custody()
        =>
        [
            Position(FirstLotId, 10_00000000L, 2, 916_000),
            Position(SecondLotId, 10_00000000L, 3, 750_000)
        ];

    private static PhysicalGoldCustodyPosition Position(
        Guid lotId,
        long gross,
        int pieces,
        int fineness)
        => new(
            PortfolioId,
            "Synthetic Portfolio",
            SourceVaultId,
            "Synthetic SOURCE",
            GoldAssetId,
            "SYNTHETIC_GOLD",
            "Synthetic Gold",
            lotId,
            gross,
            pieces,
            fineness,
            (decimal)gross * fineness / 100_000_000 / 1_000_000,
            new DateOnly(2026, 1, 1),
            CostBasisStatus.Known,
            100_000,
            "TRY",
            "916",
            "SYNTHETIC-CERTIFICATE");

    private static NavigationPage<T> Page<T>(params T[] items)
        => new(items, null);
}
