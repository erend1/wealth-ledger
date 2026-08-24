using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using WealthLedger.Application.CoreLedger;
using WealthLedger.Domain.Ledger;
using WealthLedger.Domain.Lots;
using WealthLedger.Infrastructure.Persistence.Rows;

namespace WealthLedger.Infrastructure.Persistence;

public sealed class EfCoreLedgerPostingStore : ILedgerPostingStore
{
    private readonly WealthLedgerDbContext _dbContext;

    public EfCoreLedgerPostingStore(WealthLedgerDbContext dbContext)
    {
        _dbContext = dbContext
            ?? throw new ArgumentNullException(nameof(dbContext));
    }

    public async Task SavePostedTransactionAsync(
        LedgerTransaction transaction,
        IReadOnlyCollection<AssetLot> newLots,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(transaction);
        ArgumentNullException.ThrowIfNull(newLots);

        if (transaction.Status != TransactionStatus.Posted
            || transaction.PostedAtUtc is null)
        {
            throw new ArgumentException(
                "Only a Domain-validated posted transaction can be persisted.",
                nameof(transaction));
        }

        var lots = newLots.ToArray();
        ValidateNewLots(transaction, lots);

        var transactionRow = MapDraftTransaction(transaction);
        var entryRows = transaction.Entries
            .Select(entry => MapEntry(transaction, entry))
            .ToArray();
        var costRows = transaction.Costs
            .Select(cost => MapCost(transaction, cost))
            .ToArray();
        var cashFlowRow = MapCashFlowDetail(transaction);
        var lotRows = lots
            .Select(MapLot)
            .ToArray();
        var allocationRows = lots
            .SelectMany(MapAllocations)
            .ToArray();
        var physicalGoldRows = lots
            .Select(MapPhysicalGoldDetail)
            .Where(row => row is not null)
            .Cast<PhysicalGoldLotDetailRow>()
            .ToArray();

        try
        {
            await using var databaseTransaction =
                await _dbContext.Database.BeginTransactionAsync(
                    cancellationToken);

            _dbContext.LedgerTransactions.Add(transactionRow);
            _dbContext.TransactionEntries.AddRange(entryRows);
            _dbContext.TransactionCostComponents.AddRange(costRows);

            if (cashFlowRow is not null)
            {
                _dbContext.CashFlowDetails.Add(cashFlowRow);
            }

            _dbContext.AssetLots.AddRange(lotRows);
            _dbContext.LotEntryAllocations.AddRange(allocationRows);
            _dbContext.PhysicalGoldLotDetails.AddRange(physicalGoldRows);

            await _dbContext.SaveChangesAsync(cancellationToken);

            transactionRow.Status = TransactionStatus.Posted;
            transactionRow.PostedAtUtc =
                transaction.PostedAtUtc.Value.UtcDateTime;

            await _dbContext.SaveChangesAsync(cancellationToken);
            await databaseTransaction.CommitAsync(cancellationToken);
        }
        catch (Exception exception)
            when (exception is DbUpdateException or SqliteException)
        {
            throw new CoreLedgerPersistenceException(
                "The posted ledger transaction could not be persisted atomically.",
                exception);
        }
    }

    private static void ValidateNewLots(
        LedgerTransaction transaction,
        IReadOnlyCollection<AssetLot> lots)
    {
        var entryIds = transaction.Entries
            .Select(x => x.Id)
            .ToHashSet();

        if (lots.Select(x => x.Id).Distinct().Count() != lots.Count)
        {
            throw new ArgumentException(
                "New asset lots must have unique IDs.",
                nameof(lots));
        }

        foreach (var lot in lots)
        {
            if (!entryIds.Contains(lot.OpeningTransactionEntryId))
            {
                throw new ArgumentException(
                    "Every new lot must be opened by an entry in the transaction being saved.",
                    nameof(lots));
            }

            if (lot.Allocations.Any(x =>
                    !entryIds.Contains(x.TransactionEntryId)))
            {
                throw new ArgumentException(
                    "Every allocation of a new lot must reference the transaction being saved.",
                    nameof(lots));
            }
        }
    }

    private static LedgerTransactionRow MapDraftTransaction(
        LedgerTransaction transaction)
        => new()
        {
            Id = transaction.Id,
            HouseholdId = transaction.HouseholdId,
            Type = transaction.Type,
            Status = TransactionStatus.Draft,
            OrderDate = transaction.OrderDate,
            ExecutionDate = transaction.ExecutionDate,
            SettlementDate = transaction.SettlementDate,
            ExternalReference = transaction.ExternalReference,
            Note = transaction.Note,
            ReversalOfTransactionId = transaction.ReversalOfTransactionId,
            CreatedAtUtc = transaction.CreatedAtUtc.UtcDateTime,
            PostedAtUtc = null
        };

    private static TransactionEntryRow MapEntry(
        LedgerTransaction transaction,
        TransactionEntry entry)
        => new()
        {
            Id = entry.Id,
            TransactionId = transaction.Id,
            EntrySequence = entry.Sequence,
            PortfolioId = entry.PortfolioId,
            AccountId = entry.AccountId,
            AssetId = entry.AssetId,
            QuantityDeltaE8 = entry.QuantityDelta.RawE8,
            Role = entry.Role,
            UnitPriceE8 = entry.UnitPrice?.RawE8,
            PriceCurrencyCode = entry.UnitPrice?.Currency.Value,
            CreatedAtUtc = transaction.CreatedAtUtc.UtcDateTime
        };

    private static TransactionCostComponentRow MapCost(
        LedgerTransaction transaction,
        TransactionCostComponent cost)
        => new()
        {
            Id = cost.Id,
            TransactionId = transaction.Id,
            Type = cost.Type,
            Treatment = cost.Treatment,
            AmountMinor = cost.Amount.MinorUnits,
            CurrencyCode = cost.Amount.Currency.Value,
            Note = cost.Note
        };

    private static CashFlowDetailRow? MapCashFlowDetail(
        LedgerTransaction transaction)
        => transaction.CashFlowDetail is null
            ? null
            : new CashFlowDetailRow
            {
                TransactionId = transaction.Id,
                Category = transaction.CashFlowDetail.Category,
                HouseholdMemberId =
                    transaction.CashFlowDetail.HouseholdMemberId
            };

    private static AssetLotRow MapLot(AssetLot lot)
        => new()
        {
            Id = lot.Id,
            AssetId = lot.AssetId,
            OpeningTransactionEntryId = lot.OpeningTransactionEntryId,
            AcquiredOn = lot.AcquiredOn,
            OriginalCostBasisMinor = lot.CostBasis.Amount?.MinorUnits,
            CostBasisCurrencyCode = lot.CostBasis.Amount?.Currency.Value,
            CostBasisStatus = lot.CostBasis.Status,
            CreatedAtUtc = lot.CreatedAtUtc.UtcDateTime
        };

    private static IEnumerable<LotEntryAllocationRow> MapAllocations(
        AssetLot lot)
        => lot.Allocations.Select(allocation => new LotEntryAllocationRow
        {
            Id = allocation.Id,
            AssetLotId = allocation.AssetLotId,
            TransactionEntryId = allocation.TransactionEntryId,
            QuantityDeltaE8 = allocation.QuantityDelta.RawE8,
            CreatedAtUtc = lot.CreatedAtUtc.UtcDateTime
        });

    private static PhysicalGoldLotDetailRow? MapPhysicalGoldDetail(
        AssetLot lot)
        => lot.PhysicalGoldDetail is null
            ? null
            : new PhysicalGoldLotDetailRow
            {
                AssetLotId = lot.Id,
                ActualFinenessPpm = lot.PhysicalGoldDetail.Fineness.Ppm,
                PieceCount = lot.PhysicalGoldDetail.PieceCount,
                Hallmark = lot.PhysicalGoldDetail.Hallmark,
                CertificateReference =
                    lot.PhysicalGoldDetail.CertificateReference,
                Note = lot.PhysicalGoldDetail.Note
            };
}
