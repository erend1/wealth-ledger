using WealthLedger.Application.Common;
using WealthLedger.Application.FundTrades;
using WealthLedger.Application.OpeningBalances;
using WealthLedger.Domain.Assets;
using WealthLedger.Domain.Ledger;
using WealthLedger.Domain.Lots;
using WealthLedger.Domain.Portfolios;
using WealthLedger.Domain.ValueObjects;

namespace WealthLedger.Application.CoreLedger;

/// <summary>
/// A completed fund purchase.
/// </summary>
/// <remarks>
/// The version-1 shape is preserved exactly and extended additively, so an
/// existing caller keeps working: <see cref="AccountId"/> remains the fund
/// account, and an omitted <see cref="CashAccountId"/> keeps both legs in
/// that same account.
///
/// Transport compatibility is not the same as validation compatibility. A new
/// first submission must still satisfy the accepted provenance, date, currency
/// and cost rules even in the legacy shape.
/// </remarks>
public sealed record RecordFundPurchaseCommand(
    Guid HouseholdId,
    Guid PortfolioId,
    Guid AccountId,
    Guid FundAssetId,
    Guid CashAssetId,
    Quantity FundQuantity,
    UnitPrice ExecutedUnitPrice,
    Money CashConsideration,
    DateOnly ExecutionDate,
    string? ExternalReference = null,
    string? Note = null,
    Guid? CashAccountId = null,
    DateOnly? OrderDate = null,
    DateOnly? SettlementDate = null,
    IReadOnlyList<FundTradeCostInput>? Costs = null)
{
    /// <summary>
    /// The account whose cash position changes.
    /// </summary>
    public Guid ResolvedCashAccountId
        => CashAccountId ?? AccountId;

    /// <summary>
    /// True when every field added after version 1 still holds its legacy
    /// default, so the command means exactly what version 1 meant.
    /// </summary>
    internal bool HasOnlyLegacyFacts
        => CashAccountId is null
            && OrderDate is null
            && SettlementDate is null
            && (Costs is null || Costs.Count == 0);
}

public sealed record RecordFundPurchaseResult(
    Guid TransactionId,
    Guid AssetLotId);

/// <summary>
/// Records a completed fund purchase exactly once.
/// </summary>
/// <remarks>
/// Receipt lookup happens before any current-state validation, so an
/// equivalent retry returns the original result even after the world has
/// moved on. Only a first submission is validated against current references.
/// </remarks>
public sealed class RecordFundPurchaseUseCase
{
    private const int MaximumIdempotencyKeyLength = 256;

    private readonly IOpeningBalanceReferenceReadStore _referenceStore;
    private readonly ILedgerSubmissionStore _submissionStore;
    private readonly IFundTradePostingStore _postingStore;
    private readonly TimeProvider _timeProvider;

    public RecordFundPurchaseUseCase(
        IOpeningBalanceReferenceReadStore referenceStore,
        ILedgerSubmissionStore submissionStore,
        IFundTradePostingStore postingStore,
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

    public async Task<RecordFundPurchaseResult> ExecuteAsync(
        string idempotencyKey,
        RecordFundPurchaseCommand command,
        CancellationToken cancellationToken = default)
    {
        ValidateIdempotencyKey(idempotencyKey);
        ArgumentNullException.ThrowIfNull(command);

        if (command.HouseholdId == Guid.Empty)
        {
            throw new ArgumentException(
                "Household ID cannot be empty.",
                nameof(command));
        }

        var scope =
            new LedgerSubmissionScope(
                command.HouseholdId,
                LedgerOperationCodes.RecordFundPurchase,
                idempotencyKey);

        var existingReceipt =
            await _submissionStore.FindReceiptAsync(
                scope,
                cancellationToken);

        if (existingReceipt is not null)
        {
            return ResolveReceipt(
                scope,
                command,
                existingReceipt);
        }

        var recordedAtUtc = _timeProvider.GetUtcNow();

        var tradeScope =
            new FundTradeScope(
                command.HouseholdId,
                command.PortfolioId,
                command.AccountId,
                command.ResolvedCashAccountId,
                command.FundAssetId,
                command.CashAssetId);

        var validated =
            await FundTradeEvaluator.EvaluateAsync(
                TransactionType.Buy,
                tradeScope,
                command.FundQuantity,
                command.ExecutedUnitPrice,
                command.CashConsideration,
                command.ExecutionDate,
                command.OrderDate,
                command.SettlementDate,
                command.Costs,
                command.ExternalReference,
                command.Note,
                recordedAtUtc,
                _timeProvider.LocalTimeZone,
                _referenceStore,
                cancellationToken);

        var (transaction, principal) =
            FundTradeBuilder.BuildTransaction(
                validated,
                recordedAtUtc);

        var assetLot =
            FundTradeBuilder.BuildAcquisitionLot(
                validated,
                principal,
                recordedAtUtc);

        transaction.Post(recordedAtUtc);

        var fingerprint =
            RecordFundPurchaseCommandFingerprint
                .ComputeCurrent(command);

        var receipt =
            new LedgerSubmissionReceipt(
                scope,
                fingerprint,
                transaction.Id,
                AssetLotId: assetLot.Id,
                CreatedAtUtc: recordedAtUtc);

        var commit =
            await _postingStore.TryCommitPurchaseAsync(
                receipt,
                transaction,
                assetLot,
                tradeScope,
                hasExplanatoryNote:
                    !string.IsNullOrEmpty(validated.Note),
                cancellationToken);

        switch (commit.Status)
        {
            case FundSaleCommitStatus.Committed:
                return new RecordFundPurchaseResult(
                    transaction.Id,
                    assetLot.Id);

            case FundSaleCommitStatus.AlreadyRecorded:
                return ResolveReceipt(
                    scope,
                    command,
                    commit.Receipt!);

            case FundSaleCommitStatus.NegativeCashNoteRequired:
                throw FundTradeException.Invalid(
                    FundTradeErrorCodes.NegativeCashNoteRequired,
                    "Explain the funding gap in the note before recording a purchase that overdraws the derived cash position.");

            default:
                throw FundTradeException.Conflict(
                    FundTradeErrorCodes.PersistenceConflict,
                    "The fund purchase could not be recorded.");
        }
    }

    /// <summary>
    /// Replays an existing receipt at the version it was written with.
    /// </summary>
    /// <remarks>
    /// A version-1 receipt is only equivalent to a command that still carries
    /// nothing but version-1 facts; the fingerprint computation enforces that
    /// rather than quietly ignoring the newer fields.
    /// </remarks>
    private static RecordFundPurchaseResult ResolveReceipt(
        LedgerSubmissionScope expectedScope,
        RecordFundPurchaseCommand command,
        LedgerSubmissionReceipt receipt)
    {
        if (receipt.Scope != expectedScope)
        {
            throw new InvalidOperationException(
                "The submission store returned a receipt for a different scope.");
        }

        if (receipt.AssetLotId is not Guid assetLotId
            || assetLotId == Guid.Empty)
        {
            throw new InvalidOperationException(
                "A fund-purchase receipt must contain an asset-lot result.");
        }

        var replayFingerprint =
            RecordFundPurchaseCommandFingerprint.Compute(
                command,
                receipt.Fingerprint.AlgorithmCode,
                receipt.Fingerprint.Version);

        if (replayFingerprint != receipt.Fingerprint)
        {
            throw new IdempotencyConflictException();
        }

        return new RecordFundPurchaseResult(
            receipt.TransactionId,
            assetLotId);
    }

    private static void ValidateIdempotencyKey(string idempotencyKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(idempotencyKey);

        if (idempotencyKey.Length > MaximumIdempotencyKeyLength
            || idempotencyKey.Any(char.IsControl))
        {
            throw new ArgumentException(
                "The idempotency key is not in a supported form.",
                nameof(idempotencyKey));
        }
    }
}
