using WealthLedger.Domain.Reconciliation;

namespace WealthLedger.Infrastructure.Persistence.Rows;

internal sealed class EvidenceObservationRow
{
    public Guid Id { get; set; }

    public Guid EvidenceId { get; set; }

    public Guid PortfolioId { get; set; }

    public Guid AccountId { get; set; }

    public Guid AssetId { get; set; }

    public long ObservedQuantityE8 { get; set; }

    public long ToleranceE8 { get; set; }

    public int? ObservedPieceCount { get; set; }

    public DateTime RecordedAtUtc { get; set; }
}

internal sealed class ReconciliationRunRow
{
    public Guid Id { get; set; }

    public Guid HouseholdId { get; set; }

    public Guid EvidenceId { get; set; }

    public DateOnly EvidenceEffectiveDate { get; set; }

    public DateTime RunAtUtc { get; set; }

    public ReconciliationMethod Method { get; set; }

    public int MethodVersion { get; set; }

    public int ItemCount { get; set; }

    public string IdempotencyKey { get; set; } = null!;
}

internal sealed class ReconciliationItemRow
{
    public Guid Id { get; set; }

    public Guid RunId { get; set; }

    public int Sequence { get; set; }

    public Guid ObservationId { get; set; }

    public Guid PortfolioId { get; set; }

    public Guid AccountId { get; set; }

    public Guid AssetId { get; set; }

    public long ObservedQuantityE8 { get; set; }

    public long LedgerQuantityE8 { get; set; }

    public long DeltaE8 { get; set; }

    public long ToleranceE8 { get; set; }

    public int? ObservedPieceCount { get; set; }

    public int? LedgerPieceCount { get; set; }

    public int? PieceDelta { get; set; }

    public ReconciliationComparisonStatus ComparisonStatus { get; set; }
}

internal sealed class ReconciliationItemResolutionRow
{
    public Guid ItemId { get; set; }

    public ReconciliationResolutionKind Kind { get; set; }

    public Guid? CorrectiveTransactionId { get; set; }

    public string? Explanation { get; set; }

    public DateTime ResolvedAtUtc { get; set; }
}
