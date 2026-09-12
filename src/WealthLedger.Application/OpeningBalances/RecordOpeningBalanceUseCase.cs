using WealthLedger.Application.CoreLedger;
using System.Globalization;
using WealthLedger.Domain.Assets;
using WealthLedger.Domain.Ledger;
using WealthLedger.Domain.Lots;
using WealthLedger.Domain.ValueObjects;

namespace WealthLedger.Application.OpeningBalances;

public sealed class RecordOpeningBalanceUseCase
{
    private const int MaximumIdempotencyKeyLength = 256;

    private readonly IOpeningBalanceReferenceReadStore _referenceStore;
    private readonly IOpeningBalanceEffectiveHistoryReadStore _historyStore;
    private readonly ILedgerSubmissionStore _submissionStore;
    private readonly ILedgerTransactionReadStore _transactionReadStore;
    private readonly TimeProvider _timeProvider;

    public RecordOpeningBalanceUseCase(
        IOpeningBalanceReferenceReadStore referenceStore,
        IOpeningBalanceEffectiveHistoryReadStore historyStore,
        ILedgerSubmissionStore submissionStore,
        ILedgerTransactionReadStore transactionReadStore,
        TimeProvider timeProvider)
    {
        _referenceStore = referenceStore
            ?? throw new ArgumentNullException(nameof(referenceStore));
        _historyStore = historyStore
            ?? throw new ArgumentNullException(nameof(historyStore));
        _submissionStore = submissionStore
            ?? throw new ArgumentNullException(nameof(submissionStore));
        _transactionReadStore = transactionReadStore
            ?? throw new ArgumentNullException(nameof(transactionReadStore));
        _timeProvider = timeProvider
            ?? throw new ArgumentNullException(nameof(timeProvider));
    }

    public async Task<RecordOpeningBalanceResult> ExecuteAsync(
        string idempotencyKey,
        RecordOpeningBalanceCommand command,
        CancellationToken cancellationToken = default)
    {
        ValidateIdempotencyKey(idempotencyKey);

        var normalized =
            OpeningBalanceCommandCanonicalizer.Normalize(command);

        if (normalized.HouseholdId == Guid.Empty)
        {
            throw new ArgumentException(
                "Household ID cannot be empty.",
                nameof(command));
        }

        var scope = new LedgerSubmissionScope(
            normalized.HouseholdId,
            LedgerOperationCodes.RecordOpeningBalance,
            idempotencyKey);

        var existingReceipt =
            await _submissionStore.FindReceiptAsync(
                scope,
                cancellationToken);

        if (existingReceipt is not null)
        {
            return await ResolveReceiptAsync(
                scope,
                normalized,
                existingReceipt,
                cancellationToken);
        }

        var recordedAtUtc = _timeProvider.GetUtcNow();

        var validated =
            await OpeningBalanceCommandEvaluator.EvaluateAsync(
                normalized,
                recordedAtUtc,
                _timeProvider.LocalTimeZone,
                _referenceStore,
                _historyStore,
                cancellationToken);

        var fingerprint =
            RecordOpeningBalanceCommandFingerprint.ComputeCurrent(
                validated.Command);

        var asset = Asset.Create(
            validated.Asset.AssetId,
            validated.Asset.Code,
            validated.Asset.Name,
            validated.Asset.Type,
            validated.Asset.BaseUnit,
            validated.Asset.BaseCurrency,
            validated.Asset.LotTrackingMode);

        var transaction = LedgerTransaction.CreateDraft(
            Guid.NewGuid(),
            validated.Command.HouseholdId,
            TransactionType.OpeningBalance,
            recordedAtUtc,
            executionDate: validated.Command.AsOfDate,
            externalReference: validated.Command.ExternalReference,
            note: validated.Command.Note);

        var entry = transaction.AddEntry(
            validated.Command.PortfolioId,
            validated.Command.AccountId,
            validated.Command.AssetId,
            QuantityDelta.FromRaw(validated.Command.Quantity.RawE8),
            EntryRole.Principal);

        var lots = validated.Command.Lots
            .Select(lot => AssetLot.Create(
                Guid.NewGuid(),
                asset,
                entry,
                lot.Quantity,
                lot.AcquiredOn,
                lot.CostBasis,
                recordedAtUtc,
                lot.PhysicalGoldDetail))
            .ToArray();

        transaction.Post(recordedAtUtc);

        var receipt = new LedgerSubmissionReceipt(
            scope,
            fingerprint,
            transaction.Id,
            AssetLotId: null,
            CreatedAtUtc: recordedAtUtc);

        var commitResult =
            await _submissionStore.TryCommitAsync(
                receipt,
                transaction,
                lots,
                cancellationToken);

        if (commitResult.WasCommitted)
        {
            if (commitResult.Receipt != receipt)
            {
                throw new InvalidOperationException(
                    "The submission store returned an inconsistent committed receipt.");
            }

            return await ReadAndVerifyPersistedResultAsync(
                validated.Command,
                transaction.Id,
                cancellationToken);
        }

        return await ResolveReceiptAsync(
            scope,
            normalized,
            commitResult.Receipt,
            cancellationToken);
    }

    private async Task<RecordOpeningBalanceResult> ResolveReceiptAsync(
        LedgerSubmissionScope expectedScope,
        RecordOpeningBalanceCommand normalizedCommand,
        LedgerSubmissionReceipt receipt,
        CancellationToken cancellationToken)
    {
        if (receipt.Scope != expectedScope)
        {
            throw new InvalidOperationException(
                "The submission store returned a receipt for a different scope.");
        }

        if (receipt.AssetLotId is not null)
        {
            throw new InvalidOperationException(
                "An opening-balance receipt cannot contain one selected asset-lot result.");
        }

        var replayFingerprint =
            RecordOpeningBalanceCommandFingerprint.Compute(
                normalizedCommand,
                receipt.Fingerprint.AlgorithmCode,
                receipt.Fingerprint.Version);

        if (replayFingerprint != receipt.Fingerprint)
        {
            throw new IdempotencyConflictException();
        }

        return await ReadAndVerifyPersistedResultAsync(
            normalizedCommand,
            receipt.TransactionId,
            cancellationToken);
    }

    private async Task<RecordOpeningBalanceResult>
        ReadAndVerifyPersistedResultAsync(
            RecordOpeningBalanceCommand submittedCommand,
            Guid transactionId,
            CancellationToken cancellationToken)
    {
        var persisted =
            await _transactionReadStore.FindByIdAsync(
                transactionId,
                cancellationToken)
            ?? throw new InvalidOperationException(
                "The persisted opening-balance transaction could not be read back.");

        if (persisted.TransactionId != transactionId
            || persisted.HouseholdId != submittedCommand.HouseholdId
            || persisted.Type != TransactionType.OpeningBalance
            || persisted.Status != TransactionStatus.Posted
            || persisted.ExecutionDate != submittedCommand.AsOfDate
            || persisted.OrderDate is not null
            || persisted.SettlementDate is not null
            || persisted.CashFlow is not null
            || persisted.Costs.Count != 0)
        {
            throw new InvalidOperationException(
                "The persisted opening-balance transaction failed readback verification.");
        }

        var entry = persisted.Entries.SingleOrDefault();

        if (entry is null
            || entry.PortfolioId != submittedCommand.PortfolioId
            || entry.AccountId != submittedCommand.AccountId
            || entry.AssetId != submittedCommand.AssetId
            || entry.Role != EntryRole.Principal
            || entry.UnitPriceRawE8 is not null
            || entry.PriceCurrencyCode is not null
            || entry.QuantityDeltaRawE8 != submittedCommand.Quantity.RawE8)
        {
            throw new InvalidOperationException(
                "The persisted opening-balance entry failed readback verification.");
        }

        if (persisted.CreatedLots.Count != submittedCommand.Lots.Count)
        {
            throw new InvalidOperationException(
                "The persisted opening-balance lots failed readback verification.");
        }

        if (submittedCommand.Lots.Count == 0)
        {
            if (persisted.LotAllocations.Count != 0)
            {
                throw new InvalidOperationException(
                    "An unlotted opening balance returned persisted allocations.");
            }
        }
        else
        {
            long allocationTotal = 0;
            var persistedLotKeys = new HashSet<OpeningBalanceLotCanonicalKey>();

            foreach (var lot in persisted.CreatedLots)
            {
                if (lot.AssetId != submittedCommand.AssetId
                    || lot.OpeningTransactionEntryId != entry.EntryId)
                {
                    throw new InvalidOperationException(
                        "An opening-balance lot failed lineage verification.");
                }

                var allocation = persisted.LotAllocations
                    .SingleOrDefault(item => item.AssetLotId == lot.AssetLotId);

                if (allocation is null
                    || allocation.TransactionEntryId != entry.EntryId
                    || allocation.QuantityDeltaRawE8 <= 0)
                {
                    throw new InvalidOperationException(
                        "An opening-balance lot failed allocation verification.");
                }

                allocationTotal = checked(
                    allocationTotal + allocation.QuantityDeltaRawE8);

                var gold = lot.PhysicalGoldDetail;

                if (gold is not null)
                {
                    var expectedFineWeight =
                        new PhysicalGoldLotDetail(
                                new Fineness(gold.FinenessPartsPerMillion),
                                gold.PieceCount,
                                gold.Hallmark,
                                gold.CertificateReference,
                                gold.Note)
                            .CalculateFineWeightGrams(
                                Quantity.FromRaw(
                                    allocation.QuantityDeltaRawE8));

                    if (gold.FineWeightGrams != expectedFineWeight)
                    {
                        throw new InvalidOperationException(
                            "An opening-balance gold lot failed derived-weight verification.");
                    }
                }

                persistedLotKeys.Add(new OpeningBalanceLotCanonicalKey(
                    allocation.QuantityDeltaRawE8,
                    lot.AcquiredOn?.ToString(
                        "yyyy-MM-dd",
                        CultureInfo.InvariantCulture),
                    OpeningBalanceCommandCanonicalizer.ToCostBasisStatusCode(
                        lot.CostBasisStatus),
                    lot.OriginalCostBasisMinorUnits,
                    lot.CostBasisCurrencyCode,
                    gold?.FinenessPartsPerMillion,
                    gold?.PieceCount,
                    gold?.Hallmark,
                    gold?.CertificateReference,
                    gold?.Note));
            }

            var submittedLotKeys = submittedCommand.Lots
                .Select(OpeningBalanceLotCanonicalKey.From)
                .ToHashSet();

            if (persisted.LotAllocations.Count != submittedCommand.Lots.Count
                || allocationTotal != submittedCommand.Quantity.RawE8
                || persistedLotKeys.Count != submittedLotKeys.Count
                || !persistedLotKeys.SetEquals(submittedLotKeys))
            {
                throw new InvalidOperationException(
                    "The persisted opening-balance allocations failed exact reconciliation.");
            }
        }

        return new RecordOpeningBalanceResult(
            transactionId,
            persisted.CreatedLots
                .Select(lot => lot.AssetLotId)
                .OrderBy(id => id)
                .ToArray(),
            submittedCommand.Quantity.RawE8,
            entry.QuantityDeltaRawE8);
    }

    private static void ValidateIdempotencyKey(string idempotencyKey)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey)
            || idempotencyKey.Length > MaximumIdempotencyKeyLength
            || idempotencyKey.Any(char.IsControl))
        {
            throw new ArgumentException(
                "Idempotency key must contain between 1 and 256 non-control characters.",
                nameof(idempotencyKey));
        }
    }
}
