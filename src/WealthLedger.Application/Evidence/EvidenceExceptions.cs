namespace WealthLedger.Application.Evidence;

public enum EvidenceErrorCategory
{
    NotFound,
    Conflict,
    Validation,
    Integrity
}

public static class EvidenceErrorCodes
{
    public const string InputInvalid = "EVIDENCE_INPUT_INVALID";
    public const string EffectiveDateInFuture = "EVIDENCE_EFFECTIVE_DATE_IN_FUTURE";
    public const string HouseholdNotFound = "EVIDENCE_HOUSEHOLD_NOT_FOUND";
    public const string ContextNotFound = "EVIDENCE_CONTEXT_NOT_FOUND";
    public const string ContextMismatch = "EVIDENCE_CONTEXT_MISMATCH";
    public const string TransactionLinkInvalid = "EVIDENCE_TRANSACTION_LINK_INVALID";
    public const string TransactionLinkLimitExceeded =
        "EVIDENCE_TRANSACTION_LINK_LIMIT_EXCEEDED";
    public const string SupersessionInvalid = "EVIDENCE_SUPERSESSION_INVALID";
    public const string Superseded = "EVIDENCE_SUPERSEDED";
    public const string NotFound = "EVIDENCE_NOT_FOUND";
    public const string AttachmentEmpty = "EVIDENCE_ATTACHMENT_EMPTY";
    public const string AttachmentTooLarge = "EVIDENCE_ATTACHMENT_TOO_LARGE";
    public const string AttachmentFileNameInvalid =
        "EVIDENCE_ATTACHMENT_FILE_NAME_INVALID";
    public const string AttachmentTypeMismatch = "EVIDENCE_ATTACHMENT_TYPE_MISMATCH";
    public const string AttachmentContentRejected =
        "EVIDENCE_ATTACHMENT_CONTENT_REJECTED";
    public const string AttachmentNotFound = "EVIDENCE_ATTACHMENT_NOT_FOUND";
    public const string AttachmentIntegrityFailed =
        "EVIDENCE_ATTACHMENT_INTEGRITY_FAILED";
    public const string PageSizeInvalid = "EVIDENCE_PAGE_SIZE_INVALID";
    public const string CursorInvalid = "EVIDENCE_CURSOR_INVALID";
    public const string CursorScopeMismatch = "EVIDENCE_CURSOR_SCOPE_MISMATCH";
    public const string ObservationInvalid = "EVIDENCE_OBSERVATION_INVALID";
    public const string ObservationScopeNotFound = "EVIDENCE_OBSERVATION_SCOPE_NOT_FOUND";
    public const string ObservationScopeMismatch = "EVIDENCE_OBSERVATION_SCOPE_MISMATCH";
    public const string ObservationConflict = "EVIDENCE_OBSERVATION_CONFLICT";
    public const string ObservationLimitExceeded = "EVIDENCE_OBSERVATION_LIMIT_EXCEEDED";
}

/// <summary>
/// A stable, sanitized evidence failure.
/// </summary>
/// <remarks>
/// Messages name the rule only. They never carry labels, references, notes,
/// file names, digests, bytes or master names, so they are safe to log.
/// </remarks>
public sealed class EvidenceException : Exception
{
    public EvidenceException(
        EvidenceErrorCategory category,
        string errorCode,
        string message,
        Exception? innerException = null)
        : base(message, innerException)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(errorCode);

        Category = category;
        ErrorCode = errorCode;
    }

    public EvidenceErrorCategory Category { get; }

    public string ErrorCode { get; }

    internal static EvidenceException Invalid(
        string errorCode,
        string message)
        => new(EvidenceErrorCategory.Validation, errorCode, message);

    internal static EvidenceException Conflict(
        string errorCode,
        string message,
        Exception? innerException = null)
        => new(EvidenceErrorCategory.Conflict, errorCode, message, innerException);

    internal static EvidenceException Missing(
        string errorCode,
        string message)
        => new(EvidenceErrorCategory.NotFound, errorCode, message);
}

public sealed class EvidencePersistenceException : Exception
{
    public EvidencePersistenceException(string message)
        : base(message)
    {
    }

    public EvidencePersistenceException(
        string message,
        Exception innerException)
        : base(message, innerException)
    {
    }
}
