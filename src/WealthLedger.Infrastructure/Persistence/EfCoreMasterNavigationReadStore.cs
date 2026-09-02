using Microsoft.EntityFrameworkCore;
using WealthLedger.Application.Navigation;
using WealthLedger.Domain.Portfolios;
using WealthLedger.Infrastructure.Persistence.Rows;

namespace WealthLedger.Infrastructure.Persistence;

public sealed class EfCoreMasterNavigationReadStore
    : IMasterNavigationReadStore
{
    private readonly WealthLedgerDbContext _dbContext;

    public EfCoreMasterNavigationReadStore(
        WealthLedgerDbContext dbContext)
    {
        _dbContext = dbContext
            ?? throw new ArgumentNullException(nameof(dbContext));
    }

    public async Task<IReadOnlyList<HouseholdNavigationItem>>
        ListHouseholdsAsync(
            int take,
            NavigationCreatedAtKey? after,
            CancellationToken cancellationToken = default)
    {
        var query = _dbContext.Households.AsNoTracking();

        if (after is not null)
        {
            var afterTimestamp = after.CreatedAtUtc.UtcDateTime;
            query = query.Where(
                row => row.CreatedAtUtc > afterTimestamp
                       || (row.CreatedAtUtc == afterTimestamp
                           && row.Id.CompareTo(after.Id) > 0));
        }

        var rows = await (
                from household in query
                join currency in _dbContext.Currencies.AsNoTracking()
                    on household.BaseCurrencyCode equals currency.Code
                orderby household.CreatedAtUtc, household.Id
                select new
                {
                    household.Id,
                    household.Name,
                    CurrencyCode = currency.Code,
                    CurrencyName = currency.Name,
                    currency.MinorUnitDigits,
                    household.CreatedAtUtc
                })
            .Take(take)
            .ToListAsync(cancellationToken);

        return rows
            .Select(
                row => new HouseholdNavigationItem(
                    row.Id,
                    row.Name,
                    new CurrencyNavigationItem(
                        row.CurrencyCode,
                        row.CurrencyName,
                        row.MinorUnitDigits),
                    ToDateTimeOffset(row.CreatedAtUtc)))
            .ToArray();
    }

    public async Task<HouseholdNavigationItem?> FindHouseholdAsync(
        Guid householdId,
        CancellationToken cancellationToken = default)
    {
        var row = await (
                from household in _dbContext.Households.AsNoTracking()
                join currency in _dbContext.Currencies.AsNoTracking()
                    on household.BaseCurrencyCode equals currency.Code
                where household.Id == householdId
                select new
                {
                    household.Id,
                    household.Name,
                    CurrencyCode = currency.Code,
                    CurrencyName = currency.Name,
                    currency.MinorUnitDigits,
                    household.CreatedAtUtc
                })
            .SingleOrDefaultAsync(cancellationToken);

        return row is null
            ? null
            : new HouseholdNavigationItem(
                row.Id,
                row.Name,
                new CurrencyNavigationItem(
                    row.CurrencyCode,
                    row.CurrencyName,
                    row.MinorUnitDigits),
                ToDateTimeOffset(row.CreatedAtUtc));
    }

    public async Task<IReadOnlyList<HouseholdMemberNavigationItem>>
        ListHouseholdMembersAsync(
            Guid householdId,
            bool includeInactive,
            int take,
            NavigationCreatedAtKey? after,
            CancellationToken cancellationToken = default)
    {
        var query = _dbContext.HouseholdMembers
            .AsNoTracking()
            .Where(row => row.HouseholdId == householdId);

        if (!includeInactive)
        {
            query = query.Where(row => row.IsActive);
        }

        if (after is not null)
        {
            var afterTimestamp = after.CreatedAtUtc.UtcDateTime;
            query = query.Where(
                row => row.CreatedAtUtc > afterTimestamp
                       || (row.CreatedAtUtc == afterTimestamp
                           && row.Id.CompareTo(after.Id) > 0));
        }

        var rows = await query
            .OrderBy(row => row.CreatedAtUtc)
            .ThenBy(row => row.Id)
            .Take(take)
            .ToListAsync(cancellationToken);

        return rows
            .Select(
                row => new HouseholdMemberNavigationItem(
                    row.Id,
                    row.HouseholdId,
                    row.DisplayName,
                    row.IsActive,
                    ToDateTimeOffset(row.CreatedAtUtc)))
            .ToArray();
    }

    public async Task<IReadOnlyList<InstitutionNavigationItem>>
        ListInstitutionsAsync(
            bool includeInactive,
            int take,
            NavigationCodeKey? after,
            CancellationToken cancellationToken = default)
    {
        var query = _dbContext.Institutions.AsNoTracking();

        if (!includeInactive)
        {
            query = query.Where(row => row.IsActive);
        }

        if (after is not null)
        {
            query = query.Where(
                row => string.Compare(row.Code, after.Code) > 0
                       || (row.Code == after.Code
                           && row.Id.CompareTo(after.Id) > 0));
        }

        return await query
            .OrderBy(row => row.Code)
            .ThenBy(row => row.Id)
            .Take(take)
            .Select(
                row => new InstitutionNavigationItem(
                    row.Id,
                    row.Code,
                    row.Name,
                    row.Type,
                    row.IsActive))
            .ToArrayAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<PortfolioNavigationItem>>
        ListPortfoliosAsync(
            Guid householdId,
            bool includeInactive,
            int take,
            NavigationCodeKey? after,
            CancellationToken cancellationToken = default)
    {
        var query = _dbContext.Portfolios
            .AsNoTracking()
            .Where(row => row.HouseholdId == householdId);

        if (!includeInactive)
        {
            query = query.Where(row => row.Status == PortfolioStatus.Active);
        }

        if (after is not null)
        {
            query = query.Where(
                row => string.Compare(row.Code, after.Code) > 0
                       || (row.Code == after.Code
                           && row.Id.CompareTo(after.Id) > 0));
        }

        var rows = await query
            .OrderBy(row => row.Code)
            .ThenBy(row => row.Id)
            .Take(take)
            .ToListAsync(cancellationToken);

        return rows
            .Select(
                row => new PortfolioNavigationItem(
                    row.Id,
                    row.HouseholdId,
                    row.Code,
                    row.Name,
                    row.Status,
                    ToDateTimeOffset(row.CreatedAtUtc),
                    row.ClosedAtUtc is null
                        ? null
                        : ToDateTimeOffset(row.ClosedAtUtc.Value)))
            .ToArray();
    }

    public async Task<IReadOnlyList<AccountNavigationItem>> ListAccountsAsync(
        Guid householdId,
        bool includeInactive,
        int take,
        NavigationCodeKey? after,
        CancellationToken cancellationToken = default)
    {
        var accounts = _dbContext.Accounts
            .AsNoTracking()
            .Where(row => row.HouseholdId == householdId);

        if (!includeInactive)
        {
            accounts = accounts.Where(row => row.IsActive);
        }

        if (after is not null)
        {
            accounts = accounts.Where(
                row => string.Compare(row.Code, after.Code) > 0
                       || (row.Code == after.Code
                           && row.Id.CompareTo(after.Id) > 0));
        }

        var rows = await (
                from account in accounts
                join institution in _dbContext.Institutions.AsNoTracking()
                    on account.InstitutionId equals (Guid?)institution.Id
                    into accountInstitutions
                from institution in accountInstitutions.DefaultIfEmpty()
                orderby account.Code, account.Id
                select new
                {
                    account.Id,
                    account.HouseholdId,
                    account.Code,
                    account.Name,
                    account.Type,
                    account.IsActive,
                    account.OpenedOn,
                    account.ClosedOn,
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
                        : institution.IsActive
                })
            .Take(take)
            .ToListAsync(cancellationToken);

        return rows
            .Select(
                row => new AccountNavigationItem(
                    row.Id,
                    row.HouseholdId,
                    row.InstitutionId is null
                        ? null
                        : new AccountInstitutionNavigationItem(
                            row.InstitutionId.Value,
                            row.InstitutionCode!,
                            row.InstitutionName!,
                            row.InstitutionType!.Value,
                            row.InstitutionIsActive!.Value),
                    row.Code,
                    row.Name,
                    row.Type,
                    row.IsActive,
                    row.OpenedOn,
                    row.ClosedOn))
            .ToArray();
    }

    public async Task<IReadOnlyList<CurrencyNavigationItem>>
        ListCurrenciesAsync(
            int take,
            NavigationCurrencyKey? after,
            CancellationToken cancellationToken = default)
    {
        var query = _dbContext.Currencies.AsNoTracking();

        if (after is not null)
        {
            query = query.Where(
                row => string.Compare(row.Code, after.Code) > 0);
        }

        return await query
            .OrderBy(row => row.Code)
            .Take(take)
            .Select(
                row => new CurrencyNavigationItem(
                    row.Code,
                    row.Name,
                    row.MinorUnitDigits))
            .ToArrayAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<AssetNavigationItem>> ListAssetsAsync(
        bool includeInactive,
        int take,
        NavigationCodeKey? after,
        CancellationToken cancellationToken = default)
    {
        var query = _dbContext.Assets.AsNoTracking();

        if (!includeInactive)
        {
            query = query.Where(row => row.IsActive);
        }

        if (after is not null)
        {
            query = query.Where(
                row => string.Compare(row.Code, after.Code) > 0
                       || (row.Code == after.Code
                           && row.Id.CompareTo(after.Id) > 0));
        }

        var rows = await query
            .OrderBy(row => row.Code)
            .ThenBy(row => row.Id)
            .Take(take)
            .ToListAsync(cancellationToken);

        return rows
            .Select(
                row => new AssetNavigationItem(
                    row.Id,
                    row.Code,
                    row.Name,
                    row.Type,
                    row.BaseUnit,
                    row.BaseCurrencyCode,
                    row.LotTrackingMode,
                    row.IsActive,
                    ToDateTimeOffset(row.CreatedAtUtc)))
            .ToArray();
    }

    private static DateTimeOffset ToDateTimeOffset(DateTime value)
        => new(
            value.Kind == DateTimeKind.Utc
                ? value
                : DateTime.SpecifyKind(value, DateTimeKind.Utc));
}
