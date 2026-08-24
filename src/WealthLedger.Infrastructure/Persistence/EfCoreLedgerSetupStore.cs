using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using WealthLedger.Application.CoreLedger;
using WealthLedger.Application.Setup;
using WealthLedger.Domain.Assets;
using WealthLedger.Domain.Portfolios;
using WealthLedger.Domain.ValueObjects;
using WealthLedger.Infrastructure.Persistence.Rows;

namespace WealthLedger.Infrastructure.Persistence;

public sealed class EfCoreLedgerSetupStore : ICoreLedgerSetupStore
{
    private readonly WealthLedgerDbContext _dbContext;

    public EfCoreLedgerSetupStore(WealthLedgerDbContext dbContext)
    {
        _dbContext = dbContext
            ?? throw new ArgumentNullException(nameof(dbContext));
    }

    public async Task<bool> TryInitializeAsync(
        CoreLedgerSetup setup,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(setup);
        ValidateSetup(setup);

        try
        {
            await using var transaction =
                await _dbContext.Database.BeginTransactionAsync(
                    cancellationToken);

            if (await HasAnyMasterDataAsync(cancellationToken))
            {
                return false;
            }

            _dbContext.Currencies.Add(new CurrencyRow
            {
                Code = setup.BaseCurrency.Code.Value,
                Name = setup.BaseCurrency.Name,
                MinorUnitDigits = setup.BaseCurrency.MinorUnitDigits
            });

            _dbContext.Households.Add(new HouseholdRow
            {
                Id = setup.Household.Id,
                Name = setup.Household.Name,
                BaseCurrencyCode = setup.Household.BaseCurrency.Value,
                CreatedAtUtc = setup.Household.CreatedAtUtc.UtcDateTime
            });

            if (setup.HouseholdMember is not null)
            {
                _dbContext.HouseholdMembers.Add(new HouseholdMemberRow
                {
                    Id = setup.HouseholdMember.Id,
                    HouseholdId = setup.HouseholdMember.HouseholdId,
                    DisplayName = setup.HouseholdMember.DisplayName,
                    IsActive = setup.HouseholdMember.IsActive,
                    CreatedAtUtc =
                        setup.HouseholdMember.CreatedAtUtc.UtcDateTime
                });
            }

            _dbContext.Institutions.Add(new InstitutionRow
            {
                Id = setup.Institution.Id,
                Code = setup.Institution.Code,
                Name = setup.Institution.Name,
                Type = setup.Institution.Type,
                IsActive = setup.Institution.IsActive
            });

            _dbContext.Portfolios.Add(new PortfolioRow
            {
                Id = setup.Portfolio.Id,
                HouseholdId = setup.Portfolio.HouseholdId,
                Code = setup.Portfolio.Code,
                Name = setup.Portfolio.Name,
                Status = setup.Portfolio.Status,
                CreatedAtUtc = setup.Portfolio.CreatedAtUtc.UtcDateTime,
                ClosedAtUtc = setup.Portfolio.ClosedAtUtc?.UtcDateTime
            });

            _dbContext.Accounts.Add(new AccountRow
            {
                Id = setup.Account.Id,
                HouseholdId = setup.Account.HouseholdId,
                InstitutionId = setup.Account.InstitutionId,
                Code = setup.Account.Code,
                Name = setup.Account.Name,
                Type = setup.Account.Type,
                IsActive = setup.Account.IsActive,
                OpenedOn = setup.Account.OpenedOn,
                ClosedOn = setup.Account.ClosedOn
            });

            _dbContext.Assets.AddRange(
                MapAsset(setup.CashAsset, setup.InitializedAtUtc),
                MapAsset(setup.FundAsset, setup.InitializedAtUtc));

            await _dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            return true;
        }
        catch (Exception exception)
            when (exception is DbUpdateException or SqliteException)
        {
            throw new CoreLedgerPersistenceException(
                "Core ledger setup could not be persisted atomically.",
                exception);
        }
    }

    private async Task<bool> HasAnyMasterDataAsync(
        CancellationToken cancellationToken)
        => await _dbContext.Currencies.AnyAsync(cancellationToken)
           || await _dbContext.Households.AnyAsync(cancellationToken)
           || await _dbContext.HouseholdMembers.AnyAsync(cancellationToken)
           || await _dbContext.Institutions.AnyAsync(cancellationToken)
           || await _dbContext.Portfolios.AnyAsync(cancellationToken)
           || await _dbContext.Accounts.AnyAsync(cancellationToken)
           || await _dbContext.Assets.AnyAsync(cancellationToken);

    private static AssetRow MapAsset(
        Asset asset,
        DateTimeOffset initializedAtUtc)
        => new()
        {
            Id = asset.Id,
            Code = asset.Code,
            Name = asset.Name,
            Type = asset.Type,
            BaseUnit = asset.BaseUnit,
            BaseCurrencyCode = asset.BaseCurrency?.Value,
            LotTrackingMode = asset.LotTrackingMode,
            IsActive = asset.IsActive,
            CreatedAtUtc = initializedAtUtc.UtcDateTime
        };

    private static void ValidateSetup(CoreLedgerSetup setup)
    {
        if (setup.Household.BaseCurrency != setup.BaseCurrency.Code)
        {
            throw new ArgumentException(
                "The household base currency must match the setup currency.",
                nameof(setup));
        }

        if (setup.HouseholdMember is not null
            && setup.HouseholdMember.HouseholdId != setup.Household.Id)
        {
            throw new ArgumentException(
                "The household member must belong to the setup household.",
                nameof(setup));
        }

        if (setup.Portfolio.HouseholdId != setup.Household.Id
            || setup.Account.HouseholdId != setup.Household.Id)
        {
            throw new ArgumentException(
                "The portfolio and account must belong to the setup household.",
                nameof(setup));
        }

        if (setup.Account.InstitutionId != setup.Institution.Id)
        {
            throw new ArgumentException(
                "The account must reference the setup institution.",
                nameof(setup));
        }

        ValidateAsset(
            setup.CashAsset,
            setup.BaseCurrency.Code,
            AssetType.Cash,
            AssetUnit.CurrencyUnit,
            LotTrackingMode.None,
            "cash");

        ValidateAsset(
            setup.FundAsset,
            setup.BaseCurrency.Code,
            AssetType.Fund,
            AssetUnit.FundUnit,
            LotTrackingMode.Required,
            "fund");

        if (setup.CashAsset.Id == setup.FundAsset.Id
            || setup.CashAsset.Code == setup.FundAsset.Code)
        {
            throw new ArgumentException(
                "The setup assets must have distinct identities and codes.",
                nameof(setup));
        }

        if (setup.Portfolio.Status != PortfolioStatus.Active
            || !setup.Account.IsActive
            || !setup.Institution.IsActive
            || !setup.CashAsset.IsActive
            || !setup.FundAsset.IsActive)
        {
            throw new ArgumentException(
                "All initial master entities must be active.",
                nameof(setup));
        }
    }

    private static void ValidateAsset(
        Asset asset,
        CurrencyCode currency,
        AssetType expectedType,
        AssetUnit expectedUnit,
        LotTrackingMode expectedLotTrackingMode,
        string label)
    {
        if (asset.Type != expectedType
            || asset.BaseUnit != expectedUnit
            || asset.LotTrackingMode != expectedLotTrackingMode
            || asset.BaseCurrency != currency)
        {
            throw new ArgumentException(
                $"The initial {label} asset has an invalid setup shape.",
                nameof(asset));
        }
    }
}
