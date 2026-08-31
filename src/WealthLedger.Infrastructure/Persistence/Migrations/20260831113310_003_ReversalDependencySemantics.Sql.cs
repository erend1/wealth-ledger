using Microsoft.EntityFrameworkCore.Migrations;

namespace WealthLedger.Infrastructure.Persistence.Migrations;

public partial class _003_ReversalDependencySemantics
{
    private const string TriggerName =
        "TR_LedgerTransaction_ValidateBeforePosting";

    private static void ReplacePostingTriggerWithNeutralizedDependencySemantics(
        MigrationBuilder migrationBuilder)
    {
        DropPostingTrigger(migrationBuilder);

        migrationBuilder.Sql(
            RevisedValidateBeforePostingTriggerSql);
    }

    private static void RestoreLegacyPostingTrigger(
        MigrationBuilder migrationBuilder)
    {
        DropPostingTrigger(migrationBuilder);

        migrationBuilder.Sql(
            LegacyValidateBeforePostingTriggerSql);
    }

    private static void DropPostingTrigger(
        MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            $"""DROP TRIGGER IF EXISTS "{TriggerName}";""");
    }

    private const string RevisedValidateBeforePostingTriggerSql =
        """
        CREATE TRIGGER "TR_LedgerTransaction_ValidateBeforePosting"
        BEFORE UPDATE OF "StatusCode" ON "LedgerTransaction"
        WHEN NEW."StatusCode" = 'POSTED'
             AND OLD."StatusCode" <> 'POSTED'
        BEGIN
            SELECT CASE
                WHEN NEW."TransactionTypeCode" = 'REVERSAL'
                 AND EXISTS (
                    SELECT 1

                    FROM "AssetLot" AS lot

                    JOIN "TransactionEntry" AS openingEntry
                      ON openingEntry."Id"
                         = lot."OpeningTransactionEntryId"

                    JOIN "LotEntryAllocation" AS dependentAllocation
                      ON dependentAllocation."AssetLotId"
                         = lot."Id"

                    JOIN "TransactionEntry" AS dependentEntry
                      ON dependentEntry."Id"
                         = dependentAllocation."TransactionEntryId"

                    JOIN "LedgerTransaction" AS dependentTransaction
                      ON dependentTransaction."Id"
                         = dependentEntry."TransactionId"

                    WHERE openingEntry."TransactionId"
                        = NEW."ReversalOfTransactionId"

                      AND dependentTransaction."StatusCode"
                        = 'POSTED'

                      AND dependentTransaction."TransactionTypeCode"
                        <> 'REVERSAL'

                      AND dependentTransaction."Id"
                        <> NEW."ReversalOfTransactionId"

                      AND NOT EXISTS (
                          SELECT 1

                          FROM "LedgerTransaction" AS dependentReversal

                          WHERE dependentReversal."TransactionTypeCode"
                                = 'REVERSAL'

                            AND dependentReversal."StatusCode"
                                = 'POSTED'

                            AND dependentReversal."ReversalOfTransactionId"
                                = dependentTransaction."Id"
                      )
                 )

                THEN RAISE(
                    ABORT,
                    'An acquisition cannot be reversed while later posted lot allocations depend on it.')
            END;
        END;
        """;

    private const string LegacyValidateBeforePostingTriggerSql =
        """
        CREATE TRIGGER "TR_LedgerTransaction_ValidateBeforePosting"
        BEFORE UPDATE OF "StatusCode" ON "LedgerTransaction"
        WHEN NEW."StatusCode" = 'POSTED'
             AND OLD."StatusCode" <> 'POSTED'
        BEGIN
            SELECT CASE
            WHEN NEW."TransactionTypeCode" = 'REVERSAL'
            AND EXISTS (
                SELECT 1

                FROM "AssetLot" AS lot

                JOIN "TransactionEntry" AS openingEntry
                    ON openingEntry."Id" = lot."OpeningTransactionEntryId"

                JOIN "LotEntryAllocation" AS dependentAllocation
                    ON dependentAllocation."AssetLotId" = lot."Id"

                JOIN "TransactionEntry" AS dependentEntry
                    ON dependentEntry."Id" = dependentAllocation."TransactionEntryId"

                JOIN "LedgerTransaction" AS dependentTransaction
                    ON dependentTransaction."Id" = dependentEntry."TransactionId"

                WHERE openingEntry."TransactionId"
                    = NEW."ReversalOfTransactionId"

                AND dependentTransaction."StatusCode" = 'POSTED'

                AND dependentTransaction."Id"
                    <> NEW."ReversalOfTransactionId"
            )
            THEN RAISE(
                ABORT,
                'An acquisition cannot be reversed while later posted lot allocations depend on it.')
            END;
        END;
        """;
}