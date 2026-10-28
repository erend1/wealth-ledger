using WealthLedger.Application.CoreLedger;
using WealthLedger.Domain.Assets;
using WealthLedger.Domain.Evidence;
using WealthLedger.Domain.Ledger;

namespace WealthLedger.Application.Evidence;

/// <summary>
/// Write-side persistence for the evidence aggregate and its retry receipts.
/// </summary>
/// <remarks>
/// Evidence writes never touch the ledger: the store inserts evidence,
/// attachment, link and receipt rows only. Link records are added through
/// this aggregate boundary rather than a link-specific repository.
/// </remarks>
public interface IEvidenceStore
{
    Task<bool> HouseholdExistsAsync(
        Guid householdId,
        CancellationToken cancellationToken = default);

    Task<bool> InstitutionExistsAsync(
        Guid institutionId,
        CancellationToken cancellationToken = default);

    Task<EvidenceAccountReference?> FindAccountAsync(
        Guid accountId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<EvidenceTransactionReference>> FindTransactionsAsync(
        IReadOnlyCollection<Guid> transactionIds,
        CancellationToken cancellationToken = default);

    Task<EvidenceRecordReference?> FindEvidenceAsync(
        Guid evidenceId,
        CancellationToken cancellationToken = default);

    Task<EvidenceCommandReceipt?> FindReceiptAsync(
        EvidenceCommandScope scope,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Atomically inserts the record, its attachment, its initial links and
    /// the receipt. When another writer already committed the same scope,
    /// nothing is inserted and that writer's receipt is returned.
    /// </summary>
    Task<EvidenceCommitResult> TryCommitAsync(
        EvidenceCommandReceipt receipt,
        EvidenceRecord record,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Inserts the links that do not exist yet and returns how many were
    /// added. An existing link is left untouched.
    /// </summary>
    /// <remarks>
    /// The write is atomic and enforces the per-record cap of
    /// <see cref="EvidenceTransactionLink.MaximumPerEvidenceRecord"/> links
    /// against every committed writer: when the record would exceed it, the
    /// store adds nothing and throws a Validation
    /// <see cref="EvidenceException"/> with
    /// <see cref="EvidenceErrorCodes.TransactionLinkLimitExceeded"/>. Only the
    /// store can see concurrent links, so the Application does not pre-check.
    /// </remarks>
    Task<int> AddTransactionLinksAsync(
        IReadOnlyCollection<EvidenceTransactionLink> links,
        CancellationToken cancellationToken = default);

    Task<EvidenceObservationReferences> FindObservationReferencesAsync(
        IReadOnlyCollection<Guid> portfolioIds,
        IReadOnlyCollection<Guid> accountIds,
        IReadOnlyCollection<Guid> assetIds,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns a record's observations in recording order.
    /// </summary>
    Task<IReadOnlyList<EvidenceObservation>> ListObservationsAsync(
        Guid evidenceId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Atomically inserts the observations whose scope has none yet and
    /// returns how many were added. An observation that repeats a stored one
    /// is left untouched; one that contradicts a stored one is a conflict, and
    /// then nothing is inserted.
    /// </summary>
    Task<int> AddObservationsAsync(
        Guid evidenceId,
        IReadOnlyCollection<EvidenceObservation> observations,
        CancellationToken cancellationToken = default);
}

public interface IEvidenceReadStore
{
    Task<bool> HouseholdExistsAsync(
        Guid householdId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<EvidenceSummary>> ListAsync(
        Guid householdId,
        int take,
        EvidenceCursorKey? after,
        CancellationToken cancellationToken = default);

    Task<EvidenceDetail?> GetAsync(
        Guid householdId,
        Guid evidenceId,
        CancellationToken cancellationToken = default);

    Task<StoredEvidenceAttachment?> ReadAttachmentAsync(
        Guid householdId,
        Guid evidenceId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the record's observations, or null when the record does not
    /// exist in the household.
    /// </summary>
    Task<EvidenceObservationList?> ListObservationsAsync(
        Guid householdId,
        Guid evidenceId,
        CancellationToken cancellationToken = default);
}

public sealed record EvidenceAccountReference(
    Guid AccountId,
    Guid HouseholdId,
    Guid? InstitutionId);

public sealed record EvidenceTransactionReference(
    Guid TransactionId,
    Guid HouseholdId,
    TransactionStatus Status);

public sealed record EvidenceRecordReference(
    Guid EvidenceId,
    Guid HouseholdId,
    Guid? SupersededByEvidenceId,
    DateOnly EffectiveDate,
    Guid? InstitutionId,
    Guid? AccountId);

public sealed record EvidencePortfolioReference(
    Guid PortfolioId,
    Guid HouseholdId);

public sealed record EvidenceAssetReference(
    Guid AssetId,
    AssetType AssetType);

public sealed record EvidenceObservationReferences(
    IReadOnlyList<EvidencePortfolioReference> Portfolios,
    IReadOnlyList<EvidenceAccountReference> Accounts,
    IReadOnlyList<EvidenceAssetReference> Assets);

/// <summary>
/// Retry operation codes for evidence commands. Evidence is not a ledger
/// write, so its receipts live apart from ledger command receipts.
/// </summary>
public static class EvidenceOperationCodes
{
    public const string RecordEvidence = "RECORD_EVIDENCE";
}

public sealed record EvidenceCommandScope(
    Guid HouseholdId,
    string OperationCode,
    string IdempotencyKey);

public sealed record EvidenceCommandReceipt(
    EvidenceCommandScope Scope,
    CommandFingerprint Fingerprint,
    Guid EvidenceId,
    DateTimeOffset CreatedAtUtc);

public sealed record EvidenceCommitResult(
    bool WasCommitted,
    EvidenceCommandReceipt Receipt);

public sealed record EvidenceCursorKey(
    DateOnly EffectiveDate,
    DateTimeOffset CreatedAtUtc,
    Guid EvidenceId);

/// <summary>
/// Attachment bytes exactly as stored, with the stored integrity metadata,
/// before verification.
/// </summary>
public sealed class StoredEvidenceAttachment
{
    public StoredEvidenceAttachment(
        Guid evidenceId,
        string fileName,
        EvidenceMediaType mediaType,
        long byteLength,
        string sha256,
        byte[] content)
    {
        EvidenceId = evidenceId;
        FileName = fileName;
        MediaType = mediaType;
        ByteLength = byteLength;
        Sha256 = sha256;
        Content = content;
    }

    public Guid EvidenceId { get; }

    public string FileName { get; }

    public EvidenceMediaType MediaType { get; }

    public long ByteLength { get; }

    public string Sha256 { get; }

    public byte[] Content { get; }

    public override string ToString() => nameof(StoredEvidenceAttachment);
}
