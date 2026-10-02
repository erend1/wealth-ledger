using Microsoft.EntityFrameworkCore;
using WealthLedger.Application.LedgerSearch;
using WealthLedger.Domain.Ledger;

namespace WealthLedger.Infrastructure.Persistence;

public sealed class EfCoreLedgerSearchReadStore : ILedgerSearchReadStore
{
    private readonly WealthLedgerDbContext _dbContext;

    public EfCoreLedgerSearchReadStore(WealthLedgerDbContext dbContext)
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
        Guid? assetId,
        Guid? institutionId,
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

        if (accountId is Guid account
            && !await _dbContext.Accounts
                .AsNoTracking()
                .AnyAsync(
                    row => row.Id == account
                           && row.HouseholdId == householdId,
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

        if (institutionId is Guid institution
            && !await _dbContext.Institutions
                .AsNoTracking()
                .AnyAsync(row => row.Id == institution, cancellationToken))
        {
            return false;
        }

        return true;
    }

    public async Task<IReadOnlyList<LedgerSearchTransactionItem>>
        SearchPostedTransactionsAsync(
            Guid householdId,
            LedgerSearchNormalizedFilters filters,
            int take,
            LedgerSearchCursorKey? after,
            CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filters);

        var transactions = _dbContext.LedgerTransactions
            .AsNoTracking()
            .Where(
                transaction =>
                    transaction.HouseholdId == householdId
                    && transaction.Status == TransactionStatus.Posted
                    && transaction.ExecutionDate != null
                    && transaction.PostedAtUtc != null);

        if (filters.ExecutedFrom is DateOnly executedFrom)
        {
            transactions = transactions.Where(
                transaction => transaction.ExecutionDate >= executedFrom);
        }

        if (filters.ExecutedTo is DateOnly executedTo)
        {
            transactions = transactions.Where(
                transaction => transaction.ExecutionDate <= executedTo);
        }

        if (filters.Types.Count > 0)
        {
            var types = filters.Types.ToArray();
            transactions = transactions.Where(
                transaction => types.Contains(transaction.Type));
        }

        if (filters.ExternalReferenceContains is string externalReference)
        {
            transactions = transactions.Where(
                transaction => transaction.ExternalReference != null
                    && transaction.ExternalReference
                        .ToUpper()
                        .Contains(externalReference));
        }

        if (filters.AssetId is Guid assetId)
        {
            transactions = transactions.Where(
                transaction => _dbContext.TransactionEntries
                    .AsNoTracking()
                    .Any(
                        entry => entry.TransactionId == transaction.Id
                                 && entry.AssetId == assetId));
        }

        if (filters.AccountId is Guid accountId)
        {
            transactions = transactions.Where(
                transaction => _dbContext.TransactionEntries
                    .AsNoTracking()
                    .Any(
                        entry => entry.TransactionId == transaction.Id
                                 && entry.AccountId == accountId));
        }

        if (filters.PortfolioId is Guid portfolioId)
        {
            transactions = transactions.Where(
                transaction => _dbContext.TransactionEntries
                    .AsNoTracking()
                    .Any(
                        entry => entry.TransactionId == transaction.Id
                                 && entry.PortfolioId == portfolioId));
        }

        if (filters.InstitutionId is Guid institutionId)
        {
            transactions = transactions.Where(
                transaction => _dbContext.TransactionEntries
                    .AsNoTracking()
                    .Any(
                        entry => entry.TransactionId == transaction.Id
                            && _dbContext.Accounts
                                .AsNoTracking()
                                .Any(
                                    account => account.Id == entry.AccountId
                                        && account.InstitutionId
                                        == institutionId)));
        }

        transactions = filters.ReversalRelationship switch
        {
            LedgerSearchReversalRelationship.Any => transactions,
            LedgerSearchReversalRelationship.OriginalOnly =>
                transactions.Where(
                    transaction => transaction.Type != TransactionType.Reversal),
            LedgerSearchReversalRelationship.ReversalOnly =>
                transactions.Where(
                    transaction => transaction.Type == TransactionType.Reversal),
            LedgerSearchReversalRelationship.ReversedOriginal =>
                transactions.Where(
                    transaction => transaction.Type != TransactionType.Reversal
                        && _dbContext.LedgerTransactions
                            .AsNoTracking()
                            .Any(
                                reversal =>
                                    reversal.HouseholdId == householdId
                                    && reversal.Status == TransactionStatus.Posted
                                    && reversal.Type == TransactionType.Reversal
                                    && reversal.ReversalOfTransactionId
                                    == transaction.Id)),
            _ => throw new LedgerSearchPersistenceException(
                "The normalized reversal filter is unsupported.")
        };

        if (after is not null)
        {
            var afterPostedAtUtc = after.PostedAtUtc.UtcDateTime;
            transactions = transactions.Where(
                transaction =>
                    transaction.ExecutionDate < after.ExecutionDate
                    || (transaction.ExecutionDate == after.ExecutionDate
                        && transaction.PostedAtUtc < afterPostedAtUtc)
                    || (transaction.ExecutionDate == after.ExecutionDate
                        && transaction.PostedAtUtc == afterPostedAtUtc
                        && transaction.Id.CompareTo(after.TransactionId) < 0));
        }

        var transactionRows = await transactions
            .OrderByDescending(transaction => transaction.ExecutionDate)
            .ThenByDescending(transaction => transaction.PostedAtUtc)
            .ThenByDescending(transaction => transaction.Id)
            .Take(take)
            .Select(
                transaction => new
                {
                    transaction.Id,
                    transaction.HouseholdId,
                    transaction.Type,
                    transaction.Status,
                    transaction.OrderDate,
                    ExecutionDate = transaction.ExecutionDate!.Value,
                    transaction.SettlementDate,
                    transaction.ExternalReference,
                    transaction.ReversalOfTransactionId,
                    ReversedByTransactionId = _dbContext.LedgerTransactions
                        .AsNoTracking()
                        .Where(
                            reversal =>
                                reversal.HouseholdId == householdId
                                && reversal.Type == TransactionType.Reversal
                                && reversal.Status == TransactionStatus.Posted
                                && reversal.ReversalOfTransactionId
                                == transaction.Id)
                        .Select(reversal => (Guid?)reversal.Id)
                        .SingleOrDefault(),
                    transaction.CreatedAtUtc,
                    PostedAtUtc = transaction.PostedAtUtc!.Value,
                    EntryCount = _dbContext.TransactionEntries
                        .AsNoTracking()
                        .Count(entry => entry.TransactionId == transaction.Id)
                })
            .ToListAsync(cancellationToken);

        if (transactionRows.Count == 0)
        {
            return [];
        }

        var transactionIds = transactionRows
            .Select(row => row.Id)
            .ToArray();
        var effectRows = await (
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
                where transactionIds.Contains(entry.TransactionId)
                      && transaction.HouseholdId == householdId
                      && transaction.Status == TransactionStatus.Posted
                      && portfolio.HouseholdId == householdId
                      && account.HouseholdId == householdId
                orderby transaction.ExecutionDate descending,
                    transaction.PostedAtUtc descending,
                    transaction.Id descending,
                    entry.EntrySequence
                select new
                {
                    entry.TransactionId,
                    EntryId = entry.Id,
                    entry.EntrySequence,
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
                        : (Domain.Portfolios.InstitutionType?)institution.Type,
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
                    AssetIsActive = asset.IsActive,
                    entry.QuantityDeltaE8,
                    entry.Role
                })
            .ToListAsync(cancellationToken);

        var effectsByTransaction = effectRows
            .GroupBy(row => row.TransactionId)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<LedgerSearchEntryEffect>)group
                    .OrderBy(row => row.EntrySequence)
                    .Select(
                        row => new LedgerSearchEntryEffect(
                            row.EntryId,
                            row.EntrySequence,
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
                            row.AssetIsActive,
                            row.QuantityDeltaE8,
                            row.Role))
                    .ToArray());
        var result = new List<LedgerSearchTransactionItem>(
            transactionRows.Count);

        foreach (var row in transactionRows)
        {
            effectsByTransaction.TryGetValue(row.Id, out var effects);
            effects ??= [];

            if (effects.Count != row.EntryCount)
            {
                throw new LedgerSearchPersistenceException(
                    "Posted ledger history could not be projected completely.");
            }

            result.Add(
                new LedgerSearchTransactionItem(
                    row.Id,
                    row.HouseholdId,
                    row.Type,
                    row.Status,
                    row.OrderDate,
                    row.ExecutionDate,
                    row.SettlementDate,
                    row.ExternalReference,
                    row.ReversalOfTransactionId,
                    row.ReversedByTransactionId,
                    ToDateTimeOffset(row.CreatedAtUtc),
                    ToDateTimeOffset(row.PostedAtUtc),
                    effects));
        }

        return result;
    }

    private static DateTimeOffset ToDateTimeOffset(DateTime value)
        => new(
            value.Kind == DateTimeKind.Utc
                ? value
                : DateTime.SpecifyKind(value, DateTimeKind.Utc));
}
