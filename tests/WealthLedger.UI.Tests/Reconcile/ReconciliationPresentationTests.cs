using WealthLedger.Application.Evidence;
using WealthLedger.Application.Navigation;
using WealthLedger.Application.Reconciliation;
using WealthLedger.Domain.Assets;
using WealthLedger.Domain.Portfolios;
using WealthLedger.Domain.Reconciliation;
using WealthLedger.UI.Pages;
using WealthLedger.UI.Pages.Reconcile;
using WealthLedger.UI.Presentation;

namespace WealthLedger.UI.Tests.Reconcile;

public sealed class ReconciliationPresentationTests
{
    private static readonly Guid PortfolioId = Guid.NewGuid();
    private static readonly Guid AccountId = Guid.NewGuid();
    private static readonly Guid CashId = Guid.NewGuid();
    private static readonly Guid GoldId = Guid.NewGuid();

    [Fact]
    public void SixStates_HaveDistinctTextCodesAndBadgeStyles()
    {
        var presenter = CreatePresenter();
        var badges = new[]
        {
            presenter.Badge(ReconciliationComparisonStatus.Matched),
            presenter.Badge(ReconciliationComparisonStatus.OutOfTolerance),
            presenter.Badge(ReconciliationResolutionState.Open),
            presenter.Badge(ReconciliationResolutionState.CorrectedByTransaction),
            presenter.Badge(ReconciliationResolutionState.DocumentedException),
            presenter.StaleBadge()
        };

        Assert.Equal(6, badges.Select(badge => badge.Text).Distinct().Count());
        Assert.Equal(6, badges.Select(badge => badge.Code).Distinct().Count());
        Assert.Equal(6, badges.Select(badge => badge.Modifier).Distinct().Count());
        Assert.Equal(
            ["Eşleşti", "Tolerans dışı", "Açık", "İşlemle düzeltildi", "Belgelenmiş istisna", "Güncel değil"],
            badges.Select(badge => badge.Text));
    }

    [Fact]
    public void PresentDetail_KeepsADocumentedExceptionOutOfToleranceAndOnlyOpenItemsResolvable()
    {
        var presenter = CreatePresenter();
        var detail = presenter.PresentDetail(
            new ReconciliationRunDetail(
                Guid.NewGuid(),
                Guid.NewGuid(),
                Guid.NewGuid(),
                new DateOnly(2026, 1, 31),
                DateTimeOffset.UnixEpoch,
                ReconciliationMethod.ExactScopeQuantityAsOf,
                1,
                IsStale: true,
                [
                    Item(2, ReconciliationComparisonStatus.OutOfTolerance, ReconciliationResolutionState.DocumentedException, isStale: false),
                    Item(1, ReconciliationComparisonStatus.Matched, null, isStale: true),
                    Item(3, ReconciliationComparisonStatus.OutOfTolerance, ReconciliationResolutionState.Open, isStale: false)
                ]));

        Assert.True(detail.IsStale);
        Assert.Equal([1, 2, 3], detail.Items.Select(item => item.Sequence));

        var matched = detail.Items[0];
        var exception = detail.Items[1];
        var open = detail.Items[2];

        Assert.Equal("MATCHED", matched.Comparison.Code);
        Assert.Null(matched.Resolution);
        Assert.False(matched.CanResolve);
        Assert.Equal("STALE", matched.Staleness!.Code);
        Assert.Equal("1.400 para birimi", matched.CurrentLedger!.Text);

        Assert.Equal("OUT_OF_TOLERANCE", exception.Comparison.Code);
        Assert.Equal("DOCUMENTED_EXCEPTION", exception.Resolution!.Code);
        Assert.False(exception.CanResolve);
        Assert.Null(exception.Staleness);
        Assert.Null(exception.CurrentLedger);

        Assert.True(open.CanResolve);
        Assert.Equal("+100 para birimi — artış", open.Delta.Text);
        Assert.Equal("1.500 para birimi", open.Observed.Text);
        Assert.Equal("1.400 para birimi", open.Ledger.Text);
        Assert.Equal("Synthetic Portfolio · Synthetic Account · Synthetic Bank", open.Scope);
    }

    [Theory]
    [InlineData("1500", true, 150_000_000_000)]
    [InlineData("1,25", true, 125_000_000L)]
    [InlineData("0.00000001", true, 1L)]
    [InlineData("-2", true, -200_000_000L)]
    [InlineData("0,123456789", true, null)]
    [InlineData("1.234,5", true, null)]
    [InlineData("1e3", true, null)]
    [InlineData("1 000", true, null)]
    [InlineData("100000000000", true, null)]
    [InlineData("-1", false, null)]
    [InlineData("", false, null)]
    public void TryParseRawE8_IsExactCheckedAndNeverRounds(
        string value,
        bool allowNegative,
        long? expected)
    {
        var parsed = ObservationFormMapper.TryParseRawE8(value, allowNegative, out var rawE8);

        Assert.Equal(expected is not null, parsed);

        if (expected is not null)
        {
            Assert.Equal(expected.Value, rawE8);
        }
    }

    [Fact]
    public void ObservationFormMapper_MapsAScopeAndRefusesPiecesOutsideGold()
    {
        var choices = Choices();
        var mapped = ObservationFormMapper.TryMap(
            new ObservationForm
            {
                PortfolioId = PortfolioId.ToString("D"),
                AccountId = AccountId.ToString("D"),
                AssetId = GoldId.ToString("D"),
                ObservedQuantity = "15",
                Tolerance = "",
                ObservedPieceCount = "1"
            },
            choices,
            out var input,
            out var asset,
            out var errors);

        Assert.True(mapped);
        Assert.Empty(errors);
        Assert.Equal(GoldId, asset!.AssetId);
        Assert.Equal(1_500_000_000, input!.ObservedQuantityRawE8);
        Assert.Equal(0, input.ToleranceRawE8);
        Assert.Equal(1, input.ObservedPieceCount);

        Assert.False(ObservationFormMapper.TryMap(
            new ObservationForm
            {
                PortfolioId = Guid.NewGuid().ToString("D"),
                AccountId = AccountId.ToString("D"),
                AssetId = CashId.ToString("D"),
                ObservedQuantity = "1500",
                Tolerance = "-1",
                ObservedPieceCount = "1"
            },
            choices,
            out _,
            out _,
            out var refused));
        Assert.Equal(
            [
                new ObservationFormError("PortfolioId", "Observation_Validation_Selection"),
                new ObservationFormError("Tolerance", "Observation_Validation_Tolerance"),
                new ObservationFormError("ObservedPieceCount", "Observation_Validation_PiecesGoldOnly")
            ],
            refused);
    }

    private static ReconciliationPresenter CreatePresenter()
    {
        var culture = PresentationCulture.CreateDefault();

        return new ReconciliationPresenter(
            new ValuePresenter(culture),
            new UiText(culture),
            culture);
    }

    private static ReconciliationItemDetail Item(
        int sequence,
        ReconciliationComparisonStatus status,
        ReconciliationResolutionState? state,
        bool isStale)
        => new(
            Guid.NewGuid(),
            sequence,
            Guid.NewGuid(),
            new EvidenceObservationScope(
                PortfolioId,
                "CORE",
                "Synthetic Portfolio",
                AccountId,
                "CASH",
                "Synthetic Account",
                Guid.NewGuid(),
                "BANK",
                "Synthetic Bank",
                CashId,
                "TRY",
                "Synthetic Lira",
                AssetType.Cash,
                AssetUnit.CurrencyUnit),
            ObservedQuantityRawE8: 150_000_000_000,
            LedgerQuantityRawE8: status == ReconciliationComparisonStatus.Matched
                ? 150_000_000_000
                : 140_000_000_000,
            DeltaRawE8: status == ReconciliationComparisonStatus.Matched ? 0 : 10_000_000_000,
            ToleranceRawE8: 0,
            ObservedPieceCount: null,
            LedgerPieceCount: null,
            PieceDelta: null,
            status,
            state,
            CorrectiveTransactionId: null,
            Explanation: state == ReconciliationResolutionState.DocumentedException
                ? "Synthetic explanation."
                : null,
            ResolvedAtUtc: state is null or ReconciliationResolutionState.Open
                ? null
                : DateTimeOffset.UnixEpoch,
            CurrentLedgerQuantityRawE8: isStale ? 140_000_000_000 : 150_000_000_000,
            CurrentLedgerPieceCount: null,
            isStale);

    private static ScopeChoices Choices()
        => new(
            [
                new PortfolioNavigationItem(
                    PortfolioId,
                    Guid.NewGuid(),
                    "CORE",
                    "Synthetic Portfolio",
                    PortfolioStatus.Active,
                    DateTimeOffset.UnixEpoch,
                    ClosedAtUtc: null)
            ],
            [
                new AccountNavigationItem(
                    AccountId,
                    Guid.NewGuid(),
                    Institution: null,
                    "VAULT",
                    "Synthetic Vault",
                    AccountType.PhysicalVault,
                    IsActive: true,
                    OpenedOn: null,
                    ClosedOn: null)
            ],
            [],
            [
                new AssetNavigationItem(
                    CashId,
                    "TRY",
                    "Synthetic Lira",
                    AssetType.Cash,
                    AssetUnit.CurrencyUnit,
                    "TRY",
                    Domain.Assets.LotTrackingMode.None,
                    IsActive: true,
                    DateTimeOffset.UnixEpoch),
                new AssetNavigationItem(
                    GoldId,
                    "GOLD",
                    "Synthetic Gold",
                    AssetType.PhysicalGold,
                    AssetUnit.GrossGram,
                    "TRY",
                    Domain.Assets.LotTrackingMode.Required,
                    IsActive: true,
                    DateTimeOffset.UnixEpoch)
            ]);
}
