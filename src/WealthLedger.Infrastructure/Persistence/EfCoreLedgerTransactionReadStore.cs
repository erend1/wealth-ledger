using Microsoft.EntityFrameworkCore;
using WealthLedger.Application.CoreLedger;
using WealthLedger.Domain.Ledger;
using WealthLedger.Domain.Lots;
using WealthLedger.Domain.ValueObjects;

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

            var reversedByTransactionId =
                await _dbContext.LedgerTransactions
                    .AsNoTracking()
                    .Where(
                        x =>
                            x.Type == TransactionType.Reversal
                            && x.Status == TransactionStatus.Posted
                            && x.ReversalOfTransactionId == transactionId)
                    .Select(x => (Guid?)x.Id)
                    .SingleOrDefaultAsync(
                        cancellationToken);

            var lotAllocations =
                entryIds.Length == 0
                    ? []
                    : await _dbContext.LotEntryAllocations
                        .AsNoTracking()
                        .Where(
                            x =>
                                entryIds.Contains(
                                    x.TransactionEntryId))
                        .Join(
                            _dbContext.TransactionEntries
                                .AsNoTracking(),
                            allocation =>
                                allocation.TransactionEntryId,
                            entry =>
                                entry.Id,
                            (allocation, entry) =>
                                new
                                {
                                    allocation,
                                    entry.EntrySequence
                                })
                        .OrderBy(x => x.EntrySequence)
                        .ThenBy(x => x.allocation.AssetLotId)
                        .ThenBy(x => x.allocation.Id)
                        .Select(
                            x =>
                                new LedgerTransactionLotAllocationDetail(
                                    x.allocation.Id,
                                    x.allocation.AssetLotId,
                                    x.allocation.TransactionEntryId,
                                    x.allocation.QuantityDeltaE8,
                                    ToDateTimeOffset(
                                        x.allocation.CreatedAtUtc)))
                        .ToArrayAsync(
                            cancellationToken);

            var createdLots =
                entryIds.Length == 0
                    ? []
                    : await (
                            from lot in _dbContext.AssetLots.AsNoTracking()
                            join gold in _dbContext.PhysicalGoldLotDetails
                                    .AsNoTracking()
                                on lot.Id equals gold.AssetLotId
                                into lotGoldDetails
                            from gold in lotGoldDetails.DefaultIfEmpty()
                            where entryIds.Contains(
                                lot.OpeningTransactionEntryId)
                            orderby lot.CreatedAtUtc, lot.Id
                            select new
                            {
                                Lot = lot,
                                Gold = gold
                            })
                        .ToListAsync(cancellationToken);

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
                reversedByTransactionId,
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
                                x.Lot.Id,
                                x.Lot.AssetId,
                                x.Lot.OpeningTransactionEntryId,
                                x.Lot.AcquiredOn,
                                x.Lot.OriginalCostBasisMinor,
                                x.Lot.CostBasisCurrencyCode,
                                x.Lot.CostBasisStatus,
                                ToDateTimeOffset(
                                    x.Lot.CreatedAtUtc),
                                MapPhysicalGoldDetail(
                                    x.Lot.Id,
                                    x.Lot.OpeningTransactionEntryId,
                                    x.Gold,
                                    lotAllocations)))
                    .ToArray(),

                lotAllocations);
        }

        private static LedgerTransactionPhysicalGoldDetail?
            MapPhysicalGoldDetail(
                Guid assetLotId,
                Guid openingTransactionEntryId,
                Rows.PhysicalGoldLotDetailRow? row,
                IReadOnlyList<LedgerTransactionLotAllocationDetail> allocations)
        {
            if (row is null)
            {
                return null;
            }

            var openingAllocation = allocations.SingleOrDefault(
                allocation => allocation.AssetLotId == assetLotId
                    && allocation.TransactionEntryId
                        == openingTransactionEntryId);

            if (openingAllocation is null
                || openingAllocation.QuantityDeltaRawE8 <= 0)
            {
                throw new CoreLedgerPersistenceException(
                    "A physical-gold lot has no positive opening allocation.");
            }

            var detail = new PhysicalGoldLotDetail(
                new Fineness(row.ActualFinenessPpm),
                row.PieceCount,
                row.Hallmark,
                row.CertificateReference,
                row.Note);

            return new LedgerTransactionPhysicalGoldDetail(
                detail.Fineness.Ppm,
                detail.PieceCount,
                detail.Hallmark,
                detail.CertificateReference,
                detail.Note,
                detail.CalculateFineWeightGrams(
                    Quantity.FromRaw(
                        openingAllocation.QuantityDeltaRawE8)));
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
