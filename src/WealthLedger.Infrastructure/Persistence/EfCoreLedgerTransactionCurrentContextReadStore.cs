using Microsoft.EntityFrameworkCore;
using WealthLedger.Application.Navigation;
using WealthLedger.Domain.Portfolios;

namespace WealthLedger.Infrastructure.Persistence;

/// <summary>
/// Resolves current display labels for one already-read transaction in a
/// fixed number of batched queries. It never performs a lookup per entry or
/// allocation.
/// </summary>
public sealed class EfCoreLedgerTransactionCurrentContextReadStore
    : ILedgerTransactionCurrentContextReadStore
{
    private readonly WealthLedgerDbContext _dbContext;

    public EfCoreLedgerTransactionCurrentContextReadStore(
        WealthLedgerDbContext dbContext)
    {
        _dbContext = dbContext
            ?? throw new ArgumentNullException(nameof(dbContext));
    }

    public async Task<LedgerTransactionCurrentContext?> ReadAsync(
        LedgerTransactionCurrentContextQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var householdRow = await (
                from household in _dbContext.Households.AsNoTracking()
                join currency in _dbContext.Currencies.AsNoTracking()
                    on household.BaseCurrencyCode equals currency.Code
                where household.Id == query.HouseholdId
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

        if (householdRow is null)
        {
            return null;
        }

        var entryIds = query.EntryIds.Distinct().ToArray();
        var entryRows = entryIds.Length == 0
            ? []
            : await (
                    from entry in _dbContext.TransactionEntries.AsNoTracking()
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
                    where entryIds.Contains(entry.Id)
                          && portfolio.HouseholdId == query.HouseholdId
                          && account.HouseholdId == query.HouseholdId
                    orderby entry.EntrySequence, entry.Id
                    select new
                    {
                        EntryId = entry.Id,
                        PortfolioId = portfolio.Id,
                        portfolio.HouseholdId,
                        PortfolioCode = portfolio.Code,
                        PortfolioName = portfolio.Name,
                        PortfolioStatus = portfolio.Status,
                        PortfolioCreatedAtUtc = portfolio.CreatedAtUtc,
                        PortfolioClosedAtUtc = portfolio.ClosedAtUtc,
                        AccountId = account.Id,
                        AccountInstitutionId = institution == null
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
                        AccountCode = account.Code,
                        AccountName = account.Name,
                        AccountType = account.Type,
                        AccountIsActive = account.IsActive,
                        account.OpenedOn,
                        account.ClosedOn,
                        AssetId = asset.Id,
                        AssetCode = asset.Code,
                        AssetName = asset.Name,
                        AssetType = asset.Type,
                        AssetBaseUnit = asset.BaseUnit,
                        AssetBaseCurrencyCode = asset.BaseCurrencyCode,
                        AssetLotTrackingMode = asset.LotTrackingMode,
                        AssetIsActive = asset.IsActive,
                        AssetCreatedAtUtc = asset.CreatedAtUtc
                    })
                .ToArrayAsync(cancellationToken);

        var lotIds = query.AssetLotIds.Distinct().ToArray();
        var lotRows = lotIds.Length == 0
            ? []
            : await (
                    from lot in _dbContext.AssetLots.AsNoTracking()
                    join asset in _dbContext.Assets.AsNoTracking()
                        on lot.AssetId equals asset.Id
                    where lotIds.Contains(lot.Id)
                    orderby lot.Id
                    select new
                    {
                        AssetLotId = lot.Id,
                        AssetId = asset.Id,
                        AssetCode = asset.Code,
                        AssetName = asset.Name,
                        AssetType = asset.Type,
                        AssetBaseUnit = asset.BaseUnit,
                        AssetBaseCurrencyCode = asset.BaseCurrencyCode,
                        AssetLotTrackingMode = asset.LotTrackingMode,
                        AssetIsActive = asset.IsActive,
                        AssetCreatedAtUtc = asset.CreatedAtUtc
                    })
                .ToArrayAsync(cancellationToken);

        HouseholdMemberNavigationItem? member = null;

        if (query.HouseholdMemberId is Guid memberId)
        {
            var memberRow = await _dbContext.HouseholdMembers
                .AsNoTracking()
                .Where(
                    row => row.Id == memberId
                           && row.HouseholdId == query.HouseholdId)
                .Select(
                    row => new
                    {
                        row.Id,
                        row.HouseholdId,
                        row.DisplayName,
                        row.IsActive,
                        row.CreatedAtUtc
                    })
                .SingleOrDefaultAsync(cancellationToken);

            if (memberRow is not null)
            {
                member = new HouseholdMemberNavigationItem(
                    memberRow.Id,
                    memberRow.HouseholdId,
                    memberRow.DisplayName,
                    memberRow.IsActive,
                    ToDateTimeOffset(memberRow.CreatedAtUtc));
            }
        }

        var currencyCodes = query.CurrencyCodes
            .Where(code => !string.IsNullOrWhiteSpace(code))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var currencies = currencyCodes.Length == 0
            ? []
            : await _dbContext.Currencies
                .AsNoTracking()
                .Where(row => currencyCodes.Contains(row.Code))
                .OrderBy(row => row.Code)
                .Select(
                    row => new CurrencyNavigationItem(
                        row.Code,
                        row.Name,
                        row.MinorUnitDigits))
                .ToArrayAsync(cancellationToken);

        return new LedgerTransactionCurrentContext(
            new HouseholdNavigationItem(
                householdRow.Id,
                householdRow.Name,
                new CurrencyNavigationItem(
                    householdRow.CurrencyCode,
                    householdRow.CurrencyName,
                    householdRow.MinorUnitDigits),
                ToDateTimeOffset(householdRow.CreatedAtUtc)),
            entryRows
                .Select(
                    row => new LedgerTransactionEntryCurrentContext(
                        row.EntryId,
                        new PortfolioNavigationItem(
                            row.PortfolioId,
                            row.HouseholdId,
                            row.PortfolioCode,
                            row.PortfolioName,
                            row.PortfolioStatus,
                            ToDateTimeOffset(row.PortfolioCreatedAtUtc),
                            row.PortfolioClosedAtUtc is null
                                ? null
                                : ToDateTimeOffset(
                                    row.PortfolioClosedAtUtc.Value)),
                        new AccountNavigationItem(
                            row.AccountId,
                            row.HouseholdId,
                            row.AccountInstitutionId is null
                                ? null
                                : new AccountInstitutionNavigationItem(
                                    row.AccountInstitutionId.Value,
                                    row.InstitutionCode!,
                                    row.InstitutionName!,
                                    row.InstitutionType!.Value,
                                    row.InstitutionIsActive!.Value),
                            row.AccountCode,
                            row.AccountName,
                            row.AccountType,
                            row.AccountIsActive,
                            row.OpenedOn,
                            row.ClosedOn),
                        new AssetNavigationItem(
                            row.AssetId,
                            row.AssetCode,
                            row.AssetName,
                            row.AssetType,
                            row.AssetBaseUnit,
                            row.AssetBaseCurrencyCode,
                            row.AssetLotTrackingMode,
                            row.AssetIsActive,
                            ToDateTimeOffset(row.AssetCreatedAtUtc))))
                .ToArray(),
            lotRows
                .Select(
                    row => new LedgerTransactionLotCurrentContext(
                        row.AssetLotId,
                        new AssetNavigationItem(
                            row.AssetId,
                            row.AssetCode,
                            row.AssetName,
                            row.AssetType,
                            row.AssetBaseUnit,
                            row.AssetBaseCurrencyCode,
                            row.AssetLotTrackingMode,
                            row.AssetIsActive,
                            ToDateTimeOffset(row.AssetCreatedAtUtc))))
                .ToArray(),
            member,
            currencies);
    }

    private static DateTimeOffset ToDateTimeOffset(DateTime value)
        => new(
            value.Kind == DateTimeKind.Utc
                ? value
                : DateTime.SpecifyKind(value, DateTimeKind.Utc));
}
