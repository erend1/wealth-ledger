using Microsoft.EntityFrameworkCore;
using WealthLedger.Application.CoreLedger;
using WealthLedger.Application.Positions;
using WealthLedger.Domain.Ledger;
using WealthLedger.Domain.ValueObjects;

namespace WealthLedger.Infrastructure.Persistence;

public sealed class EfCorePostedEntrySource : IPostedEntrySource
{
    private readonly WealthLedgerDbContext _dbContext;

    public EfCorePostedEntrySource(WealthLedgerDbContext dbContext)
    {
        _dbContext = dbContext
            ?? throw new ArgumentNullException(nameof(dbContext));
    }

    public async Task<IReadOnlyList<PostedEntryFact>> ListPositionEntriesAsync(
        Guid householdId,
        Guid portfolioId,
        Guid accountId,
        Guid assetId,
        CancellationToken cancellationToken = default)
    {
        var rows = await (
                from entry in _dbContext.TransactionEntries.AsNoTracking()
                join transaction in _dbContext.LedgerTransactions.AsNoTracking()
                    on entry.TransactionId equals transaction.Id
                where transaction.HouseholdId == householdId
                      && transaction.Status == TransactionStatus.Posted
                      && entry.PortfolioId == portfolioId
                      && entry.AccountId == accountId
                      && entry.AssetId == assetId
                select new
                {
                    TransactionId = transaction.Id,
                    transaction.ExecutionDate,
                    transaction.CreatedAtUtc,
                    entry.EntrySequence,
                    entry.QuantityDeltaE8
                })
            .ToListAsync(cancellationToken);

        var result = new List<PostedEntryFact>(rows.Count);

        foreach (var row in rows)
        {
            if (row.ExecutionDate is null)
            {
                throw new CoreLedgerPersistenceException(
                    "Posted ledger history contains a transaction without an execution date.");
            }

            result.Add(new PostedEntryFact(
                row.TransactionId,
                row.ExecutionDate.Value,
                ToDateTimeOffset(row.CreatedAtUtc),
                row.EntrySequence,
                QuantityDelta.FromRaw(row.QuantityDeltaE8)));
        }

        return result;
    }

    private static DateTimeOffset ToDateTimeOffset(DateTime value)
        => new(
            value.Kind == DateTimeKind.Utc
                ? value
                : DateTime.SpecifyKind(value, DateTimeKind.Utc));
}
