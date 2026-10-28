using Microsoft.EntityFrameworkCore.Migrations;

namespace WealthLedger.Infrastructure.Persistence.Migrations;

/// <summary>
/// M010 observation and reconciliation database guards.
/// </summary>
/// <remarks>
/// Observations and reconciliation runs are audit data beside the ledger.
/// These triggers keep an observation's exact scope inside its evidence
/// household and context, admit a piece count only for physical gold, cap a
/// record at 100 observations, and refuse observations and runs on superseded
/// evidence. A run must carry its evidence's household and effective date.
/// Each item must repeat its observation's scope and observed facts, belong to
/// the run's evidence, and fit the run's recorded item count. A resolution is
/// admitted only for an out-of-tolerance item, and a corrective transaction
/// must be Posted in the run's household. Every new row is insert-only. The
/// triggers touch no ledger table, so every M001-M009 guard is unchanged.
/// Down removes only these objects and the new tables.
/// </remarks>
public partial class _010_ReconciliationPersistence
{
    private static readonly string[] TriggerNames =
    [
        "TR_EvidenceObservation_ValidateInsert",
        "TR_EvidenceObservation_PreventUpdate",
        "TR_EvidenceObservation_PreventDelete",
        "TR_ReconciliationRun_ValidateInsert",
        "TR_ReconciliationRun_PreventUpdate",
        "TR_ReconciliationRun_PreventDelete",
        "TR_ReconciliationItem_ValidateInsert",
        "TR_ReconciliationItem_PreventUpdate",
        "TR_ReconciliationItem_PreventDelete",
        "TR_ReconciliationItemResolution_ValidateInsert",
        "TR_ReconciliationItemResolution_PreventUpdate",
        "TR_ReconciliationItemResolution_PreventDelete"
    ];

    private static void AddReconciliationGuards(MigrationBuilder migrationBuilder)
    {
        foreach (var sql in GuardSql)
        {
            migrationBuilder.Sql(sql);
        }
    }

    private static void DropReconciliationGuards(MigrationBuilder migrationBuilder)
    {
        foreach (var name in TriggerNames.Reverse())
        {
            migrationBuilder.Sql($"DROP TRIGGER IF EXISTS \"{name}\";");
        }
    }

    private static readonly string[] GuardSql =
    [
        ObservationInsertSql,
        PreventSql("UPDATE", "EvidenceObservation", "TR_EvidenceObservation_PreventUpdate", "WL_M010_EVIDENCE_IMMUTABLE"),
        PreventSql("DELETE", "EvidenceObservation", "TR_EvidenceObservation_PreventDelete", "WL_M010_EVIDENCE_IMMUTABLE"),
        RunInsertSql,
        PreventSql("UPDATE", "ReconciliationRun", "TR_ReconciliationRun_PreventUpdate", "WL_M010_RECONCILIATION_IMMUTABLE"),
        PreventSql("DELETE", "ReconciliationRun", "TR_ReconciliationRun_PreventDelete", "WL_M010_RECONCILIATION_IMMUTABLE"),
        ItemInsertSql,
        PreventSql("UPDATE", "ReconciliationItem", "TR_ReconciliationItem_PreventUpdate", "WL_M010_RECONCILIATION_IMMUTABLE"),
        PreventSql("DELETE", "ReconciliationItem", "TR_ReconciliationItem_PreventDelete", "WL_M010_RECONCILIATION_IMMUTABLE"),
        ResolutionInsertSql,
        PreventSql("UPDATE", "ReconciliationItemResolution", "TR_ReconciliationItemResolution_PreventUpdate", "WL_M010_RECONCILIATION_IMMUTABLE"),
        PreventSql("DELETE", "ReconciliationItemResolution", "TR_ReconciliationItemResolution_PreventDelete", "WL_M010_RECONCILIATION_IMMUTABLE")
    ];

    private const string ObservationInsertSql =
        """
        CREATE TRIGGER "TR_EvidenceObservation_ValidateInsert"
        BEFORE INSERT ON "EvidenceObservation"
        BEGIN
            SELECT CASE WHEN NOT EXISTS (
                SELECT 1
                FROM "EvidenceRecord" AS evidence
                JOIN "Portfolio" AS portfolio
                  ON portfolio."Id" = NEW."PortfolioId"
                JOIN "Account" AS account
                  ON account."Id" = NEW."AccountId"
                WHERE evidence."Id" = NEW."EvidenceId"
                  AND portfolio."HouseholdId" = evidence."HouseholdId"
                  AND account."HouseholdId" = evidence."HouseholdId"
                  AND (
                      evidence."AccountId" IS NULL
                      OR evidence."AccountId" = NEW."AccountId"
                  )
                  AND (
                      evidence."InstitutionId" IS NULL
                      OR account."InstitutionId" = evidence."InstitutionId"
                  )
            ) THEN RAISE(ABORT, 'WL_M010_OBSERVATION_SCOPE_INVALID') END;

            SELECT CASE WHEN NEW."ObservedPieceCount" IS NOT NULL AND NOT EXISTS (
                SELECT 1
                FROM "Asset" AS asset
                WHERE asset."Id" = NEW."AssetId"
                  AND asset."AssetTypeCode" = 'PHYSICAL_GOLD'
            ) THEN RAISE(ABORT, 'WL_M010_OBSERVATION_PIECES_INVALID') END;

            SELECT CASE WHEN EXISTS (
                SELECT 1
                FROM "EvidenceRecord" AS successor
                WHERE successor."SupersedesEvidenceId" = NEW."EvidenceId"
            ) THEN RAISE(ABORT, 'WL_M010_EVIDENCE_SUPERSEDED') END;

            SELECT CASE WHEN (
                SELECT COUNT(*)
                FROM "EvidenceObservation" AS existing
                WHERE existing."EvidenceId" = NEW."EvidenceId"
            ) >= 100 THEN RAISE(ABORT, 'WL_M010_OBSERVATION_LIMIT') END;
        END;
        """;

    private const string RunInsertSql =
        """
        CREATE TRIGGER "TR_ReconciliationRun_ValidateInsert"
        BEFORE INSERT ON "ReconciliationRun"
        BEGIN
            SELECT CASE WHEN NOT EXISTS (
                SELECT 1
                FROM "EvidenceRecord" AS evidence
                WHERE evidence."Id" = NEW."EvidenceId"
                  AND evidence."HouseholdId" = NEW."HouseholdId"
                  AND evidence."EffectiveDate" = NEW."EvidenceEffectiveDate"
            ) THEN RAISE(ABORT, 'WL_M010_RECONCILIATION_RUN_INVALID') END;

            SELECT CASE WHEN EXISTS (
                SELECT 1
                FROM "EvidenceRecord" AS successor
                WHERE successor."SupersedesEvidenceId" = NEW."EvidenceId"
            ) THEN RAISE(ABORT, 'WL_M010_EVIDENCE_SUPERSEDED') END;
        END;
        """;

    private const string ItemInsertSql =
        """
        CREATE TRIGGER "TR_ReconciliationItem_ValidateInsert"
        BEFORE INSERT ON "ReconciliationItem"
        BEGIN
            SELECT CASE WHEN NOT EXISTS (
                SELECT 1
                FROM "ReconciliationRun" AS run
                JOIN "EvidenceObservation" AS observation
                  ON observation."Id" = NEW."ObservationId"
                WHERE run."Id" = NEW."RunId"
                  AND observation."EvidenceId" = run."EvidenceId"
                  AND observation."PortfolioId" = NEW."PortfolioId"
                  AND observation."AccountId" = NEW."AccountId"
                  AND observation."AssetId" = NEW."AssetId"
                  AND observation."ObservedQuantityE8" = NEW."ObservedQuantityE8"
                  AND observation."ToleranceE8" = NEW."ToleranceE8"
                  AND observation."ObservedPieceCount" IS NEW."ObservedPieceCount"
                  AND NEW."Sequence" <= run."ItemCount"
                  AND (
                      SELECT COUNT(*)
                      FROM "ReconciliationItem" AS existing
                      WHERE existing."RunId" = NEW."RunId"
                  ) < run."ItemCount"
            ) THEN RAISE(ABORT, 'WL_M010_RECONCILIATION_ITEM_INVALID') END;
        END;
        """;

    private const string ResolutionInsertSql =
        """
        CREATE TRIGGER "TR_ReconciliationItemResolution_ValidateInsert"
        BEFORE INSERT ON "ReconciliationItemResolution"
        BEGIN
            SELECT CASE WHEN NOT EXISTS (
                SELECT 1
                FROM "ReconciliationItem" AS item
                WHERE item."Id" = NEW."ItemId"
                  AND item."ComparisonStatusCode" = 'OUT_OF_TOLERANCE'
            ) THEN RAISE(ABORT, 'WL_M010_RECONCILIATION_RESOLUTION_INVALID') END;

            SELECT CASE WHEN NEW."CorrectiveTransactionId" IS NOT NULL AND NOT EXISTS (
                SELECT 1
                FROM "ReconciliationItem" AS item
                JOIN "ReconciliationRun" AS run
                  ON run."Id" = item."RunId"
                JOIN "LedgerTransaction" AS tx
                  ON tx."Id" = NEW."CorrectiveTransactionId"
                WHERE item."Id" = NEW."ItemId"
                  AND tx."HouseholdId" = run."HouseholdId"
                  AND tx."StatusCode" = 'POSTED'
            ) THEN RAISE(ABORT, 'WL_M010_RECONCILIATION_CORRECTION_INVALID') END;
        END;
        """;

    private static string PreventSql(
        string operation,
        string table,
        string trigger,
        string message)
        => $"""
            CREATE TRIGGER "{trigger}"
            BEFORE {operation} ON "{table}"
            BEGIN
                SELECT RAISE(ABORT, '{message}');
            END;
            """;
}
