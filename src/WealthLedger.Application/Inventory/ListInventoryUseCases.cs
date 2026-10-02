using WealthLedger.Domain.Assets;
using WealthLedger.Domain.Lots;
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
        ValidateScopeIdentities(
            query.HouseholdId,
            query.PortfolioId,
            query.AccountId,
            query.InstitutionId,
            query.AssetId);

        if (!await _readStore.HouseholdExistsAsync(
                query.HouseholdId,
                cancellationToken)
            || !await _readStore.FilterScopeExistsAsync(
                query.HouseholdId,
                query.PortfolioId,
                query.AccountId,
                query.InstitutionId,
                query.AssetId,
                cancellationToken))
        {
            throw new InventoryScopeNotFoundException();
        }

        var facts = await _readStore.ListPositionEntryFactsAsync(
            query.HouseholdId,
            query.PortfolioId,
            query.AccountId,
            query.InstitutionId,
            query.AssetId,
            query.AsOf,
            cancellationToken);
        var items = new List<PositionInventoryItem>();

        foreach (var group in facts
                     .OrderBy(FactOrderKey)
                     .GroupBy(
                         fact => new
                         {
                             fact.Scope.PortfolioId,
                             fact.Scope.AccountId,
                             fact.Scope.AssetId
                         }))
        {
            var ordered = group
                .OrderBy(FactOrderKey)
                .ToArray();
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

            if (query.InstitutionId is null
                || exactScope.InstitutionId == query.InstitutionId)
            {
                items.Add(ToPositionItem(exactScope, 0, []));
            }
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

    private static object FactOrderKey(InventoryEntryFact fact)
        => new
        {
            fact.ExecutionDate,
            fact.TransactionCreatedAtUtc,
            fact.TransactionId,
            fact.EntrySequence,
            fact.EntryId
        };

    internal static void ValidateScopeIdentities(
        Guid householdId,
        Guid? portfolioId,
        Guid? accountId,
        Guid? institutionId,
        Guid? assetId)
    {
        if (householdId == Guid.Empty
            || portfolioId == Guid.Empty
            || accountId == Guid.Empty
            || institutionId == Guid.Empty
            || assetId == Guid.Empty)
        {
            throw new InventoryScopeNotFoundException();
        }
    }

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
        ListPositionInventoryUseCase.ValidateScopeIdentities(
            query.HouseholdId,
            query.PortfolioId,
            query.AccountId,
            query.InstitutionId,
            query.AssetId);

        if (!await _readStore.HouseholdExistsAsync(
                query.HouseholdId,
                cancellationToken)
            || !await _readStore.FilterScopeExistsAsync(
                query.HouseholdId,
                query.PortfolioId,
                query.AccountId,
                query.InstitutionId,
                query.AssetId,
                cancellationToken))
        {
            throw new InventoryScopeNotFoundException();
        }

        var facts = await _readStore.ListLotFactsAsync(
            query.HouseholdId,
            query.AssetId,
            query.AsOf,
            cancellationToken);
        var allocationsByLot = facts.Allocations
            .GroupBy(allocation => allocation.AssetLotId)
            .ToDictionary(
                group => group.Key,
                group => group
                    .OrderBy(AllocationOrderKey)
                    .ToArray());
        var items = new List<LotInventoryItem>();

        foreach (var lot in facts.Lots
                     .OrderBy(item => item.CreatedAtUtc)
                     .ThenBy(item => item.AssetLotId))
        {
            if (!allocationsByLot.TryGetValue(
                    lot.AssetLotId,
                    out var allocations)
                || allocations.Length == 0)
            {
                // The lot did not yet exist at the requested as-of date.
                continue;
            }

            var openingAllocations = allocations
                .Where(
                    allocation => allocation.TransactionEntryId
                                  == lot.OpeningTransactionEntryId)
                .ToArray();

            if (openingAllocations.Length != 1
                || openingAllocations[0].QuantityDeltaRawE8 <= 0)
            {
                throw new InventoryPersistenceException(
                    "Asset-lot acquisition lineage is incomplete or ambiguous.");
            }

            long globalQuantityRawE8 = 0;

            foreach (var allocation in allocations)
            {
                globalQuantityRawE8 = checked(
                    globalQuantityRawE8 + allocation.QuantityDeltaRawE8);
            }

            if (globalQuantityRawE8 < 0)
            {
                throw new InventoryPersistenceException(
                    "Derived asset-lot quantity cannot be negative.");
            }

            if (!query.IncludeExhausted && globalQuantityRawE8 == 0)
            {
                continue;
            }

            var custody = DeriveCustody(lot, allocations);

            if (!MatchesRequestedCustody(query, allocations, custody))
            {
                continue;
            }

            var physicalGold = DerivePhysicalGold(
                lot,
                allocations,
                globalQuantityRawE8);

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
                    openingAllocations[0].QuantityDeltaRawE8,
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
                    physicalGold));
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

    private static IReadOnlyList<LotCustodyInventoryItem> DeriveCustody(
        InventoryLotDescriptor lot,
        IReadOnlyList<InventoryLotAllocationFact> allocations)
    {
        var result = new List<LotCustodyInventoryItem>();

        foreach (var group in allocations.GroupBy(
                     allocation => new
                     {
                         allocation.Scope.PortfolioId,
                         allocation.Scope.AccountId
                     }))
        {
            var ordered = group.OrderBy(AllocationOrderKey).ToArray();
            var scope = ordered[0].Scope;
            long quantityRawE8 = 0;
            int? pieceCount = lot.AssetType == AssetType.PhysicalGold ? 0 : null;

            foreach (var allocation in ordered)
            {
                if (allocation.Scope != scope)
                {
                    throw new InventoryPersistenceException(
                        "Current master context changed within one lot-custody projection.");
                }

                quantityRawE8 = checked(
                    quantityRawE8 + allocation.QuantityDeltaRawE8);

                if (pieceCount is not null)
                {
                    pieceCount = checked(
                        pieceCount.Value
                        + RequireGoldPieceDelta(allocation));
                }
                else if (allocation.PieceDelta is not null)
                {
                    throw new InventoryPersistenceException(
                        "Non-physical-gold allocation contains piece movement.");
                }
            }

            if (quantityRawE8 < 0 || pieceCount is < 0)
            {
                throw new InventoryPersistenceException(
                    "Derived lot custody cannot be negative.");
            }

            if (quantityRawE8 == 0 && pieceCount is null or 0)
            {
                continue;
            }

            decimal? fineWeightGrams = null;

            if (lot.AssetType == AssetType.PhysicalGold)
            {
                fineWeightGrams = DeriveFineWeight(
                    quantityRawE8,
                    RequireFinenessPpm(lot));
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
                    fineWeightGrams));
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
        IReadOnlyList<InventoryLotAllocationFact> allocations,
        long globalQuantityRawE8)
    {
        if (lot.AssetType != AssetType.PhysicalGold)
        {
            if (lot.FinenessPpm is not null
                || lot.OriginalPieceCount is not null
                || allocations.Any(allocation => allocation.PieceDelta is not null))
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
        var globalPieceCount = 0;

        foreach (var allocation in allocations)
        {
            globalPieceCount = checked(
                globalPieceCount + RequireGoldPieceDelta(allocation));
        }

        if (globalPieceCount < 0)
        {
            throw new InventoryPersistenceException(
                "Derived physical-gold piece count cannot be negative.");
        }

        return new PhysicalGoldLotInventoryDetail(
            finenessPpm,
            originalPieceCount,
            globalPieceCount,
            DeriveFineWeight(globalQuantityRawE8, finenessPpm),
            lot.Hallmark,
            lot.CertificateReference,
            lot.Note);
    }

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

    private static int RequireGoldPieceDelta(
        InventoryLotAllocationFact allocation)
        => allocation.PieceDelta
           ?? throw new InventoryPersistenceException(
               "Physical-gold allocation is missing exact piece movement.");

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

    private static decimal DeriveFineWeight(
        long grossQuantityRawE8,
        int finenessPpm)
    {
        if (grossQuantityRawE8 < 0)
        {
            throw new InventoryPersistenceException(
                "Physical-gold gross quantity cannot be negative.");
        }

        return checked(
            grossQuantityRawE8 / (decimal)Quantity.Scale
            * (finenessPpm / 1_000_000m));
    }

    private static object AllocationOrderKey(
        InventoryLotAllocationFact allocation)
        => new
        {
            allocation.ExecutionDate,
            allocation.TransactionCreatedAtUtc,
            allocation.TransactionId,
            allocation.EntrySequence,
            allocation.AllocationId
        };
}
