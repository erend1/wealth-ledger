using WealthLedger.Application.CoreLedger;
using WealthLedger.Application.OpeningBalances;
using WealthLedger.Application.PhysicalGold;
using WealthLedger.Domain.Assets;
using WealthLedger.Domain.Ledger;
using WealthLedger.Domain.Lots;
using WealthLedger.Domain.Portfolios;
using WealthLedger.Domain.ValueObjects;

namespace WealthLedger.Application.Tests.PhysicalGold;

internal static class GoldIds
{
    internal static readonly Guid Household = Guid.Parse(
        "10000000-0000-0000-0000-000000000901");
    internal static readonly Guid Portfolio = Guid.Parse(
        "20000000-0000-0000-0000-000000000901");
    internal static readonly Guid OtherPortfolio = Guid.Parse(
        "20000000-0000-0000-0000-000000000902");
    internal static readonly Guid Vault = Guid.Parse(
        "30000000-0000-0000-0000-000000000901");
    internal static readonly Guid OtherVault = Guid.Parse(
        "30000000-0000-0000-0000-000000000902");
    internal static readonly Guid CashAccount = Guid.Parse(
        "30000000-0000-0000-0000-000000000903");
    internal static readonly Guid GoldAsset = Guid.Parse(
        "40000000-0000-0000-0000-000000000901");
    internal static readonly Guid CashAsset = Guid.Parse(
        "40000000-0000-0000-0000-000000000902");
    internal static readonly Guid Counterparty = Guid.Parse(
        "50000000-0000-0000-0000-000000000901");
    internal static readonly Guid Lot = Guid.Parse(
        "60000000-0000-0000-0000-000000000901");
    internal static readonly CurrencyCode Try = new("TRY");
    internal static readonly DateOnly ExecutionDate = new(2026, 3, 10);
    internal static readonly DateTimeOffset Now =
        new(2026, 3, 10, 12, 0, 0, TimeSpan.Zero);
}

internal sealed class GoldReferenceStoreFake
    : IOpeningBalanceReferenceReadStore
{
    internal Dictionary<Guid, OpeningBalancePortfolioReference> Portfolios
    { get; } = [];
    internal Dictionary<Guid, OpeningBalanceAccountReference> Accounts
    { get; } = [];
    internal Dictionary<Guid, OpeningBalanceAssetReference> Assets
    { get; } = [];
    internal Dictionary<Guid, OpeningBalanceInstitutionReference> Institutions
    { get; } = [];
    internal bool ThrowOnRead { get; set; }
    internal int ReadCalls { get; private set; }

    internal static GoldReferenceStoreFake CreateValid()
    {
        var store = new GoldReferenceStoreFake();
        store.Portfolios[GoldIds.Portfolio] = new(
            GoldIds.Portfolio,
            GoldIds.Household,
            "MAIN",
            "Main portfolio",
            PortfolioStatus.Active);
        store.Portfolios[GoldIds.OtherPortfolio] = new(
            GoldIds.OtherPortfolio,
            GoldIds.Household,
            "OTHER",
            "Other portfolio",
            PortfolioStatus.Active);
        store.Accounts[GoldIds.Vault] = Account(
            GoldIds.Vault, AccountType.PhysicalVault, null, "Vault");
        store.Accounts[GoldIds.OtherVault] = Account(
            GoldIds.OtherVault, AccountType.PhysicalVault, null, "Other vault");
        store.Accounts[GoldIds.CashAccount] = Account(
            GoldIds.CashAccount, AccountType.Cash, null, "Cash");
        store.Assets[GoldIds.GoldAsset] = new(
            GoldIds.GoldAsset,
            "GOLD_916",
            "Synthetic gold",
            AssetType.PhysicalGold,
            AssetUnit.GrossGram,
            GoldIds.Try,
            LotTrackingMode.Required,
            true,
            GoldIds.Now);
        store.Assets[GoldIds.CashAsset] = new(
            GoldIds.CashAsset,
            "TRY_CASH",
            "Synthetic cash",
            AssetType.Cash,
            AssetUnit.CurrencyUnit,
            GoldIds.Try,
            LotTrackingMode.None,
            true,
            GoldIds.Now);
        store.Institutions[GoldIds.Counterparty] = new(
            GoldIds.Counterparty,
            "JEWELER",
            "Synthetic jeweler",
            InstitutionType.Jeweler,
            true);
        return store;
    }

    private static OpeningBalanceAccountReference Account(
        Guid id,
        AccountType type,
        Guid? institutionId,
        string name)
        => new(
            id,
            GoldIds.Household,
            institutionId,
            name.ToUpperInvariant(),
            name,
            type,
            true,
            new DateOnly(2020, 1, 1),
            null);

    private void Read()
    {
        ReadCalls++;
        if (ThrowOnRead)
        {
            throw new InvalidOperationException("Current references were read.");
        }
    }

    public Task<OpeningBalanceHouseholdReference?> FindHouseholdAsync(
        Guid householdId,
        CancellationToken cancellationToken = default)
    {
        Read();
        return Task.FromResult<OpeningBalanceHouseholdReference?>(
            householdId == GoldIds.Household
                ? new(householdId, GoldIds.Try)
                : null);
    }

    public Task<OpeningBalanceCurrencyReference?> FindCurrencyAsync(
        CurrencyCode code,
        CancellationToken cancellationToken = default)
    {
        Read();
        return Task.FromResult<OpeningBalanceCurrencyReference?>(
            code == GoldIds.Try
                ? new(code, "Turkish lira", 2)
                : null);
    }

    public Task<OpeningBalanceInstitutionReference?> FindInstitutionAsync(
        Guid institutionId,
        CancellationToken cancellationToken = default)
    {
        Read();
        Institutions.TryGetValue(institutionId, out var value);
        return Task.FromResult(value);
    }

    public Task<OpeningBalancePortfolioReference?> FindPortfolioAsync(
        Guid portfolioId,
        CancellationToken cancellationToken = default)
    {
        Read();
        Portfolios.TryGetValue(portfolioId, out var value);
        return Task.FromResult(value);
    }

    public Task<OpeningBalanceAccountReference?> FindAccountAsync(
        Guid accountId,
        CancellationToken cancellationToken = default)
    {
        Read();
        Accounts.TryGetValue(accountId, out var value);
        return Task.FromResult(value);
    }

    public Task<OpeningBalanceAssetReference?> FindAssetAsync(
        Guid assetId,
        CancellationToken cancellationToken = default)
    {
        Read();
        Assets.TryGetValue(assetId, out var value);
        return Task.FromResult(value);
    }
}

internal sealed class GoldCustodyStoreFake : IPhysicalGoldCustodyReadStore
{
    internal List<PhysicalGoldCustodyLot> Lots { get; } = [];
    internal long CashRawE8 { get; set; } = 100_000_000_000_000;

    internal GoldCustodyStoreFake AddLot(
        Guid? id = null,
        decimal scopedGross = 20m,
        int scopedPieces = 2,
        CostBasis? cost = null)
    {
        Lots.Add(new PhysicalGoldCustodyLot(
            id ?? GoldIds.Lot,
            GoldIds.GoldAsset,
            new DateOnly(2025, 1, 1),
            GoldIds.Now,
            cost ?? CostBasis.Known(
                Money.FromMinorUnits(200_000, GoldIds.Try)),
            new Fineness(916_000),
            2,
            "916",
            "CERT",
            "Synthetic",
            Quantity.FromDecimal(scopedGross),
            scopedPieces,
            Quantity.FromDecimal(scopedGross),
            scopedPieces));
        return this;
    }

    public Task<IReadOnlyList<PhysicalGoldCustodyLot>> ListScopedLotsAsync(
        PhysicalGoldCustodyScope scope,
        CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<PhysicalGoldCustodyLot>>(Lots);

    public Task<long> DeriveCashPositionRawE8Async(
        Guid householdId,
        Guid portfolioId,
        Guid accountId,
        Guid assetId,
        CancellationToken cancellationToken = default)
        => Task.FromResult(CashRawE8);
}

internal sealed class GoldRealizedCostStoreFake
    : IPhysicalGoldRealizedCostReadStore
{
    internal Dictionary<Guid, RealizedCostLotHistory> Histories { get; } = [];

    internal GoldRealizedCostStoreFake AddKnownLot(
        Guid? id = null,
        decimal quantity = 20m,
        long costMinor = 200_000)
    {
        var lotId = id ?? GoldIds.Lot;
        Histories[lotId] = new RealizedCostLotHistory(
            lotId,
            Quantity.FromDecimal(quantity),
            CostBasis.Known(Money.FromMinorUnits(costMinor, GoldIds.Try)),
            []);
        return this;
    }

    public Task<IReadOnlyList<RealizedCostLotHistory>>
        ListEffectiveLotHistoryAsync(
            Guid householdId,
            Guid saleTransactionId,
            CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<RealizedCostLotHistory>>(
            Histories.Values.ToArray());

    public Task<IReadOnlyList<RealizedCostLotHistory>> ListLotHistoryAsync(
        Guid householdId,
        IReadOnlyCollection<Guid> assetLotIds,
        CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<RealizedCostLotHistory>>(
            assetLotIds.Where(Histories.ContainsKey)
                .Select(x => Histories[x]).ToArray());
}

internal sealed class GoldSubmissionStoreFake : ILedgerSubmissionStore
{
    internal LedgerSubmissionReceipt? Receipt { get; set; }

    public Task<LedgerSubmissionReceipt?> FindReceiptAsync(
        LedgerSubmissionScope scope,
        CancellationToken cancellationToken = default)
        => Task.FromResult(Receipt?.Scope == scope ? Receipt : null);

    public Task<LedgerSubmissionCommitResult> TryCommitAsync(
        LedgerSubmissionReceipt receipt,
        LedgerTransaction transaction,
        IReadOnlyCollection<AssetLot> newLots,
        CancellationToken cancellationToken = default)
        => throw new InvalidOperationException();
}

internal sealed class GoldPostingStoreFake : IPhysicalGoldPostingStore
{
    internal PhysicalGoldCommitStatus Status { get; set; }
        = PhysicalGoldCommitStatus.Committed;
    internal LedgerSubmissionReceipt? Receipt { get; private set; }
    internal LedgerTransaction? Transaction { get; private set; }
    internal AssetLot? Lot { get; private set; }
    internal IReadOnlyList<PhysicalGoldSelectedLot> Plan { get; private set; }
        = [];

    public Task<PhysicalGoldCommitResult> TryCommitPurchaseAsync(
        LedgerSubmissionReceipt receipt,
        LedgerTransaction transaction,
        AssetLot newLot,
        PhysicalGoldTradeScope scope,
        bool hasExplanatoryNote,
        CancellationToken cancellationToken = default)
    {
        Capture(receipt, transaction);
        Lot = newLot;
        return Result(receipt);
    }

    public Task<PhysicalGoldCommitResult> TryCommitSaleAsync(
        LedgerSubmissionReceipt receipt,
        LedgerTransaction transaction,
        PhysicalGoldTradeScope scope,
        IReadOnlyList<PhysicalGoldSelectedLot> reviewedPlan,
        bool hasExplanatoryNote,
        CancellationToken cancellationToken = default)
    {
        Capture(receipt, transaction);
        Plan = reviewedPlan;
        return Result(receipt);
    }

    public Task<PhysicalGoldCommitResult> TryCommitTransferAsync(
        LedgerSubmissionReceipt receipt,
        LedgerTransaction transaction,
        PhysicalGoldTransferScope scope,
        IReadOnlyList<PhysicalGoldSelectedLot> reviewedPlan,
        bool hasExplanatoryNote,
        CancellationToken cancellationToken = default)
    {
        Capture(receipt, transaction);
        Plan = reviewedPlan;
        return Result(receipt);
    }

    private void Capture(
        LedgerSubmissionReceipt receipt,
        LedgerTransaction transaction)
    {
        Receipt = receipt;
        Transaction = transaction;
    }

    private Task<PhysicalGoldCommitResult> Result(
        LedgerSubmissionReceipt receipt)
        => Task.FromResult(new PhysicalGoldCommitResult(Status, receipt));
}

internal sealed class GoldTimeProvider : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => GoldIds.Now;
    public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
}
