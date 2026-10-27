using WealthLedger.Domain.Assets;
using WealthLedger.Domain.ValueObjects;

namespace WealthLedger.Application.Inventory;

public sealed class ListPositionInventoryUseCase
{
    private readonly IInventoryReadStore _readStore;

    public ListPositionInventoryUseCase(IInventoryReadStore readStore)
    {
        _readStore = readStore
            ?? throw new ArgumentNullException(nameof(readStore));
    }

    public async Task<PositionInventory> ExecuteAsync(
        ListPositionInventoryQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        await InventoryScopeValidation.EnsureScopeExistsAsync(
            _readStore,
            query.HouseholdId,
            query.PortfolioId,
            query.AccountId,
            query.InstitutionId,
            query.AssetId,
            cancellationToken);

        var facts = await _readStore.ListPositionEntryFactsAsync(
            query.HouseholdId,
            query.PortfolioId,
            query.AccountId,
            query.InstitutionId,
            query.AssetId,
            query.AsOf,
            cancellationToken);
        var items = new List<PositionInventoryItem>();

        // GroupBy keeps the deterministic fact order inside each group.
        foreach (var group in OrderPositionFacts(facts)
                     .GroupBy(
                         fact => new
                         {
                             fact.Scope.PortfolioId,
                             fact.Scope.AccountId,
                             fact.Scope.AssetId
                         }))
        {
            var ordered = group.ToArray();
            var scope = ordered[0].Scope;
            long quantityRawE8 = 0;

            foreach (var fact in ordered)
            {
                EnsureSameScopeContext(scope, fact.Scope);
                quantityRawE8 = checked(
                    quantityRawE8 + fact.QuantityDeltaRawE8);
            }

            if (!query.IncludeZero && quantityRawE8 == 0)
            {
                continue;
            }

            items.Add(ToPositionItem(scope, quantityRawE8, ordered));
        }

        if (query.IncludeZero
            && query.PortfolioId is Guid portfolioId
            && query.AccountId is Guid accountId
            && query.AssetId is Guid assetId
            && !items.Any(
                item => item.PortfolioId == portfolioId
                        && item.AccountId == accountId
                        && item.AssetId == assetId))
        {
            var exactScope = await _readStore.FindExactPositionScopeAsync(
                query.HouseholdId,
                portfolioId,
                accountId,
                assetId,
                cancellationToken)
                ?? throw new InventoryScopeNotFoundException();

            if (query.InstitutionId is not null
                && exactScope.InstitutionId != query.InstitutionId)
            {
                throw new InventoryPersistenceException(
                    "An exact inventory scope disagrees with its validated filters.");
            }

            items.Add(ToPositionItem(exactScope, 0, []));
        }

        return new PositionInventory(
            query.HouseholdId,
            query.AsOf,
            items
                .OrderBy(item => item.PortfolioCode, StringComparer.Ordinal)
                .ThenBy(item => item.PortfolioId)
                .ThenBy(item => item.AccountCode, StringComparer.Ordinal)
                .ThenBy(item => item.AccountId)
                .ThenBy(item => item.AssetCode, StringComparer.Ordinal)
                .ThenBy(item => item.AssetId)
                .ToArray());
    }

    private static PositionInventoryItem ToPositionItem(
        InventoryScopeContext scope,
        long quantityRawE8,
        IReadOnlyList<InventoryEntryFact> facts)
        => new(
            scope.PortfolioId,
            scope.PortfolioCode,
            scope.PortfolioName,
            scope.PortfolioStatus,
            scope.AccountId,
            scope.AccountCode,
            scope.AccountName,
            scope.AccountType,
            scope.AccountIsActive,
            scope.InstitutionId,
            scope.InstitutionCode,
            scope.InstitutionName,
            scope.InstitutionType,
            scope.InstitutionIsActive,
            scope.AssetId,
            scope.AssetCode,
            scope.AssetName,
            scope.AssetType,
            scope.AssetBaseUnit,
            scope.AssetBaseCurrencyCode,
            scope.AssetLotTrackingMode,
            scope.AssetIsActive,
            quantityRawE8,
            facts.Count,
            facts.Select(fact => fact.EntryId).ToArray(),
            facts.Select(fact => fact.TransactionId).Distinct().ToArray());

    private static IOrderedEnumerable<InventoryEntryFact> OrderPositionFacts(
        IEnumerable<InventoryEntryFact> facts)
        => facts
            .OrderBy(fact => fact.ExecutionDate)
            .ThenBy(fact => fact.TransactionCreatedAtUtc)
            .ThenBy(fact => fact.TransactionId)
            .ThenBy(fact => fact.EntrySequence)
            .ThenBy(fact => fact.EntryId);

    private static void EnsureSameScopeContext(
        InventoryScopeContext expected,
        InventoryScopeContext actual)
    {
        if (expected != actual)
        {
            throw new InventoryPersistenceException(
                "Current master context changed within one derived position projection.");
        }
    }
}

public sealed class ListLotInventoryUseCase
{
    private readonly IInventoryReadStore _readStore;

    public ListLotInventoryUseCase(IInventoryReadStore readStore)
    {
        _readStore = readStore
            ?? throw new ArgumentNullException(nameof(readStore));
    }

    public async Task<LotInventory> ExecuteAsync(
        ListLotInventoryQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        await InventoryScopeValidation.EnsureScopeExistsAsync(
            _readStore,
            query.HouseholdId,
            query.PortfolioId,
            query.AccountId,
            query.InstitutionId,
            query.AssetId,
            cancellationToken);

        var facts = await _readStore.ListLotFactsAsync(
            query.HouseholdId,
            query.AssetId,
            query.AsOf,
            cancellationToken);
        var allocationsByLot = facts.Allocations
            .GroupBy(allocation => allocation.AssetLotId)
            .ToDictionary(
                group => group.Key,
                group => OrderLotAllocations(group).ToArray());

        // The posting guards keep complete Posted history non-negative, so a
        // negative current quantity is a persisted-shape fault. An as-of
        // slice has no such guarantee: M008 and M009 check disposals and
        // transfers against current custody only, so one may carry an
        // earlier ExecutionDate than the acquisition or custody it consumed.
        // Such a slice is reported with its signed quantities.
        var completeHistory = query.AsOf is null;
        var items = new List<LotInventoryItem>();

        foreach (var lot in facts.Lots.OrderBy(item => item.AssetLotId))
        {
            var allocations = allocationsByLot.GetValueOrDefault(
                lot.AssetLotId,
                []);
            var originalQuantityRawE8 = RequireLineage(
                lot,
                allocations,
                completeHistory);

            if (allocations.Length == 0)
            {
                // No effective allocation on or before the as-of date.
                continue;
            }

            long globalQuantityRawE8 = 0;
            int? globalPieceCount =
                lot.AssetType == AssetType.PhysicalGold ? 0 : null;

            foreach (var allocation in allocations)
            {
                globalQuantityRawE8 = checked(
                    globalQuantityRawE8 + allocation.QuantityDeltaRawE8);
                globalPieceCount = AddPieceDelta(
                    globalPieceCount,
                    allocation);
            }

            if (completeHistory
                && (globalQuantityRawE8 < 0 || globalPieceCount < 0))
            {
                throw new InventoryPersistenceException(
                    "Derived asset-lot quantity cannot be negative.");
            }

            // Gross weight and whole pieces are independent physical facts:
            // a lot is exhausted only when neither remains.
            if (!query.IncludeExhausted
                && globalQuantityRawE8 == 0
                && globalPieceCount is null or 0)
            {
                continue;
            }

            var custody = DeriveCustody(lot, allocations, completeHistory);

            if (!MatchesRequestedCustody(query, allocations, custody))
            {
                continue;
            }

            items.Add(
                new LotInventoryItem(
                    lot.AssetLotId,
                    lot.CreatingTransactionId,
                    lot.OpeningTransactionEntryId,
                    lot.AssetId,
                    lot.AssetCode,
                    lot.AssetName,
                    lot.AssetType,
                    lot.AssetBaseUnit,
                    lot.AssetBaseCurrencyCode,
                    lot.AssetLotTrackingMode,
                    lot.AssetIsActive,
                    lot.AcquiredOn,
                    lot.CostStatus,
                    lot.CostMinorUnits,
                    lot.CostCurrencyCode,
                    originalQuantityRawE8,
                    globalQuantityRawE8,
                    custody,
                    allocations
                        .Select(
                            allocation => new LotAllocationInventoryItem(
                                allocation.AllocationId,
                                allocation.TransactionEntryId,
                                allocation.TransactionId,
                                allocation.TransactionType,
                                allocation.ExecutionDate,
                                allocation.Scope.PortfolioId,
                                allocation.Scope.AccountId,
                                allocation.QuantityDeltaRawE8,
                                allocation.PieceDelta))
                        .ToArray(),
                    DerivePhysicalGold(
                        lot,
                        globalQuantityRawE8,
                        globalPieceCount)));
        }

        return new LotInventory(
            query.HouseholdId,
            query.AsOf,
            items
                .OrderBy(item => item.AssetCode, StringComparer.Ordinal)
                .ThenBy(item => item.AcquiredOn)
                .ThenBy(item => item.AssetLotId)
                .ToArray());
    }

    private static long RequireLineage(
        InventoryLotDescriptor lot,
        IReadOnlyList<InventoryLotAllocationFact> allocations,
        bool completeHistory)
    {
        var openingAllocationCount = allocations.Count(
            allocation => allocation.TransactionEntryId
                          == lot.OpeningTransactionEntryId);

        // Complete history must contain the acquisition itself. An as-of
        // slice may end before a lot that was opened later.
        if (lot.OriginalQuantityRawE8 is not > 0
            || openingAllocationCount > 1
            || (completeHistory && openingAllocationCount != 1))
        {
            throw new InventoryPersistenceException(
                "Asset-lot acquisition lineage is incomplete or ambiguous.");
        }

        return lot.OriginalQuantityRawE8.Value;
    }

    private static IReadOnlyList<LotCustodyInventoryItem> DeriveCustody(
        InventoryLotDescriptor lot,
        IReadOnlyList<InventoryLotAllocationFact> allocations,
        bool completeHistory)
    {
        var result = new List<LotCustodyInventoryItem>();

        foreach (var group in allocations.GroupBy(
                     allocation => new
                     {
                         allocation.Scope.PortfolioId,
                         allocation.Scope.AccountId
                     }))
        {
            var scope = group.First().Scope;
            long quantityRawE8 = 0;
            int? pieceCount = lot.AssetType == AssetType.PhysicalGold ? 0 : null;

            foreach (var allocation in group)
            {
                if (allocation.Scope != scope)
                {
                    throw new InventoryPersistenceException(
                        "Current master context changed within one lot-custody projection.");
                }

                quantityRawE8 = checked(
                    quantityRawE8 + allocation.QuantityDeltaRawE8);
                pieceCount = AddPieceDelta(pieceCount, allocation);
            }

            if (completeHistory && (quantityRawE8 < 0 || pieceCount < 0))
            {
                throw new InventoryPersistenceException(
                    "Derived lot custody cannot be negative.");
            }

            if (quantityRawE8 == 0 && pieceCount is null or 0)
            {
                continue;
            }

            result.Add(
                new LotCustodyInventoryItem(
                    scope.PortfolioId,
                    scope.PortfolioCode,
                    scope.PortfolioName,
                    scope.PortfolioStatus,
                    scope.AccountId,
                    scope.AccountCode,
                    scope.AccountName,
                    scope.AccountType,
                    scope.AccountIsActive,
                    scope.InstitutionId,
                    scope.InstitutionCode,
                    scope.InstitutionName,
                    scope.InstitutionType,
                    scope.InstitutionIsActive,
                    quantityRawE8,
                    pieceCount,
                    pieceCount is null
                        ? null
                        : DeriveFineWeight(
                            quantityRawE8,
                            RequireFinenessPpm(lot))));
        }

        return result
            .OrderBy(item => item.PortfolioCode, StringComparer.Ordinal)
            .ThenBy(item => item.PortfolioId)
            .ThenBy(item => item.AccountCode, StringComparer.Ordinal)
            .ThenBy(item => item.AccountId)
            .ToArray();
    }

    private static PhysicalGoldLotInventoryDetail? DerivePhysicalGold(
        InventoryLotDescriptor lot,
        long globalQuantityRawE8,
        int? globalPieceCount)
    {
        // Only physical-gold lots carry a piece count (see AddPieceDelta).
        if (globalPieceCount is not int pieceCount)
        {
            if (lot.FinenessPpm is not null
                || lot.OriginalPieceCount is not null)
            {
                throw new InventoryPersistenceException(
                    "Non-physical-gold lot contains physical-gold detail.");
            }

            return null;
        }

        var finenessPpm = RequireFinenessPpm(lot);
        var originalPieceCount = lot.OriginalPieceCount
            ?? throw new InventoryPersistenceException(
                "Physical-gold lot is missing original piece evidence.");

        return new PhysicalGoldLotInventoryDetail(
            finenessPpm,
            originalPieceCount,
            pieceCount,
            DeriveFineWeight(globalQuantityRawE8, finenessPpm),
            lot.Hallmark,
            lot.CertificateReference,
            lot.Note);
    }

    // Exhaustion is judged at the requested scope. Without a custody filter
    // the scope is the household, so a lot is exhausted when nothing remains
    // globally. With a filter, a lot is held there only through a non-empty
    // custody row; IncludeExhausted also keeps lots that merely have history
    // in that scope.
    private static bool MatchesRequestedCustody(
        ListLotInventoryQuery query,
        IReadOnlyList<InventoryLotAllocationFact> allocations,
        IReadOnlyList<LotCustodyInventoryItem> custody)
    {
        if (query.PortfolioId is null
            && query.AccountId is null
            && query.InstitutionId is null)
        {
            return true;
        }

        if (!query.IncludeExhausted)
        {
            return custody.Any(
                item => (query.PortfolioId is null
                         || item.PortfolioId == query.PortfolioId)
                        && (query.AccountId is null
                            || item.AccountId == query.AccountId)
                        && (query.InstitutionId is null
                            || item.InstitutionId == query.InstitutionId));
        }

        return allocations.Any(
            allocation => (query.PortfolioId is null
                           || allocation.Scope.PortfolioId == query.PortfolioId)
                          && (query.AccountId is null
                              || allocation.Scope.AccountId == query.AccountId)
                          && (query.InstitutionId is null
                              || allocation.Scope.InstitutionId
                              == query.InstitutionId));
    }

    private static int? AddPieceDelta(
        int? pieceCount,
        InventoryLotAllocationFact allocation)
    {
        if (pieceCount is not int count)
        {
            return allocation.PieceDelta is null
                ? null
                : throw new InventoryPersistenceException(
                    "Non-physical-gold allocation contains piece movement.");
        }

        return checked(
            count
            + (allocation.PieceDelta
               ?? throw new InventoryPersistenceException(
                   "Physical-gold allocation is missing exact piece movement.")));
    }

    private static int RequireFinenessPpm(InventoryLotDescriptor lot)
    {
        if (lot.FinenessPpm is not int ppm
            || ppm is <= 0 or > Fineness.MaximumPpm)
        {
            throw new InventoryPersistenceException(
                "Physical-gold lot contains invalid fineness evidence.");
        }

        return ppm;
    }

    // Same expression order as M009 custody and sale planning, so equal facts
    // project the identical decimal value and scale. Signed, because an as-of
    // custody slice may be negative.
    private static decimal DeriveFineWeight(
        long grossQuantityRawE8,
        int finenessPpm)
        => checked((decimal)grossQuantityRawE8 * finenessPpm)
           / Quantity.Scale
           / Fineness.MaximumPpm;

    private static IOrderedEnumerable<InventoryLotAllocationFact>
        OrderLotAllocations(IEnumerable<InventoryLotAllocationFact> allocations)
        => allocations
            .OrderBy(allocation => allocation.ExecutionDate)
            .ThenBy(allocation => allocation.TransactionCreatedAtUtc)
            .ThenBy(allocation => allocation.TransactionId)
            .ThenBy(allocation => allocation.EntrySequence)
            .ThenBy(allocation => allocation.AllocationId);
}

internal static class InventoryScopeValidation
{
    internal static async Task EnsureScopeExistsAsync(
        IInventoryReadStore readStore,
        Guid householdId,
        Guid? portfolioId,
        Guid? accountId,
        Guid? institutionId,
        Guid? assetId,
        CancellationToken cancellationToken)
    {
        if (householdId == Guid.Empty
            || portfolioId == Guid.Empty
            || accountId == Guid.Empty
            || institutionId == Guid.Empty
            || assetId == Guid.Empty)
        {
            throw new InventoryScopeNotFoundException();
        }

        if (!await readStore.HouseholdExistsAsync(
                householdId,
                cancellationToken)
            || !await readStore.FilterScopeExistsAsync(
                householdId,
                portfolioId,
                accountId,
                institutionId,
                assetId,
                cancellationToken))
        {
            throw new InventoryScopeNotFoundException();
        }
    }
}
