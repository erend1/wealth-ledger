using WealthLedger.Domain.Evidence;

namespace WealthLedger.Domain.Tests.Evidence;

public sealed class EvidenceRecordTests
{
    private static readonly Guid EvidenceId =
        Guid.Parse("e0000000-0000-0000-0000-000000000001");

    private static readonly Guid HouseholdId =
        Guid.Parse("e0000000-0000-0000-0000-0000000000a1");

    private static readonly DateOnly EffectiveDate = new(2026, 9, 30);

    private static readonly DateTimeOffset CreatedAt =
        new(2026, 10, 1, 12, 30, 0, TimeSpan.FromHours(3));

    [Fact]
    public void Create_MetadataOnlyRecordNormalizesTextAndUsesUtc()
    {
        var institutionId = Guid.NewGuid();
        var accountId = Guid.NewGuid();
        var previousId = Guid.NewGuid();

        var record = EvidenceRecord.Create(
            EvidenceId,
            HouseholdId,
            EvidenceSourceKind.Statement,
            EffectiveDate,
            "  Synthetic September statement  ",
            CreatedAt,
            institutionId,
            accountId,
            reference: "  SYN-REF-1  ",
            note: "  First line\r\nsecond\tline  ",
            supersedesEvidenceId: previousId);

        Assert.Equal(EvidenceId, record.Id);
        Assert.Equal(HouseholdId, record.HouseholdId);
        Assert.Equal(EvidenceSourceKind.Statement, record.SourceKind);
        Assert.Equal(EffectiveDate, record.EffectiveDate);
        Assert.Equal("Synthetic September statement", record.Label);
        Assert.Equal("SYN-REF-1", record.Reference);
        Assert.Equal("First line\r\nsecond\tline", record.Note);
        Assert.Equal(institutionId, record.InstitutionId);
        Assert.Equal(accountId, record.AccountId);
        Assert.Equal(previousId, record.SupersedesEvidenceId);
        Assert.Equal(TimeSpan.Zero, record.CreatedAtUtc.Offset);
        Assert.Equal(CreatedAt.UtcDateTime, record.CreatedAtUtc.UtcDateTime);
        Assert.Null(record.Attachment);
        Assert.Empty(record.TransactionLinks);
    }

    [Fact]
    public void Create_BlankOptionalTextBecomesAbsent()
    {
        var record = CreateRecord(reference: "   ", note: "\r\n");

        Assert.Null(record.Reference);
        Assert.Null(record.Note);
    }

    [Fact]
    public void Create_CarriesAttachmentAndInitialLinksAtCreationTime()
    {
        var attachment = EvidenceAttachment.Create(
            "count.csv",
            EvidenceMediaType.Csv,
            "asset,quantity\nbar,1\n"u8);
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();

        var record = CreateRecord(
            attachment: attachment,
            linkedTransactionIds: [first, second]);

        Assert.Same(attachment, record.Attachment);
        Assert.Collection(
            record.TransactionLinks,
            link =>
            {
                Assert.Equal(EvidenceId, link.EvidenceId);
                Assert.Equal(first, link.TransactionId);
                Assert.Equal(record.CreatedAtUtc, link.LinkedAtUtc);
            },
            link => Assert.Equal(second, link.TransactionId));
    }

    [Fact]
    public void Create_RejectsDuplicateTransactionLinks()
    {
        var transactionId = Guid.NewGuid();

        Assert.Throws<ArgumentException>(
            () => CreateRecord(
                linkedTransactionIds: [transactionId, transactionId]));
    }

    [Fact]
    public void Create_RejectsEmptyLinkedTransaction()
    {
        Assert.Throws<ArgumentException>(
            () => CreateRecord(linkedTransactionIds: [Guid.Empty]));
    }

    [Fact]
    public void Create_RejectsEmptyIdentities()
    {
        Assert.Throws<ArgumentException>(
            () => EvidenceRecord.Create(
                Guid.Empty,
                HouseholdId,
                EvidenceSourceKind.Receipt,
                EffectiveDate,
                "label",
                CreatedAt));
        Assert.Throws<ArgumentException>(
            () => EvidenceRecord.Create(
                EvidenceId,
                Guid.Empty,
                EvidenceSourceKind.Receipt,
                EffectiveDate,
                "label",
                CreatedAt));
        Assert.Throws<ArgumentException>(
            () => CreateRecord(institutionId: Guid.Empty));
        Assert.Throws<ArgumentException>(
            () => CreateRecord(accountId: Guid.Empty));
        Assert.Throws<ArgumentException>(
            () => CreateRecord(supersedesEvidenceId: Guid.Empty));
    }

    [Fact]
    public void Create_RejectsSelfSupersession()
    {
        Assert.Throws<ArgumentException>(
            () => CreateRecord(supersedesEvidenceId: EvidenceId));
    }

    [Fact]
    public void Create_RejectsUndefinedSourceKind()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => EvidenceRecord.Create(
                EvidenceId,
                HouseholdId,
                (EvidenceSourceKind)99,
                EffectiveDate,
                "label",
                CreatedAt));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("two\nlines")]
    [InlineData("bell\u0007")]
    public void Create_RejectsInvalidLabel(string label)
    {
        Assert.Throws<ArgumentException>(
            () => EvidenceRecord.Create(
                EvidenceId,
                HouseholdId,
                EvidenceSourceKind.Other,
                EffectiveDate,
                label,
                CreatedAt));
    }

    [Fact]
    public void TextBounds_AcceptTheLimitAndRejectOneMore()
    {
        Assert.True(EvidenceText.TryNormalizeLabel(new string('L', 256), out _));
        Assert.False(EvidenceText.TryNormalizeLabel(new string('L', 257), out _));
        Assert.True(EvidenceText.TryNormalizeReference(new string('R', 256), out _));
        Assert.False(EvidenceText.TryNormalizeReference(new string('R', 257), out _));
        Assert.True(EvidenceText.TryNormalizeNote(new string('N', 2_000), out _));
        Assert.False(EvidenceText.TryNormalizeNote(new string('N', 2_001), out _));
    }

    [Fact]
    public void TextRules_ReferenceIsSingleLineAndNoteRejectsOtherControls()
    {
        Assert.False(EvidenceText.TryNormalizeReference("a\tb", out _));
        Assert.True(EvidenceText.TryNormalizeNote("a\tb\nc", out _));
        Assert.False(EvidenceText.TryNormalizeNote("a\u0000b", out _));
        Assert.False(EvidenceText.TryNormalizeNote("a\u0085b", out _));
    }

    [Fact]
    public void TransactionLink_RejectsEmptyIdentitiesAndUsesUtc()
    {
        Assert.Throws<ArgumentException>(
            () => EvidenceTransactionLink.Create(Guid.Empty, Guid.NewGuid(), CreatedAt));
        Assert.Throws<ArgumentException>(
            () => EvidenceTransactionLink.Create(EvidenceId, Guid.Empty, CreatedAt));

        var link = EvidenceTransactionLink.Create(EvidenceId, Guid.NewGuid(), CreatedAt);

        Assert.Equal(TimeSpan.Zero, link.LinkedAtUtc.Offset);
    }

    private static EvidenceRecord CreateRecord(
        Guid? institutionId = null,
        Guid? accountId = null,
        string? reference = null,
        string? note = null,
        Guid? supersedesEvidenceId = null,
        EvidenceAttachment? attachment = null,
        IEnumerable<Guid>? linkedTransactionIds = null)
        => EvidenceRecord.Create(
            EvidenceId,
            HouseholdId,
            EvidenceSourceKind.PhysicalCount,
            EffectiveDate,
            "Synthetic physical count",
            CreatedAt,
            institutionId,
            accountId,
            reference,
            note,
            supersedesEvidenceId,
            attachment,
            linkedTransactionIds);
}
