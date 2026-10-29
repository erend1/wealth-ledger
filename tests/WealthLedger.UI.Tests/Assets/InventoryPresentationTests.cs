using WealthLedger.Application.Inventory;
using WealthLedger.Application.Navigation;
using WealthLedger.Domain.Assets;
using WealthLedger.Domain.Ledger;
using WealthLedger.Domain.Lots;
using WealthLedger.Domain.Portfolios;
using WealthLedger.UI.Pages.Assets;
using WealthLedger.UI.Presentation;

namespace WealthLedger.UI.Tests.Assets;

public sealed class InventoryPresentationTests
{
    private static readonly Guid HouseholdId = Guid.NewGuid();
    private static readonly Guid PortfolioId = Guid.NewGuid();
    private static readonly Guid AccountA = Guid.NewGuid();
    private static readonly Guid AccountB = Guid.NewGuid();

    [Fact]
    public void PresentPositions_GroupsByPortfolioAndAccountInUseCaseOrder()
    {
        var presenter = CreatePresenter();
        var inventory = new PositionInventory(
            HouseholdId,
            AsOf: null,
            [
                Position(AccountA, "CASH", 150_000_000_000, AssetUnit.CurrencyUnit, LotTrackingMode.None),
                Position(AccountA, "FUND", 125_000_000, AssetUnit.FundUnit, LotTrackingMode.Required),
                Position(AccountB, "GOLD", 0, AssetUnit.GrossGram, LotTrackingMode.Required)
            ]);

        var groups = presenter.PresentPositions(inventory);

        Assert.Equal(2, groups.Count);
        Assert.Equal(AccountA, groups[0].AccountId);
        Assert.Equal(["CASH", "FUND"], groups[0].Positions.Select(item => item.AssetCode));
        Assert.False(groups[0].Positions[0].IsLotTracked);
        Assert.True(groups[0].Positions[1].IsLotTracked);
        Assert.Equal("1,25 fon birimi", groups[0].Positions[1].Quantity.Text);
        Assert.Equal(125_000_000, groups[0].Positions[1].QuantityRawE8);
        Assert.Equal(DisplayState.Zero, groups[1].Positions[0].Quantity.State);
    }

    [Fact]
    public void PresentLot_KeepsGoldGrossPiecesFinenessAndFineWeightSeparate()
    {
        var presenter = CreatePresenter();
        var lot = Lot(
            CostBasisStatus.Known,
            costMinorUnits: 100_000,
            globalQuantity: 2_000_000_000,
            custody:
            [
                Custody(AccountA, 1_500_000_000, pieces: 1, fineWeight: 13.74m),
                Custody(AccountB, 500_000_000, pieces: 1, fineWeight: 4.58m)
            ],
            gold: new PhysicalGoldLotInventoryDetail(
                916_000,
                OriginalPieceCount: 2,
                GlobalPieceCount: 2,
                GlobalFineWeightGrams: 18.32m,
                Hallmark: "916",
                CertificateReference: "SYNTH-CERT",
                Note: null));

        var display = presenter.PresentLot(lot, choices: null, Currencies());

        Assert.False(display.IsExhausted);
        Assert.Equal("20 gram", display.GlobalQuantity.Text);
        Assert.Equal("1.000,00 TRY", display.Cost.Text);
        Assert.NotNull(display.PhysicalGold);
        Assert.Equal("916 ‰ (916000 ppm)", display.PhysicalGold.Fineness);
        Assert.Equal("2 adet", display.PhysicalGold.GlobalPieces);
        Assert.Equal("18,32 gram saf altın", display.PhysicalGold.GlobalFineWeight);
        Assert.Equal(["15 gram", "5 gram"], display.Custody.Select(item => item.Quantity.Text));
        Assert.Equal(["1 adet", "1 adet"], display.Custody.Select(item => item.Pieces));
        Assert.Equal(
            ["13,74 gram saf altın", "4,58 gram saf altın"],
            display.Custody.Select(item => item.FineWeight));
        Assert.Equal("-5 gram — azalış", display.Allocations[1].QuantityDelta.Text);
        Assert.Equal("-1 adet", display.Allocations[1].PieceDelta);
    }

    [Fact]
    public void PresentLot_KeepsUnknownCostUnknownAndMarksAnExhaustedLot()
    {
        var presenter = CreatePresenter();

        var unknown = presenter.PresentLot(
            Lot(CostBasisStatus.Unknown, costMinorUnits: null, globalQuantity: 0, custody: []),
            choices: null,
            Currencies());
        var missingCurrency = presenter.PresentLot(
            Lot(CostBasisStatus.Known, costMinorUnits: 500, globalQuantity: 1, custody: [], currency: "USD"),
            choices: null,
            Currencies());

        Assert.True(unknown.IsExhausted);
        Assert.Equal(DisplayState.Unknown, unknown.Cost.State);
        Assert.Equal(DisplayState.Unknown, unknown.AcquiredOn.State);
        Assert.Null(unknown.PhysicalGold);
        Assert.Equal(DisplayState.Unavailable, missingCurrency.Cost.State);
        Assert.Null(missingCurrency.Custody.FirstOrDefault()?.Pieces);
    }

    [Fact]
    public void InventoryFilterMapper_MapsScopeFlagsAndPagingAndRejectsMalformedValues()
    {
        var accountId = Guid.NewGuid();
        var form = new InventoryFilterForm
        {
            AsOf = "2026-01-31",
            IncludeZero = "true",
            IncludeExhausted = "true",
            AccountId = accountId.ToString("D"),
            Cursor = "opaque"
        };

        Assert.True(InventoryFilterMapper.TryMapPositions(form, HouseholdId, out var positions, out _));
        Assert.True(InventoryFilterMapper.TryMapLots(form, HouseholdId, 5, out var lots, out _));
        Assert.Equal(new DateOnly(2026, 1, 31), positions!.AsOf);
        Assert.True(positions.IncludeZero);
        Assert.Equal(accountId, positions.AccountId);
        Assert.True(lots!.IncludeExhausted);
        Assert.Equal(5, lots.PageSize);
        Assert.Equal("opaque", lots.Cursor);

        Assert.False(InventoryFilterMapper.TryMapPositions(
            new InventoryFilterForm
            {
                AsOf = "31.01.2026",
                IncludeZero = "on",
                AssetId = Guid.Empty.ToString("D")
            },
            HouseholdId,
            out _,
            out var errors));
        Assert.Equal(
            [
                new InventoryFilterError("AsOf", "Assets_Validation_Date"),
                new InventoryFilterError("AssetId", "Assets_Validation_Selection"),
                new InventoryFilterError("IncludeZero", "Assets_Validation_Flag")
            ],
            errors);

        Assert.True(InventoryFilterMapper.TryParseLotPageSize(null, out var defaultSize));
        Assert.Equal(InventoryFilterMapper.DefaultLotPageSize, defaultSize);
        Assert.False(InventoryFilterMapper.TryParseLotPageSize("0", out _));
        Assert.False(InventoryFilterMapper.TryParseLotPageSize("101", out _));
        Assert.Equal(
            $"?pageSize=1&asOf=2026-01-31&includeZero=true&includeExhausted=true&accountId={accountId:D}&cursor=next",
            InventoryFilterMapper.NextPageQuery(form, 1, "next").Value);
    }

    private static InventoryPresenter CreatePresenter()
    {
        var culture = PresentationCulture.CreateDefault();

        return new InventoryPresenter(
            new ValuePresenter(culture),
            new UiText(culture),
            culture);
    }

    private static Dictionary<string, CurrencyNavigationItem> Currencies()
        => new(StringComparer.Ordinal)
        {
            ["TRY"] = new CurrencyNavigationItem("TRY", "Turkish lira", 2)
        };

    private static PositionInventoryItem Position(
        Guid accountId,
        string assetCode,
        long quantityRawE8,
        AssetUnit unit,
        LotTrackingMode mode)
        => new(
            PortfolioId,
            "CORE",
            "Synthetic Portfolio",
            PortfolioStatus.Active,
            accountId,
            "ACC",
            "Synthetic Account",
            AccountType.Investment,
            AccountIsActive: true,
            InstitutionId: null,
            InstitutionCode: null,
            InstitutionName: null,
            InstitutionType: null,
            InstitutionIsActive: null,
            Guid.NewGuid(),
            assetCode,
            "Synthetic " + assetCode,
            AssetType.Other,
            unit,
            "TRY",
            mode,
            AssetIsActive: true,
            quantityRawE8,
            SourceEntryCount: 1,
            SourceTransactionCount: 1);

    private static LotCustodyInventoryItem Custody(
        Guid accountId,
        long quantityRawE8,
        int? pieces,
        decimal? fineWeight)
        => new(
            PortfolioId,
            "CORE",
            "Synthetic Portfolio",
            PortfolioStatus.Active,
            accountId,
            "VAULT",
            "Synthetic Vault",
            AccountType.PhysicalVault,
            AccountIsActive: true,
            InstitutionId: null,
            InstitutionCode: null,
            InstitutionName: null,
            InstitutionType: null,
            InstitutionIsActive: null,
            quantityRawE8,
            pieces,
            fineWeight);

    private static LotInventoryItem Lot(
        CostBasisStatus costStatus,
        long? costMinorUnits,
        long globalQuantity,
        IReadOnlyList<LotCustodyInventoryItem> custody,
        PhysicalGoldLotInventoryDetail? gold = null,
        string currency = "TRY")
    {
        var purchaseId = Guid.NewGuid();
        var transferId = Guid.NewGuid();

        return new LotInventoryItem(
            Guid.NewGuid(),
            purchaseId,
            Guid.NewGuid(),
            Guid.NewGuid(),
            "SYNTH_GOLD",
            "Synthetic Gold",
            AssetType.PhysicalGold,
            AssetUnit.GrossGram,
            "TRY",
            LotTrackingMode.Required,
            AssetIsActive: true,
            AcquiredOn: costStatus == CostBasisStatus.Unknown ? null : new DateOnly(2026, 9, 14),
            costStatus,
            costMinorUnits,
            costMinorUnits is null ? null : currency,
            OriginalQuantityRawE8: 2_000_000_000,
            globalQuantity,
            custody,
            [
                new LotAllocationInventoryItem(
                    Guid.NewGuid(),
                    Guid.NewGuid(),
                    purchaseId,
                    TransactionType.Buy,
                    new DateOnly(2026, 9, 14),
                    PortfolioId,
                    AccountA,
                    2_000_000_000,
                    gold is null ? null : 2),
                new LotAllocationInventoryItem(
                    Guid.NewGuid(),
                    Guid.NewGuid(),
                    transferId,
                    TransactionType.Transfer,
                    new DateOnly(2026, 9, 14),
                    PortfolioId,
                    AccountA,
                    -500_000_000,
                    gold is null ? null : -1)
            ],
            gold);
    }
}
