namespace WealthLedger.Application.Reconciliation;

public enum ReconciliationErrorCategory
{
    NotFound,
    Conflict,
    Validation
}

public static class ReconciliationErrorCodes
{
    public const string HouseholdNotFound = "RECONCILIATION_HOUSEHOLD_NOT_FOUND";
    public const string EvidenceNotFound = "RECONCILIATION_EVIDENCE_NOT_FOUND";
    public const string EvidenceSuperseded = "RECONCILIATION_EVIDENCE_SUPERSEDED";
    public const string NoObservations = "RECONCILIATION_NO_OBSERVATIONS";
    public const string ArithmeticOverflow = "RECONCILIATION_ARITHMETIC_OVERFLOW";
    public const string RunNotFound = "RECONCILIATION_RUN_NOT_FOUND";
    public const string ItemNotFound = "RECONCILIATION_ITEM_NOT_FOUND";
    public const string ItemMatched = "RECONCILIATION_ITEM_MATCHED";
    public const string ItemAlreadyResolved = "RECONCILIATION_ITEM_ALREADY_RESOLVED";
    public const string ResolutionInvalid = "RECONCILIATION_RESOLUTION_INVALID";
    public const string CorrectionInvalid = "RECONCILIATION_CORRECTION_INVALID";
    public const string PageSizeInvalid = "RECONCILIATION_PAGE_SIZE_INVALID";
    public const string CursorInvalid = "RECONCILIATION_CURSOR_INVALID";
    public const string CursorScopeMismatch = "RECONCILIATION_CURSOR_SCOPE_MISMATCH";
}

/// <summary>
/// A stable, sanitized reconciliation failure.
/// </summary>
/// <remarks>
/// Messages name the rule only. They never carry observed or ledger
/// quantities, explanations, evidence text or master names, so they are safe
/// to log.
/// </remarks>
public sealed class ReconciliationException : Exception
{
    public ReconciliationException(
        ReconciliationErrorCategory category,
        string errorCode,
        string message,
        Exception? innerException = null)
        : base(message, innerException)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(errorCode);

        Category = category;
        ErrorCode = errorCode;
    }

    public ReconciliationErrorCategory Category { get; }

    public string ErrorCode { get; }

    internal static ReconciliationException Invalid(
        string errorCode,
        string message,
        Exception? innerException = null)
        => new(ReconciliationErrorCategory.Validation, errorCode, message, innerException);

    internal static ReconciliationException Conflict(
        string errorCode,
        string message)
        => new(ReconciliationErrorCategory.Conflict, errorCode, message);

    /// <summary>
    /// A derived ledger quantity or a difference does not fit 64-bit fixed
    /// point; checked arithmetic refused it instead of wrapping.
    /// </summary>
    internal static ReconciliationException ArithmeticOverflow(OverflowException exception)
        => Invalid(
            ReconciliationErrorCodes.ArithmeticOverflow,
            "A reconciliation quantity or difference does not fit the fixed-point range.",
            exception);

    internal static ReconciliationException Missing(
        string errorCode,
        string message)
        => new(ReconciliationErrorCategory.NotFound, errorCode, message);
}

public sealed class ReconciliationPersistenceException : Exception
{
    public ReconciliationPersistenceException(string message)
        : base(message)
    {
    }

    public ReconciliationPersistenceException(
        string message,
        Exception innerException)
        : base(message, innerException)
    {
    }
}
