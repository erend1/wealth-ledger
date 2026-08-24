using WealthLedger.Domain.Assets;
using WealthLedger.Domain.Ledger;
using WealthLedger.Domain.Lots;
using WealthLedger.Domain.Portfolios;

namespace WealthLedger.Infrastructure.Persistence.Rows;

internal sealed class CurrencyRow
{
    public string Code { get; set; } = null!;

    public string Name { get; set; } = null!;

    public int MinorUnitDigits { get; set; }
}

internal sealed class HouseholdRow
{
    public Guid Id { get; set; }

    public string Name { get; set; } = null!;

    public string BaseCurrencyCode { get; set; } = null!;

    public DateTime CreatedAtUtc { get; set; }
}

internal sealed class HouseholdMemberRow
{
    public Guid Id { get; set; }

    public Guid HouseholdId { get; set; }

    public string DisplayName { get; set; } = null!;

    public bool IsActive { get; set; }

    public DateTime CreatedAtUtc { get; set; }
}

internal sealed class InstitutionRow
{
    public Guid Id { get; set; }

    public string Code { get; set; } = null!;

    public string Name { get; set; } = null!;

    public InstitutionType Type { get; set; }

    public bool IsActive { get; set; }
}

internal sealed class PortfolioRow
{
    public Guid Id { get; set; }

    public Guid HouseholdId { get; set; }

    public string Code { get; set; } = null!;

    public string Name { get; set; } = null!;

    public PortfolioStatus Status { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public DateTime? ClosedAtUtc { get; set; }
}

internal sealed class AccountRow
{
    public Guid Id { get; set; }

    public Guid HouseholdId { get; set; }

    public Guid? InstitutionId { get; set; }

    public string Code { get; set; } = null!;

    public string Name { get; set; } = null!;

    public AccountType Type { get; set; }

    public bool IsActive { get; set; }

    public DateOnly? OpenedOn { get; set; }

    public DateOnly? ClosedOn { get; set; }
}

internal sealed class AssetRow
{
    public Guid Id { get; set; }

    public string Code { get; set; } = null!;

    public string Name { get; set; } = null!;

    public AssetType Type { get; set; }

    public AssetUnit BaseUnit { get; set; }

    public string? BaseCurrencyCode { get; set; }

    public LotTrackingMode LotTrackingMode { get; set; }

    public bool IsActive { get; set; }

    public DateTime CreatedAtUtc { get; set; }
}

internal sealed class LedgerTransactionRow
{
    public Guid Id { get; set; }

    public Guid HouseholdId { get; set; }

    public TransactionType Type { get; set; }

    public TransactionStatus Status { get; set; }

    public DateOnly? OrderDate { get; set; }

    public DateOnly? ExecutionDate { get; set; }

    public DateOnly? SettlementDate { get; set; }

    public string? ExternalReference { get; set; }

    public string? Note { get; set; }

    public Guid? ReversalOfTransactionId { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public DateTime? PostedAtUtc { get; set; }
}

internal sealed class TransactionEntryRow
{
    public Guid Id { get; set; }

    public Guid TransactionId { get; set; }

    public int EntrySequence { get; set; }

    public Guid PortfolioId { get; set; }

    public Guid AccountId { get; set; }

    public Guid AssetId { get; set; }

    public long QuantityDeltaE8 { get; set; }

    public EntryRole Role { get; set; }

    public long? UnitPriceE8 { get; set; }

    public string? PriceCurrencyCode { get; set; }

    public DateTime CreatedAtUtc { get; set; }
}

internal sealed class CashFlowDetailRow
{
    public Guid TransactionId { get; set; }

    public CashFlowCategory Category { get; set; }

    public Guid? HouseholdMemberId { get; set; }
}

internal sealed class TransactionCostComponentRow
{
    public Guid Id { get; set; }

    public Guid TransactionId { get; set; }

    public CostType Type { get; set; }

    public CostTreatment Treatment { get; set; }

    public long AmountMinor { get; set; }

    public string CurrencyCode { get; set; } = null!;

    public string? Note { get; set; }
}

internal sealed class AssetLotRow
{
    public Guid Id { get; set; }

    public Guid AssetId { get; set; }

    public Guid OpeningTransactionEntryId { get; set; }

    public DateOnly? AcquiredOn { get; set; }

    public long? OriginalCostBasisMinor { get; set; }

    public string? CostBasisCurrencyCode { get; set; }

    public CostBasisStatus CostBasisStatus { get; set; }

    public DateTime CreatedAtUtc { get; set; }
}

internal sealed class LotEntryAllocationRow
{
    public Guid Id { get; set; }

    public Guid AssetLotId { get; set; }

    public Guid TransactionEntryId { get; set; }

    public long QuantityDeltaE8 { get; set; }

    public DateTime CreatedAtUtc { get; set; }
}

internal sealed class PhysicalGoldLotDetailRow
{
    public Guid AssetLotId { get; set; }

    public int ActualFinenessPpm { get; set; }

    public int PieceCount { get; set; }

    public string? Hallmark { get; set; }

    public string? CertificateReference { get; set; }

    public string? Note { get; set; }
}
