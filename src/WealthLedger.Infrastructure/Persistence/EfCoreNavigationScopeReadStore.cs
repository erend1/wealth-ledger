using Microsoft.EntityFrameworkCore;
using WealthLedger.Application.Navigation;

namespace WealthLedger.Infrastructure.Persistence;

public sealed class EfCoreNavigationScopeReadStore
    : INavigationScopeReadStore
{
    private readonly WealthLedgerDbContext _dbContext;

    public EfCoreNavigationScopeReadStore(
        WealthLedgerDbContext dbContext)
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

    public Task<bool> PositionScopeExistsAsync(
        Guid householdId,
        Guid portfolioId,
        Guid accountId,
        Guid assetId,
        CancellationToken cancellationToken = default)
        => (
                from household in _dbContext.Households.AsNoTracking()
                from portfolio in _dbContext.Portfolios.AsNoTracking()
                from account in _dbContext.Accounts.AsNoTracking()
                from asset in _dbContext.Assets.AsNoTracking()
                where household.Id == householdId
                      && portfolio.Id == portfolioId
                      && portfolio.HouseholdId == household.Id
                      && account.Id == accountId
                      && account.HouseholdId == household.Id
                      && asset.Id == assetId
                select household.Id)
            .AnyAsync(cancellationToken);
}
