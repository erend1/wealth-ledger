using Microsoft.EntityFrameworkCore.Migrations;

namespace WealthLedger.Infrastructure.Persistence.Migrations;

/// <summary>
/// M010 evidence database guards.
/// </summary>
/// <remarks>
/// Evidence is adjunct audit data. These triggers keep a record, its links
/// and its retry receipt inside one household, admit links only to Posted
/// transactions, refuse new links on a superseded record, require an
/// attachment to carry its record's creation timestamp, and make every
/// evidence row, retry receipts included, insert-only. SQLite cannot see
/// transaction boundaries, so direct SQL that copies the timestamp can still
/// attach a file to an existing record; the Application writes an attachment
/// only in its record's own commit. The triggers touch no ledger table, so
/// every M001-M009 guard is unchanged. Down removes only these objects and the
/// evidence tables.
/// </remarks>
public partial class _009_EvidencePersistence
{
    private static readonly string[] TriggerNames =
    [
        "TR_EvidenceRecord_ValidateInsert",
        "TR_EvidenceRecord_PreventUpdate",
        "TR_EvidenceRecord_PreventDelete",
        "TR_EvidenceAttachment_ValidateInsert",
        "TR_EvidenceAttachment_PreventUpdate",
        "TR_EvidenceAttachment_PreventDelete",
        "TR_EvidenceTransactionLink_ValidateInsert",
        "TR_EvidenceTransactionLink_PreventUpdate",
        "TR_EvidenceTransactionLink_PreventDelete",
        "TR_EvidenceCommandReceipt_ValidateInsert",
        "TR_EvidenceCommandReceipt_PreventUpdate",
        "TR_EvidenceCommandReceipt_PreventDelete"
    ];

    private static void AddEvidenceGuards(MigrationBuilder migrationBuilder)
    {
        foreach (var sql in GuardSql)
        {
            migrationBuilder.Sql(sql);
        }
    }

    private static void DropEvidenceGuards(MigrationBuilder migrationBuilder)
    {
        foreach (var name in TriggerNames.Reverse())
        {
            migrationBuilder.Sql($"DROP TRIGGER IF EXISTS \"{name}\";");
        }
    }

    private static readonly string[] GuardSql =
    [
        RecordInsertSql,
        PreventUpdateSql("EvidenceRecord", "TR_EvidenceRecord_PreventUpdate"),
        PreventDeleteSql("EvidenceRecord", "TR_EvidenceRecord_PreventDelete"),
        AttachmentInsertSql,
        PreventUpdateSql("EvidenceAttachment", "TR_EvidenceAttachment_PreventUpdate"),
        PreventDeleteSql("EvidenceAttachment", "TR_EvidenceAttachment_PreventDelete"),
        LinkInsertSql,
        PreventUpdateSql("EvidenceTransactionLink", "TR_EvidenceTransactionLink_PreventUpdate"),
        PreventDeleteSql("EvidenceTransactionLink", "TR_EvidenceTransactionLink_PreventDelete"),
        ReceiptInsertSql,
        PreventUpdateSql("EvidenceCommandReceipt", "TR_EvidenceCommandReceipt_PreventUpdate"),
        PreventDeleteSql("EvidenceCommandReceipt", "TR_EvidenceCommandReceipt_PreventDelete")
    ];

    private const string RecordInsertSql =
        """
        CREATE TRIGGER "TR_EvidenceRecord_ValidateInsert"
        BEFORE INSERT ON "EvidenceRecord"
        BEGIN
            SELECT CASE WHEN NEW."AccountId" IS NOT NULL AND NOT EXISTS (
                SELECT 1
                FROM "Account" AS account
                WHERE account."Id" = NEW."AccountId"
                  AND account."HouseholdId" = NEW."HouseholdId"
                  AND (
                      NEW."InstitutionId" IS NULL
                      OR account."InstitutionId" = NEW."InstitutionId"
                  )
            ) THEN RAISE(ABORT, 'WL_M010_EVIDENCE_CONTEXT_INVALID') END;

            SELECT CASE WHEN NEW."SupersedesEvidenceId" IS NOT NULL AND NOT EXISTS (
                SELECT 1
                FROM "EvidenceRecord" AS superseded
                WHERE superseded."Id" = NEW."SupersedesEvidenceId"
                  AND superseded."HouseholdId" = NEW."HouseholdId"
            ) THEN RAISE(ABORT, 'WL_M010_EVIDENCE_SUPERSESSION_INVALID') END;
        END;
        """;

    private const string AttachmentInsertSql =
        """
        CREATE TRIGGER "TR_EvidenceAttachment_ValidateInsert"
        BEFORE INSERT ON "EvidenceAttachment"
        BEGIN
            SELECT CASE WHEN NOT EXISTS (
                SELECT 1
                FROM "EvidenceRecord" AS evidence
                WHERE evidence."Id" = NEW."EvidenceId"
                  AND evidence."CreatedAtUtc" = NEW."CreatedAtUtc"
            ) THEN RAISE(ABORT, 'WL_M010_EVIDENCE_ATTACHMENT_INVALID') END;
        END;
        """;

    private const string LinkInsertSql =
        """
        CREATE TRIGGER "TR_EvidenceTransactionLink_ValidateInsert"
        BEFORE INSERT ON "EvidenceTransactionLink"
        BEGIN
            SELECT CASE WHEN NOT EXISTS (
                SELECT 1
                FROM "EvidenceRecord" AS evidence
                JOIN "LedgerTransaction" AS tx
                  ON tx."Id" = NEW."LedgerTransactionId"
                WHERE evidence."Id" = NEW."EvidenceId"
                  AND tx."HouseholdId" = evidence."HouseholdId"
                  AND tx."StatusCode" = 'POSTED'
            ) THEN RAISE(ABORT, 'WL_M010_EVIDENCE_LINK_INVALID') END;

            SELECT CASE WHEN EXISTS (
                SELECT 1
                FROM "EvidenceRecord" AS successor
                WHERE successor."SupersedesEvidenceId" = NEW."EvidenceId"
            ) THEN RAISE(ABORT, 'WL_M010_EVIDENCE_SUPERSEDED') END;
        END;
        """;

    private const string ReceiptInsertSql =
        """
        CREATE TRIGGER "TR_EvidenceCommandReceipt_ValidateInsert"
        BEFORE INSERT ON "EvidenceCommandReceipt"
        BEGIN
            SELECT CASE WHEN NOT EXISTS (
                SELECT 1
                FROM "EvidenceRecord" AS evidence
                WHERE evidence."Id" = NEW."ResultEvidenceId"
                  AND evidence."HouseholdId" = NEW."HouseholdId"
            ) THEN RAISE(ABORT, 'WL_M010_EVIDENCE_RECEIPT_INVALID') END;
        END;
        """;

    private static string PreventUpdateSql(string table, string trigger)
        => $"""
            CREATE TRIGGER "{trigger}"
            BEFORE UPDATE ON "{table}"
            BEGIN
                SELECT RAISE(ABORT, 'WL_M010_EVIDENCE_IMMUTABLE');
            END;
            """;

    private static string PreventDeleteSql(string table, string trigger)
        => $"""
            CREATE TRIGGER "{trigger}"
            BEFORE DELETE ON "{table}"
            BEGIN
                SELECT RAISE(ABORT, 'WL_M010_EVIDENCE_IMMUTABLE');
            END;
            """;
}
