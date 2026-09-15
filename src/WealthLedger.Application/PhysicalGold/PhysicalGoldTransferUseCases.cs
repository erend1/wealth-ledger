using WealthLedger.Application.CoreLedger;
using WealthLedger.Application.OpeningBalances;

namespace WealthLedger.Application.PhysicalGold;

public sealed class PreviewPhysicalGoldTransferUseCase
{
    private readonly IOpeningBalanceReferenceReadStore _referenceStore;
    private readonly IPhysicalGoldCustodyReadStore _custodyStore;
    private readonly TimeProvider _timeProvider;

    public PreviewPhysicalGoldTransferUseCase(
        IOpeningBalanceReferenceReadStore referenceStore,
        IPhysicalGoldCustodyReadStore custodyStore,
        TimeProvider timeProvider)
    {
        _referenceStore = referenceStore;
        _custodyStore = custodyStore;
        _timeProvider = timeProvider;
    }

    public async Task<PhysicalGoldTransferPreview> ExecuteAsync(
        PhysicalGoldTransferCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        var validated = await PhysicalGoldEvaluator.EvaluateTransferAsync(
            command,
            _timeProvider.GetUtcNow(),
            _timeProvider.LocalTimeZone,
            _referenceStore,
            cancellationToken);
        var candidates = await _custodyStore.ListScopedLotsAsync(
            new PhysicalGoldCustodyScope(
                command.HouseholdId,
                command.SourcePortfolioId,
                command.SourceGoldAccountId,
                command.GoldAssetId),
            cancellationToken);
        var plan = PhysicalGoldPlanBuilder.Build(
            command.GoldAssetId,
            command.GrossWeight,
            command.PieceCount,
            command.SelectedLots,
            candidates);
        var warnings = validated.WarningCodes.ToList();
        if (validated.CashPortfolio is not null)
        {
            var current = await _custodyStore.DeriveCashPositionRawE8Async(
                command.HouseholdId,
                validated.CashPortfolio.PortfolioId,
                validated.CashAccount!.AccountId,
                validated.CashAsset!.AssetId,
                cancellationToken);
            var effect = PhysicalGoldCashConversion.ToQuantityRawE8(
                validated.Economics.NetCashEffect,
                validated.Currency.MinorUnitDigits);
            if (checked(current + effect) < 0)
            {
                warnings.Add(PhysicalGoldWarningCodes.NegativeProjectedCash);
            }
        }

        return new PhysicalGoldTransferPreview(
            validated.Scope,
            validated.SourcePortfolio.Name,
            validated.SourceAccount.Name,
            validated.DestinationPortfolio.Name,
            validated.DestinationAccount.Name,
            validated.GoldAsset.Code,
            validated.GoldAsset.Name,
            validated.Currency.Code.Value,
            validated.Currency.MinorUnitDigits,
            validated.ExecutionDate,
            validated.GrossWeight.RawE8,
            validated.PieceCount,
            plan.FineWeightGrams,
            validated.Economics,
            validated.Costs,
            plan.Lines,
            PhysicalGoldPlanFingerprint.ComputeTransfer(
                validated.Scope,
                validated.GrossWeight.RawE8,
                validated.PieceCount,
                plan.Selections),
            validated.ExternalReference,
            validated.Note,
            warnings);
    }
}

public sealed class RecordPhysicalGoldTransferUseCase
{
    private readonly IOpeningBalanceReferenceReadStore _referenceStore;
    private readonly ILedgerSubmissionStore _submissionStore;
    private readonly IPhysicalGoldPostingStore _postingStore;
    private readonly TimeProvider _timeProvider;

    public RecordPhysicalGoldTransferUseCase(
        IOpeningBalanceReferenceReadStore referenceStore,
        ILedgerSubmissionStore submissionStore,
        IPhysicalGoldPostingStore postingStore,
        TimeProvider timeProvider)
    {
        _referenceStore = referenceStore;
        _submissionStore = submissionStore;
        _postingStore = postingStore;
        _timeProvider = timeProvider;
    }

    public async Task<RecordPhysicalGoldActivityResult> ExecuteAsync(
        string idempotencyKey,
        PhysicalGoldTransferCommand command,
        CancellationToken cancellationToken = default)
    {
        PhysicalGoldIdempotency.ValidateKey(idempotencyKey);
        ArgumentNullException.ThrowIfNull(command);
        var submissionScope = new LedgerSubmissionScope(
            command.HouseholdId,
            LedgerOperationCodes.RecordPhysicalGoldTransfer,
            idempotencyKey);
        var existing = await _submissionStore.FindReceiptAsync(
            submissionScope, cancellationToken);
        if (existing is not null)
        {
            return Resolve(submissionScope, command, existing);
        }

        var now = _timeProvider.GetUtcNow();
        var validated = await PhysicalGoldEvaluator.EvaluateTransferAsync(
            command,
            now,
            _timeProvider.LocalTimeZone,
            _referenceStore,
            cancellationToken);
        var expected = PhysicalGoldPlanFingerprint.ComputeTransfer(
            validated.Scope,
            validated.GrossWeight.RawE8,
            validated.PieceCount,
            command.SelectedLots);
        if (!PhysicalGoldPlanFingerprint.Matches(
                command.ReviewedPlanFingerprint, expected))
        {
            throw PhysicalGoldException.Conflict(
                PhysicalGoldErrorCodes.StaleReviewedPlan,
                "The submitted transfer does not match the reviewed plan.");
        }

        var (transaction, _, _) = PhysicalGoldBuilder.BuildTransfer(
            validated, now);
        transaction.Post(now);
        var receipt = new LedgerSubmissionReceipt(
            submissionScope,
            PhysicalGoldCommandFingerprints.ComputeCurrent(command, validated),
            transaction.Id,
            AssetLotId: null,
            now);
        var commit = await _postingStore.TryCommitTransferAsync(
            receipt,
            transaction,
            validated.Scope,
            PhysicalGoldCanonicalizer.OrderSelections(command.SelectedLots),
            !string.IsNullOrEmpty(validated.Note),
            cancellationToken);

        return commit.Status switch
        {
            PhysicalGoldCommitStatus.Committed =>
                new RecordPhysicalGoldActivityResult(transaction.Id),
            PhysicalGoldCommitStatus.AlreadyRecorded =>
                Resolve(submissionScope, command, commit.Receipt!),
            PhysicalGoldCommitStatus.StaleReviewedPlan =>
                throw PhysicalGoldException.Conflict(
                    PhysicalGoldErrorCodes.StaleReviewedPlan,
                    "Custody changed after this transfer was reviewed."),
            PhysicalGoldCommitStatus.InsufficientGrossWeight =>
                throw PhysicalGoldException.Conflict(
                    PhysicalGoldErrorCodes.InsufficientGrossWeight,
                    "The source custody no longer contains the reviewed gross weight."),
            PhysicalGoldCommitStatus.InsufficientPieces =>
                throw PhysicalGoldException.Conflict(
                    PhysicalGoldErrorCodes.InsufficientPieces,
                    "The source custody no longer contains the reviewed pieces."),
            PhysicalGoldCommitStatus.NegativeCashNoteRequired =>
                throw PhysicalGoldException.Invalid(
                    PhysicalGoldErrorCodes.NegativeCashNoteRequired,
                    "Explain the funding gap before recording this transfer."),
            _ => throw PhysicalGoldException.Conflict(
                PhysicalGoldErrorCodes.PersistenceConflict,
                "The physical-gold transfer could not be recorded.")
        };
    }

    private static RecordPhysicalGoldActivityResult Resolve(
        LedgerSubmissionScope scope,
        PhysicalGoldTransferCommand command,
        LedgerSubmissionReceipt receipt)
    {
        if (receipt.Scope != scope)
        {
            throw new InvalidOperationException(
                "The submission store returned a receipt for another scope.");
        }

        var fingerprint = PhysicalGoldCommandFingerprints.Compute(
            command,
            receipt.Fingerprint.AlgorithmCode,
            receipt.Fingerprint.Version);
        if (fingerprint != receipt.Fingerprint)
        {
            throw new IdempotencyConflictException();
        }

        return new RecordPhysicalGoldActivityResult(receipt.TransactionId);
    }
}
