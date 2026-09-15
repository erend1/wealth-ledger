using WealthLedger.Application.CoreLedger;
using WealthLedger.Application.OpeningBalances;
using WealthLedger.Domain.Ledger;
using WealthLedger.Domain.Lots;

namespace WealthLedger.Application.PhysicalGold;

public sealed class PreviewPhysicalGoldPurchaseUseCase
{
    private readonly IOpeningBalanceReferenceReadStore _referenceStore;
    private readonly IPhysicalGoldCustodyReadStore _custodyStore;
    private readonly TimeProvider _timeProvider;

    public PreviewPhysicalGoldPurchaseUseCase(
        IOpeningBalanceReferenceReadStore referenceStore,
        IPhysicalGoldCustodyReadStore custodyStore,
        TimeProvider timeProvider)
    {
        _referenceStore = referenceStore
            ?? throw new ArgumentNullException(nameof(referenceStore));
        _custodyStore = custodyStore
            ?? throw new ArgumentNullException(nameof(custodyStore));
        _timeProvider = timeProvider
            ?? throw new ArgumentNullException(nameof(timeProvider));
    }

    public async Task<PhysicalGoldPurchasePreview> ExecuteAsync(
        PhysicalGoldPurchaseCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        var scope = ToScope(command);
        var validated = await PhysicalGoldEvaluator.EvaluateTradeAsync(
            TransactionType.Buy,
            scope,
            command.GrossWeight,
            command.PieceCount,
            command.Fineness,
            command.ExecutedUnitPrice,
            command.CashConsideration,
            command.ExecutionDate,
            command.OrderDate,
            command.SettlementDate,
            command.Costs,
            command.CounterpartyInstitutionId,
            command.Hallmark,
            command.CertificateReference,
            command.LotNote,
            command.ExternalReference,
            command.Note,
            _timeProvider.GetUtcNow(),
            _timeProvider.LocalTimeZone,
            _referenceStore,
            cancellationToken);
        var warnings = validated.WarningCodes.ToList();
        await AddCashWarningAsync(
            scope,
            validated.Economics,
            validated.Currency.MinorUnitDigits,
            warnings,
            cancellationToken);
        var fineness = validated.Fineness
            ?? throw new InvalidOperationException(
                "A validated physical-gold purchase must carry fineness.");
        var detail = new PhysicalGoldLotDetail(
            fineness,
            validated.PieceCount,
            validated.Hallmark,
            validated.CertificateReference,
            validated.LotNote);

        return new PhysicalGoldPurchasePreview(
            scope,
            validated.Portfolio.Name,
            validated.GoldAccount.Name,
            validated.CashAccount.Name,
            validated.GoldAsset.Code,
            validated.GoldAsset.Name,
            validated.Currency.Code.Value,
            validated.Currency.MinorUnitDigits,
            validated.ExecutionDate,
            validated.OrderDate,
            validated.SettlementDate,
            validated.GrossWeight.RawE8,
            fineness.Ppm,
            detail.CalculateFineWeightGrams(validated.GrossWeight),
            validated.PieceCount,
            validated.Counterparty?.InstitutionId,
            validated.Counterparty?.Name,
            validated.Economics,
            validated.Costs,
            validated.Hallmark,
            validated.CertificateReference,
            validated.LotNote,
            validated.ExternalReference,
            validated.Note,
            warnings);
    }

    private async Task AddCashWarningAsync(
        PhysicalGoldTradeScope scope,
        PhysicalGoldEconomics economics,
        int minorUnitDigits,
        List<string> warnings,
        CancellationToken cancellationToken)
    {
        var current = await _custodyStore.DeriveCashPositionRawE8Async(
            scope.HouseholdId,
            scope.PortfolioId,
            scope.CashAccountId,
            scope.CashAssetId,
            cancellationToken);
        var effect = PhysicalGoldCashConversion.ToQuantityRawE8(
            economics.NetCashEffect,
            minorUnitDigits);
        if (checked(current + effect) < 0)
        {
            warnings.Add(PhysicalGoldWarningCodes.NegativeProjectedCash);
        }
    }

    internal static PhysicalGoldTradeScope ToScope(
        PhysicalGoldPurchaseCommand command)
        => new(
            command.HouseholdId,
            command.PortfolioId,
            command.GoldAccountId,
            command.CashAccountId,
            command.GoldAssetId,
            command.CashAssetId);
}

public sealed class RecordPhysicalGoldPurchaseUseCase
{
    private readonly IOpeningBalanceReferenceReadStore _referenceStore;
    private readonly ILedgerSubmissionStore _submissionStore;
    private readonly IPhysicalGoldPostingStore _postingStore;
    private readonly TimeProvider _timeProvider;

    public RecordPhysicalGoldPurchaseUseCase(
        IOpeningBalanceReferenceReadStore referenceStore,
        ILedgerSubmissionStore submissionStore,
        IPhysicalGoldPostingStore postingStore,
        TimeProvider timeProvider)
    {
        _referenceStore = referenceStore
            ?? throw new ArgumentNullException(nameof(referenceStore));
        _submissionStore = submissionStore
            ?? throw new ArgumentNullException(nameof(submissionStore));
        _postingStore = postingStore
            ?? throw new ArgumentNullException(nameof(postingStore));
        _timeProvider = timeProvider
            ?? throw new ArgumentNullException(nameof(timeProvider));
    }

    public async Task<RecordPhysicalGoldPurchaseResult> ExecuteAsync(
        string idempotencyKey,
        PhysicalGoldPurchaseCommand command,
        CancellationToken cancellationToken = default)
    {
        PhysicalGoldIdempotency.ValidateKey(idempotencyKey);
        ArgumentNullException.ThrowIfNull(command);
        var submissionScope = new LedgerSubmissionScope(
            command.HouseholdId,
            LedgerOperationCodes.RecordPhysicalGoldPurchase,
            idempotencyKey);
        var existing = await _submissionStore.FindReceiptAsync(
            submissionScope, cancellationToken);
        if (existing is not null)
        {
            return Resolve(submissionScope, command, existing);
        }

        var now = _timeProvider.GetUtcNow();
        var tradeScope = PreviewPhysicalGoldPurchaseUseCase.ToScope(command);
        var validated = await PhysicalGoldEvaluator.EvaluateTradeAsync(
            TransactionType.Buy,
            tradeScope,
            command.GrossWeight,
            command.PieceCount,
            command.Fineness,
            command.ExecutedUnitPrice,
            command.CashConsideration,
            command.ExecutionDate,
            command.OrderDate,
            command.SettlementDate,
            command.Costs,
            command.CounterpartyInstitutionId,
            command.Hallmark,
            command.CertificateReference,
            command.LotNote,
            command.ExternalReference,
            command.Note,
            now,
            _timeProvider.LocalTimeZone,
            _referenceStore,
            cancellationToken);
        var (transaction, principal) = PhysicalGoldBuilder.BuildTrade(
            validated, now);
        var lot = PhysicalGoldBuilder.BuildAcquisitionLot(
            validated, principal, now);
        transaction.Post(now);
        var receipt = new LedgerSubmissionReceipt(
            submissionScope,
            PhysicalGoldCommandFingerprints.ComputeCurrent(
                command, validated),
            transaction.Id,
            lot.Id,
            now);
        var commit = await _postingStore.TryCommitPurchaseAsync(
            receipt,
            transaction,
            lot,
            tradeScope,
            !string.IsNullOrEmpty(validated.Note),
            cancellationToken);

        return commit.Status switch
        {
            PhysicalGoldCommitStatus.Committed =>
                new RecordPhysicalGoldPurchaseResult(transaction.Id, lot.Id),
            PhysicalGoldCommitStatus.AlreadyRecorded =>
                Resolve(submissionScope, command, commit.Receipt!),
            PhysicalGoldCommitStatus.NegativeCashNoteRequired =>
                throw PhysicalGoldException.Invalid(
                    PhysicalGoldErrorCodes.NegativeCashNoteRequired,
                    "Explain the funding gap before recording this purchase."),
            _ => throw PhysicalGoldException.Conflict(
                PhysicalGoldErrorCodes.PersistenceConflict,
                "The physical-gold purchase could not be recorded.")
        };
    }

    private static RecordPhysicalGoldPurchaseResult Resolve(
        LedgerSubmissionScope scope,
        PhysicalGoldPurchaseCommand command,
        LedgerSubmissionReceipt receipt)
    {
        if (receipt.Scope != scope
            || receipt.AssetLotId is not Guid lotId
            || lotId == Guid.Empty)
        {
            throw new InvalidOperationException(
                "A physical-gold purchase receipt has an invalid shape.");
        }

        var fingerprint = PhysicalGoldCommandFingerprints.Compute(
            command,
            receipt.Fingerprint.AlgorithmCode,
            receipt.Fingerprint.Version);
        if (fingerprint != receipt.Fingerprint)
        {
            throw new IdempotencyConflictException();
        }

        return new RecordPhysicalGoldPurchaseResult(
            receipt.TransactionId, lotId);
    }
}
