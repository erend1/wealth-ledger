using WealthLedger.Domain.Evidence;

namespace WealthLedger.Infrastructure.Persistence.Rows;

internal sealed class EvidenceRecordRow
{
    public Guid Id { get; set; }

    public Guid HouseholdId { get; set; }

    public EvidenceSourceKind SourceKind { get; set; }

    public DateOnly EffectiveDate { get; set; }

    public Guid? InstitutionId { get; set; }

    public Guid? AccountId { get; set; }

    public string Label { get; set; } = null!;

    public string? Reference { get; set; }

    public string? Note { get; set; }

    public Guid? SupersedesEvidenceId { get; set; }

    public DateTime CreatedAtUtc { get; set; }
}

internal sealed class EvidenceAttachmentRow
{
    public Guid EvidenceId { get; set; }

    public string FileName { get; set; } = null!;

    public EvidenceMediaType MediaType { get; set; }

    public long ByteLength { get; set; }

    public string Sha256 { get; set; } = null!;

    public byte[] Content { get; set; } = null!;

    public DateTime CreatedAtUtc { get; set; }
}

internal sealed class EvidenceTransactionLinkRow
{
    public Guid EvidenceId { get; set; }

    public Guid LedgerTransactionId { get; set; }

    public DateTime LinkedAtUtc { get; set; }
}

internal sealed class EvidenceCommandReceiptRow
{
    public Guid HouseholdId { get; set; }

    public string OperationCode { get; set; } = null!;

    public string IdempotencyKey { get; set; } = null!;

    public string FingerprintAlgorithmCode { get; set; } = null!;

    public int FingerprintVersion { get; set; }

    public string FingerprintValue { get; set; } = null!;

    public Guid ResultEvidenceId { get; set; }

    public DateTime CreatedAtUtc { get; set; }
}
