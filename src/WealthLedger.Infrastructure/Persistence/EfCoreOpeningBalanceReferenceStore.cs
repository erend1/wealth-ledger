using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using WealthLedger.Application.CoreLedger;
using WealthLedger.Application.OpeningBalances;
using WealthLedger.Domain.Assets;
using WealthLedger.Domain.Portfolios;
using WealthLedger.Domain.ValueObjects;
using WealthLedger.Infrastructure.Persistence.Rows;

namespace WealthLedger.Infrastructure.Persistence;

public sealed class EfCoreOpeningBalanceReferenceStore
    : IOpeningBalanceReferenceReadStore,
      IOpeningBalanceReferenceWriteStore
{
    private readonly WealthLedgerDbContext _dbContext;

    public EfCoreOpeningBalanceReferenceStore(
        WealthLedgerDbContext dbContext)
    {
        _dbContext = dbContext
            ?? throw new ArgumentNullException(nameof(dbContext));
    }

    public async Task<OpeningBalanceHouseholdReference?> FindHouseholdAsync(
        Guid householdId,
        CancellationToken cancellationToken = default)
    {
        var row = await _dbContext.Households
            .AsNoTracking()
            .Where(candidate => candidate.Id == householdId)
            .Select(candidate => new
            {
                candidate.Id,
                candidate.BaseCurrencyCode
            })
            .SingleOrDefaultAsync(cancellationToken);

        return row is null
            ? null
            : new OpeningBalanceHouseholdReference(
                row.Id,
                new CurrencyCode(row.BaseCurrencyCode));
    }

    public Task<OpeningBalanceCurrencyReference?> FindCurrencyAsync(
        CurrencyCode code,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(code);

        return FindCurrencyByCodeAsync(
            code.Value,
            cancellationToken);
    }

    public async Task<OpeningBalanceInstitutionReference?> FindInstitutionAsync(
        Guid institutionId,
        CancellationToken cancellationToken = default)
    {
        var row = await _dbContext.Institutions
            .AsNoTracking()
            .SingleOrDefaultAsync(
                candidate => candidate.Id == institutionId,
                cancellationToken);

        return row is null
            ? null
            : MapInstitution(row);
    }

    public async Task<OpeningBalancePortfolioReference?> FindPortfolioAsync(
        Guid portfolioId,
        CancellationToken cancellationToken = default)
    {
        var row = await _dbContext.Portfolios
            .AsNoTracking()
            .SingleOrDefaultAsync(
                candidate => candidate.Id == portfolioId,
                cancellationToken);

        return row is null
            ? null
            : new OpeningBalancePortfolioReference(
                row.Id,
                row.HouseholdId,
                row.Code,
                row.Name,
                row.Status);
    }

    public async Task<OpeningBalanceAccountReference?> FindAccountAsync(
        Guid accountId,
        CancellationToken cancellationToken = default)
    {
        var row = await _dbContext.Accounts
            .AsNoTracking()
            .SingleOrDefaultAsync(
                candidate => candidate.Id == accountId,
                cancellationToken);

        return row is null
            ? null
            : MapAccount(row);
    }

    public async Task<OpeningBalanceAssetReference?> FindAssetAsync(
        Guid assetId,
        CancellationToken cancellationToken = default)
    {
        var row = await _dbContext.Assets
            .AsNoTracking()
            .SingleOrDefaultAsync(
                candidate => candidate.Id == assetId,
                cancellationToken);

        return row is null
            ? null
            : MapAsset(row);
    }

    public async Task<OpeningBalanceReferenceWriteResult<
        OpeningBalanceCurrencyReference>> TryCreateCurrencyAsync(
            OpeningBalanceCurrencyReference candidate,
            CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(candidate);

        var existing =
            await FindCurrencyByCodeAsync(
                candidate.Code.Value,
                cancellationToken);

        if (existing is not null)
        {
            return ClassifyCurrency(candidate, existing);
        }

        var row = new CurrencyRow
        {
            Code = candidate.Code.Value,
            Name = candidate.Name,
            MinorUnitDigits = candidate.MinorUnitDigits
        };

        _dbContext.Currencies.Add(row);

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);

            return OpeningBalanceReferenceWriteResult<
                OpeningBalanceCurrencyReference>.Created(
                    MapCurrency(row));
        }
        catch (Exception exception)
            when (exception is DbUpdateException or SqliteException)
        {
            _dbContext.Entry(row).State = EntityState.Detached;

            existing =
                await FindCurrencyByCodeAsync(
                    candidate.Code.Value,
                    cancellationToken);

            return existing is not null
                ? ClassifyCurrency(candidate, existing)
                : throw PersistenceFailure(exception);
        }
    }

    public async Task<OpeningBalanceReferenceWriteResult<
        OpeningBalanceInstitutionReference>> TryCreateInstitutionAsync(
            Institution candidate,
            CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(candidate);

        var existing =
            await FindInstitutionByCodeAsync(
                candidate.Code,
                cancellationToken);

        if (existing is not null)
        {
            return ClassifyInstitution(candidate, existing);
        }

        var row = new InstitutionRow
        {
            Id = candidate.Id,
            Code = candidate.Code,
            Name = candidate.Name,
            Type = candidate.Type,
            IsActive = candidate.IsActive
        };

        _dbContext.Institutions.Add(row);

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);

            return OpeningBalanceReferenceWriteResult<
                OpeningBalanceInstitutionReference>.Created(
                    MapInstitution(row));
        }
        catch (Exception exception)
            when (exception is DbUpdateException or SqliteException)
        {
            _dbContext.Entry(row).State = EntityState.Detached;

            existing =
                await FindInstitutionByCodeAsync(
                    candidate.Code,
                    cancellationToken);

            return existing is not null
                ? ClassifyInstitution(candidate, existing)
                : throw PersistenceFailure(exception);
        }
    }

    public async Task<OpeningBalanceReferenceWriteResult<
        OpeningBalanceAccountReference>> TryCreateAccountAsync(
            Account candidate,
            CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(candidate);

        var existing =
            await FindAccountByCodeAsync(
                candidate.HouseholdId,
                candidate.Code,
                cancellationToken);

        if (existing is not null)
        {
            return ClassifyAccount(candidate, existing);
        }

        var row = new AccountRow
        {
            Id = candidate.Id,
            HouseholdId = candidate.HouseholdId,
            InstitutionId = candidate.InstitutionId,
            Code = candidate.Code,
            Name = candidate.Name,
            Type = candidate.Type,
            IsActive = candidate.IsActive,
            OpenedOn = candidate.OpenedOn,
            ClosedOn = candidate.ClosedOn
        };

        _dbContext.Accounts.Add(row);

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);

            return OpeningBalanceReferenceWriteResult<
                OpeningBalanceAccountReference>.Created(
                    MapAccount(row));
        }
        catch (Exception exception)
            when (exception is DbUpdateException or SqliteException)
        {
            _dbContext.Entry(row).State = EntityState.Detached;

            existing =
                await FindAccountByCodeAsync(
                    candidate.HouseholdId,
                    candidate.Code,
                    cancellationToken);

            return existing is not null
                ? ClassifyAccount(candidate, existing)
                : throw PersistenceFailure(exception);
        }
    }

    public async Task<OpeningBalanceReferenceWriteResult<
        OpeningBalanceAssetReference>> TryCreateAssetAsync(
            Asset candidate,
            DateTimeOffset createdAtUtc,
            CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(candidate);

        var existing =
            await FindAssetByCodeAsync(
                candidate.Code,
                cancellationToken);

        if (existing is not null)
        {
            return ClassifyAsset(candidate, existing);
        }

        var normalizedCreatedAtUtc =
            createdAtUtc.ToUniversalTime();

        var row = new AssetRow
        {
            Id = candidate.Id,
            Code = candidate.Code,
            Name = candidate.Name,
            Type = candidate.Type,
            BaseUnit = candidate.BaseUnit,
            BaseCurrencyCode = candidate.BaseCurrency?.Value,
            LotTrackingMode = candidate.LotTrackingMode,
            IsActive = candidate.IsActive,
            CreatedAtUtc = normalizedCreatedAtUtc.UtcDateTime
        };

        _dbContext.Assets.Add(row);

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);

            return OpeningBalanceReferenceWriteResult<
                OpeningBalanceAssetReference>.Created(
                    MapAsset(row));
        }
        catch (Exception exception)
            when (exception is DbUpdateException or SqliteException)
        {
            _dbContext.Entry(row).State = EntityState.Detached;

            existing =
                await FindAssetByCodeAsync(
                    candidate.Code,
                    cancellationToken);

            return existing is not null
                ? ClassifyAsset(candidate, existing)
                : throw PersistenceFailure(exception);
        }
    }

    private async Task<OpeningBalanceCurrencyReference?> FindCurrencyByCodeAsync(
        string code,
        CancellationToken cancellationToken)
    {
        var row = await _dbContext.Currencies
            .AsNoTracking()
            .SingleOrDefaultAsync(
                candidate => candidate.Code == code,
                cancellationToken);

        return row is null
            ? null
            : MapCurrency(row);
    }

    private async Task<OpeningBalanceInstitutionReference?>
        FindInstitutionByCodeAsync(
            string code,
            CancellationToken cancellationToken)
    {
        var row = await _dbContext.Institutions
            .AsNoTracking()
            .SingleOrDefaultAsync(
                candidate => candidate.Code == code,
                cancellationToken);

        return row is null
            ? null
            : MapInstitution(row);
    }

    private async Task<OpeningBalanceAccountReference?> FindAccountByCodeAsync(
        Guid householdId,
        string code,
        CancellationToken cancellationToken)
    {
        var row = await _dbContext.Accounts
            .AsNoTracking()
            .SingleOrDefaultAsync(
                candidate => candidate.HouseholdId == householdId
                    && candidate.Code == code,
                cancellationToken);

        return row is null
            ? null
            : MapAccount(row);
    }

    private async Task<OpeningBalanceAssetReference?> FindAssetByCodeAsync(
        string code,
        CancellationToken cancellationToken)
    {
        var row = await _dbContext.Assets
            .AsNoTracking()
            .SingleOrDefaultAsync(
                candidate => candidate.Code == code,
                cancellationToken);

        return row is null
            ? null
            : MapAsset(row);
    }

    private static OpeningBalanceReferenceWriteResult<
        OpeningBalanceCurrencyReference> ClassifyCurrency(
            OpeningBalanceCurrencyReference candidate,
            OpeningBalanceCurrencyReference existing)
        => existing.Code == candidate.Code
           && string.Equals(
               existing.Name,
               candidate.Name,
               StringComparison.Ordinal)
           && existing.MinorUnitDigits == candidate.MinorUnitDigits
            ? OpeningBalanceReferenceWriteResult<
                OpeningBalanceCurrencyReference>.Equivalent(existing)
            : OpeningBalanceReferenceWriteResult<
                OpeningBalanceCurrencyReference>.Conflict();

    private static OpeningBalanceReferenceWriteResult<
        OpeningBalanceInstitutionReference> ClassifyInstitution(
            Institution candidate,
            OpeningBalanceInstitutionReference existing)
        => existing.IsActive
           && string.Equals(existing.Code, candidate.Code, StringComparison.Ordinal)
           && string.Equals(existing.Name, candidate.Name, StringComparison.Ordinal)
           && existing.Type == candidate.Type
            ? OpeningBalanceReferenceWriteResult<
                OpeningBalanceInstitutionReference>.Equivalent(existing)
            : OpeningBalanceReferenceWriteResult<
                OpeningBalanceInstitutionReference>.Conflict();

    private static OpeningBalanceReferenceWriteResult<
        OpeningBalanceAccountReference> ClassifyAccount(
            Account candidate,
            OpeningBalanceAccountReference existing)
        => existing.IsActive
           && existing.ClosedOn is null
           && existing.HouseholdId == candidate.HouseholdId
           && existing.InstitutionId == candidate.InstitutionId
           && string.Equals(existing.Code, candidate.Code, StringComparison.Ordinal)
           && string.Equals(existing.Name, candidate.Name, StringComparison.Ordinal)
           && existing.Type == candidate.Type
           && existing.OpenedOn == candidate.OpenedOn
            ? OpeningBalanceReferenceWriteResult<
                OpeningBalanceAccountReference>.Equivalent(existing)
            : OpeningBalanceReferenceWriteResult<
                OpeningBalanceAccountReference>.Conflict();

    private static OpeningBalanceReferenceWriteResult<
        OpeningBalanceAssetReference> ClassifyAsset(
            Asset candidate,
            OpeningBalanceAssetReference existing)
        => existing.IsActive
           && string.Equals(existing.Code, candidate.Code, StringComparison.Ordinal)
           && string.Equals(existing.Name, candidate.Name, StringComparison.Ordinal)
           && existing.Type == candidate.Type
           && existing.BaseUnit == candidate.BaseUnit
           && existing.BaseCurrency == candidate.BaseCurrency
           && existing.LotTrackingMode == candidate.LotTrackingMode
            ? OpeningBalanceReferenceWriteResult<
                OpeningBalanceAssetReference>.Equivalent(existing)
            : OpeningBalanceReferenceWriteResult<
                OpeningBalanceAssetReference>.Conflict();

    private static OpeningBalanceCurrencyReference MapCurrency(
        CurrencyRow row)
        => new(
            new CurrencyCode(row.Code),
            row.Name,
            row.MinorUnitDigits);

    private static OpeningBalanceInstitutionReference MapInstitution(
        InstitutionRow row)
        => new(
            row.Id,
            row.Code,
            row.Name,
            row.Type,
            row.IsActive);

    private static OpeningBalanceAccountReference MapAccount(
        AccountRow row)
        => new(
            row.Id,
            row.HouseholdId,
            row.InstitutionId,
            row.Code,
            row.Name,
            row.Type,
            row.IsActive,
            row.OpenedOn,
            row.ClosedOn);

    private static OpeningBalanceAssetReference MapAsset(
        AssetRow row)
        => new(
            row.Id,
            row.Code,
            row.Name,
            row.Type,
            row.BaseUnit,
            row.BaseCurrencyCode is null
                ? null
                : new CurrencyCode(row.BaseCurrencyCode),
            row.LotTrackingMode,
            row.IsActive,
            ToDateTimeOffset(row.CreatedAtUtc));

    private static DateTimeOffset ToDateTimeOffset(DateTime value)
        => new(
            value.Kind == DateTimeKind.Utc
                ? value
                : DateTime.SpecifyKind(value, DateTimeKind.Utc));

    private static CoreLedgerPersistenceException PersistenceFailure(
        Exception innerException)
        => new(
            "Opening-balance reference data could not be persisted.",
            innerException);
}
