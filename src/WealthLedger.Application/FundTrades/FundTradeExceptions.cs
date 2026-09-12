namespace WealthLedger.Application.FundTrades;

public enum FundTradeErrorCategory
{
    NotFound,
    Conflict,
    Validation
}

/// <summary>
/// Stable, sanitized failure codes for fund-trade workflows.
/// </summary>
/// <remarks>
/// Every code names a condition the user can act on. None of them carries a
/// note, reference, amount, fingerprint, resolved path or SQL fragment.
/// </remarks>
public static class FundTradeErrorCodes
{
    public const string ReferenceNotFound =
        "FUND_TRADE_REFERENCE_NOT_FOUND";

    public const string ReferenceInactive =
        "FUND_TRADE_REFERENCE_INACTIVE";

    public const string ReferenceShapeInvalid =
        "FUND_TRADE_REFERENCE_SHAPE_INVALID";

    public const string HouseholdMismatch =
        "FUND_TRADE_HOUSEHOLD_MISMATCH";

    public const string CurrencyMismatch =
        "FUND_TRADE_CURRENCY_MISMATCH";

    public const string DateInFuture =
        "FUND_TRADE_DATE_IN_FUTURE";

    public const string DateOrderInvalid =
        "FUND_TRADE_DATE_ORDER_INVALID";

    public const string DateBeforeAccountOpening =
        "FUND_TRADE_DATE_BEFORE_ACCOUNT_OPENING";

    public const string QuantityInvalid =
        "FUND_TRADE_QUANTITY_INVALID";

    public const string PriceInvalid =
        "FUND_TRADE_PRICE_INVALID";

    public const string AmountInvalid =
        "FUND_TRADE_AMOUNT_INVALID";

    public const string PrecisionOverflow =
        "FUND_TRADE_PRECISION_OVERFLOW";

    public const string CostTypeNotSupported =
        "FUND_TRADE_COST_TYPE_NOT_SUPPORTED";

    public const string CostTreatmentNotSupported =
        "FUND_TRADE_COST_TREATMENT_NOT_SUPPORTED";

    public const string CostAmountInvalid =
        "FUND_TRADE_COST_AMOUNT_INVALID";

    public const string CostDuplicate =
        "FUND_TRADE_COST_DUPLICATE";

    public const string CostLimitExceeded =
        "FUND_TRADE_COST_LIMIT_EXCEEDED";

    public const string UnexplainedDiscrepancy =
        "FUND_TRADE_UNEXPLAINED_DISCREPANCY";

    public const string NegativeExpectedProceeds =
        "FUND_TRADE_NEGATIVE_EXPECTED_PROCEEDS";

    public const string ProvenanceRequired =
        "FUND_TRADE_PROVENANCE_REQUIRED";

    public const string SourceTextInvalid =
        "FUND_TRADE_SOURCE_TEXT_INVALID";

    public const string NegativeCashNoteRequired =
        "FUND_TRADE_NEGATIVE_CASH_NOTE_REQUIRED";

    public const string InsufficientFundQuantity =
        "FUND_TRADE_INSUFFICIENT_FUND_QUANTITY";

    public const string StaleReviewedPlan =
        "FUND_TRADE_STALE_REVIEWED_PLAN";

    public const string ReviewedPlanRequired =
        "FUND_TRADE_REVIEWED_PLAN_REQUIRED";

    public const string PersistenceConflict =
        "FUND_TRADE_PERSISTENCE_CONFLICT";

    public const string UnsupportedPersistedShape =
        "FUND_TRADE_UNSUPPORTED_PERSISTED_SHAPE";

    public const string NotFound =
        "FUND_TRADE_NOT_FOUND";
}

/// <summary>
/// Warning codes that do not block posting but must stay visible.
/// </summary>
public static class FundTradeWarningCodes
{
    /// <summary>
    /// Entered consideration differs from the price-implied expectation by
    /// more than one minor unit, and the user explained it in a note.
    /// </summary>
    public const string ExplainedDiscrepancy =
        "FUND_TRADE_EXPLAINED_DISCREPANCY";

    /// <summary>
    /// The difference is within one minor unit, so it is consistent with
    /// ordinary rounding rather than a data problem.
    /// </summary>
    public const string RoundingConsistent =
        "FUND_TRADE_ROUNDING_CONSISTENT";

    /// <summary>
    /// The derived cash position for the selected scope would become
    /// negative. The ledger records source facts and may not hold all cash
    /// history, so this is a data-quality warning, not a rejection.
    /// </summary>
    public const string NegativeProjectedCash =
        "FUND_TRADE_NEGATIVE_PROJECTED_CASH";

    /// <summary>
    /// At least one consumed lot has no recorded acquisition date, so its
    /// position in first-in-first-out order is an assumption.
    /// </summary>
    public const string UnknownAcquisitionDateOrdering =
        "FUND_TRADE_UNKNOWN_ACQUISITION_DATE_ORDERING";

    /// <summary>
    /// Some or all of the disposed quantity has no known acquisition cost.
    /// </summary>
    public const string IncompleteRealizedCost =
        "FUND_TRADE_INCOMPLETE_REALIZED_COST";
}

public sealed class FundTradeException : Exception
{
    public FundTradeException(
        FundTradeErrorCategory category,
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

    public FundTradeErrorCategory Category { get; }

    public string ErrorCode { get; }

    public Guid? RelatedTransactionId { get; }

    internal static FundTradeException Invalid(
        string errorCode,
        string message)
        => new(
            FundTradeErrorCategory.Validation,
            errorCode,
            message);

    internal static FundTradeException NotFound(
        string message)
        => new(
            FundTradeErrorCategory.NotFound,
            FundTradeErrorCodes.ReferenceNotFound,
            message);

    internal static FundTradeException Conflict(
        string errorCode,
        string message)
        => new(
            FundTradeErrorCategory.Conflict,
            errorCode,
            message);
}
