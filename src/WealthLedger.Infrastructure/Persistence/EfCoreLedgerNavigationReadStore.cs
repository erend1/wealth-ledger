using Microsoft.EntityFrameworkCore;
using WealthLedger.Application.Navigation;
using WealthLedger.Domain.Ledger;
using WealthLedger.Domain.Portfolios;

namespace WealthLedger.Infrastructure.Persistence;

public sealed class EfCoreLedgerNavigationReadStore
    : ILedgerNavigationReadStore
{
    private readonly WealthLedgerDbContext _dbContext;

    public EfCoreLedgerNavigationReadStore(
        WealthLedgerDbContext dbContext)
    {
        _dbContext = dbContext
            ?? throw new ArgumentNullException(nameof(dbContext));
    }

    public async Task<IReadOnlyList<RecentLedgerTransactionNavigationItem>>
        ListRecentPostedTransactionsAsync(
            Guid householdId,
            int take,
            RecentLedgerNavigationKey? after,
            CancellationToken cancellationToken = default)
    {
        var transactions = _dbContext.LedgerTransactions
            .AsNoTracking()
            .Where(
                transaction =>
                    transaction.HouseholdId == householdId
                    && transaction.Status == TransactionStatus.Posted);

        if (after is not null)
        {
            var afterTimestamp = after.PostedAtUtc.UtcDateTime;
            transactions = transactions.Where(
                transaction =>
                    transaction.PostedAtUtc < afterTimestamp
                    || (transaction.PostedAtUtc == afterTimestamp
                        && transaction.Id.CompareTo(after.TransactionId) < 0));
        }

        var transactionRows = await transactions
            .OrderByDescending(transaction => transaction.PostedAtUtc)
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
                    transaction.ExecutionDate,
                    transaction.SettlementDate,
                    transaction.ExternalReference,
                    transaction.ReversalOfTransactionId,
                    ReversedByTransactionId = _dbContext.LedgerTransactions
                        .AsNoTracking()
                        .Where(
                            reversal =>
                                reversal.Type == TransactionType.Reversal
                                && reversal.Status == TransactionStatus.Posted
                                && reversal.ReversalOfTransactionId
                                == transaction.Id)
                        .Select(reversal => (Guid?)reversal.Id)
                        .SingleOrDefault(),
                    transaction.CreatedAtUtc,
                    transaction.PostedAtUtc,
                    EntryCount = _dbContext.TransactionEntries
                        .AsNoTracking()
                        .Count(entry => entry.TransactionId == transaction.Id)
                })
            .ToListAsync(cancellationToken);

        if (transactionRows.Count == 0)
        {
            return [];
        }

        if (transactionRows.Any(row => row.PostedAtUtc is null))
        {
            throw new NavigationPersistenceException(
                "Posted ledger history contains an invalid posting timestamp.");
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
                orderby transaction.PostedAtUtc descending,
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
                        ? (InstitutionType?)null
                        : institution.Type,
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
                group => (IReadOnlyList<RecentLedgerEntryEffectNavigationItem>)
                    group
                        .OrderBy(row => row.EntrySequence)
                        .Select(
                            row => new RecentLedgerEntryEffectNavigationItem(
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
        var result = new List<RecentLedgerTransactionNavigationItem>(
            transactionRows.Count);

        foreach (var row in transactionRows)
        {
            effectsByTransaction.TryGetValue(row.Id, out var effects);
            effects ??= [];

            if (effects.Count != row.EntryCount)
            {
                throw new NavigationPersistenceException(
                    "Posted ledger history could not be projected completely.");
            }

            result.Add(
                new RecentLedgerTransactionNavigationItem(
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
                    ToDateTimeOffset(row.PostedAtUtc!.Value),
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
