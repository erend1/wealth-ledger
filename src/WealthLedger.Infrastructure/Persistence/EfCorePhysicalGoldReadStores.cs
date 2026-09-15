using Microsoft.EntityFrameworkCore;
using WealthLedger.Application.CoreLedger;
using WealthLedger.Application.PhysicalGold;
using WealthLedger.Domain.Assets;
using WealthLedger.Domain.Ledger;
using WealthLedger.Domain.Lots;
using WealthLedger.Domain.ValueObjects;

namespace WealthLedger.Infrastructure.Persistence;

public sealed class EfCorePhysicalGoldCustodyReadStore
    : IPhysicalGoldCustodyReadStore
{
    private readonly EfCoreLedgerPostingStore _postingStore;

    public EfCorePhysicalGoldCustodyReadStore(
        EfCoreLedgerPostingStore postingStore)
    {
        _postingStore = postingStore;
    }

    public Task<IReadOnlyList<PhysicalGoldCustodyLot>> ListScopedLotsAsync(
        PhysicalGoldCustodyScope scope,
        CancellationToken cancellationToken = default)
        => _postingStore.ReadPhysicalGoldScopedLotsAsync(
            scope, cancellationToken);

    public Task<long> DeriveCashPositionRawE8Async(
        Guid householdId,
        Guid portfolioId,
        Guid accountId,
        Guid assetId,
        CancellationToken cancellationToken = default)
        => _postingStore.ReadCashPositionRawE8Async(
            householdId,
            portfolioId,
            accountId,
            assetId,
            cancellationToken);
}

public sealed class EfCorePhysicalGoldRealizedCostReadStore
    : IPhysicalGoldRealizedCostReadStore
{
    private readonly EfCoreFundRealizedCostReadStore _inner;

    public EfCorePhysicalGoldRealizedCostReadStore(
        WealthLedgerDbContext dbContext)
    {
        _inner = new EfCoreFundRealizedCostReadStore(dbContext);
    }

    public Task<IReadOnlyList<RealizedCostLotHistory>>
        ListEffectiveLotHistoryAsync(
            Guid householdId,
            Guid saleTransactionId,
            CancellationToken cancellationToken = default)
        => _inner.ListEffectiveLotHistoryAsync(
            householdId, saleTransactionId, cancellationToken);

    public Task<IReadOnlyList<RealizedCostLotHistory>> ListLotHistoryAsync(
        Guid householdId,
        IReadOnlyCollection<Guid> assetLotIds,
        CancellationToken cancellationToken = default)
        => _inner.ListLotHistoryAsync(
            householdId, assetLotIds, cancellationToken);
}

public sealed class EfCorePhysicalGoldVerificationReadStore
    : IPhysicalGoldVerificationReadStore
{
    private readonly WealthLedgerDbContext _dbContext;

    public EfCorePhysicalGoldVerificationReadStore(
        WealthLedgerDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<PhysicalGoldActivityPersistedFacts?>
        FindPostedActivityAsync(
            Guid householdId,
            Guid transactionId,
            CancellationToken cancellationToken = default)
    {
        var transaction = await _dbContext.LedgerTransactions
            .AsNoTracking()
            .SingleOrDefaultAsync(
                x => x.Id == transactionId
                     && x.HouseholdId == householdId
                     && x.Status == TransactionStatus.Posted,
                cancellationToken);
        if (transaction is null
            || transaction.Type is not TransactionType.Buy
                and not TransactionType.Sell
                and not TransactionType.Transfer)
        {
            return null;
        }

        var entries = await _dbContext.TransactionEntries
            .AsNoTracking()
            .Where(x => x.TransactionId == transactionId)
            .OrderBy(x => x.EntrySequence)
            .ToListAsync(cancellationToken);
        var assetIds = entries.Select(x => x.AssetId).Distinct().ToArray();
        var assets = await _dbContext.Assets
            .AsNoTracking()
            .Where(x => assetIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, cancellationToken);
        var goldEntries = entries
            .Where(x => assets.TryGetValue(x.AssetId, out var asset)
                        && asset.Type == AssetType.PhysicalGold)
            .ToArray();

        if (goldEntries.Length == 0)
        {
            return null;
        }

        var validGoldShape = transaction.Type switch
        {
            TransactionType.Buy or TransactionType.Sell =>
                goldEntries.Length == 1
                && goldEntries[0].Role == EntryRole.Principal,
            TransactionType.Transfer =>
                goldEntries.Length == 2
                && goldEntries.All(x => x.Role == EntryRole.Transfer)
                && goldEntries.Count(x => x.QuantityDeltaE8 < 0) == 1
                && goldEntries.Count(x => x.QuantityDeltaE8 > 0) == 1,
            _ => false
        };
        if (!validGoldShape)
        {
            throw new CoreLedgerPersistenceException(
                "A posted physical-gold activity has an unsupported principal shape.");
        }

        var sourcePrincipal = transaction.Type == TransactionType.Transfer
            ? goldEntries.Single(x => x.QuantityDeltaE8 < 0)
            : goldEntries[0];
        var goldAsset = assets[sourcePrincipal.AssetId];
        var currencyCode = goldAsset.BaseCurrencyCode
            ?? throw new CoreLedgerPersistenceException(
                "A persisted physical-gold asset lacks its base currency.");
        var currency = await _dbContext.Currencies
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.Code == currencyCode,
                cancellationToken)
            ?? throw new CoreLedgerPersistenceException(
                "The persisted physical-gold currency is unavailable.");
        var consideration = entries.SingleOrDefault(
            x => x.Role == EntryRole.Consideration);
        if (transaction.Type is TransactionType.Buy or TransactionType.Sell
            && consideration is null)
        {
            throw new CoreLedgerPersistenceException(
                "A posted physical-gold trade lacks consideration.");
        }

        var costs = await _dbContext.TransactionCostComponents
            .AsNoTracking()
            .Where(x => x.TransactionId == transactionId)
            .OrderBy(x => x.Id)
            .Select(x => new PhysicalGoldCostInput(
                x.Type,
                x.Treatment,
                Money.FromMinorUnits(
                    x.AmountMinor, new CurrencyCode(x.CurrencyCode)),
                x.Note))
            .ToListAsync(cancellationToken);
        var tradeDetail = await _dbContext.PhysicalGoldTradeDetails
            .AsNoTracking()
            .SingleOrDefaultAsync(
                x => x.LedgerTransactionId == transactionId,
                cancellationToken);
        var counterpartyName = tradeDetail?.CounterpartyInstitutionId is Guid id
            ? await _dbContext.Institutions.AsNoTracking()
                .Where(x => x.Id == id)
                .Select(x => x.Name)
                .SingleOrDefaultAsync(cancellationToken)
            : null;
        var allocations = await ReadActivityAllocationsAsync(
            transactionId, cancellationToken);
        if (allocations.Count == 0)
        {
            throw new CoreLedgerPersistenceException(
                "A posted physical-gold activity lacks exact lot allocations.");
        }

        var grossRaw = Math.Abs(sourcePrincipal.QuantityDeltaE8);
        var sourcePieces = allocations
            .Where(x => x.AccountId == sourcePrincipal.AccountId
                        && x.PortfolioId == sourcePrincipal.PortfolioId
                        && Math.Sign(x.GrossWeightDeltaRawE8)
                            == Math.Sign(sourcePrincipal.QuantityDeltaE8))
            .Aggregate(0, (total, x) => checked(
                total + Math.Abs(x.PieceDelta)));
        var createdLotId = await (
            from lot in _dbContext.AssetLots.AsNoTracking()
            join entry in _dbContext.TransactionEntries.AsNoTracking()
                on lot.OpeningTransactionEntryId equals entry.Id
            where entry.TransactionId == transactionId
                  && entry.Id == sourcePrincipal.Id
            select (Guid?)lot.Id).SingleOrDefaultAsync(cancellationToken);
        var reversedBy = await _dbContext.LedgerTransactions
            .AsNoTracking()
            .Where(x => x.ReversalOfTransactionId == transactionId
                        && x.Status == TransactionStatus.Posted)
            .Select(x => (Guid?)x.Id)
            .SingleOrDefaultAsync(cancellationToken);
        Guid? cashAssetId = consideration?.AssetId
            ?? entries.FirstOrDefault(
                x => x.Role is EntryRole.Fee or EntryRole.Tax)?.AssetId;

        return new PhysicalGoldActivityPersistedFacts(
            transaction.Id,
            transaction.HouseholdId,
            transaction.Type,
            transaction.Status,
            transaction.OrderDate,
            transaction.ExecutionDate
                ?? throw new CoreLedgerPersistenceException(
                    "A posted physical-gold activity lacks execution date."),
            transaction.SettlementDate,
            new DateTimeOffset(DateTime.SpecifyKind(
                transaction.PostedAtUtc!.Value, DateTimeKind.Utc)),
            transaction.ExternalReference,
            transaction.Note,
            reversedBy,
            sourcePrincipal.AssetId,
            cashAssetId,
            currencyCode,
            tradeDetail?.CounterpartyInstitutionId,
            counterpartyName,
            Quantity.FromRaw(grossRaw),
            sourcePieces,
            sourcePrincipal.UnitPriceE8 is long priceRaw
                ? UnitPrice.FromRaw(
                    priceRaw,
                    new CurrencyCode(
                        sourcePrincipal.PriceCurrencyCode
                        ?? throw new CoreLedgerPersistenceException(
                            "A persisted physical-gold price lacks currency.")))
                : null,
            consideration is null
                ? null
                : Money.FromMinorUnits(
                    ToMinorUnits(
                        Math.Abs(consideration.QuantityDeltaE8),
                        currency.MinorUnitDigits),
                    new CurrencyCode(currencyCode)),
            costs,
            allocations,
            createdLotId);
    }

    public async Task<IReadOnlyList<PhysicalGoldCustodyPosition>>
        ListCustodyAsync(
            Guid householdId,
            Guid? transactionId = null,
            CancellationToken cancellationToken = default)
    {
        Guid[]? selectedLots = null;
        if (transactionId is Guid activityId)
        {
            selectedLots = await (
                from allocation in _dbContext.LotEntryAllocations.AsNoTracking()
                join entry in _dbContext.TransactionEntries.AsNoTracking()
                    on allocation.TransactionEntryId equals entry.Id
                where entry.TransactionId == activityId
                select allocation.AssetLotId)
                .Distinct()
                .ToArrayAsync(cancellationToken);
        }

        var facts = await (
            from allocation in _dbContext.LotEntryAllocations.AsNoTracking()
            join piece in _dbContext.PhysicalGoldLotAllocationDetails.AsNoTracking()
                on allocation.Id equals piece.LotEntryAllocationId
            join lot in _dbContext.AssetLots.AsNoTracking()
                on allocation.AssetLotId equals lot.Id
            join detail in _dbContext.PhysicalGoldLotDetails.AsNoTracking()
                on lot.Id equals detail.AssetLotId
            join entry in _dbContext.TransactionEntries.AsNoTracking()
                on allocation.TransactionEntryId equals entry.Id
            join transaction in _dbContext.LedgerTransactions.AsNoTracking()
                on entry.TransactionId equals transaction.Id
            join asset in _dbContext.Assets.AsNoTracking()
                on lot.AssetId equals asset.Id
            where transaction.HouseholdId == householdId
                  && transaction.Status == TransactionStatus.Posted
                  && asset.Type == AssetType.PhysicalGold
                  && (selectedLots == null
                      || selectedLots.Contains(lot.Id))
            select new
            {
                entry.PortfolioId,
                entry.AccountId,
                lot.AssetId,
                LotId = lot.Id,
                allocation.QuantityDeltaE8,
                piece.PieceDelta,
                detail.ActualFinenessPpm,
                lot.AcquiredOn,
                lot.CostBasisStatus,
                lot.OriginalCostBasisMinor,
                lot.CostBasisCurrencyCode,
                detail.Hallmark,
                detail.CertificateReference
            }).ToListAsync(cancellationToken);

        var positions = new List<PhysicalGoldCustodyPosition>();
        foreach (var group in facts.GroupBy(x => new
                 {
                     x.PortfolioId,
                     x.AccountId,
                     x.AssetId,
                     x.LotId,
                     x.ActualFinenessPpm,
                     x.AcquiredOn,
                     x.CostBasisStatus,
                     x.OriginalCostBasisMinor,
                     x.CostBasisCurrencyCode,
                     x.Hallmark,
                     x.CertificateReference
                 }))
        {
            long gross = 0;
            var pieces = 0;
            foreach (var row in group)
            {
                gross = checked(gross + row.QuantityDeltaE8);
                pieces = checked(pieces + row.PieceDelta);
            }

            if (gross < 0 || pieces < 0)
            {
                throw new CoreLedgerPersistenceException(
                    "Persisted physical-gold custody is negative.");
            }

            if (gross == 0 && pieces == 0)
            {
                continue;
            }

            var fineWeight = checked((decimal)gross
                                     * group.Key.ActualFinenessPpm)
                             / Quantity.Scale
                             / Fineness.MaximumPpm;
            positions.Add(new PhysicalGoldCustodyPosition(
                group.Key.PortfolioId,
                group.Key.AccountId,
                group.Key.AssetId,
                group.Key.LotId,
                gross,
                pieces,
                group.Key.ActualFinenessPpm,
                fineWeight,
                group.Key.AcquiredOn,
                group.Key.CostBasisStatus,
                group.Key.OriginalCostBasisMinor,
                group.Key.CostBasisCurrencyCode,
                group.Key.Hallmark,
                group.Key.CertificateReference));
        }

        return positions
            .OrderBy(x => x.PortfolioId)
            .ThenBy(x => x.AccountId)
            .ThenBy(x => x.AssetLotId)
            .ToArray();
    }

    private async Task<IReadOnlyList<PhysicalGoldAllocationFact>>
        ReadActivityAllocationsAsync(
            Guid transactionId,
            CancellationToken cancellationToken)
    {
        var rows = await (
            from allocation in _dbContext.LotEntryAllocations.AsNoTracking()
            join piece in _dbContext.PhysicalGoldLotAllocationDetails.AsNoTracking()
                on allocation.Id equals piece.LotEntryAllocationId
            join lot in _dbContext.AssetLots.AsNoTracking()
                on allocation.AssetLotId equals lot.Id
            join detail in _dbContext.PhysicalGoldLotDetails.AsNoTracking()
                on lot.Id equals detail.AssetLotId
            join entry in _dbContext.TransactionEntries.AsNoTracking()
                on allocation.TransactionEntryId equals entry.Id
            where entry.TransactionId == transactionId
            select new
            {
                allocation.AssetLotId,
                AllocationId = allocation.Id,
                entry.PortfolioId,
                entry.AccountId,
                allocation.QuantityDeltaE8,
                piece.PieceDelta,
                lot.AcquiredOn,
                lot.CostBasisStatus,
                lot.OriginalCostBasisMinor,
                lot.CostBasisCurrencyCode,
                detail.ActualFinenessPpm,
                detail.PieceCount,
                detail.Hallmark,
                detail.CertificateReference,
                detail.Note
            }).ToListAsync(cancellationToken);

        return rows.Select(x => new PhysicalGoldAllocationFact(
            x.AssetLotId,
            x.AllocationId,
            x.PortfolioId,
            x.AccountId,
            x.QuantityDeltaE8,
            x.PieceDelta,
            x.AcquiredOn,
            x.CostBasisStatus,
            x.CostBasisStatus == CostBasisStatus.Known
                ? Money.FromMinorUnits(
                    x.OriginalCostBasisMinor!.Value,
                    new CurrencyCode(x.CostBasisCurrencyCode!))
                : null,
            x.ActualFinenessPpm,
            x.PieceCount,
            x.Hallmark,
            x.CertificateReference,
            x.Note,
            checked((decimal)x.QuantityDeltaE8
                    * x.ActualFinenessPpm)
            / Quantity.Scale
            / Fineness.MaximumPpm)).ToArray();
    }

    private static long ToMinorUnits(long rawE8, int minorUnitDigits)
    {
        long divisor = 1;
        for (var digit = minorUnitDigits; digit < 8; digit++)
        {
            divisor = checked(divisor * 10);
        }

        if (rawE8 % divisor != 0)
        {
            throw new CoreLedgerPersistenceException(
                "A persisted cash entry is not an exact minor-unit multiple.");
        }

        return rawE8 / divisor;
    }
}
