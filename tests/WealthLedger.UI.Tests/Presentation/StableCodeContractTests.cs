using WealthLedger.Api.Mapping;
using WealthLedger.Application.CoreLedger;
using WealthLedger.Application.Navigation;
using WealthLedger.Domain.Assets;
using WealthLedger.Domain.Ledger;
using WealthLedger.Domain.Lots;
using WealthLedger.Domain.Portfolios;
using WealthLedger.UI.Presentation;

namespace WealthLedger.UI.Tests.Presentation;

/// <summary>
/// The UI owns its own stable-code mapping so its assembly does not have to
/// reference Infrastructure or the API contracts. That independence is only
/// safe while the two agree, so every code is pinned here against the value the
/// API actually emits, taken from the real mapper rather than from a copied
/// list.
/// </summary>
public sealed class StableCodeContractTests
{
    [Fact]
    public void AssetCodes_MatchTheApiTransportCodes()
    {
        foreach (var type in Enum.GetValues<AssetType>())
        {
            foreach (var unit in Enum.GetValues<AssetUnit>())
            {
                foreach (var mode in Enum.GetValues<LotTrackingMode>())
                {
                    var response = NavigationContractMapper.ToResponse(
                        new AssetNavigationItem(
                            Guid.NewGuid(),
                            "SYNTHETIC",
                            "Synthetic asset",
                            type,
                            unit,
                            "TRY",
                            mode,
                            IsActive: true,
                            DateTimeOffset.UnixEpoch));

                    Assert.Equal(response.TypeCode, StableCodes.ToCode(type));
                    Assert.Equal(
                        response.BaseUnitCode,
                        StableCodes.ToCode(unit));
                    Assert.Equal(
                        response.LotTrackingModeCode,
                        StableCodes.ToCode(mode));
                }
            }
        }
    }

    [Fact]
    public void InstitutionCodes_MatchTheApiTransportCodes()
    {
        foreach (var type in Enum.GetValues<InstitutionType>())
        {
            var response = NavigationContractMapper.ToResponse(
                new InstitutionNavigationItem(
                    Guid.NewGuid(),
                    "SYNTHETIC",
                    "Synthetic institution",
                    type,
                    IsActive: true));

            Assert.Equal(response.TypeCode, StableCodes.ToCode(type));
        }
    }

    [Fact]
    public void AccountCodes_MatchTheApiTransportCodes()
    {
        foreach (var type in Enum.GetValues<AccountType>())
        {
            var response = NavigationContractMapper.ToResponse(
                new AccountNavigationItem(
                    Guid.NewGuid(),
                    Guid.NewGuid(),
                    Institution: null,
                    "SYNTHETIC",
                    "Synthetic account",
                    type,
                    IsActive: true,
                    OpenedOn: null,
                    ClosedOn: null));

            Assert.Equal(response.TypeCode, StableCodes.ToCode(type));
        }
    }

    [Fact]
    public void PortfolioCodes_MatchTheApiTransportCodes()
    {
        foreach (var status in Enum.GetValues<PortfolioStatus>())
        {
            var response = NavigationContractMapper.ToResponse(
                new PortfolioNavigationItem(
                    Guid.NewGuid(),
                    Guid.NewGuid(),
                    "SYNTHETIC",
                    "Synthetic portfolio",
                    status,
                    DateTimeOffset.UnixEpoch,
                    ClosedAtUtc: null));

            Assert.Equal(response.StatusCode, StableCodes.ToCode(status));
        }
    }

    [Fact]
    public void LedgerCodes_MatchTheApiTransportCodes()
    {
        foreach (var type in Enum.GetValues<TransactionType>())
        {
            foreach (var status in Enum.GetValues<TransactionStatus>())
            {
                foreach (var role in Enum.GetValues<EntryRole>())
                {
                    var response = CreateDetail(
                            type,
                            status,
                            role,
                            CashFlowCategory.Salary,
                            CostType.Commission,
                            CostTreatment.AdditionalCashOutflow,
                            CostBasisStatus.Known)
                        .ToResponse();

                    Assert.Equal(response.TypeCode, StableCodes.ToCode(type));
                    Assert.Equal(
                        response.StatusCode,
                        StableCodes.ToCode(status));
                    Assert.Equal(
                        response.Entries[0].RoleCode,
                        StableCodes.ToCode(role));
                }
            }
        }
    }

    [Fact]
    public void TransactionDetailCodes_MatchTheApiTransportCodes()
    {
        foreach (var category in Enum.GetValues<CashFlowCategory>())
        {
            var response = CreateDetail(
                    cashFlowCategory: category).ToResponse();

            Assert.Equal(
                response.CashFlow!.CategoryCode,
                StableCodes.ToCode(category));
        }

        foreach (var costType in Enum.GetValues<CostType>())
        {
            var response = CreateDetail(costType: costType).ToResponse();

            Assert.Equal(
                response.Costs[0].TypeCode,
                StableCodes.ToCode(costType));
        }

        foreach (var treatment in Enum.GetValues<CostTreatment>())
        {
            var response = CreateDetail(costTreatment: treatment).ToResponse();

            Assert.Equal(
                response.Costs[0].TreatmentCode,
                StableCodes.ToCode(treatment));
        }

        foreach (var basis in Enum.GetValues<CostBasisStatus>())
        {
            var response = CreateDetail(costBasisStatus: basis).ToResponse();

            Assert.Equal(
                response.CreatedLots[0].CostBasisStatusCode,
                StableCodes.ToCode(basis));
        }
    }

    [Fact]
    public void EveryStableCode_HasAHumanDescription()
    {
        var presenter = new ValuePresenter(PresentationCulture.CreateDefault());

        AssertDescribed(Enum.GetValues<AssetType>(), presenter.StableCode);
        AssertDescribed(Enum.GetValues<AssetUnit>(), presenter.StableCode);
        AssertDescribed(Enum.GetValues<LotTrackingMode>(), presenter.StableCode);
        AssertDescribed(Enum.GetValues<InstitutionType>(), presenter.StableCode);
        AssertDescribed(Enum.GetValues<AccountType>(), presenter.StableCode);
        AssertDescribed(Enum.GetValues<PortfolioStatus>(), presenter.StableCode);
        AssertDescribed(Enum.GetValues<TransactionType>(), presenter.StableCode);
        AssertDescribed(
            Enum.GetValues<TransactionStatus>(),
            presenter.StableCode);
        AssertDescribed(Enum.GetValues<EntryRole>(), presenter.StableCode);
        AssertDescribed(
            Enum.GetValues<CashFlowCategory>(),
            presenter.StableCode);
        AssertDescribed(Enum.GetValues<CostType>(), presenter.StableCode);
        AssertDescribed(Enum.GetValues<CostTreatment>(), presenter.StableCode);
        AssertDescribed(
            Enum.GetValues<CostBasisStatus>(),
            presenter.StableCode);
    }

    [Fact]
    public void EveryAssetUnit_HasAQuantityUnitLabel()
    {
        var presenter = new ValuePresenter(PresentationCulture.CreateDefault());

        foreach (var unit in Enum.GetValues<AssetUnit>())
        {
            var rendered = presenter.Quantity(
                100_000_000,
                unit,
                QuantitySign.Absolute);

            Assert.Equal(DisplayState.Known, rendered.State);
            Assert.Null(rendered.DiagnosticCategory);
            Assert.EndsWith(
                StableCodes.ToCode(unit),
                rendered.TechnicalDetail,
                StringComparison.Ordinal);
        }
    }

    private static void AssertDescribed<T>(
        IEnumerable<T> values,
        Func<T, DisplayValue> describe)
    {
        foreach (var value in values)
        {
            var rendered = describe(value);

            Assert.Equal(DisplayState.Known, rendered.State);
            Assert.False(string.IsNullOrWhiteSpace(rendered.Text));
            Assert.False(
                string.IsNullOrWhiteSpace(rendered.TechnicalDetail));
        }
    }

    private static LedgerTransactionDetail CreateDetail(
        TransactionType type = TransactionType.Contribution,
        TransactionStatus status = TransactionStatus.Posted,
        EntryRole role = EntryRole.Principal,
        CashFlowCategory cashFlowCategory = CashFlowCategory.Salary,
        CostType costType = CostType.Commission,
        CostTreatment costTreatment = CostTreatment.AdditionalCashOutflow,
        CostBasisStatus costBasisStatus = CostBasisStatus.Known)
        => new(
            Guid.NewGuid(),
            Guid.NewGuid(),
            type,
            status,
            OrderDate: null,
            ExecutionDate: null,
            SettlementDate: null,
            ExternalReference: null,
            Note: null,
            ReversalOfTransactionId: null,
            ReversedByTransactionId: null,
            DateTimeOffset.UnixEpoch,
            DateTimeOffset.UnixEpoch,
            [
                new LedgerTransactionEntryDetail(
                    Guid.NewGuid(),
                    1,
                    Guid.NewGuid(),
                    Guid.NewGuid(),
                    Guid.NewGuid(),
                    100_000_000,
                    role,
                    UnitPriceRawE8: null,
                    PriceCurrencyCode: null,
                    DateTimeOffset.UnixEpoch)
            ],
            new LedgerTransactionCashFlowDetail(
                cashFlowCategory,
                HouseholdMemberId: null),
            [
                new LedgerTransactionCostDetail(
                    Guid.NewGuid(),
                    costType,
                    costTreatment,
                    1_00,
                    "TRY",
                    Note: null)
            ],
            [
                new LedgerTransactionCreatedLotDetail(
                    Guid.NewGuid(),
                    Guid.NewGuid(),
                    Guid.NewGuid(),
                    AcquiredOn: null,
                    OriginalCostBasisMinorUnits: null,
                    CostBasisCurrencyCode: null,
                    costBasisStatus,
                    DateTimeOffset.UnixEpoch)
            ],
            []);
}
