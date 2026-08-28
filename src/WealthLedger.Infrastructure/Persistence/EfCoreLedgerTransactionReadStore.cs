using Microsoft.EntityFrameworkCore;
using WealthLedger.Application.CoreLedger;

namespace WealthLedger.Infrastructure.Persistence
{
    public sealed class EfCoreLedgerTransactionReadStore
        : ILedgerTransactionReadStore
    {
        private readonly WealthLedgerDbContext _dbContext;

        public EfCoreLedgerTransactionReadStore(
            WealthLedgerDbContext dbContext)
        {
            _dbContext = dbContext
                ?? throw new ArgumentNullException(
                    nameof(dbContext));
        }

        public async Task<LedgerTransactionDetail?>
            FindByIdAsync(
                Guid transactionId,
                CancellationToken cancellationToken = default)
        {
            var transaction =
                await _dbContext.LedgerTransactions
                    .AsNoTracking()
                    .SingleOrDefaultAsync(
                        x => x.Id == transactionId,
                        cancellationToken);

            if (transaction is null)
            {
                return null;
            }

            var entries =
                await _dbContext.TransactionEntries
                    .AsNoTracking()
                    .Where(
                        x => x.TransactionId
                            == transactionId)
                    .OrderBy(x => x.EntrySequence)
                    .ToListAsync(cancellationToken);

            var cashFlow =
                await _dbContext.CashFlowDetails
                    .AsNoTracking()
                    .SingleOrDefaultAsync(
                        x => x.TransactionId
                            == transactionId,
                        cancellationToken);

            var costs =
                await _dbContext
                    .TransactionCostComponents
                    .AsNoTracking()
                    .Where(
                        x => x.TransactionId
                            == transactionId)
                    .OrderBy(x => x.Type)
                    .ThenBy(x => x.Id)
                    .ToListAsync(cancellationToken);

            var entryIds =
                entries
                    .Select(x => x.Id)
                    .ToArray();

            var createdLots =
                entryIds.Length == 0
                    ? []
                    : await _dbContext.AssetLots
                        .AsNoTracking()
                        .Where(
                            x => entryIds.Contains(
                                x.OpeningTransactionEntryId))
                        .OrderBy(x => x.CreatedAtUtc)
                        .ThenBy(x => x.Id)
                        .ToListAsync(
                            cancellationToken);

            return new LedgerTransactionDetail(
                transaction.Id,
                transaction.HouseholdId,
                transaction.Type,
                transaction.Status,
                transaction.OrderDate,
                transaction.ExecutionDate,
                transaction.SettlementDate,
                transaction.ExternalReference,
                transaction.Note,
                transaction.ReversalOfTransactionId,
                ToDateTimeOffset(
                    transaction.CreatedAtUtc),
                transaction.PostedAtUtc is null
                    ? null
                    : ToDateTimeOffset(
                        transaction.PostedAtUtc.Value),

                entries
                    .Select(
                        x =>
                            new LedgerTransactionEntryDetail(
                                x.Id,
                                x.EntrySequence,
                                x.PortfolioId,
                                x.AccountId,
                                x.AssetId,
                                x.QuantityDeltaE8,
                                x.Role,
                                x.UnitPriceE8,
                                x.PriceCurrencyCode,
                                ToDateTimeOffset(
                                    x.CreatedAtUtc)))
                    .ToArray(),

                cashFlow is null
                    ? null
                    : new LedgerTransactionCashFlowDetail(
                        cashFlow.Category,
                        cashFlow.HouseholdMemberId),

                costs
                    .Select(
                        x =>
                            new LedgerTransactionCostDetail(
                                x.Id,
                                x.Type,
                                x.Treatment,
                                x.AmountMinor,
                                x.CurrencyCode,
                                x.Note))
                    .ToArray(),

                createdLots
                    .Select(
                        x =>
                            new LedgerTransactionCreatedLotDetail(
                                x.Id,
                                x.AssetId,
                                x.OpeningTransactionEntryId,
                                x.AcquiredOn,
                                x.OriginalCostBasisMinor,
                                x.CostBasisCurrencyCode,
                                x.CostBasisStatus,
                                ToDateTimeOffset(
                                    x.CreatedAtUtc)))
                    .ToArray());
        }

        private static DateTimeOffset ToDateTimeOffset(
            DateTime value)
            => new(
                value.Kind == DateTimeKind.Utc
                    ? value
                    : DateTime.SpecifyKind(
                        value,
                        DateTimeKind.Utc));
    }
}
