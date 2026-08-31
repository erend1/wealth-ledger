using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using WealthLedger.Application.CoreLedger;
using WealthLedger.Domain.Common;
using WealthLedger.Domain.Ledger;
using WealthLedger.Domain.Lots;
using WealthLedger.Domain.ValueObjects;
using WealthLedger.Infrastructure.Persistence.Rows;

namespace WealthLedger.Infrastructure.Persistence
{
    public sealed class EfCoreLedgerReversalStore
        : ILedgerReversalStore
    {
        private enum CommitStage
        {
            Graph,
            Receipt,
            Posting
        }

        private readonly WealthLedgerDbContext _dbContext;

        public EfCoreLedgerReversalStore(
            WealthLedgerDbContext dbContext)
        {
            _dbContext =
                dbContext
                ?? throw new ArgumentNullException(
                    nameof(dbContext));
        }

        public async Task<ReversalTargetIdentity?>
            FindTargetIdentityAsync(
                Guid transactionId,
                CancellationToken cancellationToken = default)
        {
            if (transactionId == Guid.Empty)
            {
                throw new ArgumentException(
                    "Transaction ID cannot be empty.",
                    nameof(transactionId));
            }

            return await _dbContext.LedgerTransactions
                .AsNoTracking()
                .Where(x => x.Id == transactionId)
                .Select(
                    x =>
                        new ReversalTargetIdentity(
                            x.Id,
                            x.HouseholdId))
                .SingleOrDefaultAsync(
                    cancellationToken);
        }

        public async Task<LedgerSubmissionReceipt?>
            FindReceiptAsync(
                LedgerSubmissionScope scope,
                CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(scope);

            var row =
                await _dbContext.CommandReceipts
                    .AsNoTracking()
                    .SingleOrDefaultAsync(
                        x =>
                            x.HouseholdId
                                == scope.HouseholdId
                            && x.OperationCode
                                == scope.OperationCode
                            && x.IdempotencyKey
                                == scope.IdempotencyKey,
                        cancellationToken);

            return row is null
                ? null
                : ToReceipt(row);
        }

        public async Task<ReversalCandidate?>
            LoadCandidateAsync(
                Guid transactionId,
                CancellationToken cancellationToken = default)
        {
            if (transactionId == Guid.Empty)
            {
                throw new ArgumentException(
                    "Transaction ID cannot be empty.",
                    nameof(transactionId));
            }

            var transactionRow =
                await _dbContext.LedgerTransactions
                    .AsNoTracking()
                    .SingleOrDefaultAsync(
                        x => x.Id == transactionId,
                        cancellationToken);

            if (transactionRow is null)
            {
                return null;
            }

            var existingReversalId =
                await FindPostedReversalIdAsync(
                    transactionId,
                    cancellationToken);

            var blockers =
                await FindBlockingTransactionIdsAsync(
                    transactionId,
                    cancellationToken);

            if (transactionRow.Status
                    != TransactionStatus.Posted
                || transactionRow.Type
                    == TransactionType.Reversal
                || existingReversalId is not null)
            {
                return new ReversalCandidate(
                    transactionRow.Id,
                    transactionRow.HouseholdId,
                    transactionRow.Status,
                    transactionRow.Type,
                    existingReversalId,
                    blockers,
                    Original: null,
                    AffectedLots: []);
            }

            try
            {
                var entries =
                    await _dbContext.TransactionEntries
                        .AsNoTracking()
                        .Where(
                            x =>
                                x.TransactionId
                                == transactionId)
                        .OrderBy(
                            x => x.EntrySequence)
                        .ToArrayAsync(
                            cancellationToken);

                var costs =
                    await _dbContext
                        .TransactionCostComponents
                        .AsNoTracking()
                        .Where(
                            x =>
                                x.TransactionId
                                == transactionId)
                        .OrderBy(x => x.Id)
                        .ToArrayAsync(
                            cancellationToken);

                var cashFlow =
                    await _dbContext.CashFlowDetails
                        .AsNoTracking()
                        .SingleOrDefaultAsync(
                            x =>
                                x.TransactionId
                                == transactionId,
                            cancellationToken);

                var original =
                    ReconstituteTransaction(
                        transactionRow,
                        entries,
                        costs,
                        cashFlow);

                var affectedLots =
                    await LoadAffectedLotsAsync(
                        entries,
                        cancellationToken);

                // Candidate loading spans several SQLite reads.
                // Another writer may have committed the unique posted
                // reversal after our initial reversal lookup but before
                // the transaction/lot graph finished loading.
                //
                // Reconcile once before exposing the candidate. If a
                // winner appeared, ALREADY_REVERSED takes precedence
                // over any pre-winner candidate state.
                var concurrentReversalId =
                    await FindPostedReversalIdAsync(
                        transactionId,
                        cancellationToken);

                if (concurrentReversalId is Guid winnerId)
                {
                    return new ReversalCandidate(
                        transactionRow.Id,
                        transactionRow.HouseholdId,
                        transactionRow.Status,
                        transactionRow.Type,
                        winnerId,
                        blockers,
                        Original: null,
                        AffectedLots: []);
                }

                return new ReversalCandidate(
                    transactionRow.Id,
                    transactionRow.HouseholdId,
                    transactionRow.Status,
                    transactionRow.Type,
                    existingReversalId,
                    blockers,
                    original,
                    affectedLots);
            }
            catch (Exception exception)
                when (IsUnsupportedPersistedShape(
                    exception))
            {
                // A concurrent reversal may have committed while
                // this multi-query candidate was being reconstructed.
                //
                // In that case the correct externally visible state
                // is ALREADY_REVERSED, not UNSUPPORTED_PERSISTED_SHAPE.
                var concurrentReversalId =
                    await FindPostedReversalIdAsync(
                        transactionId,
                        cancellationToken);

                if (concurrentReversalId is Guid winnerId)
                {
                    return new ReversalCandidate(
                        transactionRow.Id,
                        transactionRow.HouseholdId,
                        transactionRow.Status,
                        transactionRow.Type,
                        winnerId,
                        blockers,
                        Original: null,
                        AffectedLots: []);
                }

                return new ReversalCandidate(
                    transactionRow.Id,
                    transactionRow.HouseholdId,
                    transactionRow.Status,
                    transactionRow.Type,
                    ExistingReversalTransactionId: null,
                    blockers,
                    Original: null,
                    AffectedLots: []);
            }
        }

        public async Task<ReversalCommitResult>
            TryCommitAsync(
                LedgerSubmissionReceipt receipt,
                LedgerTransaction reversal,
                IReadOnlyCollection<AssetLot> affectedLots,
                CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(receipt);
            ArgumentNullException.ThrowIfNull(reversal);
            ArgumentNullException.ThrowIfNull(affectedLots);

            ValidateCommit(
                receipt,
                reversal,
                affectedLots);

            var transactionRow =
                MapDraftTransaction(
                    reversal);

            var entryRows =
                reversal.Entries
                    .OrderBy(x => x.Sequence)
                    .Select(
                        x =>
                            MapEntry(
                                reversal,
                                x))
                    .ToArray();

            var reversalEntryIds =
                reversal.Entries
                    .Select(x => x.Id)
                    .ToHashSet();

            var allocationRows =
                affectedLots
                    .SelectMany(
                        lot =>
                            lot.Allocations
                                .Where(
                                    allocation =>
                                        reversalEntryIds.Contains(
                                            allocation
                                                .TransactionEntryId))
                                .Select(
                                    allocation =>
                                        MapReversalAllocation(
                                            reversal,
                                            lot,
                                            allocation)))
                    .ToArray();

            var receiptRow =
                MapReceipt(receipt);

            var stage =
                CommitStage.Graph;

            try
            {
                await using (
                    var databaseTransaction =
                        await _dbContext.Database
                            .BeginTransactionAsync(
                                cancellationToken))
                {
                    _dbContext.LedgerTransactions.Add(
                        transactionRow);

                    _dbContext.TransactionEntries.AddRange(
                        entryRows);

                    _dbContext.LotEntryAllocations.AddRange(
                        allocationRows);

                    await _dbContext.SaveChangesAsync(
                        cancellationToken);

                    stage =
                        CommitStage.Receipt;

                    _dbContext.CommandReceipts.Add(
                        receiptRow);

                    await _dbContext.SaveChangesAsync(
                        cancellationToken);

                    stage =
                        CommitStage.Posting;

                    transactionRow.Status =
                        TransactionStatus.Posted;

                    transactionRow.PostedAtUtc =
                        reversal.PostedAtUtc!
                            .Value
                            .UtcDateTime;

                    await _dbContext.SaveChangesAsync(
                        cancellationToken);

                    await databaseTransaction.CommitAsync(
                        cancellationToken);
                }

                return new ReversalCommitResult.Committed(
                    receipt);
            }
            catch (DbUpdateException exception)
                when (
                    stage == CommitStage.Receipt
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
                    return new ReversalCommitResult
                        .ReceiptWinner(
                            winner);
                }

                throw new CoreLedgerPersistenceException(
                    "The reversal submission collided with another receipt writer but no committed receipt could be recovered.",
                    exception);
            }
            catch (DbUpdateException exception)
                when (
                    stage == CommitStage.Graph
                    && IsUniqueConstraintViolation(
                        exception))
            {
                _dbContext.ChangeTracker.Clear();

                // The graph collision may have been caused by an equivalent
                // writer using the same idempotency scope.
                //
                // Recover the winning receipt first. Application will validate
                // its fingerprint, so:
                //
                // same key + equivalent command
                //     => successful replay
                //
                // same key + different command
                //     => IDEMPOTENCY_KEY_CONFLICT
                //
                // different key
                //     => no receipt in this scope, then fall through to
                //        ALREADY_REVERSED recovery.
                var receiptWinner =
                    await FindReceiptAsync(
                        receipt.Scope,
                        cancellationToken);

                if (receiptWinner is not null)
                {
                    return new ReversalCommitResult
                        .ReceiptWinner(
                            receiptWinner);
                }

                var reversalWinner =
                    await FindPostedReversalIdAsync(
                        reversal
                            .ReversalOfTransactionId!
                            .Value,
                        cancellationToken);

                if (reversalWinner
                    is Guid reversalWinnerId)
                {
                    return new ReversalCommitResult
                        .AlreadyReversed(
                            reversalWinnerId);
                }

                throw new CoreLedgerPersistenceException(
                    "The reversal collided with another writer but no committed receipt or reversal could be recovered.",
                    exception);
            }
            catch (Exception exception)
                when (
                    stage is CommitStage.Graph
                        or CommitStage.Posting
                    && IsPotentialDependencyConflict(
                        exception))
            {
                _dbContext.ChangeTracker.Clear();

                var blockers =
                    await FindBlockingTransactionIdsAsync(
                        reversal
                            .ReversalOfTransactionId!
                            .Value,
                        cancellationToken);

                if (blockers.Count != 0)
                {
                    return new ReversalCommitResult
                        .DependencyConflict(
                            blockers);
                }

                throw new CoreLedgerPersistenceException(
                    "The reversal failed a persistence dependency constraint but no blocking transaction could be recovered.",
                    exception);
            }
            catch (Exception exception)
                when (exception is
                    DbUpdateException
                    or SqliteException)
            {
                throw new CoreLedgerPersistenceException(
                    "The reversal could not be persisted atomically.",
                    exception);
            }
        }

        private async Task<Guid?>
            FindPostedReversalIdAsync(
                Guid originalTransactionId,
                CancellationToken cancellationToken)
        {
            return await _dbContext.LedgerTransactions
                .AsNoTracking()
                .Where(
                    x =>
                        x.Type
                            == TransactionType.Reversal
                        && x.Status
                            == TransactionStatus.Posted
                        && x.ReversalOfTransactionId
                            == originalTransactionId)
                .Select(x => (Guid?)x.Id)
                .SingleOrDefaultAsync(
                    cancellationToken);
        }

        private async Task<IReadOnlyList<Guid>>
            FindBlockingTransactionIdsAsync(
                Guid originalTransactionId,
                CancellationToken cancellationToken)
        {
            var originalEntryIds =
                await _dbContext.TransactionEntries
                    .AsNoTracking()
                    .Where(
                        x =>
                            x.TransactionId
                            == originalTransactionId)
                    .Select(x => x.Id)
                    .ToArrayAsync(
                        cancellationToken);

            if (originalEntryIds.Length == 0)
            {
                return [];
            }

            var openedLotIds =
                await _dbContext.AssetLots
                    .AsNoTracking()
                    .Where(
                        x =>
                            originalEntryIds.Contains(
                                x.OpeningTransactionEntryId))
                    .Select(x => x.Id)
                    .ToArrayAsync(
                        cancellationToken);

            if (openedLotIds.Length == 0)
            {
                return [];
            }

            var blockerIds =
                await (
                    from allocation
                        in _dbContext.LotEntryAllocations
                            .AsNoTracking()
                    join entry
                        in _dbContext.TransactionEntries
                            .AsNoTracking()
                        on allocation.TransactionEntryId
                        equals entry.Id
                    join transaction
                        in _dbContext.LedgerTransactions
                            .AsNoTracking()
                        on entry.TransactionId
                        equals transaction.Id
                    where
                        openedLotIds.Contains(
                            allocation.AssetLotId)
                        && transaction.Status
                            == TransactionStatus.Posted
                        && transaction.Type
                            != TransactionType.Reversal
                        && transaction.Id
                            != originalTransactionId
                        && !_dbContext.LedgerTransactions
                            .Any(
                                reversal =>
                                    reversal.Type
                                        == TransactionType.Reversal
                                    && reversal.Status
                                        == TransactionStatus.Posted
                                    && reversal
                                        .ReversalOfTransactionId
                                        == transaction.Id)
                    select transaction.Id
                )
                .Distinct()
                .OrderBy(x => x)
                .ToArrayAsync(
                    cancellationToken);

            return blockerIds;
        }

        private async Task<IReadOnlyCollection<AssetLot>>
            LoadAffectedLotsAsync(
                IReadOnlyCollection<TransactionEntryRow>
                    originalEntries,
                CancellationToken cancellationToken)
        {
            var entryIds =
                originalEntries
                    .Select(x => x.Id)
                    .ToArray();

            if (entryIds.Length == 0)
            {
                return [];
            }

            var allocatedLotIds =
                await _dbContext.LotEntryAllocations
                    .AsNoTracking()
                    .Where(
                        x =>
                            entryIds.Contains(
                                x.TransactionEntryId))
                    .Select(x => x.AssetLotId)
                    .Distinct()
                    .ToArrayAsync(
                        cancellationToken);

            var openedLotIds =
                await _dbContext.AssetLots
                    .AsNoTracking()
                    .Where(
                        x =>
                            entryIds.Contains(
                                x.OpeningTransactionEntryId))
                    .Select(x => x.Id)
                    .ToArrayAsync(
                        cancellationToken);

            var affectedLotIds =
                allocatedLotIds
                    .Concat(openedLotIds)
                    .Distinct()
                    .OrderBy(x => x)
                    .ToArray();

            if (affectedLotIds.Length == 0)
            {
                return [];
            }

            var lotRows =
                await _dbContext.AssetLots
                    .AsNoTracking()
                    .Where(
                        x =>
                            affectedLotIds.Contains(
                                x.Id))
                    .OrderBy(x => x.Id)
                    .ToArrayAsync(
                        cancellationToken);

            var allocationRows =
                await _dbContext.LotEntryAllocations
                    .AsNoTracking()
                    .Where(
                        x =>
                            affectedLotIds.Contains(
                                x.AssetLotId))
                    .OrderBy(x => x.AssetLotId)
                    .ThenBy(x => x.Id)
                    .ToArrayAsync(
                        cancellationToken);

            var goldRows =
                await _dbContext.PhysicalGoldLotDetails
                    .AsNoTracking()
                    .Where(
                        x =>
                            affectedLotIds.Contains(
                                x.AssetLotId))
                    .ToArrayAsync(
                        cancellationToken);

            var goldByLot =
                goldRows.ToDictionary(
                    x => x.AssetLotId);

            var allocationsByLot =
                allocationRows
                    .GroupBy(x => x.AssetLotId)
                    .ToDictionary(
                        x => x.Key,
                        x => x.ToArray());

            return lotRows
                .Select(
                    row =>
                        ReconstituteLot(
                            row,
                            allocationsByLot
                                .GetValueOrDefault(
                                    row.Id,
                                    []),
                            goldByLot
                                .GetValueOrDefault(
                                    row.Id)))
                .ToArray();
        }

        private static LedgerTransaction
            ReconstituteTransaction(
                LedgerTransactionRow transaction,
                IReadOnlyCollection<TransactionEntryRow>
                    entries,
                IReadOnlyCollection<TransactionCostComponentRow>
                    costs,
                CashFlowDetailRow? cashFlow)
        {
            if (transaction.PostedAtUtc is null)
            {
                throw new InvalidOperationException(
                    "A persisted posted transaction must contain PostedAtUtc.");
            }

            return LedgerTransaction.ReconstitutePosted(
                transaction.Id,
                transaction.HouseholdId,
                transaction.Type,
                ToDateTimeOffset(
                    transaction.CreatedAtUtc),
                ToDateTimeOffset(
                    transaction.PostedAtUtc.Value),
                transaction.OrderDate,
                transaction.ExecutionDate,
                transaction.SettlementDate,
                transaction.ReversalOfTransactionId,
                transaction.ExternalReference,
                transaction.Note,
                entries
                    .OrderBy(
                        x => x.EntrySequence)
                    .Select(
                        x =>
                            new LedgerTransactionEntrySnapshot(
                                x.Id,
                                x.EntrySequence,
                                x.PortfolioId,
                                x.AccountId,
                                x.AssetId,
                                QuantityDelta.FromRaw(
                                    x.QuantityDeltaE8),
                                x.Role,
                                ToUnitPrice(x)))
                    .ToArray(),
                costs
                    .Select(
                        x =>
                            new LedgerTransactionCostSnapshot(
                                x.Id,
                                x.Type,
                                x.Treatment,
                                Money.FromMinorUnits(
                                    x.AmountMinor,
                                    new CurrencyCode(
                                        x.CurrencyCode)),
                                x.Note))
                    .ToArray(),
                cashFlow is null
                    ? null
                    : new LedgerCashFlowSnapshot(
                        cashFlow.Category,
                        cashFlow.HouseholdMemberId));
        }

        private static AssetLot ReconstituteLot(
            AssetLotRow lot,
            IReadOnlyCollection<LotEntryAllocationRow>
                allocations,
            PhysicalGoldLotDetailRow? gold)
        {
            return AssetLot.Reconstitute(
                lot.Id,
                lot.AssetId,
                lot.OpeningTransactionEntryId,
                lot.AcquiredOn,
                ToCostBasis(lot),
                gold is null
                    ? null
                    : new PhysicalGoldLotDetail(
                        new Fineness(
                            gold.ActualFinenessPpm),
                        gold.PieceCount,
                        gold.Hallmark,
                        gold.CertificateReference,
                        gold.Note),
                ToDateTimeOffset(
                    lot.CreatedAtUtc),
                allocations
                    .Select(
                        x =>
                            new AssetLotAllocationSnapshot(
                                x.Id,
                                x.TransactionEntryId,
                                QuantityDelta.FromRaw(
                                    x.QuantityDeltaE8)))
                    .ToArray());
        }

        private static CostBasis ToCostBasis(
            AssetLotRow row)
        {
            return row.CostBasisStatus switch
            {
                CostBasisStatus.Known
                    when row.OriginalCostBasisMinor
                            is long amount
                        && row.CostBasisCurrencyCode
                            is string currency =>
                    CostBasis.Known(
                        Money.FromMinorUnits(
                            amount,
                            new CurrencyCode(
                                currency))),

                CostBasisStatus.Unknown
                    when row.OriginalCostBasisMinor is null
                        && row.CostBasisCurrencyCode is null =>
                    CostBasis.Unknown(),

                CostBasisStatus.NotApplicable
                    when row.OriginalCostBasisMinor is null
                        && row.CostBasisCurrencyCode is null =>
                    CostBasis.NotApplicable(),

                _ =>
                    throw new InvalidOperationException(
                        "Persisted cost-basis columns are inconsistent.")
            };
        }

        private static UnitPrice? ToUnitPrice(
            TransactionEntryRow row)
        {
            if (row.UnitPriceE8 is null
                && row.PriceCurrencyCode is null)
            {
                return null;
            }

            if (row.UnitPriceE8 is null
                || row.PriceCurrencyCode is null)
            {
                throw new InvalidOperationException(
                    "Persisted unit-price columns are inconsistent.");
            }

            return UnitPrice.FromRaw(
                row.UnitPriceE8.Value,
                new CurrencyCode(
                    row.PriceCurrencyCode));
        }

        private static void ValidateCommit(
            LedgerSubmissionReceipt receipt,
            LedgerTransaction reversal,
            IReadOnlyCollection<AssetLot> affectedLots)
        {
            if (reversal.Type
                != TransactionType.Reversal
                || reversal.Status
                != TransactionStatus.Posted
                || reversal.PostedAtUtc is null
                || reversal.ReversalOfTransactionId is null)
            {
                throw new ArgumentException(
                    "Only a Domain-validated posted reversal can be committed.",
                    nameof(reversal));
            }

            if (receipt.Scope.HouseholdId
                != reversal.HouseholdId)
            {
                throw new ArgumentException(
                    "The receipt household must match the reversal household.",
                    nameof(receipt));
            }

            if (receipt.Scope.OperationCode
                != LedgerOperationCodes
                    .ReversePostedTransaction)
            {
                throw new ArgumentException(
                    "The receipt operation must be the reversal operation.",
                    nameof(receipt));
            }

            if (receipt.TransactionId
                != reversal.Id)
            {
                throw new ArgumentException(
                    "The receipt transaction ID must match the reversal transaction ID.",
                    nameof(receipt));
            }

            if (receipt.AssetLotId is not null)
            {
                throw new ArgumentException(
                    "A reversal receipt cannot contain an asset-lot result.",
                    nameof(receipt));
            }

            if (affectedLots
                .Select(x => x.Id)
                .Distinct()
                .Count()
                != affectedLots.Count)
            {
                throw new ArgumentException(
                    "Affected reversal lots must have unique IDs.",
                    nameof(affectedLots));
            }
        }

        private static LedgerTransactionRow
            MapDraftTransaction(
                LedgerTransaction reversal)
        {
            return new LedgerTransactionRow
            {
                Id =
                    reversal.Id,

                HouseholdId =
                    reversal.HouseholdId,

                Type =
                    reversal.Type,

                Status =
                    TransactionStatus.Draft,

                OrderDate =
                    reversal.OrderDate,

                ExecutionDate =
                    reversal.ExecutionDate,

                SettlementDate =
                    reversal.SettlementDate,

                ExternalReference =
                    reversal.ExternalReference,

                Note =
                    reversal.Note,

                ReversalOfTransactionId =
                    reversal.ReversalOfTransactionId,

                CreatedAtUtc =
                    reversal.CreatedAtUtc.UtcDateTime,

                PostedAtUtc =
                    null
            };
        }

        private static TransactionEntryRow MapEntry(
            LedgerTransaction reversal,
            TransactionEntry entry)
        {
            return new TransactionEntryRow
            {
                Id =
                    entry.Id,

                TransactionId =
                    reversal.Id,

                EntrySequence =
                    entry.Sequence,

                PortfolioId =
                    entry.PortfolioId,

                AccountId =
                    entry.AccountId,

                AssetId =
                    entry.AssetId,

                QuantityDeltaE8 =
                    entry.QuantityDelta.RawE8,

                Role =
                    entry.Role,

                UnitPriceE8 =
                    entry.UnitPrice?.RawE8,

                PriceCurrencyCode =
                    entry.UnitPrice?.Currency.Value,

                CreatedAtUtc =
                    reversal.CreatedAtUtc.UtcDateTime
            };
        }

        private static LotEntryAllocationRow
            MapReversalAllocation(
                LedgerTransaction reversal,
                AssetLot lot,
                LotEntryAllocation allocation)
        {
            if (allocation.AssetLotId
                != lot.Id)
            {
                throw new InvalidOperationException(
                    "The allocation does not belong to the supplied lot.");
            }

            return new LotEntryAllocationRow
            {
                Id =
                    allocation.Id,

                AssetLotId =
                    allocation.AssetLotId,

                TransactionEntryId =
                    allocation.TransactionEntryId,

                QuantityDeltaE8 =
                    allocation.QuantityDelta.RawE8,

                // Important:
                // this is a NEW reversal allocation.
                // Do not use historical lot.CreatedAtUtc.
                CreatedAtUtc =
                    reversal.CreatedAtUtc.UtcDateTime
            };
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
                ToDateTimeOffset(
                    row.CreatedAtUtc));
        }

        private static bool IsUniqueConstraintViolation(
            DbUpdateException exception)
        {
            return exception.InnerException
                is SqliteException sqliteException
                && sqliteException.SqliteErrorCode == 19
                && sqliteException.SqliteExtendedErrorCode
                    is 1555 or 2067;
        }

        private static bool IsPotentialDependencyConflict(
            Exception exception)
        {
            var sqlite =
                exception switch
                {
                    SqliteException direct =>
                        direct,

                    DbUpdateException
                    {
                        InnerException:
                            SqliteException inner
                    } =>
                        inner,

                    _ =>
                        null
                };

            if (sqlite is null)
            {
                return false;
            }

            return sqlite.Message.Contains(
                    "later posted lot allocations depend",
                    StringComparison.OrdinalIgnoreCase)
                || sqlite.Message.Contains(
                    "Lot quantity cannot become negative",
                    StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsUnsupportedPersistedShape(
            Exception exception)
        {
            return exception is
                DomainRuleViolationException
                or ArgumentException
                or InvalidOperationException
                or OverflowException;
        }

        private static DateTimeOffset ToDateTimeOffset(
            DateTime value)
        {
            return new DateTimeOffset(
                value.Kind == DateTimeKind.Utc
                    ? value
                    : DateTime.SpecifyKind(
                        value,
                        DateTimeKind.Utc));
        }
    }
}
