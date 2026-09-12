using Microsoft.EntityFrameworkCore;
using WealthLedger.Application.OpeningBalances;
using WealthLedger.Domain.Ledger;

namespace WealthLedger.Infrastructure.Persistence;

public sealed class EfCoreOpeningBalanceEffectiveHistoryReadStore
    : IOpeningBalanceEffectiveHistoryReadStore
{
    private readonly WealthLedgerDbContext _dbContext;

    public EfCoreOpeningBalanceEffectiveHistoryReadStore(
        WealthLedgerDbContext dbContext)
    {
        _dbContext = dbContext
            ?? throw new ArgumentNullException(nameof(dbContext));
    }

    public async Task<OpeningBalanceEffectiveHistory> ReadAsync(
        OpeningBalanceScope scope,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scope);

        var rows = await (
                from entry in _dbContext.TransactionEntries.AsNoTracking()
                join transaction in _dbContext.LedgerTransactions.AsNoTracking()
                    on entry.TransactionId equals transaction.Id
                where transaction.HouseholdId == scope.HouseholdId
                      && transaction.Status == TransactionStatus.Posted
                      && transaction.Type != TransactionType.Reversal
                      && entry.PortfolioId == scope.PortfolioId
                      && entry.AccountId == scope.AccountId
                      && entry.AssetId == scope.AssetId
                      && !_dbContext.LedgerTransactions.Any(
                          reversal =>
                              reversal.Status == TransactionStatus.Posted
                              && reversal.Type == TransactionType.Reversal
                              && reversal.ReversalOfTransactionId
                                  == transaction.Id)
                select new
                {
                    transaction.Id,
                    transaction.Type
                })
            .Distinct()
            .OrderBy(row => row.Id)
            .ToArrayAsync(cancellationToken);

        var effectiveOpeningTransactionId = rows
            .Where(row => row.Type == TransactionType.OpeningBalance)
            .Select(row => (Guid?)row.Id)
            .FirstOrDefault();

        return new OpeningBalanceEffectiveHistory(
            effectiveOpeningTransactionId,
            rows.Any(row => row.Type != TransactionType.OpeningBalance));
    }
}
