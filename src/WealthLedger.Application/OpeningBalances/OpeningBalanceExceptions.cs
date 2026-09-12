namespace WealthLedger.Application.OpeningBalances;

public enum OpeningBalanceErrorCategory
{
    NotFound,
    Conflict,
    Validation
}

public static class OpeningBalanceErrorCodes
{
    public const string ReferenceNotFound =
        "OPENING_BALANCE_REFERENCE_NOT_FOUND";

    public const string ReferenceConflict =
        "OPENING_BALANCE_REFERENCE_CONFLICT";

    public const string ReferenceInactive =
        "OPENING_BALANCE_REFERENCE_INACTIVE";

    public const string ReferenceShapeInvalid =
        "OPENING_BALANCE_REFERENCE_SHAPE_INVALID";

    public const string AssetNotSupported =
        "OPENING_BALANCE_ASSET_NOT_SUPPORTED";

    public const string AccountAssetMismatch =
        "OPENING_BALANCE_ACCOUNT_ASSET_MISMATCH";

    public const string LotsRequired =
        "OPENING_BALANCE_LOTS_REQUIRED";

    public const string LotsForbidden =
        "OPENING_BALANCE_LOTS_FORBIDDEN";

    public const string LotTotalMismatch =
        "OPENING_BALANCE_LOT_TOTAL_MISMATCH";

    public const string CostBasisInvalid =
        "OPENING_BALANCE_COST_BASIS_INVALID";

    public const string GoldDetailRequired =
        "OPENING_BALANCE_GOLD_DETAIL_REQUIRED";

    public const string GoldDetailForbidden =
        "OPENING_BALANCE_GOLD_DETAIL_FORBIDDEN";

    public const string AcquisitionDateAfterAsOf =
        "OPENING_BALANCE_ACQUISITION_DATE_AFTER_AS_OF";

    public const string AsOfDateInFuture =
        "OPENING_BALANCE_AS_OF_DATE_IN_FUTURE";

    public const string AlreadyExists =
        "OPENING_BALANCE_ALREADY_EXISTS";

    public const string ScopeHasEffectiveHistory =
        "OPENING_BALANCE_SCOPE_HAS_EFFECTIVE_HISTORY";

    public const string DuplicateLot =
        "OPENING_BALANCE_DUPLICATE_LOT";

    public const string SourceTextInvalid =
        "OPENING_BALANCE_SOURCE_TEXT_INVALID";

    public const string QuantityInvalid =
        "OPENING_BALANCE_QUANTITY_INVALID";

    public const string NotFound =
        "OPENING_BALANCE_NOT_FOUND";
}

public sealed class OpeningBalanceException : Exception
{
    public OpeningBalanceException(
        OpeningBalanceErrorCategory category,
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

    public OpeningBalanceErrorCategory Category { get; }

    public string ErrorCode { get; }

    public Guid? RelatedTransactionId { get; }
}
