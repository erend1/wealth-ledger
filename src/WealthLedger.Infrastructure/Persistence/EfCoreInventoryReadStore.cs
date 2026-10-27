using Microsoft.EntityFrameworkCore;
using WealthLedger.Application.Inventory;
using WealthLedger.Domain.Ledger;
using WealthLedger.Domain.Portfolios;

namespace WealthLedger.Infrastructure.Persistence;

public sealed class EfCoreInventoryReadStore : IInventoryReadStore
{
    private readonly WealthLedgerDbContext _dbContext;

    public EfCoreInventoryReadStore(WealthLedgerDbContext dbContext)
    {
        _dbContext = dbContext
            ?? throw new ArgumentNullException(nameof(dbContext));
    }

    public Task<bool> HouseholdExistsAsync(
        Guid householdId,
        CancellationToken cancellationToken = default)
        => _dbContext.Households
            .AsNoTracking()
            .AnyAsync(
                household => household.Id == householdId,
                cancellationToken);

    public async Task<bool> FilterScopeExistsAsync(
        Guid householdId,
        Guid? portfolioId,
        Guid? accountId,
        Guid? institutionId,
        Guid? assetId,
        CancellationToken cancellationToken = default)
    {
        if (portfolioId is Guid portfolio
            && !await _dbContext.Portfolios
                .AsNoTracking()
                .AnyAsync(
                    row => row.Id == portfolio
                           && row.HouseholdId == householdId,
                    cancellationToken))
        {
            return false;
        }

        if (accountId is Guid account)
        {
            var accountExists = await _dbContext.Accounts
                .AsNoTracking()
                .AnyAsync(
                    row => row.Id == account
                           && row.HouseholdId == householdId
                           && (institutionId == null
                               || row.InstitutionId == institutionId),
                    cancellationToken);

            if (!accountExists)
            {
                return false;
            }
        }
        else if (institutionId is Guid institution
                 && !await _dbContext.Institutions
                     .AsNoTracking()
                     .AnyAsync(
                         row => row.Id == institution,
                         cancellationToken))
        {
            return false;
        }

        if (assetId is Guid asset
            && !await _dbContext.Assets
                .AsNoTracking()
                .AnyAsync(row => row.Id == asset, cancellationToken))
        {
            return false;
        }

        return true;
    }

    public async Task<InventoryScopeContext?> FindExactPositionScopeAsync(
        Guid householdId,
        Guid portfolioId,
        Guid accountId,
        Guid assetId,
        CancellationToken cancellationToken = default)
    {
        var rows = await (
                from portfolio in _dbContext.Portfolios.AsNoTracking()
                join account in _dbContext.Accounts.AsNoTracking()
                    on portfolio.HouseholdId equals account.HouseholdId
                join asset in _dbContext.Assets.AsNoTracking()
                    on assetId equals asset.Id
                join institution in _dbContext.Institutions.AsNoTracking()
                    on account.InstitutionId equals (Guid?)institution.Id
                    into accountInstitutions
                from institution in accountInstitutions.DefaultIfEmpty()
                where portfolio.HouseholdId == householdId
                      && portfolio.Id == portfolioId
                      && account.Id == accountId
                      && asset.Id == assetId
                select new
                {
                    PortfolioId = portfolio.Id,
                    PortfolioCode = portfolio.Code,
                    PortfolioName = portfolio.Name,
                    PortfolioStatus = portfolio.Status,
                    AccountId = account.Id,
                    AccountCode = account.Code,
                    AccountName = account.Name,
                    AccountType = account.Type,
                    AccountIsActive = account.IsActive,
                    InstitutionId = institution == null
                        ? (Guid?)null
                        : institution.Id,
                    InstitutionCode = institution == null
                        ? null
                        : institution.Code,
                    InstitutionName = institution == null
                        ? null
                        : institution.Name,
                    InstitutionType = institution == null
                        ? null
                        : (InstitutionType?)institution.Type,
                    InstitutionIsActive = institution == null
                        ? (bool?)null
                        : institution.IsActive,
                    AssetId = asset.Id,
                    AssetCode = asset.Code,
                    AssetName = asset.Name,
                    AssetType = asset.Type,
                    AssetBaseUnit = asset.BaseUnit,
                    AssetBaseCurrencyCode = asset.BaseCurrencyCode,
                    AssetLotTrackingMode = asset.LotTrackingMode,
                    AssetIsActive = asset.IsActive
                })
            .Take(2)
            .ToListAsync(cancellationToken);

        if (rows.Count == 0)
        {
            return null;
        }

        if (rows.Count != 1)
        {
            throw new InventoryPersistenceException(
                "An exact inventory scope projected ambiguously.");
        }

        var row = rows[0];

        return new InventoryScopeContext(
            row.PortfolioId,
            row.PortfolioCode,
            row.PortfolioName,
            row.PortfolioStatus,
            row.AccountId,
            row.AccountCode,
            row.AccountName,
            row.AccountType,
            row.AccountIsActive,
            row.InstitutionId,
            row.InstitutionCode,
            row.InstitutionName,
            row.InstitutionType,
            row.InstitutionIsActive,
            row.AssetId,
            row.AssetCode,
            row.AssetName,
            row.AssetType,
            row.AssetBaseUnit,
            row.AssetBaseCurrencyCode,
            row.AssetLotTrackingMode,
            row.AssetIsActive);
    }

    public async Task<IReadOnlyList<InventoryEntryFact>>
        ListPositionEntryFactsAsync(
            Guid householdId,
            Guid? portfolioId,
            Guid? accountId,
            Guid? institutionId,
            Guid? assetId,
            DateOnly? asOf,
            CancellationToken cancellationToken = default)
    {
        var query =
            from entry in _dbContext.TransactionEntries.AsNoTracking()
            join transaction in _dbContext.LedgerTransactions.AsNoTracking()
                on entry.TransactionId equals transaction.Id
            join portfolio in _dbContext.Portfolios.AsNoTracking()
                on entry.PortfolioId equals portfolio.Id
            join account in _dbContext.Accounts.AsNoTracking()
                on entry.AccountId equals account.Id
            join asset in _dbContext.Assets.AsNoTracking()
                on entry.AssetId equals asset.Id
            join institution in _dbContext.Institutions.AsNoTracking()
                on account.InstitutionId equals (Guid?)institution.Id
                into accountInstitutions
            from institution in accountInstitutions.DefaultIfEmpty()
            where transaction.HouseholdId == householdId
                  && transaction.Status == TransactionStatus.Posted
                  && transaction.ExecutionDate != null
                  && portfolio.HouseholdId == householdId
                  && account.HouseholdId == householdId
                  && (portfolioId == null || portfolio.Id == portfolioId)
                  && (accountId == null || account.Id == accountId)
                  && (institutionId == null
                      || institution != null && institution.Id == institutionId)
                  && (assetId == null || asset.Id == assetId)
                  && (asOf == null || transaction.ExecutionDate <= asOf)
            select new
            {
                EntryId = entry.Id,
                TransactionId = transaction.Id,
                ExecutionDate = transaction.ExecutionDate!.Value,
                transaction.CreatedAtUtc,
                entry.EntrySequence,
                entry.QuantityDeltaE8,
                PortfolioId = portfolio.Id,
                PortfolioCode = portfolio.Code,
                PortfolioName = portfolio.Name,
                PortfolioStatus = portfolio.Status,
                AccountId = account.Id,
                AccountCode = account.Code,
                AccountName = account.Name,
                AccountType = account.Type,
                AccountIsActive = account.IsActive,
                InstitutionId = institution == null
                    ? (Guid?)null
                    : institution.Id,
                InstitutionCode = institution == null
                    ? null
                    : institution.Code,
                InstitutionName = institution == null
                    ? null
                    : institution.Name,
                InstitutionType = institution == null
                    ? null
                    : (InstitutionType?)institution.Type,
                InstitutionIsActive = institution == null
                    ? (bool?)null
                    : institution.IsActive,
                AssetId = asset.Id,
                AssetCode = asset.Code,
                AssetName = asset.Name,
                AssetType = asset.Type,
                AssetBaseUnit = asset.BaseUnit,
                AssetBaseCurrencyCode = asset.BaseCurrencyCode,
                AssetLotTrackingMode = asset.LotTrackingMode,
                AssetIsActive = asset.IsActive
            };
        var rows = await query.ToListAsync(cancellationToken);

        return rows
            .Select(
                row => new InventoryEntryFact(
                    row.EntryId,
                    row.TransactionId,
                    row.ExecutionDate,
                    ToDateTimeOffset(row.CreatedAtUtc),
                    row.EntrySequence,
                    new InventoryScopeContext(
                        row.PortfolioId,
                        row.PortfolioCode,
                        row.PortfolioName,
                        row.PortfolioStatus,
                        row.AccountId,
                        row.AccountCode,
                        row.AccountName,
                        row.AccountType,
                        row.AccountIsActive,
                        row.InstitutionId,
                        row.InstitutionCode,
                        row.InstitutionName,
                        row.InstitutionType,
                        row.InstitutionIsActive,
                        row.AssetId,
                        row.AssetCode,
                        row.AssetName,
                        row.AssetType,
                        row.AssetBaseUnit,
                        row.AssetBaseCurrencyCode,
                        row.AssetLotTrackingMode,
                        row.AssetIsActive),
                    row.QuantityDeltaE8))
            .ToArray();
    }

    public async Task<InventoryLotFactSet> ListLotFactsAsync(
        Guid householdId,
        Guid? assetId,
        DateOnly? asOf,
        CancellationToken cancellationToken = default)
    {
        var lotQuery =
            from lot in _dbContext.AssetLots.AsNoTracking()
            join asset in _dbContext.Assets.AsNoTracking()
                on lot.AssetId equals asset.Id
            join openingEntry in _dbContext.TransactionEntries.AsNoTracking()
                on lot.OpeningTransactionEntryId equals openingEntry.Id
            join openingTransaction in _dbContext.LedgerTransactions.AsNoTracking()
                on openingEntry.TransactionId equals openingTransaction.Id
            join goldDetail in _dbContext.PhysicalGoldLotDetails.AsNoTracking()
                on lot.Id equals goldDetail.AssetLotId
                into goldDetails
            from goldDetail in goldDetails.DefaultIfEmpty()
            where openingTransaction.HouseholdId == householdId
                  && openingTransaction.Status == TransactionStatus.Posted
                  && openingTransaction.ExecutionDate != null
                  && (assetId == null || asset.Id == assetId)
                  && (asOf == null || openingTransaction.ExecutionDate <= asOf)
            select new
            {
                AssetLotId = lot.Id,
                CreatingTransactionId = openingTransaction.Id,
                lot.OpeningTransactionEntryId,
                AssetId = asset.Id,
                AssetCode = asset.Code,
                AssetName = asset.Name,
                AssetType = asset.Type,
                AssetBaseUnit = asset.BaseUnit,
                AssetBaseCurrencyCode = asset.BaseCurrencyCode,
                AssetLotTrackingMode = asset.LotTrackingMode,
                AssetIsActive = asset.IsActive,
                lot.AcquiredOn,
                CostStatus = lot.CostBasisStatus,
                CostMinorUnits = lot.OriginalCostBasisMinor,
                lot.CostBasisCurrencyCode,
                lot.CreatedAtUtc,
                FinenessPpm = goldDetail == null
                    ? (int?)null
                    : goldDetail.ActualFinenessPpm,
                OriginalPieceCount = goldDetail == null
                    ? (int?)null
                    : goldDetail.PieceCount,
                Hallmark = goldDetail == null ? null : goldDetail.Hallmark,
                CertificateReference = goldDetail == null
                    ? null
                    : goldDetail.CertificateReference,
                Note = goldDetail == null ? null : goldDetail.Note
            };
        var lotRows = await lotQuery.ToListAsync(cancellationToken);

        if (lotRows.Count == 0)
        {
            return new InventoryLotFactSet([], []);
        }

        var lotIds = lotRows.Select(row => row.AssetLotId).ToArray();
        var allocationQuery =
            from allocation in _dbContext.LotEntryAllocations.AsNoTracking()
            join lot in _dbContext.AssetLots.AsNoTracking()
                on allocation.AssetLotId equals lot.Id
            join entry in _dbContext.TransactionEntries.AsNoTracking()
                on allocation.TransactionEntryId equals entry.Id
            join transaction in _dbContext.LedgerTransactions.AsNoTracking()
                on entry.TransactionId equals transaction.Id
            join portfolio in _dbContext.Portfolios.AsNoTracking()
                on entry.PortfolioId equals portfolio.Id
            join account in _dbContext.Accounts.AsNoTracking()
                on entry.AccountId equals account.Id
            join asset in _dbContext.Assets.AsNoTracking()
                on lot.AssetId equals asset.Id
            join institution in _dbContext.Institutions.AsNoTracking()
                on account.InstitutionId equals (Guid?)institution.Id
                into accountInstitutions
            from institution in accountInstitutions.DefaultIfEmpty()
            join pieceDetail in _dbContext.PhysicalGoldLotAllocationDetails
                    .AsNoTracking()
                on allocation.Id equals pieceDetail.LotEntryAllocationId
                into pieceDetails
            from pieceDetail in pieceDetails.DefaultIfEmpty()
            where lotIds.Contains(allocation.AssetLotId)
                  && transaction.HouseholdId == householdId
                  && transaction.Status == TransactionStatus.Posted
                  && transaction.ExecutionDate != null
                  && entry.AssetId == lot.AssetId
                  && portfolio.HouseholdId == householdId
                  && account.HouseholdId == householdId
                  && (asOf == null || transaction.ExecutionDate <= asOf)
            select new
            {
                allocation.AssetLotId,
                AllocationId = allocation.Id,
                TransactionEntryId = entry.Id,
                TransactionId = transaction.Id,
                TransactionType = transaction.Type,
                ExecutionDate = transaction.ExecutionDate!.Value,
                transaction.CreatedAtUtc,
                entry.EntrySequence,
                QuantityDeltaRawE8 = allocation.QuantityDeltaE8,
                PieceDelta = pieceDetail == null
                    ? (int?)null
                    : pieceDetail.PieceDelta,
                PortfolioId = portfolio.Id,
                PortfolioCode = portfolio.Code,
                PortfolioName = portfolio.Name,
                PortfolioStatus = portfolio.Status,
                AccountId = account.Id,
                AccountCode = account.Code,
                AccountName = account.Name,
                AccountType = account.Type,
                AccountIsActive = account.IsActive,
                InstitutionId = institution == null
                    ? (Guid?)null
                    : institution.Id,
                InstitutionCode = institution == null
                    ? null
                    : institution.Code,
                InstitutionName = institution == null
                    ? null
                    : institution.Name,
                InstitutionType = institution == null
                    ? null
                    : (InstitutionType?)institution.Type,
                InstitutionIsActive = institution == null
                    ? (bool?)null
                    : institution.IsActive,
                AssetId = asset.Id,
                AssetCode = asset.Code,
                AssetName = asset.Name,
                AssetType = asset.Type,
                AssetBaseUnit = asset.BaseUnit,
                AssetBaseCurrencyCode = asset.BaseCurrencyCode,
                AssetLotTrackingMode = asset.LotTrackingMode,
                AssetIsActive = asset.IsActive
            };
        var allocationRows = await allocationQuery.ToListAsync(cancellationToken);

        var lots = lotRows
            .Select(
                row => new InventoryLotDescriptor(
                    row.AssetLotId,
                    row.CreatingTransactionId,
                    row.OpeningTransactionEntryId,
                    row.AssetId,
                    row.AssetCode,
                    row.AssetName,
                    row.AssetType,
                    row.AssetBaseUnit,
                    row.AssetBaseCurrencyCode,
                    row.AssetLotTrackingMode,
                    row.AssetIsActive,
                    row.AcquiredOn,
                    row.CostStatus,
                    row.CostMinorUnits,
                    row.CostBasisCurrencyCode,
                    ToDateTimeOffset(row.CreatedAtUtc),
                    row.FinenessPpm,
                    row.OriginalPieceCount,
                    row.Hallmark,
                    row.CertificateReference,
                    row.Note))
            .ToArray();
        var allocations = allocationRows
            .Select(
                row => new InventoryLotAllocationFact(
                    row.AssetLotId,
                    row.AllocationId,
                    row.TransactionEntryId,
                    row.TransactionId,
                    row.TransactionType,
                    row.ExecutionDate,
                    ToDateTimeOffset(row.CreatedAtUtc),
                    row.EntrySequence,
                    new InventoryScopeContext(
                        row.PortfolioId,
                        row.PortfolioCode,
                        row.PortfolioName,
                        row.PortfolioStatus,
                        row.AccountId,
                        row.AccountCode,
                        row.AccountName,
                        row.AccountType,
                        row.AccountIsActive,
                        row.InstitutionId,
                        row.InstitutionCode,
                        row.InstitutionName,
                        row.InstitutionType,
                        row.InstitutionIsActive,
                        row.AssetId,
                        row.AssetCode,
                        row.AssetName,
                        row.AssetType,
                        row.AssetBaseUnit,
                        row.AssetBaseCurrencyCode,
                        row.AssetLotTrackingMode,
                        row.AssetIsActive),
                    row.QuantityDeltaRawE8,
                    row.PieceDelta))
            .ToArray();

        return new InventoryLotFactSet(lots, allocations);
    }

    private static DateTimeOffset ToDateTimeOffset(DateTime value)
        => new(
            value.Kind == DateTimeKind.Utc
                ? value
                : DateTime.SpecifyKind(value, DateTimeKind.Utc));
}
