using WealthLedger.Application.Navigation;
using WealthLedger.Domain.Assets;
using WealthLedger.Domain.Ledger;
using WealthLedger.Domain.Portfolios;
using WealthLedger.UI.Pages;
using WealthLedger.UI.Presentation;

namespace WealthLedger.UI.Tests;

public sealed class ReadyPresentationTests
{
    private static readonly DateTimeOffset CreatedAt = new(
        2026,
        9,
        7,
        6,
        0,
        0,
        TimeSpan.Zero);

    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 1)]
    [InlineData(2, 2)]
    [InlineData(3, 2)]
    public async Task HouseholdResolver_NeverGuessesAmongMultipleRows(
        int householdCount,
        int expectedState)
    {
        var households = Enumerable.Range(1, householdCount)
            .Select(
                index => CreateHousehold(
                    Guid.Parse(
                        $"10000000-0000-0000-0000-{index:D12}")))
            .ToArray();
        var store = new MasterStoreFake(households);
        var resolver = new ReadyHouseholdResolver(
            new ListHouseholdsUseCase(store));

        var result = await resolver.ResolveAsync();

        Assert.Equal(expectedState, (int)result.State);
        Assert.Equal(1, store.CallCount);

        if (result.State == ReadyHouseholdResolutionState.Single)
        {
            Assert.Equal(households[0], result.Household);
        }
        else
        {
            Assert.Null(result.Household);
        }
    }

    [Fact]
    public void RecentPresenter_OrdersEffectsAndUsesExactValuePresenter()
    {
        var presenter = new RecentLedgerPresenter(
            new ValuePresenter(PresentationCulture.CreateDefault()));
        var later = CreateEffect(
            Guid.Parse("20000000-0000-0000-0000-000000000002"),
            sequence: 1,
            rawE8: -25_000_000);
        var earlier = CreateEffect(
            Guid.Parse("20000000-0000-0000-0000-000000000001"),
            sequence: 0,
            rawE8: 1_234_567_890);
        var source = new RecentLedgerTransactionNavigationItem(
            Guid.Parse("30000000-0000-0000-0000-000000000001"),
            Guid.Parse("10000000-0000-0000-0000-000000000001"),
            TransactionType.Buy,
            TransactionStatus.Posted,
            OrderDate: null,
            new DateOnly(2026, 9, 7),
            SettlementDate: null,
            ExternalReference: null,
            ReversalOfTransactionId: null,
            ReversedByTransactionId: null,
            CreatedAt,
            CreatedAt.AddMinutes(1),
            [later, earlier]);

        var result = presenter.Present(source);

        Assert.Equal([0, 1], result.EntryEffects.Select(item => item.Sequence));
        Assert.Equal(
            "+12,3456789 fon birimi — artış",
            result.EntryEffects[0].Quantity.Text);
        Assert.Equal(
            "-0,25 fon birimi — azalış",
            result.EntryEffects[1].Quantity.Text);
        Assert.Equal(DisplayState.Unknown, result.OrderDate.State);
        Assert.Equal("BUY", result.Type.TechnicalDetail);
        Assert.Equal("POSTED", result.Status.TechnicalDetail);
    }

    private static HouseholdNavigationItem CreateHousehold(Guid id)
        => new(
            id,
            "Synthetic Household",
            new CurrencyNavigationItem("TRY", "Synthetic Currency", 2),
            CreatedAt.AddTicks(id.ToByteArray()[15]));

    private static RecentLedgerEntryEffectNavigationItem CreateEffect(
        Guid id,
        int sequence,
        long rawE8)
        => new(
            id,
            sequence,
            Guid.Parse("40000000-0000-0000-0000-000000000001"),
            "CORE",
            "Core Portfolio",
            PortfolioStatus.Active,
            Guid.Parse("50000000-0000-0000-0000-000000000001"),
            "PRIMARY",
            "Primary Account",
            AccountType.Investment,
            AccountIsActive: true,
            Guid.Parse("60000000-0000-0000-0000-000000000001"),
            "SYNTHETIC_INSTITUTION",
            "Synthetic Institution",
            InstitutionType.Broker,
            InstitutionIsActive: true,
            Guid.Parse("70000000-0000-0000-0000-000000000001"),
            "SYNTHETIC_FUND",
            "Synthetic Fund",
            AssetType.Fund,
            AssetUnit.FundUnit,
            "TRY",
            LotTrackingMode.Required,
            AssetIsActive: true,
            rawE8,
            EntryRole.Principal);

    private sealed class MasterStoreFake : IMasterNavigationReadStore
    {
        private readonly IReadOnlyList<HouseholdNavigationItem> _households;

        internal MasterStoreFake(
            IReadOnlyList<HouseholdNavigationItem> households)
        {
            _households = households;
        }

        internal int CallCount { get; private set; }

        public Task<IReadOnlyList<HouseholdNavigationItem>> ListHouseholdsAsync(
            int take,
            NavigationCreatedAtKey? after,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            return Task.FromResult<IReadOnlyList<HouseholdNavigationItem>>(
                _households.Take(take).ToArray());
        }

        public Task<HouseholdNavigationItem?> FindHouseholdAsync(
            Guid householdId,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<IReadOnlyList<HouseholdMemberNavigationItem>>
            ListHouseholdMembersAsync(
                Guid householdId,
                bool includeInactive,
                int take,
                NavigationCreatedAtKey? after,
                CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<IReadOnlyList<InstitutionNavigationItem>>
            ListInstitutionsAsync(
                bool includeInactive,
                int take,
                NavigationCodeKey? after,
                CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<IReadOnlyList<PortfolioNavigationItem>> ListPortfoliosAsync(
            Guid householdId,
            bool includeInactive,
            int take,
            NavigationCodeKey? after,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<IReadOnlyList<AccountNavigationItem>> ListAccountsAsync(
            Guid householdId,
            bool includeInactive,
            int take,
            NavigationCodeKey? after,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<IReadOnlyList<CurrencyNavigationItem>> ListCurrenciesAsync(
            int take,
            NavigationCurrencyKey? after,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<IReadOnlyList<AssetNavigationItem>> ListAssetsAsync(
            bool includeInactive,
            int take,
            NavigationCodeKey? after,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }
}
