using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using WealthLedger.Application.CoreLedger;
using WealthLedger.Domain.Ledger;
using WealthLedger.Domain.Lots;
using WealthLedger.Infrastructure.Persistence.Rows;

namespace WealthLedger.Infrastructure.Persistence;

public sealed class EfCoreLedgerPostingStore : ILedgerPostingStore, ILedgerSubmissionStore
{
    private sealed record PreparedLedgerGraph(
        LedgerTransactionRow Transaction,
        TransactionEntryRow[] Entries,
        TransactionCostComponentRow[] Costs,
        CashFlowDetailRow? CashFlow,
        AssetLotRow[] Lots,
        LotEntryAllocationRow[] Allocations,
        PhysicalGoldLotDetailRow[] PhysicalGoldDetails);

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
        var graph =
            PrepareLedgerGraph(
                transaction,
                newLots);

        try
        {
            await using var databaseTransaction =
                await _dbContext.Database
                    .BeginTransactionAsync(
                        cancellationToken);

            AddGraph(graph);

            await _dbContext.SaveChangesAsync(
                cancellationToken);

            MarkPosted(
                graph,
                transaction);

            await _dbContext.SaveChangesAsync(
                cancellationToken);

            await databaseTransaction.CommitAsync(
                cancellationToken);
        }
        catch (Exception exception)
            when (exception is
                DbUpdateException or SqliteException)
        {
            throw new CoreLedgerPersistenceException(
                "The posted ledger transaction could not be persisted atomically.",
                exception);
        }
    }

    public async Task<LedgerSubmissionReceipt?> FindReceiptAsync(
        LedgerSubmissionScope scope,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scope);

        var row =
            await _dbContext.CommandReceipts
                .AsNoTracking()
                .SingleOrDefaultAsync(
                    x =>
                        x.HouseholdId == scope.HouseholdId
                        && x.OperationCode == scope.OperationCode
                        && x.IdempotencyKey == scope.IdempotencyKey,
                    cancellationToken);

        return row is null
            ? null
            : ToReceipt(row);
    }

    public async Task<LedgerSubmissionCommitResult> TryCommitAsync(
        LedgerSubmissionReceipt receipt,
        LedgerTransaction transaction,
        IReadOnlyCollection<AssetLot> newLots,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(receipt);
        ArgumentNullException.ThrowIfNull(transaction);
        ArgumentNullException.ThrowIfNull(newLots);

        var lots =
            newLots.ToArray();

        ValidateReceipt(
            receipt,
            transaction,
            lots);

        var graph =
            PrepareLedgerGraph(
                transaction,
                lots);

        var receiptRow =
            MapReceipt(receipt);

        var writingReceipt = false;

        try
        {
            await using (
                var databaseTransaction =
                    await _dbContext.Database
                        .BeginTransactionAsync(
                            cancellationToken))
            {
                AddGraph(graph);

                await _dbContext.SaveChangesAsync(
                    cancellationToken);

                writingReceipt = true;

                _dbContext.CommandReceipts.Add(
                    receiptRow);

                await _dbContext.SaveChangesAsync(
                    cancellationToken);

                writingReceipt = false;

                MarkPosted(
                    graph,
                    transaction);

                await _dbContext.SaveChangesAsync(
                    cancellationToken);

                await databaseTransaction.CommitAsync(
                    cancellationToken);
            }

            return new LedgerSubmissionCommitResult(
                WasCommitted: true,
                Receipt: receipt);
        }
        catch (DbUpdateException exception)
            when (
                writingReceipt
                && IsUniqueConstraintViolation(
                    exception))
        {
            _dbContext.ChangeTracker.Clear();

            var winner =
                await FindReceiptAsync(
                    receipt.Scope,
                    cancellationToken);

            if (winner is not null)
            {
                return new LedgerSubmissionCommitResult(
                    WasCommitted: false,
                    Receipt: winner);
            }

            throw new CoreLedgerPersistenceException(
                "The ledger submission collided with another writer but no committed receipt could be recovered.",
                exception);
        }
        catch (Exception exception)
            when (exception is
                DbUpdateException or SqliteException)
        {
            throw new CoreLedgerPersistenceException(
                "The ledger submission could not be persisted atomically.",
                exception);
        }
    }

    private static PreparedLedgerGraph PrepareLedgerGraph(
        LedgerTransaction transaction,
        IReadOnlyCollection<AssetLot> newLots)
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

        return new PreparedLedgerGraph(
            MapDraftTransaction(transaction),

            transaction.Entries
                .Select(entry => MapEntry(transaction, entry))
                .ToArray(),

            transaction.Costs
                .Select(cost => MapCost(transaction, cost))
                .ToArray(),

            MapCashFlowDetail(transaction),

            lots
                .Select(MapLot)
                .ToArray(),

            lots
                .SelectMany(MapAllocations)
                .ToArray(),

            lots
                .Select(MapPhysicalGoldDetail)
                .Where(row => row is not null)
                .Cast<PhysicalGoldLotDetailRow>()
                .ToArray());
    }

    private void AddGraph(
        PreparedLedgerGraph graph)
    {
        _dbContext.LedgerTransactions.Add(
            graph.Transaction);

        _dbContext.TransactionEntries.AddRange(
            graph.Entries);

        _dbContext.TransactionCostComponents.AddRange(
            graph.Costs);

        if (graph.CashFlow is not null)
        {
            _dbContext.CashFlowDetails.Add(
                graph.CashFlow);
        }

        _dbContext.AssetLots.AddRange(
            graph.Lots);

        _dbContext.LotEntryAllocations.AddRange(
            graph.Allocations);

        _dbContext.PhysicalGoldLotDetails.AddRange(
            graph.PhysicalGoldDetails);
    }

    private static void ValidateReceipt(
        LedgerSubmissionReceipt receipt,
        LedgerTransaction transaction,
        IReadOnlyCollection<AssetLot> newLots)
    {
        ArgumentNullException.ThrowIfNull(receipt);

        if (receipt.Scope.HouseholdId
            != transaction.HouseholdId)
        {
            throw new ArgumentException(
                "The submission receipt household must match the ledger transaction household.",
                nameof(receipt));
        }

        if (receipt.TransactionId
            != transaction.Id)
        {
            throw new ArgumentException(
                "The submission receipt transaction ID must match the ledger transaction ID.",
                nameof(receipt));
        }

        if (receipt.AssetLotId is Guid assetLotId
            && !newLots.Any(x => x.Id == assetLotId))
        {
            throw new ArgumentException(
                "The submission receipt asset-lot result must belong to the newly persisted lot set.",
                nameof(receipt));
        }

        if (receipt.Scope.HouseholdId == Guid.Empty)
        {
            throw new ArgumentException(
                "The submission receipt household ID cannot be empty.",
                nameof(receipt));
        }

        if (string.IsNullOrWhiteSpace(
                receipt.Scope.OperationCode))
        {
            throw new ArgumentException(
                "The submission operation code is required.",
                nameof(receipt));
        }

        if (string.IsNullOrWhiteSpace(
                receipt.Scope.IdempotencyKey))
        {
            throw new ArgumentException(
                "The idempotency key is required.",
                nameof(receipt));
        }

        if (string.IsNullOrWhiteSpace(
                receipt.Fingerprint.AlgorithmCode)
            || receipt.Fingerprint.Version < 1
            || string.IsNullOrWhiteSpace(
                receipt.Fingerprint.Value))
        {
            throw new ArgumentException(
                "The submission fingerprint is invalid.",
                nameof(receipt));
        }
    }

    private static CommandReceiptRow MapReceipt(
        LedgerSubmissionReceipt receipt)
    {
        return new CommandReceiptRow
        {
            HouseholdId =
                receipt.Scope.HouseholdId,

            OperationCode =
                receipt.Scope.OperationCode,

            IdempotencyKey =
                receipt.Scope.IdempotencyKey,

            FingerprintAlgorithmCode =
                receipt.Fingerprint.AlgorithmCode,

            FingerprintVersion =
                receipt.Fingerprint.Version,

            FingerprintValue =
                receipt.Fingerprint.Value,

            ResultTransactionId =
                receipt.TransactionId,

            ResultAssetLotId =
                receipt.AssetLotId,

            CreatedAtUtc =
                receipt.CreatedAtUtc.UtcDateTime
        };
    }

    private static LedgerSubmissionReceipt ToReceipt(
        CommandReceiptRow row)
    {
        return new LedgerSubmissionReceipt(
            new LedgerSubmissionScope(
                row.HouseholdId,
                row.OperationCode,
                row.IdempotencyKey),

            new CommandFingerprint(
                row.FingerprintAlgorithmCode,
                row.FingerprintVersion,
                row.FingerprintValue),

            row.ResultTransactionId,
            row.ResultAssetLotId,

            new DateTimeOffset(
                DateTime.SpecifyKind(
                    row.CreatedAtUtc,
                    DateTimeKind.Utc)));
    }

    private static bool IsUniqueConstraintViolation(
        DbUpdateException exception)
    {
        if (exception.InnerException
            is not SqliteException sqliteException)
        {
            return false;
        }

        return sqliteException.SqliteErrorCode == 19
            && sqliteException.SqliteExtendedErrorCode
                is 1555 or 2067;
    }

    private static void MarkPosted(
        PreparedLedgerGraph graph,
        LedgerTransaction transaction)
    {
        graph.Transaction.Status =
            TransactionStatus.Posted;

        graph.Transaction.PostedAtUtc =
            transaction.PostedAtUtc!.Value.UtcDateTime;
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
