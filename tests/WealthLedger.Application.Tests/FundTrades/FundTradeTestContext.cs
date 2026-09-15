using WealthLedger.Application.CoreLedger;
using WealthLedger.Application.FundTrades;
using WealthLedger.Domain.Ledger;
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

    /// <summary>
    /// How many reference reads this store served, so a test can prove that
    /// receipt-first replay short-circuits before any current-state lookup.
    /// </summary>
    internal int ReadCalls { get; private set; }

    /// <summary>
    /// Builds a valid reference set for callers that use their own
    /// identifiers, so existing suites keep their fixtures.
    /// </summary>
    internal static FundTradeReferenceStoreFake CreateFor(
        Guid householdId,
        Guid portfolioId,
        Guid fundAccountId,
        Guid cashAccountId,
        Guid fundAssetId,
        Guid cashAssetId,
        int minorUnitDigits = 2)
    {
        var store = CreateValid();

        store.Household =
            new OpeningBalanceHouseholdReference(
                householdId,
                FundTradeIds.Try);

        store.Portfolio =
            store.Portfolio! with
            {
                PortfolioId = portfolioId,
                HouseholdId = householdId
            };

        var fundAccount =
            store.Accounts[FundTradeIds.FundAccount] with
            {
                AccountId = fundAccountId,
                HouseholdId = householdId
            };

        var cashAccount =
            store.Accounts[FundTradeIds.CashAccount] with
            {
                AccountId = cashAccountId,
                HouseholdId = householdId
            };

        var fundAsset =
            store.Assets[FundTradeIds.FundAsset] with
            {
                AssetId = fundAssetId
            };

        var cashAsset =
            store.Assets[FundTradeIds.CashAsset] with
            {
                AssetId = cashAssetId
            };

        store.Accounts.Clear();

        /*
         * One account may carry both legs, which is what a legacy
         * single-account purchase does. That shared account still has to
         * satisfy the stricter fund-account rule, so it stays an investment
         * account rather than being overwritten by the cash-typed one.
         */
        store.Accounts[fundAccountId] = fundAccount;

        if (cashAccountId != fundAccountId)
        {
            store.Accounts[cashAccountId] = cashAccount;
        }

        store.Assets.Clear();
        store.Assets[fundAssetId] = fundAsset;
        store.Assets[cashAssetId] = cashAsset;

        store.Currencies[FundTradeIds.Try] =
            store.Currencies[FundTradeIds.Try] with
            {
                MinorUnitDigits = minorUnitDigits
            };

        return store;
    }

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
    {
        ReadCalls++;

        return Task.FromResult(
            Household?.HouseholdId == householdId
                ? Household
                : null);
    }

    public Task<OpeningBalanceCurrencyReference?> FindCurrencyAsync(
        CurrencyCode code,
        CancellationToken cancellationToken = default)
    {
        ReadCalls++;
        Currencies.TryGetValue(code, out var currency);
        return Task.FromResult(currency);
    }

    public Task<OpeningBalanceInstitutionReference?> FindInstitutionAsync(
        Guid institutionId,
        CancellationToken cancellationToken = default)
    {
        ReadCalls++;

        return Task.FromResult<OpeningBalanceInstitutionReference?>(
            institutionId == FundTradeIds.Institution
                ? new OpeningBalanceInstitutionReference(
                    FundTradeIds.Institution,
                    "BANK",
                    "Synthetic bank",
                    InstitutionType.Bank,
                    IsActive: true)
                : null);
    }

    public Task<OpeningBalancePortfolioReference?> FindPortfolioAsync(
        Guid portfolioId,
        CancellationToken cancellationToken = default)
    {
        ReadCalls++;

        return Task.FromResult(
            Portfolio?.PortfolioId == portfolioId
                ? Portfolio
                : null);
    }

    public Task<OpeningBalanceAccountReference?> FindAccountAsync(
        Guid accountId,
        CancellationToken cancellationToken = default)
    {
        ReadCalls++;
        Accounts.TryGetValue(accountId, out var account);
        return Task.FromResult(account);
    }

    public Task<OpeningBalanceAssetReference?> FindAssetAsync(
        Guid assetId,
        CancellationToken cancellationToken = default)
    {
        ReadCalls++;
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

/// <summary>
/// Captures what a fund trade would have written, without a database.
/// </summary>
internal sealed class FundTradePostingStoreFake : IFundTradePostingStore
{
    internal LedgerTransaction? Transaction { get; private set; }

    internal AssetLot? NewLot { get; private set; }

    internal IReadOnlyList<ReviewedLotAllocation> ReviewedPlan
    { get; private set; } = [];

    internal LedgerSubmissionReceipt? AttemptedReceipt
    { get; private set; }

    internal bool LastHadExplanatoryNote { get; private set; }

    internal int CommitCalls { get; private set; }

    internal FundSaleCommitStatus NextStatus { get; set; }
        = FundSaleCommitStatus.Committed;

    internal LedgerSubmissionReceipt? WinningReceipt { get; set; }

    public Task<FundSaleCommitResult> TryCommitPurchaseAsync(
        LedgerSubmissionReceipt receipt,
        LedgerTransaction transaction,
        AssetLot newLot,
        FundTradeScope scope,
        bool hasExplanatoryNote,
        CancellationToken cancellationToken = default)
    {
        CommitCalls++;
        AttemptedReceipt = receipt;
        Transaction = transaction;
        NewLot = newLot;
        LastHadExplanatoryNote = hasExplanatoryNote;

        return Task.FromResult(
            new FundSaleCommitResult(
                NextStatus,
                WinningReceipt ?? receipt));
    }

    public Task<FundSaleCommitResult> TryCommitSaleAsync(
        LedgerSubmissionReceipt receipt,
        LedgerTransaction transaction,
        FundTradeScope scope,
        IReadOnlyList<ReviewedLotAllocation> reviewedPlan,
        bool hasExplanatoryNote,
        CancellationToken cancellationToken = default)
    {
        CommitCalls++;
        AttemptedReceipt = receipt;
        Transaction = transaction;
        ReviewedPlan = reviewedPlan;
        LastHadExplanatoryNote = hasExplanatoryNote;

        return Task.FromResult(
            new FundSaleCommitResult(
                NextStatus,
                WinningReceipt ?? receipt));
    }
}

/// <summary>
/// Supplies lot cost history without a database.
/// </summary>
/// <remarks>
/// Histories are built from the custody fake's candidates, so a lot with no
/// recorded disposal has its available quantity as its original quantity.
/// A prior disposal can be declared explicitly, which is what makes the
/// cumulative rule observable at this level.
/// </remarks>
internal sealed class FundRealizedCostStoreFake : IFundRealizedCostReadStore
{
    private readonly FundLotCustodyStoreFake _custody;

    internal FundRealizedCostStoreFake(FundLotCustodyStoreFake custody)
        => _custody = custody;

    /// <summary>
    /// Quantity already disposed from a lot before the sale under review.
    /// </summary>
    internal Dictionary<Guid, decimal> PriorDisposals { get; } = [];

    public Task<IReadOnlyList<RealizedCostLotHistory>>
        ListEffectiveLotHistoryAsync(
            Guid householdId,
            Guid saleTransactionId,
            CancellationToken cancellationToken = default)
        => ListLotHistoryAsync(
            householdId,
            _custody.Candidates.Select(x => x.AssetLotId).ToArray(),
            cancellationToken);

    public Task<IReadOnlyList<RealizedCostLotHistory>> ListLotHistoryAsync(
        Guid householdId,
        IReadOnlyCollection<Guid> assetLotIds,
        CancellationToken cancellationToken = default)
    {
        var histories = new List<RealizedCostLotHistory>();

        foreach (var candidate in _custody.Candidates
                     .Where(x => assetLotIds.Contains(x.AssetLotId)))
        {
            var prior =
                PriorDisposals.TryGetValue(
                    candidate.AssetLotId,
                    out var declared)
                    ? declared
                    : 0m;

            var disposals = new List<EffectiveLotDisposal>();

            if (prior > 0)
            {
                disposals.Add(
                    new EffectiveLotDisposal(
                        Guid.Parse("dddddddd-0000-0000-0000-000000000001"),
                        new DateTimeOffset(
                            2026, 2, 1, 0, 0, 0, TimeSpan.Zero),
                        EntrySequence: 0,
                        AllocationId:
                            Guid.Parse(
                                "eeeeeeee-0000-0000-0000-000000000001"),
                        Quantity.FromDecimal(prior)));
            }

            // Available is what remains, so the original is the sum.
            histories.Add(
                new RealizedCostLotHistory(
                    candidate.AssetLotId,
                    Quantity.FromDecimal(
                        candidate.AvailableQuantity.ToDecimal() + prior),
                    candidate.CostBasis,
                    disposals));
        }

        return Task.FromResult<IReadOnlyList<RealizedCostLotHistory>>(
            histories);
    }
}
