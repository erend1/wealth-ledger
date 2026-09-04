using Microsoft.EntityFrameworkCore;
using System.Data.Common;
using WealthLedger.Application.LocalData;
using WealthLedger.Application.Setup;
using WealthLedger.Domain.Assets;

namespace WealthLedger.Infrastructure.Persistence
{
    public sealed class EfCoreLedgerSetupStateReader
        : ICoreLedgerSetupStateReader
    {
        private readonly WealthLedgerDbContext _dbContext;

        public EfCoreLedgerSetupStateReader(
            WealthLedgerDbContext dbContext)
        {
            _dbContext = dbContext
                ?? throw new ArgumentNullException(nameof(dbContext));
        }

        public async Task<
            LocalDataOperationResult<CoreLedgerSetupStateSnapshot>> ReadAsync(
            CancellationToken cancellationToken = default)
        {
            try
            {
                var hasAnyMasterData =
                    await _dbContext.Currencies
                        .AsNoTracking()
                        .AnyAsync(cancellationToken)
                    || await _dbContext.Households
                        .AsNoTracking()
                        .AnyAsync(cancellationToken)
                    || await _dbContext.HouseholdMembers
                        .AsNoTracking()
                        .AnyAsync(cancellationToken)
                    || await _dbContext.Institutions
                        .AsNoTracking()
                        .AnyAsync(cancellationToken)
                    || await _dbContext.Portfolios
                        .AsNoTracking()
                        .AnyAsync(cancellationToken)
                    || await _dbContext.Accounts
                        .AsNoTracking()
                        .AnyAsync(cancellationToken)
                    || await _dbContext.Assets
                        .AsNoTracking()
                        .AnyAsync(cancellationToken);

                if (!hasAnyMasterData)
                {
                    return Success(
                        CoreLedgerSetupState.Empty);
                }

                var households =
                    await _dbContext.Households
                        .AsNoTracking()
                        .Select(x => new
                        {
                            x.Id,
                            x.BaseCurrencyCode
                        })
                        .Take(2)
                        .ToListAsync(cancellationToken);

                if (households.Count != 1)
                {
                    return Success(
                        CoreLedgerSetupState.PartialOrConflicting);
                }

                var household =
                    households[0];

                var baseCurrencyExists =
                    await _dbContext.Currencies
                        .AsNoTracking()
                        .AnyAsync(
                            x =>
                                x.Code
                                == household.BaseCurrencyCode,
                            cancellationToken);

                var institutionExists =
                    await _dbContext.Institutions
                        .AsNoTracking()
                        .AnyAsync(cancellationToken);

                var portfolioExists =
                    await _dbContext.Portfolios
                        .AsNoTracking()
                        .AnyAsync(
                            x =>
                                x.HouseholdId
                                == household.Id,
                            cancellationToken);

                var accountExists =
                    await _dbContext.Accounts
                        .AsNoTracking()
                        .AnyAsync(
                            x =>
                                x.HouseholdId
                                    == household.Id
                                && x.InstitutionId
                                    != null,
                            cancellationToken);

                var cashAssetExists =
                    await _dbContext.Assets
                        .AsNoTracking()
                        .AnyAsync(
                            x =>
                                x.Type
                                    == AssetType.Cash
                                && x.BaseUnit
                                    == AssetUnit.CurrencyUnit
                                && x.LotTrackingMode
                                    == LotTrackingMode.None
                                && x.BaseCurrencyCode
                                    == household.BaseCurrencyCode,
                            cancellationToken);

                var fundAssetExists =
                    await _dbContext.Assets
                        .AsNoTracking()
                        .AnyAsync(
                            x =>
                                x.Type
                                    == AssetType.Fund
                                && x.BaseUnit
                                    == AssetUnit.FundUnit
                                && x.LotTrackingMode
                                    == LotTrackingMode.Required
                                && x.BaseCurrencyCode
                                    == household.BaseCurrencyCode,
                            cancellationToken);

                var state =
                    baseCurrencyExists
                    && institutionExists
                    && portfolioExists
                    && accountExists
                    && cashAssetExists
                    && fundAssetExists
                        ? CoreLedgerSetupState.Complete
                        : CoreLedgerSetupState.PartialOrConflicting;

                return Success(state);
            }
            catch (OperationCanceledException)
                when (cancellationToken.IsCancellationRequested)
            {
                return LocalDataOperationResult<
                    CoreLedgerSetupStateSnapshot>.Failed(
                        LocalDataFailureCategory.Cancelled,
                        "Core ledger setup-state inspection was cancelled.");
            }
            catch (DbException)
            {
                return LocalDataOperationResult<
                    CoreLedgerSetupStateSnapshot>.Failed(
                        LocalDataFailureCategory.DatabaseNotReady,
                        "Core ledger setup state could not be read.");
            }
        }

        private static LocalDataOperationResult<
            CoreLedgerSetupStateSnapshot> Success(
            CoreLedgerSetupState state)
            => LocalDataOperationResult<
                CoreLedgerSetupStateSnapshot>.Success(
                    new CoreLedgerSetupStateSnapshot(
                        state));
    }
}
