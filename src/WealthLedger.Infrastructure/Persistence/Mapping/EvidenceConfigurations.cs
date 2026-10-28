using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using WealthLedger.Infrastructure.Persistence.Rows;

namespace WealthLedger.Infrastructure.Persistence.Mapping;

internal sealed class EvidenceRecordConfiguration
    : IEntityTypeConfiguration<EvidenceRecordRow>
{
    public void Configure(EntityTypeBuilder<EvidenceRecordRow> builder)
    {
        builder.ToTable("EvidenceRecord", table =>
        {
            table.HasCheckConstraint(
                "CK_EvidenceRecord_SourceKind",
                "\"SourceKindCode\" IN ('STATEMENT', 'RECEIPT', 'PHYSICAL_COUNT', 'CONTRACT_OR_CERTIFICATE', 'OTHER')");
            table.HasCheckConstraint(
                "CK_EvidenceRecord_Label_Length",
                "length(\"Label\") BETWEEN 1 AND 256");
            table.HasCheckConstraint(
                "CK_EvidenceRecord_Reference_Length",
                "\"Reference\" IS NULL OR length(\"Reference\") BETWEEN 1 AND 256");
            table.HasCheckConstraint(
                "CK_EvidenceRecord_Note_Length",
                "\"Note\" IS NULL OR length(\"Note\") BETWEEN 1 AND 2000");
            table.HasCheckConstraint(
                "CK_EvidenceRecord_SupersedesOther",
                "\"SupersedesEvidenceId\" IS NULL OR \"SupersedesEvidenceId\" <> \"Id\"");
        });

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id)
            .HasUuidTextConversion()
            .ValueGeneratedNever();

        builder.Property(x => x.HouseholdId)
            .HasUuidTextConversion();

        builder.Property(x => x.SourceKind)
            .HasColumnName("SourceKindCode")
            .HasColumnType("TEXT")
            .HasMaxLength(32)
            .HasConversion(StableCodeMappings.EvidenceSourceKindConverter);

        builder.Property(x => x.EffectiveDate)
            .HasDateTextConversion();

        builder.Property(x => x.InstitutionId)
            .HasUuidTextConversion();

        builder.Property(x => x.AccountId)
            .HasUuidTextConversion();

        builder.Property(x => x.Label)
            .HasColumnType("TEXT")
            .HasMaxLength(256)
            .IsRequired();

        builder.Property(x => x.Reference)
            .HasColumnType("TEXT")
            .HasMaxLength(256);

        builder.Property(x => x.Note)
            .HasColumnType("TEXT")
            .HasMaxLength(2_000);

        builder.Property(x => x.SupersedesEvidenceId)
            .HasUuidTextConversion();

        builder.Property(x => x.CreatedAtUtc)
            .HasUtcTimestampTextConversion();

        builder.HasOne<HouseholdRow>()
            .WithMany()
            .HasForeignKey(x => x.HouseholdId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<InstitutionRow>()
            .WithMany()
            .HasForeignKey(x => x.InstitutionId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<AccountRow>()
            .WithMany()
            .HasForeignKey(x => x.AccountId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<EvidenceRecordRow>()
            .WithMany()
            .HasForeignKey(x => x.SupersedesEvidenceId)
            .OnDelete(DeleteBehavior.Restrict);

        // A record is superseded at most once, so corrections form one chain.
        builder.HasIndex(x => x.SupersedesEvidenceId)
            .IsUnique()
            .HasFilter("\"SupersedesEvidenceId\" IS NOT NULL")
            .HasDatabaseName("UX_EvidenceRecord_Supersedes");

        builder.HasIndex(x => x.InstitutionId)
            .HasDatabaseName("IX_EvidenceRecord_Institution");

        builder.HasIndex(x => x.AccountId)
            .HasDatabaseName("IX_EvidenceRecord_Account");

        // Serves the household evidence list and its keyset cursor.
        builder.HasIndex(
                x => new
                {
                    x.HouseholdId,
                    x.EffectiveDate,
                    x.CreatedAtUtc,
                    x.Id
                })
            .IsDescending(false, true, true, true)
            .HasDatabaseName("IX_EvidenceRecord_Household_Effective_Created_Id");
    }
}

internal sealed class EvidenceAttachmentConfiguration
    : IEntityTypeConfiguration<EvidenceAttachmentRow>
{
    public void Configure(EntityTypeBuilder<EvidenceAttachmentRow> builder)
    {
        builder.ToTable("EvidenceAttachment", table =>
        {
            table.HasCheckConstraint(
                "CK_EvidenceAttachment_FileName_Length",
                "length(\"FileName\") BETWEEN 1 AND 255");
            table.HasCheckConstraint(
                "CK_EvidenceAttachment_MediaType",
                "\"MediaTypeCode\" IN ('PDF', 'PNG', 'JPEG', 'PLAIN_TEXT', 'CSV')");
            table.HasCheckConstraint(
                "CK_EvidenceAttachment_Content",
                "typeof(\"Content\") = 'blob' AND \"ByteLength\" BETWEEN 1 AND 10485760 AND \"ByteLength\" = length(\"Content\")");
            table.HasCheckConstraint(
                "CK_EvidenceAttachment_Sha256",
                "length(\"Sha256\") = 64 AND \"Sha256\" NOT GLOB '*[^0-9a-f]*'");
        });

        builder.HasKey(x => x.EvidenceId);

        builder.Property(x => x.EvidenceId)
            .HasUuidTextConversion()
            .ValueGeneratedNever();

        builder.Property(x => x.FileName)
            .HasColumnType("TEXT")
            .HasMaxLength(255)
            .IsRequired();

        builder.Property(x => x.MediaType)
            .HasColumnName("MediaTypeCode")
            .HasColumnType("TEXT")
            .HasMaxLength(16)
            .HasConversion(StableCodeMappings.EvidenceMediaTypeConverter);

        builder.Property(x => x.ByteLength)
            .HasColumnType("INTEGER");

        builder.Property(x => x.Sha256)
            .HasColumnType("TEXT")
            .HasMaxLength(64)
            .IsRequired();

        builder.Property(x => x.Content)
            .HasColumnType("BLOB")
            .IsRequired();

        builder.Property(x => x.CreatedAtUtc)
            .HasUtcTimestampTextConversion();

        builder.HasOne<EvidenceRecordRow>()
            .WithOne()
            .HasForeignKey<EvidenceAttachmentRow>(x => x.EvidenceId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class EvidenceTransactionLinkConfiguration
    : IEntityTypeConfiguration<EvidenceTransactionLinkRow>
{
    public void Configure(EntityTypeBuilder<EvidenceTransactionLinkRow> builder)
    {
        builder.ToTable("EvidenceTransactionLink");

        builder.HasKey(x => new { x.EvidenceId, x.LedgerTransactionId });

        builder.Property(x => x.EvidenceId)
            .HasUuidTextConversion();

        builder.Property(x => x.LedgerTransactionId)
            .HasUuidTextConversion();

        builder.Property(x => x.LinkedAtUtc)
            .HasUtcTimestampTextConversion();

        builder.HasOne<EvidenceRecordRow>()
            .WithMany()
            .HasForeignKey(x => x.EvidenceId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<LedgerTransactionRow>()
            .WithMany()
            .HasForeignKey(x => x.LedgerTransactionId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(x => x.LedgerTransactionId)
            .HasDatabaseName("IX_EvidenceTransactionLink_Transaction");
    }
}

internal sealed class EvidenceCommandReceiptConfiguration
    : IEntityTypeConfiguration<EvidenceCommandReceiptRow>
{
    public void Configure(EntityTypeBuilder<EvidenceCommandReceiptRow> builder)
    {
        builder.ToTable("EvidenceCommandReceipt", table =>
        {
            table.HasCheckConstraint(
                "CK_EvidenceCommandReceipt_OperationCode_Length",
                "length(\"OperationCode\") BETWEEN 1 AND 64");
            table.HasCheckConstraint(
                "CK_EvidenceCommandReceipt_IdempotencyKey_Length",
                "length(\"IdempotencyKey\") BETWEEN 1 AND 256");
            table.HasCheckConstraint(
                "CK_EvidenceCommandReceipt_FingerprintAlgorithm_Length",
                "length(\"FingerprintAlgorithmCode\") BETWEEN 1 AND 32");
            table.HasCheckConstraint(
                "CK_EvidenceCommandReceipt_FingerprintVersion",
                "\"FingerprintVersion\" >= 1");
            table.HasCheckConstraint(
                "CK_EvidenceCommandReceipt_FingerprintValue_Length",
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

        builder.Property(x => x.ResultEvidenceId)
            .HasUuidTextConversion();

        builder.Property(x => x.CreatedAtUtc)
            .HasUtcTimestampTextConversion();

        builder.HasOne<HouseholdRow>()
            .WithMany()
            .HasForeignKey(x => x.HouseholdId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<EvidenceRecordRow>()
            .WithMany()
            .HasForeignKey(x => x.ResultEvidenceId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(x => x.ResultEvidenceId)
            .HasDatabaseName("IX_EvidenceCommandReceipt_ResultEvidence");
    }
}
