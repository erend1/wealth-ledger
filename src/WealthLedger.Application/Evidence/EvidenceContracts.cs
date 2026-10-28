using WealthLedger.Domain.Assets;
using WealthLedger.Domain.Evidence;
using WealthLedger.Domain.Ledger;

namespace WealthLedger.Application.Evidence;

public sealed record RecordEvidenceCommand(
    Guid HouseholdId,
    EvidenceSourceKind SourceKind,
    DateOnly EffectiveDate,
    string Label,
    Guid? InstitutionId = null,
    Guid? AccountId = null,
    string? Reference = null,
    string? Note = null,
    Guid? SupersedesEvidenceId = null,
    IReadOnlyList<Guid>? TransactionIds = null,
    EvidenceAttachmentUpload? Attachment = null)
{
    // Commands carry human text and file content; never print them.
    public override string ToString() => nameof(RecordEvidenceCommand);
}

/// <summary>
/// One source file offered for retention with an evidence record.
/// </summary>
/// <remarks>
/// The media type is chosen by the caller; trusted code then checks that the
/// file extension and the content signature agree with it.
/// </remarks>
public sealed class EvidenceAttachmentUpload
{
    public EvidenceAttachmentUpload(
        string fileName,
        EvidenceMediaType mediaType,
        ReadOnlyMemory<byte> content)
    {
        FileName = fileName;
        MediaType = mediaType;
        Content = content;
    }

    public string FileName { get; }

    public EvidenceMediaType MediaType { get; }

    public ReadOnlyMemory<byte> Content { get; }

    public override string ToString() => nameof(EvidenceAttachmentUpload);
}

public sealed record RecordEvidenceResult(Guid EvidenceId);

public sealed record LinkEvidenceTransactionsCommand(
    Guid HouseholdId,
    Guid EvidenceId,
    IReadOnlyList<Guid> TransactionIds);

public sealed record LinkEvidenceTransactionsResult(
    Guid EvidenceId,
    int AddedCount,
    int AlreadyLinkedCount);

public sealed record ListEvidenceQuery(
    Guid HouseholdId,
    int PageSize = 50,
    string? Cursor = null);

public sealed record GetEvidenceQuery(
    Guid HouseholdId,
    Guid EvidenceId);

public sealed record GetEvidenceAttachmentContentQuery(
    Guid HouseholdId,
    Guid EvidenceId);

public sealed record EvidencePage(
    IReadOnlyList<EvidenceSummary> Items,
    string? NextCursor);

/// <summary>
/// Evidence metadata for lists. Never carries the attachment bytes.
/// </summary>
public sealed record EvidenceSummary(
    Guid EvidenceId,
    Guid HouseholdId,
    EvidenceSourceKind SourceKind,
    DateOnly EffectiveDate,
    string Label,
    string? Reference,
    EvidenceContext Context,
    DateTimeOffset CreatedAtUtc,
    Guid? SupersedesEvidenceId,
    Guid? SupersededByEvidenceId,
    EvidenceAttachmentMetadata? Attachment,
    int TransactionLinkCount)
{
    // Read models carry labels, notes and master names; never print them.
    public override string ToString() => nameof(EvidenceSummary);
}

/// <summary>
/// Complete evidence metadata and its transaction links. Never carries the
/// attachment bytes.
/// </summary>
public sealed record EvidenceDetail(
    Guid EvidenceId,
    Guid HouseholdId,
    EvidenceSourceKind SourceKind,
    DateOnly EffectiveDate,
    string Label,
    string? Reference,
    string? Note,
    EvidenceContext Context,
    DateTimeOffset CreatedAtUtc,
    Guid? SupersedesEvidenceId,
    Guid? SupersededByEvidenceId,
    EvidenceAttachmentMetadata? Attachment,
    IReadOnlyList<EvidenceTransactionLinkItem> TransactionLinks)
{
    public override string ToString() => nameof(EvidenceDetail);
}

/// <summary>
/// Optional institution and account context with current display labels.
/// </summary>
public sealed record EvidenceContext(
    Guid? InstitutionId,
    string? InstitutionCode,
    string? InstitutionName,
    Guid? AccountId,
    string? AccountCode,
    string? AccountName)
{
    public override string ToString() => nameof(EvidenceContext);
}

public sealed record EvidenceAttachmentMetadata(
    string FileName,
    EvidenceMediaType MediaType,
    int ByteLength,
    string Sha256,
    DateTimeOffset CreatedAtUtc)
{
    public override string ToString() => nameof(EvidenceAttachmentMetadata);
}

public sealed record EvidenceTransactionLinkItem(
    Guid TransactionId,
    TransactionType TransactionType,
    DateOnly? ExecutionDate,
    DateTimeOffset LinkedAtUtc);

/// <summary>
/// Adds exact observed quantities to an existing evidence record.
/// </summary>
public sealed record RecordEvidenceObservationsCommand(
    Guid HouseholdId,
    Guid EvidenceId,
    IReadOnlyList<EvidenceObservationInput> Observations)
{
    public override string ToString() => nameof(RecordEvidenceObservationsCommand);
}

/// <summary>
/// One observed quantity for one exact Portfolio/Account/Asset scope, in the
/// asset's base unit as signed raw E8. The tolerance is a non-negative raw-E8
/// quantity; a piece count is accepted only for physical gold.
/// </summary>
public sealed record EvidenceObservationInput(
    Guid PortfolioId,
    Guid AccountId,
    Guid AssetId,
    long ObservedQuantityRawE8,
    long ToleranceRawE8 = 0,
    int? ObservedPieceCount = null)
{
    // Observed quantities are household financial facts; never print them.
    public override string ToString() => nameof(EvidenceObservationInput);
}

public sealed record RecordEvidenceObservationsResult(
    Guid EvidenceId,
    int AddedCount,
    int AlreadyRecordedCount);

public sealed record ListEvidenceObservationsQuery(
    Guid HouseholdId,
    Guid EvidenceId);

/// <summary>
/// The observations of one evidence record, with current scope labels.
/// </summary>
public sealed record EvidenceObservationList(
    Guid EvidenceId,
    Guid HouseholdId,
    DateOnly EffectiveDate,
    Guid? SupersededByEvidenceId,
    IReadOnlyList<EvidenceObservationItem> Items)
{
    public override string ToString() => nameof(EvidenceObservationList);
}

public sealed record EvidenceObservationItem(
    Guid ObservationId,
    EvidenceObservationScope Scope,
    long ObservedQuantityRawE8,
    long ToleranceRawE8,
    int? ObservedPieceCount,
    DateTimeOffset RecordedAtUtc)
{
    public override string ToString() => nameof(EvidenceObservationItem);
}

/// <summary>
/// One exact Portfolio/Account/Asset scope with current display labels.
/// </summary>
public sealed record EvidenceObservationScope(
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
    AssetType AssetType,
    AssetUnit AssetBaseUnit)
{
    public override string ToString() => nameof(EvidenceObservationScope);
}

/// <summary>
/// Verified attachment content for an explicit download.
/// </summary>
public sealed class EvidenceAttachmentContent
{
    public EvidenceAttachmentContent(
        Guid evidenceId,
        string fileName,
        EvidenceMediaType mediaType,
        string sha256,
        ReadOnlyMemory<byte> content)
    {
        EvidenceId = evidenceId;
        FileName = fileName;
        MediaType = mediaType;
        Sha256 = sha256;
        Content = content;
    }

    public Guid EvidenceId { get; }

    public string FileName { get; }

    public EvidenceMediaType MediaType { get; }

    public int ByteLength => Content.Length;

    public string Sha256 { get; }

    public ReadOnlyMemory<byte> Content { get; }

    public override string ToString() => nameof(EvidenceAttachmentContent);
}
