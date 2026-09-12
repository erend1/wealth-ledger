using WealthLedger.Application.Navigation;
using WealthLedger.Application.OpeningBalances;
using WealthLedger.Domain.Assets;
using WealthLedger.Domain.Portfolios;

namespace WealthLedger.Application.Tests.OpeningBalances;

public sealed class ListOpeningBalanceChoicesUseCaseTests
{
    private static readonly Guid HouseholdId = Guid.NewGuid();

    [Fact]
    public async Task Execute_ReusesNavigationPagesAndFiltersUnsupportedChoices()
    {
        var store = new NavigationStoreFake();
        var useCase = CreateUseCase(store);

        var result = await useCase.ExecuteAsync(
            new ListOpeningBalanceChoicesQuery(
                HouseholdId,
                PageSize: 50));

        Assert.Equal(HouseholdId, result.Household.HouseholdId);
        Assert.Single(result.Portfolios.Items);
        Assert.Single(result.Institutions.Items);
        Assert.Single(result.Currencies.Items);
        Assert.Single(result.Accounts.Items);
        Assert.Equal(AccountType.Investment, result.Accounts.Items[0].Type);
        Assert.Single(result.Assets.Items);
        Assert.Equal(AssetType.Fund, result.Assets.Items[0].Type);
        Assert.Null(result.Accounts.NextCursor);
        Assert.Null(result.Assets.NextCursor);
    }

    [Fact]
    public async Task Execute_PreservesUnderlyingCursorWhenAnotherPageExists()
    {
        var store = new NavigationStoreFake();
        var useCase = CreateUseCase(store);

        var result = await useCase.ExecuteAsync(
            new ListOpeningBalanceChoicesQuery(
                HouseholdId,
                PageSize: 1));

        Assert.Single(result.Accounts.Items);
        Assert.Single(result.Assets.Items);
        Assert.NotNull(result.Accounts.NextCursor);
        Assert.NotNull(result.Assets.NextCursor);
    }

    [Fact]
    public async Task Execute_RejectsInvalidPageSizeBeforeReadingCollections()
    {
        var store = new NavigationStoreFake();
        var useCase = CreateUseCase(store);

        await Assert.ThrowsAsync<NavigationRequestException>(
            () => useCase.ExecuteAsync(
                new ListOpeningBalanceChoicesQuery(
                    HouseholdId,
                    PageSize: 101)));

        Assert.Equal(0, store.CollectionReadCount);
    }

    private static ListOpeningBalanceChoicesUseCase CreateUseCase(
        NavigationStoreFake store)
        => new(
            new GetHouseholdUseCase(store),
            new ListPortfoliosUseCase(store, store),
            new ListAccountsUseCase(store, store),
            new ListInstitutionsUseCase(store),
            new ListCurrenciesUseCase(store),
            new ListAssetsUseCase(store));

    private sealed class NavigationStoreFake
        : IMasterNavigationReadStore,
          INavigationScopeReadStore
    {
        internal int CollectionReadCount { get; private set; }

        public Task<IReadOnlyList<HouseholdNavigationItem>>
            ListHouseholdsAsync(
                int take,
                NavigationCreatedAtKey? after,
                CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<HouseholdNavigationItem>>([]);

        public Task<HouseholdNavigationItem?> FindHouseholdAsync(
            Guid householdId,
            CancellationToken cancellationToken = default)
            => Task.FromResult<HouseholdNavigationItem?>(
                householdId == HouseholdId
                    ? new HouseholdNavigationItem(
                        HouseholdId,
                        "Synthetic Household",
                        new CurrencyNavigationItem(
                            "TRY",
                            "Synthetic Turkish Lira",
                            2),
                        new DateTimeOffset(
                            2026,
                            1,
                            1,
                            0,
                            0,
                            0,
                            TimeSpan.Zero))
                    : null);

        public Task<IReadOnlyList<HouseholdMemberNavigationItem>>
            ListHouseholdMembersAsync(
                Guid householdId,
                bool includeInactive,
                int take,
                NavigationCreatedAtKey? after,
                CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<HouseholdMemberNavigationItem>>([]);

        public Task<IReadOnlyList<InstitutionNavigationItem>>
            ListInstitutionsAsync(
                bool includeInactive,
                int take,
                NavigationCodeKey? after,
                CancellationToken cancellationToken = default)
        {
            CollectionReadCount++;

            return Task.FromResult<IReadOnlyList<InstitutionNavigationItem>>(
                [
                    new InstitutionNavigationItem(
                        Guid.NewGuid(),
                        "SYNTHETIC_BROKER",
                        "Synthetic Broker",
                        InstitutionType.Broker,
                        IsActive: true)
                ]);
        }

        public Task<IReadOnlyList<PortfolioNavigationItem>> ListPortfoliosAsync(
            Guid householdId,
            bool includeInactive,
            int take,
            NavigationCodeKey? after,
            CancellationToken cancellationToken = default)
        {
            CollectionReadCount++;

            return Task.FromResult<IReadOnlyList<PortfolioNavigationItem>>(
                [
                    new PortfolioNavigationItem(
                        Guid.NewGuid(),
                        HouseholdId,
                        "CORE",
                        "Synthetic Core Portfolio",
                        PortfolioStatus.Active,
                        DateTimeOffset.UnixEpoch,
                        ClosedAtUtc: null)
                ]);
        }

        public Task<IReadOnlyList<AccountNavigationItem>> ListAccountsAsync(
            Guid householdId,
            bool includeInactive,
            int take,
            NavigationCodeKey? after,
            CancellationToken cancellationToken = default)
        {
            CollectionReadCount++;
            var institution = new AccountInstitutionNavigationItem(
                Guid.NewGuid(),
                "SYNTHETIC_BROKER",
                "Synthetic Broker",
                InstitutionType.Broker,
                IsActive: true);

            return Task.FromResult<IReadOnlyList<AccountNavigationItem>>(
                [
                    new AccountNavigationItem(
                        Guid.NewGuid(),
                        HouseholdId,
                        institution,
                        "INVESTMENT",
                        "Synthetic Investment Account",
                        AccountType.Investment,
                        IsActive: true,
                        OpenedOn: null,
                        ClosedOn: null),
                    new AccountNavigationItem(
                        Guid.NewGuid(),
                        HouseholdId,
                        Institution: null,
                        "OTHER",
                        "Unsupported Synthetic Account",
                        AccountType.Other,
                        IsActive: true,
                        OpenedOn: null,
                        ClosedOn: null)
                ]);
        }

        public Task<IReadOnlyList<CurrencyNavigationItem>> ListCurrenciesAsync(
            int take,
            NavigationCurrencyKey? after,
            CancellationToken cancellationToken = default)
        {
            CollectionReadCount++;

            return Task.FromResult<IReadOnlyList<CurrencyNavigationItem>>(
                [
                    new CurrencyNavigationItem(
                        "TRY",
                        "Synthetic Turkish Lira",
                        2)
                ]);
        }

        public Task<IReadOnlyList<AssetNavigationItem>> ListAssetsAsync(
            bool includeInactive,
            int take,
            NavigationCodeKey? after,
            CancellationToken cancellationToken = default)
        {
            CollectionReadCount++;

            return Task.FromResult<IReadOnlyList<AssetNavigationItem>>(
                [
                    new AssetNavigationItem(
                        Guid.NewGuid(),
                        "SYNTHETIC_FUND",
                        "Synthetic Fund",
                        AssetType.Fund,
                        AssetUnit.FundUnit,
                        "TRY",
                        LotTrackingMode.Required,
                        IsActive: true,
                        DateTimeOffset.UnixEpoch),
                    new AssetNavigationItem(
                        Guid.NewGuid(),
                        "SYNTHETIC_PROPERTY",
                        "Unsupported Synthetic Property",
                        AssetType.RealEstate,
                        AssetUnit.Property,
                        "TRY",
                        LotTrackingMode.Optional,
                        IsActive: true,
                        DateTimeOffset.UnixEpoch)
                ]);
        }

        public Task<bool> HouseholdExistsAsync(
            Guid householdId,
            CancellationToken cancellationToken = default)
            => Task.FromResult(householdId == HouseholdId);

        public Task<bool> PositionScopeExistsAsync(
            Guid householdId,
            Guid portfolioId,
            Guid accountId,
            Guid assetId,
            CancellationToken cancellationToken = default)
            => Task.FromResult(false);
    }
}
