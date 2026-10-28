using Microsoft.Data.Sqlite;

namespace WealthLedger.Infrastructure.Tests.Persistence;

/// <summary>
/// Proves the migration 009 evidence guards without the Application layer.
/// </summary>
public sealed class EvidenceDirectSqlGuardTests
{
    private const string CreatedAtUtc = "2026-10-01T09:00:00.0000000Z";

    private const string InsertRecordSql =
        """
        INSERT INTO EvidenceRecord (
            Id, HouseholdId, SourceKindCode, EffectiveDate, InstitutionId,
            AccountId, Label, Reference, Note, SupersedesEvidenceId, CreatedAtUtc)
        VALUES (
            $id, $household, $kind, '2026-09-30', $institution,
            $account, $label, NULL, NULL, $supersedes, $createdAt);
        """;

    private const string InsertAttachmentSql =
        """
        INSERT INTO EvidenceAttachment (
            EvidenceId, FileName, MediaTypeCode, ByteLength, Sha256, Content, CreatedAtUtc)
        VALUES ($id, $name, $media, $length, $sha, $content, $createdAt);
        """;

    private const string InsertLinkSql =
        """
        INSERT INTO EvidenceTransactionLink (EvidenceId, LedgerTransactionId, LinkedAtUtc)
        VALUES ($id, $transaction, '2026-10-01T09:00:00.0000000Z');
        """;

    private static readonly string ValidSha256 = new('a', 64);

    [Fact]
    public async Task DirectSql_RecordContextStaysInsideHouseholdAndInstitution()
    {
        await using var database = await SqliteTestDatabase.CreateAsync();
        await EvidenceTestData.SeedAsync(database.CreateContext);

        await AssertRejectedAsync(
            database,
            "WL_M010_EVIDENCE_CONTEXT_INVALID",
            InsertRecordSql,
            RecordParameters(Guid.NewGuid(), account: CoreLedgerTestData.OtherAccountId));
        await AssertRejectedAsync(
            database,
            "WL_M010_EVIDENCE_CONTEXT_INVALID",
            InsertRecordSql,
            RecordParameters(
                Guid.NewGuid(),
                institution: EvidenceTestData.SecondInstitutionId,
                account: CoreLedgerTestData.AccountId));
        await AssertRejectedAsync(
            database,
            "FOREIGN KEY constraint failed",
            InsertRecordSql,
            RecordParameters(Guid.NewGuid(), household: Guid.NewGuid()));
        await AssertRejectedAsync(
            database,
            "CK_EvidenceRecord_SourceKind",
            InsertRecordSql,
            RecordParameters(Guid.NewGuid(), kind: "INVOICE"));
        await AssertRejectedAsync(
            database,
            "CK_EvidenceRecord_Label_Length",
            InsertRecordSql,
            RecordParameters(Guid.NewGuid(), label: string.Empty));

        // The same shape with matching context is accepted.
        await database.ExecuteNonQueryAsync(
            InsertRecordSql,
            RecordParameters(
                Guid.NewGuid(),
                institution: CoreLedgerTestData.InstitutionId,
                account: CoreLedgerTestData.AccountId));
    }

    [Fact]
    public async Task DirectSql_SupersessionIsOneSameHouseholdChain()
    {
        await using var database = await SqliteTestDatabase.CreateAsync();
        await EvidenceTestData.SeedAsync(database.CreateContext);
        var original = Guid.NewGuid();
        await database.ExecuteNonQueryAsync(InsertRecordSql, RecordParameters(original));

        await AssertRejectedAsync(
            database,
            "WL_M010_EVIDENCE_SUPERSESSION_INVALID",
            InsertRecordSql,
            RecordParameters(
                Guid.NewGuid(),
                household: CoreLedgerTestData.OtherHouseholdId,
                supersedes: original));

        var self = Guid.NewGuid();
        await Assert.ThrowsAsync<SqliteException>(
            () => database.ExecuteNonQueryAsync(
                InsertRecordSql,
                RecordParameters(self, supersedes: self)));

        await database.ExecuteNonQueryAsync(
            InsertRecordSql,
            RecordParameters(Guid.NewGuid(), supersedes: original));

        await AssertRejectedAsync(
            database,
            "UNIQUE constraint failed: EvidenceRecord.SupersedesEvidenceId",
            InsertRecordSql,
            RecordParameters(Guid.NewGuid(), supersedes: original));
    }

    [Fact]
    public async Task DirectSql_AttachmentShapeIsEnforced()
    {
        await using var database = await SqliteTestDatabase.CreateAsync();
        await EvidenceTestData.SeedAsync(database.CreateContext);
        var evidenceId = Guid.NewGuid();
        await database.ExecuteNonQueryAsync(InsertRecordSql, RecordParameters(evidenceId));

        await AssertRejectedAsync(
            database,
            "WL_M010_EVIDENCE_ATTACHMENT_INVALID",
            InsertAttachmentSql,
            AttachmentParameters(evidenceId, createdAt: "2026-10-02T09:00:00.0000000Z"));
        await AssertRejectedAsync(
            database,
            "CK_EvidenceAttachment_Content",
            InsertAttachmentSql,
            AttachmentParameters(evidenceId, length: 5));
        await AssertRejectedAsync(
            database,
            "CK_EvidenceAttachment_Content",
            InsertAttachmentSql,
            AttachmentParameters(evidenceId, content: "TEXT"));
        await AssertRejectedAsync(
            database,
            "CK_EvidenceAttachment_Sha256",
            InsertAttachmentSql,
            AttachmentParameters(evidenceId, sha: new string('A', 64)));
        await AssertRejectedAsync(
            database,
            "CK_EvidenceAttachment_Sha256",
            InsertAttachmentSql,
            AttachmentParameters(evidenceId, sha: new string('a', 63)));
        await AssertRejectedAsync(
            database,
            "CK_EvidenceAttachment_MediaType",
            InsertAttachmentSql,
            AttachmentParameters(evidenceId, media: "SVG"));
        await AssertRejectedAsync(
            database,
            "CK_EvidenceAttachment_FileName_Length",
            InsertAttachmentSql,
            AttachmentParameters(evidenceId, name: string.Empty));
        await AssertRejectedAsync(
            database,
            "CK_EvidenceAttachment_Content",
            """
            INSERT INTO EvidenceAttachment (
                EvidenceId, FileName, MediaTypeCode, ByteLength, Sha256, Content, CreatedAtUtc)
            VALUES ($id, 'large.txt', 'PLAIN_TEXT', 10485761, $sha, zeroblob(10485761), $createdAt);
            """,
            new SqliteParameter("$id", evidenceId.ToString("D")),
            new SqliteParameter("$sha", ValidSha256),
            new SqliteParameter("$createdAt", CreatedAtUtc));

        // The valid shape is accepted, so each refusal above was its own rule.
        await database.ExecuteNonQueryAsync(
            InsertAttachmentSql,
            AttachmentParameters(evidenceId));
    }

    [Fact]
    public async Task DirectSql_LinksArePostedSameHouseholdAndNeverOnSupersededRecords()
    {
        await using var database = await SqliteTestDatabase.CreateAsync();
        var fixture = await EvidenceTestData.SeedAsync(database.CreateContext);
        var evidenceId = Guid.NewGuid();
        await database.ExecuteNonQueryAsync(InsertRecordSql, RecordParameters(evidenceId));

        foreach (var transactionId in new[]
                 {
                     fixture.DraftId,
                     fixture.OtherHouseholdPostedId,
                     Guid.NewGuid()
                 })
        {
            await AssertRejectedAsync(
                database,
                "WL_M010_EVIDENCE_LINK_INVALID",
                InsertLinkSql,
                LinkParameters(evidenceId, transactionId));
        }

        await database.ExecuteNonQueryAsync(
            InsertLinkSql,
            LinkParameters(evidenceId, fixture.FirstPostedId));
        await database.ExecuteNonQueryAsync(
            InsertRecordSql,
            RecordParameters(Guid.NewGuid(), supersedes: evidenceId));

        await AssertRejectedAsync(
            database,
            "WL_M010_EVIDENCE_SUPERSEDED",
            InsertLinkSql,
            LinkParameters(evidenceId, fixture.SecondPostedId));
    }

    [Fact]
    public async Task DirectSql_EvidenceRowsAreInsertOnly()
    {
        await using var database = await SqliteTestDatabase.CreateAsync();
        var fixture = await EvidenceTestData.SeedAsync(database.CreateContext);
        var evidenceId = Guid.NewGuid();
        await database.ExecuteNonQueryAsync(InsertRecordSql, RecordParameters(evidenceId));
        await database.ExecuteNonQueryAsync(InsertAttachmentSql, AttachmentParameters(evidenceId));
        await database.ExecuteNonQueryAsync(
            InsertLinkSql,
            LinkParameters(evidenceId, fixture.FirstPostedId));
        var id = new SqliteParameter("$id", evidenceId.ToString("D"));

        foreach (var sql in new[]
                 {
                     "UPDATE EvidenceRecord SET Label = 'Changed' WHERE Id = $id;",
                     "UPDATE EvidenceRecord SET EffectiveDate = '2026-01-01' WHERE Id = $id;",
                     "DELETE FROM EvidenceRecord WHERE Id = $id;",
                     "UPDATE EvidenceAttachment SET FileName = 'changed.pdf' WHERE EvidenceId = $id;",
                     "UPDATE EvidenceAttachment SET Content = x'25504446' WHERE EvidenceId = $id;",
                     "DELETE FROM EvidenceAttachment WHERE EvidenceId = $id;",
                     "UPDATE EvidenceTransactionLink SET LinkedAtUtc = '2026-01-01T00:00:00.0000000Z' WHERE EvidenceId = $id;",
                     "DELETE FROM EvidenceTransactionLink WHERE EvidenceId = $id;"
                 })
        {
            await AssertRejectedAsync(
                database,
                "WL_M010_EVIDENCE_IMMUTABLE",
                sql,
                new SqliteParameter("$id", id.Value));
        }
    }

    [Fact]
    public async Task DirectSql_ReferencedMastersCannotBeDeleted()
    {
        await using var database = await SqliteTestDatabase.CreateAsync();
        await EvidenceTestData.SeedAsync(database.CreateContext);
        await database.ExecuteNonQueryAsync(
            InsertRecordSql,
            RecordParameters(Guid.NewGuid(), institution: EvidenceTestData.SecondInstitutionId));

        await AssertRejectedAsync(
            database,
            "FOREIGN KEY constraint failed",
            "DELETE FROM Institution WHERE Id = $id;",
            new SqliteParameter("$id", EvidenceTestData.SecondInstitutionId.ToString("D")));
    }

    private static async Task AssertRejectedAsync(
        SqliteTestDatabase database,
        string expectedFragment,
        string sql,
        params SqliteParameter[] parameters)
    {
        var exception = await Assert.ThrowsAsync<SqliteException>(
            () => database.ExecuteNonQueryAsync(sql, parameters));

        Assert.Contains(expectedFragment, exception.Message, StringComparison.Ordinal);
    }

    private static SqliteParameter[] RecordParameters(
        Guid id,
        Guid? household = null,
        string kind = "STATEMENT",
        Guid? institution = null,
        Guid? account = null,
        string label = "Synthetic direct evidence",
        Guid? supersedes = null)
        =>
        [
            new("$id", id.ToString("D")),
            new("$household", (household ?? CoreLedgerTestData.HouseholdId).ToString("D")),
            new("$kind", kind),
            new("$institution", (object?)institution?.ToString("D") ?? DBNull.Value),
            new("$account", (object?)account?.ToString("D") ?? DBNull.Value),
            new("$label", label),
            new("$supersedes", (object?)supersedes?.ToString("D") ?? DBNull.Value),
            new("$createdAt", CreatedAtUtc)
        ];

    private static SqliteParameter[] AttachmentParameters(
        Guid evidenceId,
        string name = "synthetic.pdf",
        string media = "PDF",
        long length = 4,
        string? sha = null,
        object? content = null,
        string createdAt = CreatedAtUtc)
        =>
        [
            new("$id", evidenceId.ToString("D")),
            new("$name", name),
            new("$media", media),
            new("$length", length),
            new("$sha", sha ?? ValidSha256),
            new("$content", content ?? new byte[] { 0x25, 0x50, 0x44, 0x46 }),
            new("$createdAt", createdAt)
        ];

    private static SqliteParameter[] LinkParameters(Guid evidenceId, Guid transactionId)
        =>
        [
            new("$id", evidenceId.ToString("D")),
            new("$transaction", transactionId.ToString("D"))
        ];
}
