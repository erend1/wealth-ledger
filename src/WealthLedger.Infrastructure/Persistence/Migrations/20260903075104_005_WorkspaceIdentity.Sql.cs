using Microsoft.EntityFrameworkCore.Migrations;

namespace WealthLedger.Infrastructure.Persistence.Migrations;

public partial class _005_WorkspaceIdentity
{
    /*
     * Migration 005 adds one durable, non-financial lineage identity to the
     * database file so a verified `.wlbackup` package can be proved to belong
     * to the workspace it is supposed to protect.
     *
     * The value is random and opaque. It carries no household, account,
     * asset, transaction, or other private fact, and no ledger row or
     * foreign key references it. It is deliberately absent from the EF model
     * so it can never be joined into a ledger query or mistaken for a
     * financial fact; Infrastructure reads it with a bounded direct query in
     * the same way it already reads `__EFMigrationsHistory`.
     *
     * The identity is generated inside SQLite rather than by the caller so a
     * database created through any supported path — the operations console,
     * design-time `database update`, or a test harness — receives its own
     * distinct value. A compile-time constant would give every database the
     * same identity and defeat the whole purpose.
     *
     * The seed is written as a guarded INSERT ... WHERE NOT EXISTS so a
     * repeated apply is a no-op rather than a second identity.
     */

    private const string TableName = "WorkspaceIdentity";

    private static void CreateWorkspaceIdentity(
        MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(CreateWorkspaceIdentityTableSql);
        migrationBuilder.Sql(SeedWorkspaceIdentitySql);
    }

    private static void DropWorkspaceIdentity(
        MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            $"""DROP TABLE IF EXISTS "{TableName}";""");
    }

    private const string CreateWorkspaceIdentityTableSql =
        """
        CREATE TABLE "WorkspaceIdentity" (
            "Id" INTEGER NOT NULL
                CONSTRAINT "PK_WorkspaceIdentity" PRIMARY KEY,
            "WorkspaceId" TEXT NOT NULL,
            "CreatedAtUtc" TEXT NOT NULL,
            CONSTRAINT "CK_WorkspaceIdentity_SingleRow"
                CHECK ("Id" = 1),
            CONSTRAINT "CK_WorkspaceIdentity_WorkspaceIdShape"
                CHECK (length("WorkspaceId") = 36
                       AND "WorkspaceId" = lower("WorkspaceId")
                       AND substr("WorkspaceId", 9, 1) = '-'
                       AND substr("WorkspaceId", 14, 1) = '-'
                       AND substr("WorkspaceId", 19, 1) = '-'
                       AND substr("WorkspaceId", 24, 1) = '-')
        );
        """;

    /*
     * `random() & 3` selects the RFC 4122 variant nibble without calling
     * abs(), which raises an integer-overflow error for the single value
     * -9223372036854775808.
     */
    private const string SeedWorkspaceIdentitySql =
        """
        INSERT INTO "WorkspaceIdentity" ("Id", "WorkspaceId", "CreatedAtUtc")
        SELECT
            1,
            lower(
                substr(hex(randomblob(4)), 1, 8) || '-'
                || substr(hex(randomblob(2)), 1, 4) || '-'
                || '4' || substr(hex(randomblob(2)), 2, 3) || '-'
                || substr('89ab', (random() & 3) + 1, 1)
                || substr(hex(randomblob(2)), 2, 3) || '-'
                || substr(hex(randomblob(6)), 1, 12)),
            strftime('%Y-%m-%dT%H:%M:%f0000Z', 'now')
        WHERE NOT EXISTS (SELECT 1 FROM "WorkspaceIdentity");
        """;
}
