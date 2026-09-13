using Microsoft.EntityFrameworkCore;
using WealthLedger.Application.CoreLedger;
using WealthLedger.Application.FundTrades;
using WealthLedger.Domain.Ledger;
using WealthLedger.Domain.Lots;
using WealthLedger.Domain.ValueObjects;

namespace WealthLedger.Infrastructure.Persistence;

/// <summary>
/// Derives fund custody and cash position from posted history.
/// </summary>
/// <remarks>
/// This delegates to the posting store's queries so preview and posting ask
/// exactly the same question of the database. If they diverged, a plan could
/// look valid during review and be refused at commit for no visible reason.
/// </remarks>
public sealed class EfCoreFundLotCustodyReadStore : IFundLotCustodyReadStore
{
    private readonly EfCoreLedgerPostingStore _postingStore;

    public EfCoreFundLotCustodyReadStore(
        EfCoreLedgerPostingStore postingStore)
    {
        _postingStore = postingStore
            ?? throw new ArgumentNullException(nameof(postingStore));
    }

    public Task<IReadOnlyList<ScopedLotCandidate>>
        ListScopedLotCandidatesAsync(
            FundTradeScope scope,
            CancellationToken cancellationToken = default)
        => _postingStore.ReadScopedLotCandidatesAsync(
            scope,
            cancellationToken);

    public Task<long> DeriveCashPositionRawE8Async(
        FundTradeScope scope,
        CancellationToken cancellationToken = default)
        => _postingStore.ReadCashPositionRawE8Async(
            scope,
            cancellationToken);
}

/// <summary>
/// Loads the effective disposal history realized cost is derived from.
/// </summary>
public sealed class EfCoreFundRealizedCostReadStore
    : IFundRealizedCostReadStore
{
    private readonly WealthLedgerDbContext _dbContext;

    public EfCoreFundRealizedCostReadStore(
        WealthLedgerDbContext dbContext)
    {
        _dbContext = dbContext
            ?? throw new ArgumentNullException(nameof(dbContext));
    }

    public async Task<IReadOnlyList<RealizedCostLotHistory>>
        ListEffectiveLotHistoryAsync(
            Guid householdId,
            Guid saleTransactionId,
            CancellationToken cancellationToken = default)
    {
        var consumedLotIds =
            await (
                from allocation in _dbContext.LotEntryAllocations
                join entry in _dbContext.TransactionEntries
                    on allocation.TransactionEntryId equals entry.Id
                where entry.TransactionId == saleTransactionId
                      && allocation.QuantityDeltaE8 < 0
                select allocation.AssetLotId)
                .Distinct()
                .ToListAsync(cancellationToken);

        if (consumedLotIds.Count == 0)
        {
            return [];
        }

        var lots =
            await _dbContext.AssetLots
                .Where(lot => consumedLotIds.Contains(lot.Id))
                .ToListAsync(cancellationToken);

        /*
         * Only a posted sale that has no posted reversal is effective. A
         * reversed sale and its reversal stay in history but leave the
         * apportionment sequence, so neither is loaded here.
         */
        var disposals =
            await (
                from allocation in _dbContext.LotEntryAllocations
                join entry in _dbContext.TransactionEntries
                    on allocation.TransactionEntryId equals entry.Id
                join tx in _dbContext.LedgerTransactions
                    on entry.TransactionId equals tx.Id
                where consumedLotIds.Contains(allocation.AssetLotId)
                      && allocation.QuantityDeltaE8 < 0
                      && tx.HouseholdId == householdId
                      && tx.Type == TransactionType.Sell
                      && tx.Status == TransactionStatus.Posted
                      && !_dbContext.LedgerTransactions.Any(reversal =>
                          reversal.ReversalOfTransactionId == tx.Id
                          && reversal.Status == TransactionStatus.Posted)
                select new
                {
                    allocation.AssetLotId,
                    allocation.Id,
                    allocation.QuantityDeltaE8,
                    TransactionId = tx.Id,
                    tx.PostedAtUtc,
                    entry.EntrySequence
                })
                .ToListAsync(cancellationToken);

        var openingAllocations =
            await (
                from allocation in _dbContext.LotEntryAllocations
                join lot in _dbContext.AssetLots
                    on allocation.AssetLotId equals lot.Id
                where consumedLotIds.Contains(lot.Id)
                      && allocation.TransactionEntryId
                          == lot.OpeningTransactionEntryId
                select new
                {
                    allocation.AssetLotId,
                    allocation.QuantityDeltaE8
                })
                .ToListAsync(cancellationToken);

        var openingByLot =
            openingAllocations.ToDictionary(
                x => x.AssetLotId,
                x => x.QuantityDeltaE8);

        var result = new List<RealizedCostLotHistory>();

        foreach (var lot in lots)
        {
            if (!openingByLot.TryGetValue(
                    lot.Id,
                    out var openingQuantity)
                || openingQuantity <= 0)
            {
                throw new CoreLedgerPersistenceException(
                    "A consumed lot is missing its positive opening allocation.");
            }

            var ordered =
                disposals
                    .Where(x => x.AssetLotId == lot.Id)
                    .OrderBy(x => x.PostedAtUtc)
                    .ThenBy(x => x.TransactionId)
                    .ThenBy(x => x.EntrySequence)
                    .ThenBy(x => x.Id)
                    .Select(x =>
                        new EffectiveLotDisposal(
                            x.TransactionId,
                            new DateTimeOffset(
                                DateTime.SpecifyKind(
                                    x.PostedAtUtc!.Value,
                                    DateTimeKind.Utc)),
                            x.EntrySequence,
                            x.Id,
                            Quantity.FromRaw(
                                Math.Abs(x.QuantityDeltaE8))))
                    .ToList();

            result.Add(
                new RealizedCostLotHistory(
                    lot.Id,
                    Quantity.FromRaw(openingQuantity),
                    EfCoreLedgerPostingStore.MapCostBasis(
                        lot.CostBasisStatus,
                        lot.OriginalCostBasisMinor,
                        lot.CostBasisCurrencyCode),
                    ordered));
        }

        return result;
    }
}

/// <summary>
/// Reads a posted fund trade back for its receipt and verification page.
/// </summary>
public sealed class EfCoreFundTradeVerificationReadStore
    : IFundTradeVerificationReadStore
{
    private readonly WealthLedgerDbContext _dbContext;

    public EfCoreFundTradeVerificationReadStore(
        WealthLedgerDbContext dbContext)
    {
        _dbContext = dbContext
            ?? throw new ArgumentNullException(nameof(dbContext));
    }

    public async Task<FundTradePersistedFacts?> FindPostedFundTradeAsync(
        Guid householdId,
        Guid transactionId,
        CancellationToken cancellationToken = default)
    {
        var transaction =
            await _dbContext.LedgerTransactions
                .AsNoTracking()
                .SingleOrDefaultAsync(
                    x => x.Id == transactionId
                         && x.HouseholdId == householdId,
                    cancellationToken);

        if (transaction is null
            || transaction.Status != TransactionStatus.Posted
            || transaction.Type is not TransactionType.Buy
                and not TransactionType.Sell)
        {
            return null;
        }

        var entries =
            await _dbContext.TransactionEntries
                .AsNoTracking()
                .Where(x => x.TransactionId == transactionId)
                .OrderBy(x => x.EntrySequence)
                .ToListAsync(cancellationToken);

        var principal =
            entries.SingleOrDefault(
                x => x.Role == EntryRole.Principal)
            ?? throw new CoreLedgerPersistenceException(
                "A posted fund trade must contain one principal entry.");

        var consideration =
            entries.SingleOrDefault(
                x => x.Role == EntryRole.Consideration)
            ?? throw new CoreLedgerPersistenceException(
                "A posted fund trade must contain one consideration entry.");

        var costRows =
            await _dbContext.TransactionCostComponents
                .AsNoTracking()
                .Where(x => x.TransactionId == transactionId)
                .ToListAsync(cancellationToken);

        var currency =
            new CurrencyCode(
                principal.PriceCurrencyCode
                ?? throw new CoreLedgerPersistenceException(
                    "A posted fund trade must preserve its executed price currency."));

        var minorUnitDigits =
            await _dbContext.Currencies
                .AsNoTracking()
                .Where(x => x.Code == currency.Value)
                .Select(x => (int?)x.MinorUnitDigits)
                .SingleOrDefaultAsync(cancellationToken)
            ?? throw new CoreLedgerPersistenceException(
                "The persisted trade currency is no longer available.");

        var reversedBy =
            await _dbContext.LedgerTransactions
                .AsNoTracking()
                .Where(x =>
                    x.ReversalOfTransactionId == transactionId
                    && x.Status == TransactionStatus.Posted)
                .Select(x => (Guid?)x.Id)
                .SingleOrDefaultAsync(cancellationToken);

        var allocations =
            await ReadAllocationsAsync(
                principal.Id,
                cancellationToken);

        return new FundTradePersistedFacts(
            transaction.Id,
            transaction.HouseholdId,
            transaction.Type,
            transaction.Status,
            transaction.OrderDate,
            transaction.ExecutionDate
            ?? throw new CoreLedgerPersistenceException(
                "A posted fund trade must have an execution date."),
            transaction.SettlementDate,
            new DateTimeOffset(
                DateTime.SpecifyKind(
                    transaction.PostedAtUtc!.Value,
                    DateTimeKind.Utc)),
            transaction.ExternalReference,
            transaction.Note,
            reversedBy,
            new FundTradeScope(
                transaction.HouseholdId,
                principal.PortfolioId,
                principal.AccountId,
                consideration.AccountId,
                principal.AssetId,
                consideration.AssetId),
            Quantity.FromRaw(
                Math.Abs(principal.QuantityDeltaE8)),
            UnitPrice.FromRaw(
                principal.UnitPriceE8
                ?? throw new CoreLedgerPersistenceException(
                    "A posted fund trade must preserve its executed unit price."),
                currency),
            Money.FromMinorUnits(
                ToMinorUnits(
                    Math.Abs(consideration.QuantityDeltaE8),
                    minorUnitDigits),
                currency),
            costRows
                .Select(x =>
                    new FundTradeCostInput(
                        x.Type,
                        x.Treatment,
                        Money.FromMinorUnits(
                            x.AmountMinor,
                            new CurrencyCode(x.CurrencyCode)),
                        x.Note))
                .ToList(),
            allocations);
    }

    public async Task<long> DeriveFundPositionRawE8Async(
        FundTradeScope scope,
        CancellationToken cancellationToken = default)
    {
        var deltas =
            await (
                from entry in _dbContext.TransactionEntries
                join tx in _dbContext.LedgerTransactions
                    on entry.TransactionId equals tx.Id
                where tx.HouseholdId == scope.HouseholdId
                      && tx.Status == TransactionStatus.Posted
                      && entry.PortfolioId == scope.PortfolioId
                      && entry.AccountId == scope.FundAccountId
                      && entry.AssetId == scope.FundAssetId
                select entry.QuantityDeltaE8)
                .ToListAsync(cancellationToken);

        long total = 0;

        foreach (var delta in deltas)
        {
            total = checked(total + delta);
        }

        return total;
    }

    private async Task<IReadOnlyList<FundTradeAllocationFact>>
        ReadAllocationsAsync(
            Guid principalEntryId,
            CancellationToken cancellationToken)
    {
        var rows =
            await (
                from allocation in _dbContext.LotEntryAllocations
                join lot in _dbContext.AssetLots
                    on allocation.AssetLotId equals lot.Id
                where allocation.TransactionEntryId == principalEntryId
                select new
                {
                    allocation.Id,
                    allocation.AssetLotId,
                    allocation.QuantityDeltaE8,
                    lot.AcquiredOn,
                    lot.CostBasisStatus,
                    lot.OriginalCostBasisMinor,
                    lot.CostBasisCurrencyCode,
                    lot.CreatedAtUtc
                })
                .ToListAsync(cancellationToken);

        return rows
            .OrderBy(x => x.AcquiredOn ?? DateOnly.MinValue)
            .ThenBy(x => x.CreatedAtUtc)
            .ThenBy(x => x.AssetLotId)
            .Select(x =>
                new FundTradeAllocationFact(
                    x.AssetLotId,
                    x.Id,
                    x.AcquiredOn,
                    x.CostBasisStatus,
                    x.CostBasisStatus == CostBasisStatus.Known
                        ? Money.FromMinorUnits(
                            x.OriginalCostBasisMinor!.Value,
                            new CurrencyCode(
                                x.CostBasisCurrencyCode!))
                        : null,
                    Quantity.FromRaw(
                        Math.Abs(x.QuantityDeltaE8))))
            .ToList();
    }

    /// <summary>
    /// Converts an E8 cash quantity back into whole minor units.
    /// </summary>
    /// <remarks>
    /// Cash entries store an exact scaled multiple of a minor unit, so this
    /// division is exact. A remainder would mean corrupt persisted data
    /// rather than a rounding decision, and fails closed.
    /// </remarks>
    private static long ToMinorUnits(
        long quantityRawE8,
        int minorUnitDigits)
    {
        long divisor = 1;

        for (var digit = minorUnitDigits; digit < 8; digit++)
        {
            divisor = checked(divisor * 10);
        }

        if (quantityRawE8 % divisor != 0)
        {
            throw new CoreLedgerPersistenceException(
                "A persisted cash entry is not an exact multiple of one minor unit.");
        }

        return quantityRawE8 / divisor;
    }
}
