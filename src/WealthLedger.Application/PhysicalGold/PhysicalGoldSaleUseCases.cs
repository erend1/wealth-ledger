using WealthLedger.Application.CoreLedger;
using WealthLedger.Application.OpeningBalances;
using WealthLedger.Domain.Ledger;
using WealthLedger.Domain.Lots;

namespace WealthLedger.Application.PhysicalGold;

public sealed class PreviewPhysicalGoldSaleUseCase
{
    private readonly IOpeningBalanceReferenceReadStore _referenceStore;
    private readonly IPhysicalGoldCustodyReadStore _custodyStore;
    private readonly IPhysicalGoldRealizedCostReadStore _realizedCostStore;
    private readonly TimeProvider _timeProvider;

    public PreviewPhysicalGoldSaleUseCase(
        IOpeningBalanceReferenceReadStore referenceStore,
        IPhysicalGoldCustodyReadStore custodyStore,
        IPhysicalGoldRealizedCostReadStore realizedCostStore,
        TimeProvider timeProvider)
    {
        _referenceStore = referenceStore
            ?? throw new ArgumentNullException(nameof(referenceStore));
        _custodyStore = custodyStore
            ?? throw new ArgumentNullException(nameof(custodyStore));
        _realizedCostStore = realizedCostStore
            ?? throw new ArgumentNullException(nameof(realizedCostStore));
        _timeProvider = timeProvider
            ?? throw new ArgumentNullException(nameof(timeProvider));
    }

    public async Task<PhysicalGoldSalePreview> ExecuteAsync(
        PhysicalGoldSaleCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        var scope = ToScope(command);
        var validated = await EvaluateAsync(
            command, scope, _timeProvider.GetUtcNow(), cancellationToken);
        var custodyScope = new PhysicalGoldCustodyScope(
            scope.HouseholdId,
            scope.PortfolioId,
            scope.GoldAccountId,
            scope.GoldAssetId);
        var candidates = await _custodyStore.ListScopedLotsAsync(
            custodyScope, cancellationToken);
        var plan = PhysicalGoldPlanBuilder.Build(
            scope.GoldAssetId,
            validated.GrossWeight,
            validated.PieceCount,
            command.SelectedLots,
            candidates);
        var realized = await PhysicalGoldPlanBuilder.ProjectRealizedCostAsync(
            scope.HouseholdId,
            plan,
            _realizedCostStore,
            cancellationToken);
        var warnings = validated.WarningCodes.ToList();
        if (realized.Completeness != RealizedCostCompleteness.CompleteKnown)
        {
            warnings.Add(PhysicalGoldWarningCodes.IncompleteRealizedCost);
        }

        await AddCashWarningAsync(
            scope, validated, warnings, cancellationToken);
        return new PhysicalGoldSalePreview(
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
            validated.PieceCount,
            validated.Counterparty?.InstitutionId,
            validated.Counterparty?.Name,
            validated.Economics,
            validated.Costs,
            plan.Lines,
            PhysicalGoldPlanFingerprint.ComputeSale(
                scope,
                validated.GrossWeight.RawE8,
                validated.PieceCount,
                plan.Selections),
            realized,
            validated.ExternalReference,
            validated.Note,
            warnings);
    }

    internal async Task<ValidatedPhysicalGoldTrade> EvaluateAsync(
        PhysicalGoldSaleCommand command,
        PhysicalGoldTradeScope scope,
        DateTimeOffset now,
        CancellationToken cancellationToken)
        => await PhysicalGoldEvaluator.EvaluateTradeAsync(
            TransactionType.Sell,
            scope,
            command.GrossWeight,
            command.PieceCount,
            fineness: null,
            command.ExecutedUnitPrice,
            command.CashConsideration,
            command.ExecutionDate,
            command.OrderDate,
            command.SettlementDate,
            command.Costs,
            command.CounterpartyInstitutionId,
            hallmark: null,
            certificateReference: null,
            lotNote: null,
            command.ExternalReference,
            command.Note,
            now,
            _timeProvider.LocalTimeZone,
            _referenceStore,
            cancellationToken);

    private async Task AddCashWarningAsync(
        PhysicalGoldTradeScope scope,
        ValidatedPhysicalGoldTrade validated,
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
            validated.Economics.NetCashEffect,
            validated.Currency.MinorUnitDigits);
        if (checked(current + effect) < 0)
        {
            warnings.Add(PhysicalGoldWarningCodes.NegativeProjectedCash);
        }
    }

    internal static PhysicalGoldTradeScope ToScope(PhysicalGoldSaleCommand command)
        => new(
            command.HouseholdId,
            command.PortfolioId,
            command.GoldAccountId,
            command.CashAccountId,
            command.GoldAssetId,
            command.CashAssetId);
}

public sealed class RecordPhysicalGoldSaleUseCase
{
    private readonly IOpeningBalanceReferenceReadStore _referenceStore;
    private readonly ILedgerSubmissionStore _submissionStore;
    private readonly IPhysicalGoldPostingStore _postingStore;
    private readonly TimeProvider _timeProvider;

    public RecordPhysicalGoldSaleUseCase(
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

    public async Task<RecordPhysicalGoldActivityResult> ExecuteAsync(
        string idempotencyKey,
        PhysicalGoldSaleCommand command,
        CancellationToken cancellationToken = default)
    {
        PhysicalGoldIdempotency.ValidateKey(idempotencyKey);
        ArgumentNullException.ThrowIfNull(command);
        var submissionScope = new LedgerSubmissionScope(
            command.HouseholdId,
            LedgerOperationCodes.RecordPhysicalGoldSale,
            idempotencyKey);
        var existing = await _submissionStore.FindReceiptAsync(
            submissionScope, cancellationToken);
        if (existing is not null)
        {
            return Resolve(submissionScope, command, existing);
        }

        var now = _timeProvider.GetUtcNow();
        var scope = PreviewPhysicalGoldSaleUseCase.ToScope(command);
        var validated = await PhysicalGoldEvaluator.EvaluateTradeAsync(
            TransactionType.Sell,
            scope,
            command.GrossWeight,
            command.PieceCount,
            fineness: null,
            command.ExecutedUnitPrice,
            command.CashConsideration,
            command.ExecutionDate,
            command.OrderDate,
            command.SettlementDate,
            command.Costs,
            command.CounterpartyInstitutionId,
            hallmark: null,
            certificateReference: null,
            lotNote: null,
            command.ExternalReference,
            command.Note,
            now,
            _timeProvider.LocalTimeZone,
            _referenceStore,
            cancellationToken);
        EnsurePlanFingerprint(command, scope, validated);
        var (transaction, _) = PhysicalGoldBuilder.BuildTrade(validated, now);
        transaction.Post(now);
        var receipt = new LedgerSubmissionReceipt(
            submissionScope,
            PhysicalGoldCommandFingerprints.ComputeCurrent(command, validated),
            transaction.Id,
            AssetLotId: null,
            now);
        var commit = await _postingStore.TryCommitSaleAsync(
            receipt,
            transaction,
            scope,
            PhysicalGoldCanonicalizer.OrderSelections(command.SelectedLots),
            !string.IsNullOrEmpty(validated.Note),
            cancellationToken);

        return ResolveCommit(commit, submissionScope, command, transaction.Id);
    }

    private static void EnsurePlanFingerprint(
        PhysicalGoldSaleCommand command,
        PhysicalGoldTradeScope scope,
        ValidatedPhysicalGoldTrade validated)
    {
        var expected = PhysicalGoldPlanFingerprint.ComputeSale(
            scope,
            validated.GrossWeight.RawE8,
            validated.PieceCount,
            command.SelectedLots);
        if (!PhysicalGoldPlanFingerprint.Matches(
                command.ReviewedPlanFingerprint, expected))
        {
            throw PhysicalGoldException.Conflict(
                PhysicalGoldErrorCodes.StaleReviewedPlan,
                "The submitted sale does not match the reviewed plan.");
        }
    }

    private static RecordPhysicalGoldActivityResult ResolveCommit(
        PhysicalGoldCommitResult commit,
        LedgerSubmissionScope scope,
        PhysicalGoldSaleCommand command,
        Guid transactionId)
        => commit.Status switch
        {
            PhysicalGoldCommitStatus.Committed =>
                new RecordPhysicalGoldActivityResult(transactionId),
            PhysicalGoldCommitStatus.AlreadyRecorded =>
                Resolve(scope, command, commit.Receipt!),
            PhysicalGoldCommitStatus.StaleReviewedPlan =>
                throw PhysicalGoldException.Conflict(
                    PhysicalGoldErrorCodes.StaleReviewedPlan,
                    "Custody changed after this sale was reviewed."),
            PhysicalGoldCommitStatus.InsufficientGrossWeight =>
                throw PhysicalGoldException.Conflict(
                    PhysicalGoldErrorCodes.InsufficientGrossWeight,
                    "The reviewed source custody no longer contains the gross weight."),
            PhysicalGoldCommitStatus.InsufficientPieces =>
                throw PhysicalGoldException.Conflict(
                    PhysicalGoldErrorCodes.InsufficientPieces,
                    "The reviewed source custody no longer contains the pieces."),
            PhysicalGoldCommitStatus.NegativeCashNoteRequired =>
                throw PhysicalGoldException.Invalid(
                    PhysicalGoldErrorCodes.NegativeCashNoteRequired,
                    "Explain the funding gap before recording this sale."),
            _ => throw PhysicalGoldException.Conflict(
                PhysicalGoldErrorCodes.PersistenceConflict,
                "The physical-gold sale could not be recorded.")
        };

    private static RecordPhysicalGoldActivityResult Resolve(
        LedgerSubmissionScope scope,
        PhysicalGoldSaleCommand command,
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
