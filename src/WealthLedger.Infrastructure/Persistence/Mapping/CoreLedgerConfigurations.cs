using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using WealthLedger.Infrastructure.Persistence.Rows;

namespace WealthLedger.Infrastructure.Persistence.Mapping;

internal sealed class CurrencyConfiguration : IEntityTypeConfiguration<CurrencyRow>
{
    public void Configure(EntityTypeBuilder<CurrencyRow> builder)
    {
        builder.ToTable("Currency", table =>
        {
            table.HasCheckConstraint(
                "CK_Currency_Code",
                "length(\"Code\") = 3 AND \"Code\" GLOB '[A-Z][A-Z][A-Z]'");
            table.HasCheckConstraint(
                "CK_Currency_MinorUnitDigits",
                "\"MinorUnitDigits\" BETWEEN 0 AND 8");
        });

        builder.HasKey(x => x.Code);

        builder.Property(x => x.Code)
            .HasColumnType("TEXT")
            .HasMaxLength(3)
            .ValueGeneratedNever();

        builder.Property(x => x.Name)
            .HasColumnType("TEXT")
            .HasMaxLength(128)
            .IsRequired();

        builder.Property(x => x.MinorUnitDigits)
            .HasColumnType("INTEGER");
    }
}

internal sealed class HouseholdConfiguration : IEntityTypeConfiguration<HouseholdRow>
{
    public void Configure(EntityTypeBuilder<HouseholdRow> builder)
    {
        builder.ToTable("Household");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id)
            .HasUuidTextConversion()
            .ValueGeneratedNever();

        builder.Property(x => x.Name)
            .HasColumnType("TEXT")
            .HasMaxLength(256)
            .IsRequired();

        builder.Property(x => x.BaseCurrencyCode)
            .HasColumnType("TEXT")
            .HasMaxLength(3)
            .IsRequired();

        builder.Property(x => x.CreatedAtUtc)
            .HasUtcTimestampTextConversion();

        builder.HasOne<CurrencyRow>()
            .WithMany()
            .HasForeignKey(x => x.BaseCurrencyCode)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class HouseholdMemberConfiguration : IEntityTypeConfiguration<HouseholdMemberRow>
{
    public void Configure(EntityTypeBuilder<HouseholdMemberRow> builder)
    {
        builder.ToTable("HouseholdMember", table =>
            table.HasCheckConstraint(
                "CK_HouseholdMember_IsActive",
                "\"IsActive\" IN (0, 1)"));

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id)
            .HasUuidTextConversion()
            .ValueGeneratedNever();

        builder.Property(x => x.HouseholdId)
            .HasUuidTextConversion();

        builder.Property(x => x.DisplayName)
            .HasColumnType("TEXT")
            .HasMaxLength(128)
            .IsRequired();

        builder.Property(x => x.IsActive)
            .HasColumnType("INTEGER");

        builder.Property(x => x.CreatedAtUtc)
            .HasUtcTimestampTextConversion();

        builder.HasOne<HouseholdRow>()
            .WithMany()
            .HasForeignKey(x => x.HouseholdId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(x => x.HouseholdId)
            .HasDatabaseName("IX_HouseholdMember_Household");
    }
}

internal sealed class InstitutionConfiguration : IEntityTypeConfiguration<InstitutionRow>
{
    public void Configure(EntityTypeBuilder<InstitutionRow> builder)
    {
        builder.ToTable("Institution", table =>
        {
            table.HasCheckConstraint(
                "CK_Institution_Type",
                "\"InstitutionTypeCode\" IN ('BANK', 'BROKER', 'ASSET_MANAGER', 'JEWELER', 'PENSION', 'OTHER')");
            table.HasCheckConstraint(
                "CK_Institution_IsActive",
                "\"IsActive\" IN (0, 1)");
        });

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id)
            .HasUuidTextConversion()
            .ValueGeneratedNever();

        builder.Property(x => x.Code)
            .HasColumnType("TEXT")
            .HasMaxLength(64)
            .IsRequired();

        builder.Property(x => x.Name)
            .HasColumnType("TEXT")
            .HasMaxLength(256)
            .IsRequired();

        builder.Property(x => x.Type)
            .HasColumnName("InstitutionTypeCode")
            .HasColumnType("TEXT")
            .HasMaxLength(32)
            .HasConversion(StableCodeMappings.InstitutionTypeConverter);

        builder.Property(x => x.IsActive)
            .HasColumnType("INTEGER");

        builder.HasIndex(x => x.Code)
            .IsUnique()
            .HasDatabaseName("UX_Institution_Code");
    }
}

internal sealed class PortfolioConfiguration : IEntityTypeConfiguration<PortfolioRow>
{
    public void Configure(EntityTypeBuilder<PortfolioRow> builder)
    {
        builder.ToTable("Portfolio", table =>
        {
            table.HasCheckConstraint(
                "CK_Portfolio_Status",
                "\"StatusCode\" IN ('ACTIVE', 'CLOSED', 'ARCHIVED')");
            table.HasCheckConstraint(
                "CK_Portfolio_ClosedAt",
                "(\"StatusCode\" = 'ACTIVE' AND \"ClosedAtUtc\" IS NULL) OR (\"StatusCode\" IN ('CLOSED', 'ARCHIVED') AND \"ClosedAtUtc\" IS NOT NULL)");
        });

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id)
            .HasUuidTextConversion()
            .ValueGeneratedNever();

        builder.Property(x => x.HouseholdId)
            .HasUuidTextConversion();

        builder.Property(x => x.Code)
            .HasColumnType("TEXT")
            .HasMaxLength(64)
            .IsRequired();

        builder.Property(x => x.Name)
            .HasColumnType("TEXT")
            .HasMaxLength(256)
            .IsRequired();

        builder.Property(x => x.Status)
            .HasColumnName("StatusCode")
            .HasColumnType("TEXT")
            .HasMaxLength(16)
            .HasConversion(StableCodeMappings.PortfolioStatusConverter);

        builder.Property(x => x.CreatedAtUtc)
            .HasUtcTimestampTextConversion();

        builder.Property(x => x.ClosedAtUtc)
            .HasUtcTimestampTextConversion();

        builder.HasOne<HouseholdRow>()
            .WithMany()
            .HasForeignKey(x => x.HouseholdId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(x => new { x.HouseholdId, x.Code })
            .IsUnique()
            .HasDatabaseName("UX_Portfolio_Household_Code");
    }
}

internal sealed class AccountConfiguration : IEntityTypeConfiguration<AccountRow>
{
    public void Configure(EntityTypeBuilder<AccountRow> builder)
    {
        builder.ToTable("Account", table =>
        {
            table.HasCheckConstraint(
                "CK_Account_Type",
                "\"AccountTypeCode\" IN ('CASH', 'INVESTMENT', 'PHYSICAL_VAULT', 'PENSION', 'PROPERTY_REGISTRY', 'OTHER')");
            table.HasCheckConstraint(
                "CK_Account_IsActive",
                "\"IsActive\" IN (0, 1)");
            table.HasCheckConstraint(
                "CK_Account_DateOrder",
                "\"OpenedOn\" IS NULL OR \"ClosedOn\" IS NULL OR \"OpenedOn\" <= \"ClosedOn\"");
        });

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id)
            .HasUuidTextConversion()
            .ValueGeneratedNever();

        builder.Property(x => x.HouseholdId)
            .HasUuidTextConversion();

        builder.Property(x => x.InstitutionId)
            .HasUuidTextConversion();

        builder.Property(x => x.Code)
            .HasColumnType("TEXT")
            .HasMaxLength(64)
            .IsRequired();

        builder.Property(x => x.Name)
            .HasColumnType("TEXT")
            .HasMaxLength(256)
            .IsRequired();

        builder.Property(x => x.Type)
            .HasColumnName("AccountTypeCode")
            .HasColumnType("TEXT")
            .HasMaxLength(32)
            .HasConversion(StableCodeMappings.AccountTypeConverter);

        builder.Property(x => x.IsActive)
            .HasColumnType("INTEGER");

        builder.Property(x => x.OpenedOn)
            .HasDateTextConversion();

        builder.Property(x => x.ClosedOn)
            .HasDateTextConversion();

        builder.HasOne<HouseholdRow>()
            .WithMany()
            .HasForeignKey(x => x.HouseholdId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<InstitutionRow>()
            .WithMany()
            .HasForeignKey(x => x.InstitutionId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(x => new { x.HouseholdId, x.Code })
            .IsUnique()
            .HasDatabaseName("UX_Account_Household_Code");
    }
}

internal sealed class AssetConfiguration : IEntityTypeConfiguration<AssetRow>
{
    public void Configure(EntityTypeBuilder<AssetRow> builder)
    {
        builder.ToTable("Asset", table =>
        {
            table.HasCheckConstraint(
                "CK_Asset_Type",
                "\"AssetTypeCode\" IN ('CASH', 'CURRENCY', 'FUND', 'EQUITY', 'PHYSICAL_GOLD', 'REAL_ESTATE', 'LAND', 'VEHICLE', 'OTHER')");
            table.HasCheckConstraint(
                "CK_Asset_BaseUnit",
                "\"BaseUnitCode\" IN ('CURRENCY_UNIT', 'FUND_UNIT', 'SHARE', 'GROSS_GRAM', 'PIECE', 'PROPERTY', 'LAND_PARCEL', 'VEHICLE', 'OTHER')");
            table.HasCheckConstraint(
                "CK_Asset_LotTrackingMode",
                "\"LotTrackingModeCode\" IN ('NONE', 'OPTIONAL', 'REQUIRED')");
            table.HasCheckConstraint(
                "CK_Asset_IsActive",
                "\"IsActive\" IN (0, 1)");
        });

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id)
            .HasUuidTextConversion()
            .ValueGeneratedNever();

        builder.Property(x => x.Code)
            .HasColumnType("TEXT")
            .HasMaxLength(64)
            .IsRequired();

        builder.Property(x => x.Name)
            .HasColumnType("TEXT")
            .HasMaxLength(256)
            .IsRequired();

        builder.Property(x => x.Type)
            .HasColumnName("AssetTypeCode")
            .HasColumnType("TEXT")
            .HasMaxLength(32)
            .HasConversion(StableCodeMappings.AssetTypeConverter);

        builder.Property(x => x.BaseUnit)
            .HasColumnName("BaseUnitCode")
            .HasColumnType("TEXT")
            .HasMaxLength(32)
            .HasConversion(StableCodeMappings.AssetUnitConverter);

        builder.Property(x => x.BaseCurrencyCode)
            .HasColumnType("TEXT")
            .HasMaxLength(3);

        builder.Property(x => x.LotTrackingMode)
            .HasColumnName("LotTrackingModeCode")
            .HasColumnType("TEXT")
            .HasMaxLength(16)
            .HasConversion(StableCodeMappings.LotTrackingModeConverter);

        builder.Property(x => x.IsActive)
            .HasColumnType("INTEGER");

        builder.Property(x => x.CreatedAtUtc)
            .HasUtcTimestampTextConversion();

        builder.HasOne<CurrencyRow>()
            .WithMany()
            .HasForeignKey(x => x.BaseCurrencyCode)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(x => x.Code)
            .IsUnique()
            .HasDatabaseName("UX_Asset_Code");
    }
}

internal sealed class LedgerTransactionConfiguration : IEntityTypeConfiguration<LedgerTransactionRow>
{
    public void Configure(EntityTypeBuilder<LedgerTransactionRow> builder)
    {
        builder.ToTable("LedgerTransaction", table =>
        {
            table.HasCheckConstraint(
                "CK_LedgerTransaction_Type",
                "\"TransactionTypeCode\" IN ('CONTRIBUTION', 'WITHDRAWAL', 'BUY', 'SELL', 'TRANSFER', 'DIVIDEND', 'INCOME', 'EXPENSE', 'FEE', 'TAX', 'CORPORATE_ACTION', 'OPENING_BALANCE', 'ADJUSTMENT', 'REVERSAL')");
            table.HasCheckConstraint(
                "CK_LedgerTransaction_Status",
                "\"StatusCode\" IN ('DRAFT', 'ORDERED', 'POSTED', 'CANCELLED')");
            table.HasCheckConstraint(
                "CK_LedgerTransaction_ReversalTarget",
                "\"ReversalOfTransactionId\" IS NULL OR \"ReversalOfTransactionId\" <> \"Id\"");
            table.HasCheckConstraint(
                "CK_LedgerTransaction_ReversalShape",
                "(\"TransactionTypeCode\" = 'REVERSAL' AND \"ReversalOfTransactionId\" IS NOT NULL) OR (\"TransactionTypeCode\" <> 'REVERSAL' AND \"ReversalOfTransactionId\" IS NULL)");
            table.HasCheckConstraint(
                "CK_LedgerTransaction_PostedAt",
                "(\"StatusCode\" = 'POSTED' AND \"PostedAtUtc\" IS NOT NULL) OR (\"StatusCode\" <> 'POSTED' AND \"PostedAtUtc\" IS NULL)");
            table.HasCheckConstraint(
                "CK_LedgerTransaction_Ordered",
                "\"StatusCode\" <> 'ORDERED' OR (\"TransactionTypeCode\" IN ('BUY', 'SELL') AND \"OrderDate\" IS NOT NULL)");
            table.HasCheckConstraint(
                "CK_LedgerTransaction_OrderExecutionDate",
                "\"OrderDate\" IS NULL OR \"ExecutionDate\" IS NULL OR \"OrderDate\" <= \"ExecutionDate\"");
            table.HasCheckConstraint(
                "CK_LedgerTransaction_ExecutionSettlementDate",
                "\"ExecutionDate\" IS NULL OR \"SettlementDate\" IS NULL OR \"ExecutionDate\" <= \"SettlementDate\"");
        });

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id)
            .HasUuidTextConversion()
            .ValueGeneratedNever();

        builder.Property(x => x.HouseholdId)
            .HasUuidTextConversion();

        builder.Property(x => x.Type)
            .HasColumnName("TransactionTypeCode")
            .HasColumnType("TEXT")
            .HasMaxLength(32)
            .HasConversion(StableCodeMappings.TransactionTypeConverter);

        builder.Property(x => x.Status)
            .HasColumnName("StatusCode")
            .HasColumnType("TEXT")
            .HasMaxLength(16)
            .HasConversion(StableCodeMappings.TransactionStatusConverter);

        builder.Property(x => x.OrderDate)
            .HasDateTextConversion();

        builder.Property(x => x.ExecutionDate)
            .HasDateTextConversion();

        builder.Property(x => x.SettlementDate)
            .HasDateTextConversion();

        builder.Property(x => x.ExternalReference)
            .HasColumnType("TEXT")
            .HasMaxLength(256);

        builder.Property(x => x.Note)
            .HasColumnType("TEXT")
            .HasMaxLength(2_000);

        builder.Property(x => x.ReversalOfTransactionId)
            .HasUuidTextConversion();

        builder.Property(x => x.CreatedAtUtc)
            .HasUtcTimestampTextConversion();

        builder.Property(x => x.PostedAtUtc)
            .HasUtcTimestampTextConversion();

        builder.HasOne<HouseholdRow>()
            .WithMany()
            .HasForeignKey(x => x.HouseholdId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<LedgerTransactionRow>()
            .WithMany()
            .HasForeignKey(x => x.ReversalOfTransactionId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(x => x.ReversalOfTransactionId)
            .IsUnique()
            .HasFilter("\"ReversalOfTransactionId\" IS NOT NULL")
            .HasDatabaseName("UX_LedgerTransaction_Reversal");

        builder.HasIndex(x => new { x.HouseholdId, x.Status, x.ExecutionDate })
            .HasDatabaseName("IX_LedgerTransaction_Household_Status_Date");

        builder.HasIndex(
                x => new
                {
                    x.HouseholdId,
                    x.Status,
                    x.PostedAtUtc,
                    x.Id
                })
            .IsDescending(false, false, true, true)
            .HasDatabaseName(
                "IX_LedgerTransaction_Household_Status_Posted_Id");
    }
}

internal sealed class TransactionEntryConfiguration : IEntityTypeConfiguration<TransactionEntryRow>
{
    public void Configure(EntityTypeBuilder<TransactionEntryRow> builder)
    {
        builder.ToTable("TransactionEntry", table =>
        {
            table.HasCheckConstraint(
                "CK_TransactionEntry_Sequence",
                "\"EntrySequence\" >= 0");
            table.HasCheckConstraint(
                "CK_TransactionEntry_Quantity",
                "\"QuantityDeltaE8\" <> 0");
            table.HasCheckConstraint(
                "CK_TransactionEntry_Role",
                "\"EntryRoleCode\" IN ('PRINCIPAL', 'CONSIDERATION', 'TRANSFER', 'INCOME', 'FEE', 'TAX', 'ADJUSTMENT')");
            table.HasCheckConstraint(
                "CK_TransactionEntry_UnitPrice",
                "\"UnitPriceE8\" IS NULL OR \"UnitPriceE8\" >= 0");
            table.HasCheckConstraint(
                "CK_TransactionEntry_UnitPriceCurrency",
                "(\"UnitPriceE8\" IS NULL AND \"PriceCurrencyCode\" IS NULL) OR (\"UnitPriceE8\" IS NOT NULL AND \"PriceCurrencyCode\" IS NOT NULL)");
        });

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id)
            .HasUuidTextConversion()
            .ValueGeneratedNever();

        builder.Property(x => x.TransactionId)
            .HasUuidTextConversion();

        builder.Property(x => x.EntrySequence)
            .HasColumnType("INTEGER");

        builder.Property(x => x.PortfolioId)
            .HasUuidTextConversion();

        builder.Property(x => x.AccountId)
            .HasUuidTextConversion();

        builder.Property(x => x.AssetId)
            .HasUuidTextConversion();

        builder.Property(x => x.QuantityDeltaE8)
            .HasColumnType("INTEGER");

        builder.Property(x => x.Role)
            .HasColumnName("EntryRoleCode")
            .HasColumnType("TEXT")
            .HasMaxLength(24)
            .HasConversion(StableCodeMappings.EntryRoleConverter);

        builder.Property(x => x.UnitPriceE8)
            .HasColumnType("INTEGER");

        builder.Property(x => x.PriceCurrencyCode)
            .HasColumnType("TEXT")
            .HasMaxLength(3);

        builder.Property(x => x.CreatedAtUtc)
            .HasUtcTimestampTextConversion();

        builder.HasOne<LedgerTransactionRow>()
            .WithMany()
            .HasForeignKey(x => x.TransactionId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<PortfolioRow>()
            .WithMany()
            .HasForeignKey(x => x.PortfolioId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<AccountRow>()
            .WithMany()
            .HasForeignKey(x => x.AccountId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<AssetRow>()
            .WithMany()
            .HasForeignKey(x => x.AssetId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<CurrencyRow>()
            .WithMany()
            .HasForeignKey(x => x.PriceCurrencyCode)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(x => new { x.TransactionId, x.EntrySequence })
            .IsUnique()
            .HasDatabaseName("UX_TransactionEntry_Transaction_Sequence");

        builder.HasIndex(x => new { x.PortfolioId, x.AssetId })
            .HasDatabaseName("IX_TransactionEntry_Portfolio_Asset");

        builder.HasIndex(x => new { x.AccountId, x.AssetId })
            .HasDatabaseName("IX_TransactionEntry_Account_Asset");
    }
}

internal sealed class CashFlowDetailConfiguration : IEntityTypeConfiguration<CashFlowDetailRow>
{
    public void Configure(EntityTypeBuilder<CashFlowDetailRow> builder)
    {
        builder.ToTable("CashFlowDetail", table =>
            table.HasCheckConstraint(
                "CK_CashFlowDetail_Category",
                "\"CashFlowCategoryCode\" IN ('SALARY', 'BONUS', 'ACADEMIC_INCOME', 'GIFT', 'EXTERNAL_SALE', 'OTHER')"));

        builder.HasKey(x => x.TransactionId);

        builder.Property(x => x.TransactionId)
            .HasUuidTextConversion()
            .ValueGeneratedNever();

        builder.Property(x => x.Category)
            .HasColumnName("CashFlowCategoryCode")
            .HasColumnType("TEXT")
            .HasMaxLength(32)
            .HasConversion(StableCodeMappings.CashFlowCategoryConverter);

        builder.Property(x => x.HouseholdMemberId)
            .HasUuidTextConversion();

        builder.HasOne<LedgerTransactionRow>()
            .WithOne()
            .HasForeignKey<CashFlowDetailRow>(x => x.TransactionId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<HouseholdMemberRow>()
            .WithMany()
            .HasForeignKey(x => x.HouseholdMemberId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class TransactionCostComponentConfiguration
    : IEntityTypeConfiguration<TransactionCostComponentRow>
{
    public void Configure(EntityTypeBuilder<TransactionCostComponentRow> builder)
    {
        builder.ToTable("TransactionCostComponent", table =>
        {
            table.HasCheckConstraint(
                "CK_TransactionCostComponent_Type",
                "\"CostTypeCode\" IN ('COMMISSION', 'WITHHOLDING_TAX', 'OTHER_TAX', 'MAKING_CHARGE', 'BROKERAGE', 'TITLE_DEED', 'EXPERTISE', 'NOTARY', 'INSURANCE', 'OTHER')");
            table.HasCheckConstraint(
                "CK_TransactionCostComponent_Treatment",
                "\"TreatmentCode\" IN ('ADDITIONAL_CASH_OUTFLOW', 'WITHHELD_FROM_PROCEEDS', 'INCLUDED_IN_CONSIDERATION', 'INFORMATIONAL_ONLY')");
            table.HasCheckConstraint(
                "CK_TransactionCostComponent_Amount",
                "\"AmountMinor\" >= 0");
        });

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id)
            .HasUuidTextConversion()
            .ValueGeneratedNever();

        builder.Property(x => x.TransactionId)
            .HasUuidTextConversion();

        builder.Property(x => x.Type)
            .HasColumnName("CostTypeCode")
            .HasColumnType("TEXT")
            .HasMaxLength(32)
            .HasConversion(StableCodeMappings.CostTypeConverter);

        builder.Property(x => x.Treatment)
            .HasColumnName("TreatmentCode")
            .HasColumnType("TEXT")
            .HasMaxLength(32)
            .HasConversion(StableCodeMappings.CostTreatmentConverter);

        builder.Property(x => x.AmountMinor)
            .HasColumnType("INTEGER");

        builder.Property(x => x.CurrencyCode)
            .HasColumnType("TEXT")
            .HasMaxLength(3)
            .IsRequired();

        builder.Property(x => x.Note)
            .HasColumnType("TEXT")
            .HasMaxLength(1_000);

        builder.HasOne<LedgerTransactionRow>()
            .WithMany()
            .HasForeignKey(x => x.TransactionId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<CurrencyRow>()
            .WithMany()
            .HasForeignKey(x => x.CurrencyCode)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(x => x.TransactionId)
            .HasDatabaseName("IX_TransactionCostComponent_Transaction");
    }
}

internal sealed class AssetLotConfiguration : IEntityTypeConfiguration<AssetLotRow>
{
    public void Configure(EntityTypeBuilder<AssetLotRow> builder)
    {
        builder.ToTable("AssetLot", table =>
        {
            table.HasCheckConstraint(
                "CK_AssetLot_CostBasisStatus",
                "\"CostBasisStatusCode\" IN ('KNOWN', 'UNKNOWN', 'NOT_APPLICABLE')");
            table.HasCheckConstraint(
                "CK_AssetLot_CostBasisAmount",
                "\"OriginalCostBasisMinor\" IS NULL OR \"OriginalCostBasisMinor\" >= 0");
            table.HasCheckConstraint(
                "CK_AssetLot_CostBasisShape",
                "(\"CostBasisStatusCode\" = 'KNOWN' AND \"OriginalCostBasisMinor\" IS NOT NULL AND \"CostBasisCurrencyCode\" IS NOT NULL) OR (\"CostBasisStatusCode\" IN ('UNKNOWN', 'NOT_APPLICABLE') AND \"OriginalCostBasisMinor\" IS NULL AND \"CostBasisCurrencyCode\" IS NULL)");
        });

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id)
            .HasUuidTextConversion()
            .ValueGeneratedNever();

        builder.Property(x => x.AssetId)
            .HasUuidTextConversion();

        builder.Property(x => x.OpeningTransactionEntryId)
            .HasUuidTextConversion();

        builder.Property(x => x.AcquiredOn)
            .HasDateTextConversion();

        builder.Property(x => x.OriginalCostBasisMinor)
            .HasColumnType("INTEGER");

        builder.Property(x => x.CostBasisCurrencyCode)
            .HasColumnType("TEXT")
            .HasMaxLength(3);

        builder.Property(x => x.CostBasisStatus)
            .HasColumnName("CostBasisStatusCode")
            .HasColumnType("TEXT")
            .HasMaxLength(24)
            .HasConversion(StableCodeMappings.CostBasisStatusConverter);

        builder.Property(x => x.CreatedAtUtc)
            .HasUtcTimestampTextConversion();

        builder.HasOne<AssetRow>()
            .WithMany()
            .HasForeignKey(x => x.AssetId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<TransactionEntryRow>()
            .WithMany()
            .HasForeignKey(x => x.OpeningTransactionEntryId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<CurrencyRow>()
            .WithMany()
            .HasForeignKey(x => x.CostBasisCurrencyCode)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(x => new { x.AssetId, x.AcquiredOn })
            .HasDatabaseName("IX_AssetLot_Asset_Date");
    }
}

internal sealed class LotEntryAllocationConfiguration
    : IEntityTypeConfiguration<LotEntryAllocationRow>
{
    public void Configure(EntityTypeBuilder<LotEntryAllocationRow> builder)
    {
        builder.ToTable("LotEntryAllocation", table =>
            table.HasCheckConstraint(
                "CK_LotEntryAllocation_Quantity",
                "\"QuantityDeltaE8\" <> 0"));

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id)
            .HasUuidTextConversion()
            .ValueGeneratedNever();

        builder.Property(x => x.AssetLotId)
            .HasUuidTextConversion();

        builder.Property(x => x.TransactionEntryId)
            .HasUuidTextConversion();

        builder.Property(x => x.QuantityDeltaE8)
            .HasColumnType("INTEGER");

        builder.Property(x => x.CreatedAtUtc)
            .HasUtcTimestampTextConversion();

        builder.HasOne<AssetLotRow>()
            .WithMany()
            .HasForeignKey(x => x.AssetLotId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<TransactionEntryRow>()
            .WithMany()
            .HasForeignKey(x => x.TransactionEntryId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(x => new { x.AssetLotId, x.TransactionEntryId })
            .IsUnique()
            .HasDatabaseName("UX_LotEntryAllocation_Lot_Entry");

        builder.HasIndex(x => x.AssetLotId)
            .HasDatabaseName("IX_LotEntryAllocation_Lot");

        builder.HasIndex(x => x.TransactionEntryId)
            .HasDatabaseName("IX_LotEntryAllocation_Entry");
    }
}

internal sealed class PhysicalGoldLotDetailConfiguration
    : IEntityTypeConfiguration<PhysicalGoldLotDetailRow>
{
    public void Configure(EntityTypeBuilder<PhysicalGoldLotDetailRow> builder)
    {
        builder.ToTable("PhysicalGoldLotDetail", table =>
        {
            table.HasCheckConstraint(
                "CK_PhysicalGoldLotDetail_Fineness",
                "\"ActualFinenessPpm\" > 0 AND \"ActualFinenessPpm\" <= 1000000");
            table.HasCheckConstraint(
                "CK_PhysicalGoldLotDetail_PieceCount",
                "\"PieceCount\" > 0");
        });

        builder.HasKey(x => x.AssetLotId);

        builder.Property(x => x.AssetLotId)
            .HasUuidTextConversion()
            .ValueGeneratedNever();

        builder.Property(x => x.ActualFinenessPpm)
            .HasColumnType("INTEGER");

        builder.Property(x => x.PieceCount)
            .HasColumnType("INTEGER");

        builder.Property(x => x.Hallmark)
            .HasColumnType("TEXT")
            .HasMaxLength(128);

        builder.Property(x => x.CertificateReference)
            .HasColumnType("TEXT")
            .HasMaxLength(256);

        builder.Property(x => x.Note)
            .HasColumnType("TEXT")
            .HasMaxLength(1_000);

        builder.HasOne<AssetLotRow>()
            .WithOne()
            .HasForeignKey<PhysicalGoldLotDetailRow>(x => x.AssetLotId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class CommandReceiptConfiguration
    : IEntityTypeConfiguration<CommandReceiptRow>
{
    public void Configure(
        EntityTypeBuilder<CommandReceiptRow> builder)
    {
        builder.ToTable(
            "CommandReceipt",
            table =>
            {
                table.HasCheckConstraint(
                    "CK_CommandReceipt_OperationCode_Length",
                    "length(\"OperationCode\") BETWEEN 1 AND 64");

                table.HasCheckConstraint(
                    "CK_CommandReceipt_IdempotencyKey_Length",
                    "length(\"IdempotencyKey\") BETWEEN 1 AND 256");

                table.HasCheckConstraint(
                    "CK_CommandReceipt_FingerprintAlgorithm_Length",
                    "length(\"FingerprintAlgorithmCode\") BETWEEN 1 AND 32");

                table.HasCheckConstraint(
                    "CK_CommandReceipt_FingerprintVersion",
                    "\"FingerprintVersion\" >= 1");

                table.HasCheckConstraint(
                    "CK_CommandReceipt_FingerprintValue_Length",
                    "length(\"FingerprintValue\") BETWEEN 1 AND 256");
            });

        builder.HasKey(
            x => new
            {
                x.HouseholdId,
                x.OperationCode,
                x.IdempotencyKey
            });

        builder.Property(x => x.HouseholdId)
            .HasUuidTextConversion();

        builder.Property(x => x.OperationCode)
            .HasColumnType("TEXT")
            .HasMaxLength(64)
            .IsRequired();

        builder.Property(x => x.IdempotencyKey)
            .HasColumnType("TEXT")
            .HasMaxLength(256)
            .IsRequired();

        builder.Property(x => x.FingerprintAlgorithmCode)
            .HasColumnType("TEXT")
            .HasMaxLength(32)
            .IsRequired();

        builder.Property(x => x.FingerprintVersion)
            .HasColumnType("INTEGER");

        builder.Property(x => x.FingerprintValue)
            .HasColumnType("TEXT")
            .HasMaxLength(256)
            .IsRequired();

        builder.Property(x => x.ResultTransactionId)
            .HasUuidTextConversion();

        builder.Property(x => x.ResultAssetLotId)
            .HasUuidTextConversion();

        builder.Property(x => x.CreatedAtUtc)
            .HasUtcTimestampTextConversion();

        builder.HasOne<HouseholdRow>()
            .WithMany()
            .HasForeignKey(x => x.HouseholdId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<LedgerTransactionRow>()
            .WithMany()
            .HasForeignKey(x => x.ResultTransactionId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<AssetLotRow>()
            .WithMany()
            .HasForeignKey(x => x.ResultAssetLotId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(x => x.ResultTransactionId)
            .HasDatabaseName(
                "IX_CommandReceipt_ResultTransaction");

        builder.HasIndex(x => x.ResultAssetLotId)
            .HasDatabaseName(
                "IX_CommandReceipt_ResultAssetLot");
    }
}
