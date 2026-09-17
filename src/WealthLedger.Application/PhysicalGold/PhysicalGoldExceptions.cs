namespace WealthLedger.Application.PhysicalGold;

public enum PhysicalGoldErrorCategory
{
    NotFound,
    Conflict,
    Validation
}

public static class PhysicalGoldErrorCodes
{
    public const string ReferenceNotFound =
        "PHYSICAL_GOLD_REFERENCE_NOT_FOUND";
    public const string ReferenceInactive =
        "PHYSICAL_GOLD_REFERENCE_INACTIVE";
    public const string ReferenceShapeInvalid =
        "PHYSICAL_GOLD_REFERENCE_SHAPE_INVALID";
    public const string HouseholdMismatch =
        "PHYSICAL_GOLD_HOUSEHOLD_MISMATCH";
    public const string CurrencyMismatch =
        "PHYSICAL_GOLD_CURRENCY_MISMATCH";
    public const string DateInFuture =
        "PHYSICAL_GOLD_DATE_IN_FUTURE";
    public const string DateOrderInvalid =
        "PHYSICAL_GOLD_DATE_ORDER_INVALID";
    public const string DateBeforeAccountOpening =
        "PHYSICAL_GOLD_DATE_BEFORE_ACCOUNT_OPENING";
    public const string QuantityInvalid =
        "PHYSICAL_GOLD_QUANTITY_INVALID";
    public const string PieceCountInvalid =
        "PHYSICAL_GOLD_PIECE_COUNT_INVALID";
    public const string FinenessInvalid =
        "PHYSICAL_GOLD_FINENESS_INVALID";
    public const string PriceInvalid =
        "PHYSICAL_GOLD_PRICE_INVALID";
    public const string AmountInvalid =
        "PHYSICAL_GOLD_AMOUNT_INVALID";
    public const string PrecisionOverflow =
        "PHYSICAL_GOLD_PRECISION_OVERFLOW";
    public const string CostTypeNotSupported =
        "PHYSICAL_GOLD_COST_TYPE_NOT_SUPPORTED";
    public const string CostTreatmentNotSupported =
        "PHYSICAL_GOLD_COST_TREATMENT_NOT_SUPPORTED";
    public const string CostAmountInvalid =
        "PHYSICAL_GOLD_COST_AMOUNT_INVALID";
    public const string CostNoteRequired =
        "PHYSICAL_GOLD_COST_NOTE_REQUIRED";
    public const string CostDuplicate =
        "PHYSICAL_GOLD_COST_DUPLICATE";
    public const string CostLimitExceeded =
        "PHYSICAL_GOLD_COST_LIMIT_EXCEEDED";
    public const string UnexplainedDiscrepancy =
        "PHYSICAL_GOLD_UNEXPLAINED_DISCREPANCY";
    public const string NegativeExpectedProceeds =
        "PHYSICAL_GOLD_NEGATIVE_EXPECTED_PROCEEDS";
    public const string ProvenanceRequired =
        "PHYSICAL_GOLD_PROVENANCE_REQUIRED";
    public const string SourceTextInvalid =
        "PHYSICAL_GOLD_SOURCE_TEXT_INVALID";
    public const string CounterpartyInvalid =
        "PHYSICAL_GOLD_COUNTERPARTY_INVALID";
    public const string SelectedLotInvalid =
        "PHYSICAL_GOLD_SELECTED_LOT_INVALID";
    public const string DuplicateSelection =
        "PHYSICAL_GOLD_DUPLICATE_SELECTION";
    public const string SelectedTotalsMismatch =
        "PHYSICAL_GOLD_SELECTED_TOTALS_MISMATCH";
    public const string InsufficientGrossWeight =
        "PHYSICAL_GOLD_INSUFFICIENT_GROSS_WEIGHT";
    public const string InsufficientPieces =
        "PHYSICAL_GOLD_INSUFFICIENT_PIECES";
    public const string StaleReviewedPlan =
        "PHYSICAL_GOLD_STALE_REVIEWED_PLAN";
    public const string ReviewedPlanRequired =
        "PHYSICAL_GOLD_REVIEWED_PLAN_REQUIRED";
    public const string TransferScopeInvalid =
        "PHYSICAL_GOLD_TRANSFER_SCOPE_INVALID";
    public const string CashScopeRequired =
        "PHYSICAL_GOLD_CASH_SCOPE_REQUIRED";
    public const string CashScopeForbidden =
        "PHYSICAL_GOLD_CASH_SCOPE_FORBIDDEN";
    public const string NegativeCashNoteRequired =
        "PHYSICAL_GOLD_NEGATIVE_CASH_NOTE_REQUIRED";
    public const string UnsupportedPersistedShape =
        "PHYSICAL_GOLD_UNSUPPORTED_PERSISTED_SHAPE";
    public const string PersistenceConflict =
        "PHYSICAL_GOLD_PERSISTENCE_CONFLICT";
    public const string NotFound =
        "PHYSICAL_GOLD_ACTIVITY_NOT_FOUND";
}

public static class PhysicalGoldWarningCodes
{
    public const string ExplainedDiscrepancy =
        "PHYSICAL_GOLD_EXPLAINED_DISCREPANCY";
    public const string RoundingConsistent =
        "PHYSICAL_GOLD_ROUNDING_CONSISTENT";
    public const string ExecutedPriceUnavailable =
        "PHYSICAL_GOLD_EXECUTED_PRICE_UNAVAILABLE";
    public const string NegativeProjectedCash =
        "PHYSICAL_GOLD_NEGATIVE_PROJECTED_CASH";
    public const string IncompleteRealizedCost =
        "PHYSICAL_GOLD_INCOMPLETE_REALIZED_COST";
}

public sealed class PhysicalGoldException : Exception
{
    public PhysicalGoldException(
        PhysicalGoldErrorCategory category,
        string errorCode,
        string message,
        Guid? relatedTransactionId = null,
        Exception? innerException = null)
        : base(message, innerException)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(errorCode);

        Category = category;
        ErrorCode = errorCode;
        RelatedTransactionId = relatedTransactionId;
    }

    public PhysicalGoldErrorCategory Category { get; }

    public string ErrorCode { get; }

    public Guid? RelatedTransactionId { get; }

    internal static PhysicalGoldException Invalid(
        string errorCode,
        string message)
        => new(PhysicalGoldErrorCategory.Validation, errorCode, message);

    internal static PhysicalGoldException Conflict(
        string errorCode,
        string message)
        => new(PhysicalGoldErrorCategory.Conflict, errorCode, message);

    internal static PhysicalGoldException Missing(string message)
        => new(
            PhysicalGoldErrorCategory.NotFound,
            PhysicalGoldErrorCodes.ReferenceNotFound,
            message);
}
