using Microsoft.EntityFrameworkCore;
using WealthLedger.Application.CoreLedger;
using WealthLedger.Domain.Assets;
using WealthLedger.Domain.ValueObjects;

namespace WealthLedger.Infrastructure.Persistence;

public sealed class EfCoreLedgerReferenceData : ILedgerReferenceData
{
    private readonly WealthLedgerDbContext _dbContext;

    public EfCoreLedgerReferenceData(WealthLedgerDbContext dbContext)
    {
        _dbContext = dbContext
            ?? throw new ArgumentNullException(nameof(dbContext));
    }

    public async Task<LedgerLocationReference?> FindLocationAsync(
        Guid portfolioId,
        Guid accountId,
        CancellationToken cancellationToken = default)
    {
        var portfolio = await _dbContext.Portfolios
            .AsNoTracking()
            .SingleOrDefaultAsync(
                x => x.Id == portfolioId,
                cancellationToken);

        if (portfolio is null)
        {
            return null;
        }

        var account = await _dbContext.Accounts
            .AsNoTracking()
            .SingleOrDefaultAsync(
                x => x.Id == accountId,
                cancellationToken);

        if (account is null)
        {
            return null;
        }

        return new LedgerLocationReference(
            portfolio.Id,
            portfolio.HouseholdId,
            portfolio.Status,
            account.Id,
            account.HouseholdId,
            account.IsActive);
    }

    public async Task<Asset?> FindAssetAsync(
        Guid assetId,
        CancellationToken cancellationToken = default)
    {
        var row = await _dbContext.Assets
            .AsNoTracking()
            .SingleOrDefaultAsync(
                x => x.Id == assetId,
                cancellationToken);

        if (row is null)
        {
            return null;
        }

        var asset = Asset.Create(
            row.Id,
            row.Code,
            row.Name,
            row.Type,
            row.BaseUnit,
            row.BaseCurrencyCode is null
                ? null
                : new CurrencyCode(row.BaseCurrencyCode),
            row.LotTrackingMode);

        if (!row.IsActive)
        {
            asset.Deactivate();
        }

        return asset;
    }

    public async Task<CurrencyReference?> FindCurrencyAsync(
        CurrencyCode currency,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(currency);

        var row = await _dbContext.Currencies
            .AsNoTracking()
            .SingleOrDefaultAsync(
                x => x.Code == currency.Value,
                cancellationToken);

        return row is null
            ? null
            : new CurrencyReference(
                new CurrencyCode(row.Code),
                row.MinorUnitDigits);
    }

    public async Task<HouseholdMemberReference?> FindHouseholdMemberAsync(
        Guid householdMemberId,
        CancellationToken cancellationToken = default)
    {
        var row = await _dbContext.HouseholdMembers
            .AsNoTracking()
            .SingleOrDefaultAsync(
                x => x.Id == householdMemberId,
                cancellationToken);

        return row is null
            ? null
            : new HouseholdMemberReference(
                row.Id,
                row.HouseholdId,
                row.IsActive);
    }
}
