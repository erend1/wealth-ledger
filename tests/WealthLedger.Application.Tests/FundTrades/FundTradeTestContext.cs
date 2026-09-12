using WealthLedger.Application.FundTrades;
using WealthLedger.Application.OpeningBalances;
using WealthLedger.Domain.Assets;
using WealthLedger.Domain.Lots;
using WealthLedger.Domain.Portfolios;
using WealthLedger.Domain.ValueObjects;

namespace WealthLedger.Application.Tests.FundTrades;

/// <summary>
/// Synthetic references shared by the fund-trade use-case tests.
/// </summary>
internal static class FundTradeIds
{
    internal static readonly Guid Household =
        Guid.Parse("11111111-0000-0000-0000-000000000001");

    internal static readonly Guid Portfolio =
        Guid.Parse("22222222-0000-0000-0000-000000000001");

    internal static readonly Guid FundAccount =
        Guid.Parse("33333333-0000-0000-0000-000000000001");

    internal static readonly Guid CashAccount =
        Guid.Parse("33333333-0000-0000-0000-000000000002");

    internal static readonly Guid Institution =
        Guid.Parse("44444444-0000-0000-0000-000000000001");

    internal static readonly Guid FundAsset =
        Guid.Parse("55555555-0000-0000-0000-000000000001");

    internal static readonly Guid CashAsset =
        Guid.Parse("55555555-0000-0000-0000-000000000002");

    internal static readonly CurrencyCode Try = new("TRY");

    internal static FundTradeScope Scope()
        => new(
            Household,
            Portfolio,
            FundAccount,
            CashAccount,
            FundAsset,
            CashAsset);
}

internal sealed class FundTradeReferenceStoreFake
    : IOpeningBalanceReferenceReadStore
{
    internal Dictionary<Guid, OpeningBalanceAccountReference> Accounts { get; }
        = [];

    internal Dictionary<Guid, OpeningBalanceAssetReference> Assets { get; }
        = [];

    internal Dictionary<CurrencyCode, OpeningBalanceCurrencyReference>
        Currencies
    { get; } = [];

    internal OpeningBalancePortfolioReference? Portfolio { get; set; }

    internal OpeningBalanceHouseholdReference? Household { get; set; }

    internal static FundTradeReferenceStoreFake CreateValid()
    {
        var store = new FundTradeReferenceStoreFake
        {
            Household =
                new OpeningBalanceHouseholdReference(
                    FundTradeIds.Household,
                    FundTradeIds.Try),

            Portfolio =
                new OpeningBalancePortfolioReference(
                    FundTradeIds.Portfolio,
                    FundTradeIds.Household,
                    "MAIN",
                    "Main portfolio",
                    PortfolioStatus.Active)
        };

        store.Accounts[FundTradeIds.FundAccount] =
            new OpeningBalanceAccountReference(
                FundTradeIds.FundAccount,
                FundTradeIds.Household,
                FundTradeIds.Institution,
                "INV",
                "Investment account",
                AccountType.Investment,
                IsActive: true,
                OpenedOn: new DateOnly(2020, 1, 1),
                ClosedOn: null);

        store.Accounts[FundTradeIds.CashAccount] =
            new OpeningBalanceAccountReference(
                FundTradeIds.CashAccount,
                FundTradeIds.Household,
                FundTradeIds.Institution,
                "CASH",
                "Cash account",
                AccountType.Cash,
                IsActive: true,
                OpenedOn: new DateOnly(2020, 1, 1),
                ClosedOn: null);

        store.Assets[FundTradeIds.FundAsset] =
            new OpeningBalanceAssetReference(
                FundTradeIds.FundAsset,
                "FUND",
                "Synthetic fund",
                AssetType.Fund,
                AssetUnit.FundUnit,
                FundTradeIds.Try,
                LotTrackingMode.Required,
                IsActive: true,
                CreatedAtUtc:
                    new DateTimeOffset(
                        2020, 1, 1, 0, 0, 0, TimeSpan.Zero));

        store.Assets[FundTradeIds.CashAsset] =
            new OpeningBalanceAssetReference(
                FundTradeIds.CashAsset,
                "TRY_CASH",
                "Synthetic cash",
                AssetType.Cash,
                AssetUnit.CurrencyUnit,
                FundTradeIds.Try,
                LotTrackingMode.None,
                IsActive: true,
                CreatedAtUtc:
                    new DateTimeOffset(
                        2020, 1, 1, 0, 0, 0, TimeSpan.Zero));

        store.Currencies[FundTradeIds.Try] =
            new OpeningBalanceCurrencyReference(
                FundTradeIds.Try,
                "Turkish lira",
                2);

        return store;
    }

    public Task<OpeningBalanceHouseholdReference?> FindHouseholdAsync(
        Guid householdId,
        CancellationToken cancellationToken = default)
        => Task.FromResult(
            Household?.HouseholdId == householdId
                ? Household
                : null);

    public Task<OpeningBalanceCurrencyReference?> FindCurrencyAsync(
        CurrencyCode code,
        CancellationToken cancellationToken = default)
    {
        Currencies.TryGetValue(code, out var currency);
        return Task.FromResult(currency);
    }

    public Task<OpeningBalanceInstitutionReference?> FindInstitutionAsync(
        Guid institutionId,
        CancellationToken cancellationToken = default)
        => Task.FromResult<OpeningBalanceInstitutionReference?>(
            institutionId == FundTradeIds.Institution
                ? new OpeningBalanceInstitutionReference(
                    FundTradeIds.Institution,
                    "BANK",
                    "Synthetic bank",
                    InstitutionType.Bank,
                    IsActive: true)
                : null);

    public Task<OpeningBalancePortfolioReference?> FindPortfolioAsync(
        Guid portfolioId,
        CancellationToken cancellationToken = default)
        => Task.FromResult(
            Portfolio?.PortfolioId == portfolioId
                ? Portfolio
                : null);

    public Task<OpeningBalanceAccountReference?> FindAccountAsync(
        Guid accountId,
        CancellationToken cancellationToken = default)
    {
        Accounts.TryGetValue(accountId, out var account);
        return Task.FromResult(account);
    }

    public Task<OpeningBalanceAssetReference?> FindAssetAsync(
        Guid assetId,
        CancellationToken cancellationToken = default)
    {
        Assets.TryGetValue(assetId, out var asset);
        return Task.FromResult(asset);
    }
}

/// <summary>
/// A TimeProvider pinned to one instant and time zone, matching the fixed
/// provider the opening-balance tests already use.
/// </summary>
internal sealed class FundTradeTimeProvider : TimeProvider
{
    private readonly DateTimeOffset _utcNow;

    internal FundTradeTimeProvider(DateTimeOffset utcNow)
        => _utcNow = utcNow;

    public override DateTimeOffset GetUtcNow()
        => _utcNow;

    public override TimeZoneInfo LocalTimeZone
        => TimeZoneInfo.Utc;
}

internal sealed class FundLotCustodyStoreFake : IFundLotCustodyReadStore
{
    internal List<ScopedLotCandidate> Candidates { get; } = [];

    internal long CashPositionRawE8 { get; set; }
        = 100_000_000_000_000L;

    public Task<IReadOnlyList<ScopedLotCandidate>>
        ListScopedLotCandidatesAsync(
            FundTradeScope scope,
            CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<ScopedLotCandidate>>(
            Candidates);

    public Task<long> DeriveCashPositionRawE8Async(
        FundTradeScope scope,
        CancellationToken cancellationToken = default)
        => Task.FromResult(CashPositionRawE8);

    internal FundLotCustodyStoreFake WithLot(
        string lotId,
        decimal available,
        DateOnly? acquiredOn,
        CostBasis costBasis)
    {
        Candidates.Add(
            new ScopedLotCandidate(
                Guid.Parse(lotId),
                FundTradeIds.FundAsset,
                acquiredOn,
                new DateTimeOffset(
                    2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
                Quantity.FromDecimal(available),
                costBasis));

        return this;
    }
}
