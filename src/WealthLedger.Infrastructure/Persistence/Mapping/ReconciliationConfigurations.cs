using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using WealthLedger.Infrastructure.Persistence.Rows;

namespace WealthLedger.Infrastructure.Persistence.Mapping;

internal sealed class EvidenceObservationConfiguration
    : IEntityTypeConfiguration<EvidenceObservationRow>
{
    public void Configure(EntityTypeBuilder<EvidenceObservationRow> builder)
    {
        builder.ToTable("EvidenceObservation", table =>
        {
            table.HasCheckConstraint(
                "CK_EvidenceObservation_Tolerance",
                "\"ToleranceE8\" >= 0");
            table.HasCheckConstraint(
                "CK_EvidenceObservation_PieceCount",
                "\"ObservedPieceCount\" IS NULL OR \"ObservedPieceCount\" BETWEEN 0 AND 2147483647");
        });

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id)
            .HasUuidTextConversion()
            .ValueGeneratedNever();

        builder.Property(x => x.EvidenceId)
            .HasUuidTextConversion();

        builder.Property(x => x.PortfolioId)
            .HasUuidTextConversion();

        builder.Property(x => x.AccountId)
            .HasUuidTextConversion();

        builder.Property(x => x.AssetId)
            .HasUuidTextConversion();

        builder.Property(x => x.ObservedQuantityE8)
            .HasColumnType("INTEGER");

        builder.Property(x => x.ToleranceE8)
            .HasColumnType("INTEGER");

        builder.Property(x => x.ObservedPieceCount)
            .HasColumnType("INTEGER");

        builder.Property(x => x.RecordedAtUtc)
            .HasUtcTimestampTextConversion();

        builder.HasOne<EvidenceRecordRow>()
            .WithMany()
            .HasForeignKey(x => x.EvidenceId)
            .OnDelete(DeleteBehavior.Restrict);

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

        // One statement states one quantity per exact scope; it also serves
        // the per-record observation reads.
        builder.HasIndex(
                x => new
                {
                    x.EvidenceId,
                    x.PortfolioId,
                    x.AccountId,
                    x.AssetId
                })
            .IsUnique()
            .HasDatabaseName("UX_EvidenceObservation_Evidence_Scope");

        builder.HasIndex(x => x.PortfolioId)
            .HasDatabaseName("IX_EvidenceObservation_Portfolio");

        builder.HasIndex(x => x.AccountId)
            .HasDatabaseName("IX_EvidenceObservation_Account");

        builder.HasIndex(x => x.AssetId)
            .HasDatabaseName("IX_EvidenceObservation_Asset");
    }
}

internal sealed class ReconciliationRunConfiguration
    : IEntityTypeConfiguration<ReconciliationRunRow>
{
    public void Configure(EntityTypeBuilder<ReconciliationRunRow> builder)
    {
        builder.ToTable("ReconciliationRun", table =>
        {
            table.HasCheckConstraint(
                "CK_ReconciliationRun_Method",
                "\"MethodCode\" IN ('EXACT_SCOPE_QUANTITY_AS_OF')");
            table.HasCheckConstraint(
                "CK_ReconciliationRun_MethodVersion",
                "\"MethodVersion\" >= 1");
            table.HasCheckConstraint(
                "CK_ReconciliationRun_ItemCount",
                "\"ItemCount\" BETWEEN 1 AND 100");
            table.HasCheckConstraint(
                "CK_ReconciliationRun_IdempotencyKey_Length",
                "length(\"IdempotencyKey\") BETWEEN 1 AND 256");
        });

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id)
            .HasUuidTextConversion()
            .ValueGeneratedNever();

        builder.Property(x => x.HouseholdId)
            .HasUuidTextConversion();

        builder.Property(x => x.EvidenceId)
            .HasUuidTextConversion();

        builder.Property(x => x.EvidenceEffectiveDate)
            .HasDateTextConversion();

        builder.Property(x => x.RunAtUtc)
            .HasUtcTimestampTextConversion();

        builder.Property(x => x.Method)
            .HasColumnName("MethodCode")
            .HasColumnType("TEXT")
            .HasMaxLength(64)
            .HasConversion(StableCodeMappings.ReconciliationMethodConverter);

        builder.Property(x => x.MethodVersion)
            .HasColumnType("INTEGER");

        builder.Property(x => x.ItemCount)
            .HasColumnType("INTEGER");

        builder.Property(x => x.IdempotencyKey)
            .HasColumnType("TEXT")
            .HasMaxLength(256)
            .IsRequired();

        builder.HasOne<HouseholdRow>()
            .WithMany()
            .HasForeignKey(x => x.HouseholdId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<EvidenceRecordRow>()
            .WithMany()
            .HasForeignKey(x => x.EvidenceId)
            .OnDelete(DeleteBehavior.Restrict);

        // The retry identity of a run: a replay finds the original run.
        builder.HasIndex(x => new { x.HouseholdId, x.IdempotencyKey })
            .IsUnique()
            .HasDatabaseName("UX_ReconciliationRun_Household_IdempotencyKey");

        // Serve the household run list and the per-evidence run list with
        // their keyset cursors.
        builder.HasIndex(x => new { x.HouseholdId, x.RunAtUtc, x.Id })
            .IsDescending(false, true, true)
            .HasDatabaseName("IX_ReconciliationRun_Household_RunAt_Id");

        builder.HasIndex(x => new { x.EvidenceId, x.RunAtUtc, x.Id })
            .IsDescending(false, true, true)
            .HasDatabaseName("IX_ReconciliationRun_Evidence_RunAt_Id");
    }
}

internal sealed class ReconciliationItemConfiguration
    : IEntityTypeConfiguration<ReconciliationItemRow>
{
    public void Configure(EntityTypeBuilder<ReconciliationItemRow> builder)
    {
        builder.ToTable("ReconciliationItem", table =>
        {
            table.HasCheckConstraint(
                "CK_ReconciliationItem_Sequence",
                "\"Sequence\" BETWEEN 1 AND 100");
            table.HasCheckConstraint(
                "CK_ReconciliationItem_Tolerance",
                "\"ToleranceE8\" >= 0");
            table.HasCheckConstraint(
                "CK_ReconciliationItem_Delta",
                "\"DeltaE8\" = \"ObservedQuantityE8\" - \"LedgerQuantityE8\" AND typeof(\"ObservedQuantityE8\" - \"LedgerQuantityE8\") = 'integer'");
            table.HasCheckConstraint(
                "CK_ReconciliationItem_Pieces",
                "(\"ObservedPieceCount\" IS NULL AND \"LedgerPieceCount\" IS NULL AND \"PieceDelta\" IS NULL) OR (\"ObservedPieceCount\" IS NOT NULL AND \"LedgerPieceCount\" IS NOT NULL AND \"PieceDelta\" IS NOT NULL AND \"ObservedPieceCount\" BETWEEN 0 AND 2147483647 AND \"LedgerPieceCount\" BETWEEN -2147483648 AND 2147483647 AND \"PieceDelta\" BETWEEN -2147483648 AND 2147483647 AND \"PieceDelta\" =\"ObservedPieceCount\" - \"LedgerPieceCount\")");
            table.HasCheckConstraint(
                "CK_ReconciliationItem_ComparisonStatus",
                "\"ComparisonStatusCode\" IN ('MATCHED', 'OUT_OF_TOLERANCE') AND (\"ComparisonStatusCode\" = 'MATCHED') = (\"DeltaE8\" BETWEEN -\"ToleranceE8\" AND \"ToleranceE8\" AND coalesce(\"PieceDelta\", 0) = 0)");
        });

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id)
            .HasUuidTextConversion()
            .ValueGeneratedNever();

        builder.Property(x => x.RunId)
            .HasUuidTextConversion();

        builder.Property(x => x.Sequence)
            .HasColumnType("INTEGER");

        builder.Property(x => x.ObservationId)
            .HasUuidTextConversion();

        builder.Property(x => x.PortfolioId)
            .HasUuidTextConversion();

        builder.Property(x => x.AccountId)
            .HasUuidTextConversion();

        builder.Property(x => x.AssetId)
            .HasUuidTextConversion();

        builder.Property(x => x.ObservedQuantityE8)
            .HasColumnType("INTEGER");

        builder.Property(x => x.LedgerQuantityE8)
            .HasColumnType("INTEGER");

        builder.Property(x => x.DeltaE8)
            .HasColumnType("INTEGER");

        builder.Property(x => x.ToleranceE8)
            .HasColumnType("INTEGER");

        builder.Property(x => x.ObservedPieceCount)
            .HasColumnType("INTEGER");

        builder.Property(x => x.LedgerPieceCount)
            .HasColumnType("INTEGER");

        builder.Property(x => x.PieceDelta)
            .HasColumnType("INTEGER");

        builder.Property(x => x.ComparisonStatus)
            .HasColumnName("ComparisonStatusCode")
            .HasColumnType("TEXT")
            .HasMaxLength(32)
            .HasConversion(StableCodeMappings.ReconciliationComparisonStatusConverter);

        builder.HasOne<ReconciliationRunRow>()
            .WithMany()
            .HasForeignKey(x => x.RunId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<EvidenceObservationRow>()
            .WithMany()
            .HasForeignKey(x => x.ObservationId)
            .OnDelete(DeleteBehavior.Restrict);

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

        // Items are read per run in their snapshot order.
        builder.HasIndex(x => new { x.RunId, x.Sequence })
            .IsUnique()
            .HasDatabaseName("UX_ReconciliationItem_Run_Sequence");

        builder.HasIndex(x => new { x.RunId, x.ObservationId })
            .IsUnique()
            .HasDatabaseName("UX_ReconciliationItem_Run_Observation");

        builder.HasIndex(x => x.ObservationId)
            .HasDatabaseName("IX_ReconciliationItem_Observation");

        builder.HasIndex(x => x.PortfolioId)
            .HasDatabaseName("IX_ReconciliationItem_Portfolio");

        builder.HasIndex(x => x.AccountId)
            .HasDatabaseName("IX_ReconciliationItem_Account");

        builder.HasIndex(x => x.AssetId)
            .HasDatabaseName("IX_ReconciliationItem_Asset");
    }
}

internal sealed class ReconciliationItemResolutionConfiguration
    : IEntityTypeConfiguration<ReconciliationItemResolutionRow>
{
    public void Configure(EntityTypeBuilder<ReconciliationItemResolutionRow> builder)
    {
        builder.ToTable("ReconciliationItemResolution", table =>
        {
            table.HasCheckConstraint(
                "CK_ReconciliationItemResolution_Shape",
                "(\"ResolutionCode\" = 'CORRECTED_BY_TRANSACTION' AND \"CorrectiveTransactionId\" IS NOT NULL AND \"Explanation\" IS NULL) OR (\"ResolutionCode\" = 'DOCUMENTED_EXCEPTION' AND \"CorrectiveTransactionId\" IS NULL AND \"Explanation\" IS NOT NULL AND length(\"Explanation\") BETWEEN 1 AND 2000)");
        });

        builder.HasKey(x => x.ItemId);

        builder.Property(x => x.ItemId)
            .HasUuidTextConversion()
            .ValueGeneratedNever();

        builder.Property(x => x.Kind)
            .HasColumnName("ResolutionCode")
            .HasColumnType("TEXT")
            .HasMaxLength(32)
            .HasConversion(StableCodeMappings.ReconciliationResolutionKindConverter);

        builder.Property(x => x.CorrectiveTransactionId)
            .HasUuidTextConversion();

        builder.Property(x => x.Explanation)
            .HasColumnType("TEXT")
            .HasMaxLength(2_000);

        builder.Property(x => x.ResolvedAtUtc)
            .HasUtcTimestampTextConversion();

        builder.HasOne<ReconciliationItemRow>()
            .WithOne()
            .HasForeignKey<ReconciliationItemResolutionRow>(x => x.ItemId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<LedgerTransactionRow>()
            .WithMany()
            .HasForeignKey(x => x.CorrectiveTransactionId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(x => x.CorrectiveTransactionId)
            .HasDatabaseName("IX_ReconciliationItemResolution_CorrectiveTransaction");
    }
}
