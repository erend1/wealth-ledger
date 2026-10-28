namespace WealthLedger.Api.Contracts;

/// <summary>
/// Metadata for one evidence record. A retained file is sent only through
/// the dedicated upload route, never inside this JSON.
/// </summary>
public sealed record RecordEvidenceRequest(
    string SourceKindCode,
    DateOnly EffectiveDate,
    string Label,
    Guid? InstitutionId = null,
    Guid? AccountId = null,
    string? Reference = null,
    string? Note = null,
    Guid? SupersedesEvidenceId = null,
    IReadOnlyList<Guid>? TransactionIds = null)
{
    // Requests carry household text; never print them.
    public override string ToString() => nameof(RecordEvidenceRequest);
}

public sealed record RecordEvidenceResponse(
    Guid EvidenceId,
    string EvidenceLocation);

public sealed record LinkEvidenceTransactionsRequest(
    IReadOnlyList<Guid> TransactionIds);

public sealed record LinkEvidenceTransactionsResponse(
    Guid EvidenceId,
    int AddedCount,
    int AlreadyLinkedCount);

public sealed record RecordEvidenceObservationsRequest(
    IReadOnlyList<EvidenceObservationRequest> Observations)
{
    public override string ToString() => nameof(RecordEvidenceObservationsRequest);
}

/// <summary>
/// One observed signed raw-E8 quantity for one exact scope, with a
/// non-negative raw-E8 tolerance and, for physical gold only, whole pieces.
/// </summary>
public sealed record EvidenceObservationRequest(
    Guid PortfolioId,
    Guid AccountId,
    Guid AssetId,
    long ObservedQuantityRawE8,
    long ToleranceRawE8 = 0,
    int? ObservedPieceCount = null)
{
    public override string ToString() => nameof(EvidenceObservationRequest);
}

public sealed record RecordEvidenceObservationsResponse(
    Guid EvidenceId,
    int AddedCount,
    int AlreadyRecordedCount);

public sealed record EvidenceSummaryResponse(
    Guid EvidenceId,
    Guid HouseholdId,
    string SourceKindCode,
    DateOnly EffectiveDate,
    string Label,
    string? Reference,
    EvidenceContextResponse Context,
    DateTimeOffset CreatedAtUtc,
    Guid? SupersedesEvidenceId,
    Guid? SupersededByEvidenceId,
    EvidenceAttachmentMetadataResponse? Attachment,
    int TransactionLinkCount)
{
    public override string ToString() => nameof(EvidenceSummaryResponse);
}

public sealed record EvidenceDetailResponse(
    Guid EvidenceId,
    Guid HouseholdId,
    string SourceKindCode,
    DateOnly EffectiveDate,
    string Label,
    string? Reference,
    string? Note,
    EvidenceContextResponse Context,
    DateTimeOffset CreatedAtUtc,
    Guid? SupersedesEvidenceId,
    Guid? SupersededByEvidenceId,
    EvidenceAttachmentMetadataResponse? Attachment,
    IReadOnlyList<EvidenceTransactionLinkResponse> TransactionLinks)
{
    public override string ToString() => nameof(EvidenceDetailResponse);
}

public sealed record EvidenceContextResponse(
    Guid? InstitutionId,
    string? InstitutionCode,
    string? InstitutionName,
    Guid? AccountId,
    string? AccountCode,
    string? AccountName);

/// <summary>
/// Retained-file metadata and digest. The bytes are read only through the
/// explicit download location.
/// </summary>
public sealed record EvidenceAttachmentMetadataResponse(
    string FileName,
    string MediaTypeCode,
    int ByteLength,
    string Sha256,
    DateTimeOffset CreatedAtUtc,
    string DownloadLocation);

public sealed record EvidenceTransactionLinkResponse(
    Guid TransactionId,
    string TransactionTypeCode,
    DateOnly? ExecutionDate,
    DateTimeOffset LinkedAtUtc);

public sealed record EvidenceObservationListResponse(
    Guid EvidenceId,
    Guid HouseholdId,
    DateOnly EffectiveDate,
    Guid? SupersededByEvidenceId,
    IReadOnlyList<EvidenceObservationResponse> Items);

public sealed record EvidenceObservationResponse(
    Guid ObservationId,
    EvidenceObservationScopeResponse Scope,
    long ObservedQuantityRawE8,
    long ToleranceRawE8,
    int? ObservedPieceCount,
    DateTimeOffset RecordedAtUtc);

public sealed record EvidenceObservationScopeResponse(
    Guid PortfolioId,
    string PortfolioCode,
    string PortfolioName,
    Guid AccountId,
    string AccountCode,
    string AccountName,
    Guid? InstitutionId,
    string? InstitutionCode,
    string? InstitutionName,
    Guid AssetId,
    string AssetCode,
    string AssetName,
    string AssetTypeCode,
    string AssetBaseUnitCode);
