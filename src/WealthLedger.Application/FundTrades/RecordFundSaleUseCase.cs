using System.Buffers;
using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using WealthLedger.Application.CoreLedger;
using WealthLedger.Application.OpeningBalances;
using WealthLedger.Domain.Ledger;

namespace WealthLedger.Application.FundTrades;

public sealed record RecordFundSaleResult(
    Guid TransactionId);

/// <summary>
/// Records a completed fund sale exactly once, consuming reviewed lots.
/// </summary>
public sealed class RecordFundSaleUseCase
{
    private const int MaximumIdempotencyKeyLength = 256;

    private readonly IOpeningBalanceReferenceReadStore _referenceStore;
    private readonly ILedgerSubmissionStore _submissionStore;
    private readonly IFundTradePostingStore _postingStore;
    private readonly TimeProvider _timeProvider;

    public RecordFundSaleUseCase(
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

    public async Task<RecordFundSaleResult> ExecuteAsync(
        string idempotencyKey,
        FundSaleCommand command,
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

        var submissionScope =
            new LedgerSubmissionScope(
                command.HouseholdId,
                LedgerOperationCodes.RecordFundSale,
                idempotencyKey);

        /*
         * Receipt first, before any current-state check.
         *
         * A sale changes the very quantities its own eligibility rules look
         * at, so re-validating a completed sale against current state would
         * reject its own honest retry.
         */
        var existingReceipt =
            await _submissionStore.FindReceiptAsync(
                submissionScope,
                cancellationToken);

        if (existingReceipt is not null)
        {
            return ResolveReceipt(
                submissionScope,
                command,
                existingReceipt);
        }

        if (command.ReviewedPlan is null
            || command.ReviewedPlan.Count == 0)
        {
            throw FundTradeException.Invalid(
                FundTradeErrorCodes.ReviewedPlanRequired,
                "A fund sale must carry the reviewed allocation plan.");
        }

        var recordedAtUtc = _timeProvider.GetUtcNow();

        var tradeScope =
            new FundTradeScope(
                command.HouseholdId,
                command.PortfolioId,
                command.FundAccountId,
                command.CashAccountId,
                command.FundAssetId,
                command.CashAssetId);

        var validated =
            await FundTradeEvaluator.EvaluateAsync(
                TransactionType.Sell,
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

        EnsurePlanMatchesQuantity(
            command,
            validated);

        EnsureCarriedFingerprintMatchesPlan(
            command,
            tradeScope,
            validated);

        var (transaction, _) =
            FundTradeBuilder.BuildTransaction(
                validated,
                recordedAtUtc);

        var fingerprint =
            RecordFundSaleCommandFingerprint.ComputeCurrent(
                command,
                validated);

        /*
         * The receipt carries no lot identity. A sale may consume several
         * lots, so no single one can stand as its result; the persisted
         * allocations are the authoritative record of what it consumed.
         */
        var receipt =
            new LedgerSubmissionReceipt(
                submissionScope,
                fingerprint,
                transaction.Id,
                AssetLotId: null,
                CreatedAtUtc: recordedAtUtc);

        var commit =
            await _postingStore.TryCommitSaleAsync(
                receipt,
                transaction,
                tradeScope,
                command.ReviewedPlan,
                hasExplanatoryNote:
                    !string.IsNullOrEmpty(validated.Note),
                cancellationToken);

        return commit.Status switch
        {
            FundSaleCommitStatus.Committed =>
                new RecordFundSaleResult(transaction.Id),

            FundSaleCommitStatus.AlreadyRecorded =>
                ResolveReceipt(
                    submissionScope,
                    command,
                    commit.Receipt!),

            FundSaleCommitStatus.StaleReviewedPlan =>
                throw FundTradeException.Conflict(
                    FundTradeErrorCodes.StaleReviewedPlan,
                    "Holdings changed after this sale was reviewed. Review it again."),

            FundSaleCommitStatus.InsufficientQuantity =>
                throw FundTradeException.Invalid(
                    FundTradeErrorCodes.InsufficientFundQuantity,
                    "The selected portfolio and account no longer hold enough of this fund."),

            FundSaleCommitStatus.NegativeCashNoteRequired =>
                throw FundTradeException.Invalid(
                    FundTradeErrorCodes.NegativeCashNoteRequired,
                    "Explain the funding gap in the note before recording this sale."),

            _ =>
                throw FundTradeException.Conflict(
                    FundTradeErrorCodes.PersistenceConflict,
                    "The fund sale could not be recorded.")
        };
    }

    /// <summary>
    /// The reviewed plan must account for exactly the quantity being sold.
    /// </summary>
    private static void EnsurePlanMatchesQuantity(
        FundSaleCommand command,
        ValidatedFundTrade validated)
    {
        long total = 0;

        var seen = new HashSet<Guid>();

        foreach (var line in command.ReviewedPlan!)
        {
            if (line.Quantity.RawE8 <= 0)
            {
                throw FundTradeException.Invalid(
                    FundTradeErrorCodes.ReviewedPlanRequired,
                    "Every reviewed allocation must carry a positive quantity.");
            }

            if (!seen.Add(line.AssetLotId))
            {
                throw FundTradeException.Invalid(
                    FundTradeErrorCodes.ReviewedPlanRequired,
                    "A reviewed plan cannot name the same lot twice.");
            }

            total = checked(total + line.Quantity.RawE8);
        }

        if (total != validated.FundQuantity.RawE8)
        {
            throw FundTradeException.Invalid(
                FundTradeErrorCodes.ReviewedPlanRequired,
                "The reviewed allocation plan does not reconcile to the sold quantity.");
        }
    }

    /// <summary>
    /// Rejects a plan whose carried fingerprint does not describe it.
    /// </summary>
    /// <remarks>
    /// This catches a tampered or mismatched submission before any write.
    /// Whether the plan is still <em>current</em> is a separate question, and
    /// is decided inside the write transaction where the answer cannot change
    /// underneath the check.
    /// </remarks>
    private static void EnsureCarriedFingerprintMatchesPlan(
        FundSaleCommand command,
        FundTradeScope scope,
        ValidatedFundTrade validated)
    {
        var expected =
            FundSalePlanFingerprint.Compute(
                scope,
                validated.FundQuantity.RawE8,
                command.ReviewedPlan!);

        if (!FundSalePlanFingerprint.Matches(
                command.ReviewedPlanFingerprint,
                expected))
        {
            throw FundTradeException.Conflict(
                FundTradeErrorCodes.StaleReviewedPlan,
                "The submitted plan does not match the reviewed plan. Review the sale again.");
        }
    }

    private static RecordFundSaleResult ResolveReceipt(
        LedgerSubmissionScope expectedScope,
        FundSaleCommand command,
        LedgerSubmissionReceipt receipt)
    {
        if (receipt.Scope != expectedScope)
        {
            throw new InvalidOperationException(
                "The submission store returned a receipt for a different scope.");
        }

        var replayFingerprint =
            RecordFundSaleCommandFingerprint.Compute(
                command,
                receipt.Fingerprint.AlgorithmCode,
                receipt.Fingerprint.Version);

        if (replayFingerprint != receipt.Fingerprint)
        {
            throw new IdempotencyConflictException();
        }

        return new RecordFundSaleResult(
            receipt.TransactionId);
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

/// <summary>
/// The canonical identity of a fund-sale command.
/// </summary>
/// <remarks>
/// The reviewed plan is deliberately excluded. Two submissions of the same
/// trade are the same command even if one was reviewed against slightly
/// different history; plan freshness is arbitrated separately, and folding it
/// in here would turn an honest retry into a conflict.
/// </remarks>
internal static class RecordFundSaleCommandFingerprint
{
    internal const string CurrentAlgorithmCode = "SHA256";

    internal const int CurrentVersion = 1;

    private const int Version1 = 1;

    internal static CommandFingerprint ComputeCurrent(
        FundSaleCommand command,
        ValidatedFundTrade? validated = null)
        => Compute(
            command,
            CurrentAlgorithmCode,
            CurrentVersion,
            validated);

    internal static CommandFingerprint Compute(
        FundSaleCommand command,
        string algorithmCode,
        int version,
        ValidatedFundTrade? validated = null)
    {
        ArgumentNullException.ThrowIfNull(command);

        return (algorithmCode, version) switch
        {
            (CurrentAlgorithmCode, 1) =>
                ComputeV1(command, validated),

            _ =>
                throw new NotSupportedException(
                    $"Fund-sale fingerprint '{algorithmCode}' version '{version}' is not supported.")
        };
    }

    private static CommandFingerprint ComputeV1(
        FundSaleCommand command,
        ValidatedFundTrade? validated)
    {
        ArgumentNullException.ThrowIfNull(command.ExecutedUnitPrice);
        ArgumentNullException.ThrowIfNull(command.CashConsideration);

        /*
         * Replay must canonicalize the same way a first submission did, even
         * though replay never runs the full evaluator.
         */
        var costs =
            validated?.Costs
            ?? FundTradeCanonicalizer.NormalizeCosts(
                command.Costs,
                TransactionType.Sell,
                command.CashConsideration.Currency);

        var externalReference =
            validated?.ExternalReference
            ?? FundTradeCanonicalizer.NormalizeExternalReference(
                command.ExternalReference);

        var note =
            validated?.Note
            ?? FundTradeCanonicalizer.NormalizeNote(command.Note);

        var buffer = new ArrayBufferWriter<byte>();

        using (var writer =
               new Utf8JsonWriter(
                   buffer,
                   new JsonWriterOptions
                   {
                       Indented = false,
                       SkipValidation = false
                   }))
        {
            writer.WriteStartObject();

            writer.WriteNumber("version", Version1);

            writer.WriteString(
                "operation",
                LedgerOperationCodes.RecordFundSale);

            writer.WriteString(
                "householdId",
                command.HouseholdId.ToString("D"));

            writer.WriteString(
                "portfolioId",
                command.PortfolioId.ToString("D"));

            writer.WriteString(
                "fundAccountId",
                command.FundAccountId.ToString("D"));

            writer.WriteString(
                "cashAccountId",
                command.CashAccountId.ToString("D"));

            writer.WriteString(
                "fundAssetId",
                command.FundAssetId.ToString("D"));

            writer.WriteString(
                "cashAssetId",
                command.CashAssetId.ToString("D"));

            writer.WriteNumber(
                "fundQuantityRawE8",
                command.FundQuantity.RawE8);

            writer.WriteNumber(
                "executedUnitPriceRawE8",
                command.ExecutedUnitPrice.RawE8);

            writer.WriteString(
                "executedUnitPriceCurrency",
                command.ExecutedUnitPrice.Currency.Value);

            writer.WriteNumber(
                "cashConsiderationMinorUnits",
                command.CashConsideration.MinorUnits);

            writer.WriteString(
                "cashConsiderationCurrency",
                command.CashConsideration.Currency.Value);

            WriteNullableDate(writer, "orderDate", command.OrderDate);

            writer.WriteString(
                "executionDate",
                command.ExecutionDate.ToString(
                    "yyyy-MM-dd",
                    CultureInfo.InvariantCulture));

            WriteNullableDate(
                writer,
                "settlementDate",
                command.SettlementDate);

            writer.WriteStartArray("costs");

            foreach (var cost in FundTradeCanonicalizer.Order(costs))
            {
                writer.WriteStartObject();

                writer.WriteString(
                    "type",
                    FundTradeCanonicalizer.ToCostTypeCode(cost.Type));

                writer.WriteString(
                    "treatment",
                    FundTradeCanonicalizer.ToTreatmentCode(
                        cost.Treatment));

                writer.WriteNumber(
                    "amountMinorUnits",
                    cost.Amount.MinorUnits);

                writer.WriteString(
                    "amountCurrency",
                    cost.Amount.Currency.Value);

                WriteNullableText(writer, "note", cost.Note);

                writer.WriteEndObject();
            }

            writer.WriteEndArray();

            WriteNullableText(
                writer,
                "externalReference",
                externalReference);

            WriteNullableText(writer, "note", note);

            writer.WriteEndObject();
            writer.Flush();
        }

        return new CommandFingerprint(
            CurrentAlgorithmCode,
            Version1,
            Convert
                .ToHexString(SHA256.HashData(buffer.WrittenSpan))
                .ToLowerInvariant());
    }

    private static void WriteNullableDate(
        Utf8JsonWriter writer,
        string propertyName,
        DateOnly? value)
    {
        writer.WritePropertyName(propertyName);

        if (value is null)
        {
            writer.WriteNullValue();
        }
        else
        {
            writer.WriteStringValue(
                value.Value.ToString(
                    "yyyy-MM-dd",
                    CultureInfo.InvariantCulture));
        }
    }

    private static void WriteNullableText(
        Utf8JsonWriter writer,
        string propertyName,
        string? value)
    {
        writer.WritePropertyName(propertyName);

        if (value is null)
        {
            writer.WriteNullValue();
        }
        else
        {
            writer.WriteStringValue(value);
        }
    }
}
