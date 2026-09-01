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

    /*
     * M003 changes only the dependency rule.
     *
     * A downstream transaction remains a blocker only when:
     * - it is posted,
     * - it is not itself a reversal,
     * - it is not the acquisition being reversed,
     * - and it does not already have its own posted reversal.
     */
    private const string RevisedDependencyBlock =
        """
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
                WHERE openingEntry."TransactionId" = NEW."ReversalOfTransactionId"
                  AND dependentTransaction."StatusCode" = 'POSTED'
                  AND dependentTransaction."TransactionTypeCode" <> 'REVERSAL'
                  AND dependentTransaction."Id" <> NEW."ReversalOfTransactionId"
                  AND NOT EXISTS (
                      SELECT 1
                      FROM "LedgerTransaction" AS dependentReversal
                      WHERE dependentReversal."TransactionTypeCode" = 'REVERSAL'
                        AND dependentReversal."StatusCode" = 'POSTED'
                        AND dependentReversal."ReversalOfTransactionId"
                            = dependentTransaction."Id"
                  )
             )
            THEN RAISE(ABORT, 'An acquisition cannot be reversed while later posted lot allocations depend on it.')
        END;
        """;

    /*
     * Complete original 001_CoreLedger posting-validation trigger.
     *
     * We keep one complete legacy copy here and derive the revised M003
     * trigger by replacing exactly one dependency block. This makes it much
     * harder to accidentally remove one of the already-proven persistence
     * invariants when the trigger evolves.
     */
    private const string LegacyValidateBeforePostingTriggerSql =
        """
        CREATE TRIGGER "TR_LedgerTransaction_ValidateBeforePosting"
        BEFORE UPDATE OF "StatusCode" ON "LedgerTransaction"
        WHEN NEW."StatusCode" = 'POSTED' AND OLD."StatusCode" <> 'POSTED'
        BEGIN
            SELECT CASE
                WHEN NEW."ExecutionDate" IS NULL
                THEN RAISE(ABORT, 'A posted transaction must have an execution date.')
            END;

            SELECT CASE
                WHEN NOT EXISTS (
                    SELECT 1
                    FROM "TransactionEntry" AS entry
                    WHERE entry."TransactionId" = NEW."Id"
                )
                THEN RAISE(ABORT, 'A transaction cannot be posted without entries.')
            END;

            SELECT CASE
                WHEN EXISTS (
                    SELECT 1
                    FROM "TransactionEntry" AS entry
                    JOIN "Asset" AS asset
                      ON asset."Id" = entry."AssetId"
                    WHERE entry."TransactionId" = NEW."Id"
                      AND asset."LotTrackingModeCode" = 'REQUIRED'
                      AND COALESCE((
                          SELECT SUM(allocation."QuantityDeltaE8")
                          FROM "LotEntryAllocation" AS allocation
                          WHERE allocation."TransactionEntryId" = entry."Id"
                      ), 0) <> entry."QuantityDeltaE8"
                )
                THEN RAISE(
                    ABORT,
                    'Required lot allocations must exactly match the transaction entry quantity.')
            END;

            SELECT CASE
                WHEN EXISTS (
                    SELECT 1
                    FROM "LotEntryAllocation" AS allocation
                    JOIN "TransactionEntry" AS entry
                      ON entry."Id" = allocation."TransactionEntryId"
                    JOIN "AssetLot" AS lot
                      ON lot."Id" = allocation."AssetLotId"
                    JOIN "TransactionEntry" AS openingEntry
                      ON openingEntry."Id" = lot."OpeningTransactionEntryId"
                    JOIN "LedgerTransaction" AS openingTransaction
                      ON openingTransaction."Id" = openingEntry."TransactionId"
                    WHERE entry."TransactionId" = NEW."Id"
                      AND openingTransaction."Id" <> NEW."Id"
                      AND openingTransaction."StatusCode" <> 'POSTED'
                )
                THEN RAISE(
                    ABORT,
                    'A posted allocation must reference a lot with posted acquisition lineage.')
            END;

            SELECT CASE
                WHEN EXISTS (
                    SELECT 1
                    FROM "AssetLot" AS lot
                    JOIN "TransactionEntry" AS openingEntry
                      ON openingEntry."Id" = lot."OpeningTransactionEntryId"
                    WHERE openingEntry."TransactionId" = NEW."Id"
                      AND NOT EXISTS (
                          SELECT 1
                          FROM "LotEntryAllocation" AS openingAllocation
                          WHERE openingAllocation."AssetLotId" = lot."Id"
                            AND openingAllocation."TransactionEntryId" = openingEntry."Id"
                            AND openingAllocation."QuantityDeltaE8" > 0
                      )
                )
                THEN RAISE(
                    ABORT,
                    'Every lot must have a positive allocation from its opening entry.')
            END;

            SELECT CASE
                WHEN EXISTS (
                    SELECT 1
                    FROM "LotEntryAllocation" AS currentAllocation
                    JOIN "TransactionEntry" AS currentEntry
                      ON currentEntry."Id" = currentAllocation."TransactionEntryId"
                    WHERE currentEntry."TransactionId" = NEW."Id"
                      AND COALESCE((
                          SELECT SUM(allocation."QuantityDeltaE8")
                          FROM "LotEntryAllocation" AS allocation
                          JOIN "TransactionEntry" AS entry
                            ON entry."Id" = allocation."TransactionEntryId"
                          JOIN "LedgerTransaction" AS tx
                            ON tx."Id" = entry."TransactionId"
                          WHERE allocation."AssetLotId"
                              = currentAllocation."AssetLotId"
                            AND (
                                tx."StatusCode" = 'POSTED'
                                OR tx."Id" = NEW."Id"
                            )
                      ), 0) < 0
                )
                THEN RAISE(
                    ABORT,
                    'Posting the transaction would make an effective lot quantity negative.')
            END;

            SELECT CASE
                WHEN NEW."TransactionTypeCode" = 'REVERSAL'
                 AND NOT EXISTS (
                    SELECT 1
                    FROM "LedgerTransaction" AS original
                    WHERE original."Id" = NEW."ReversalOfTransactionId"
                      AND original."StatusCode" = 'POSTED'
                      AND original."TransactionTypeCode" <> 'REVERSAL'
                      AND original."HouseholdId" = NEW."HouseholdId"
                      AND original."OrderDate" IS NEW."OrderDate"
                      AND original."ExecutionDate" IS NEW."ExecutionDate"
                      AND original."SettlementDate" IS NEW."SettlementDate"
                )
                THEN RAISE(
                    ABORT,
                    'A reversal must target a posted non-reversal transaction in the same household and preserve its effective dates.')
            END;

            SELECT CASE
                WHEN NEW."TransactionTypeCode" = 'REVERSAL'
                 AND (
                    (
                        SELECT COUNT(*)
                        FROM "TransactionEntry"
                        WHERE "TransactionId" = NEW."Id"
                    )
                    <>
                    (
                        SELECT COUNT(*)
                        FROM "TransactionEntry"
                        WHERE "TransactionId" = NEW."ReversalOfTransactionId"
                    )
                 )
                THEN RAISE(
                    ABORT,
                    'A reversal must contain exactly the original transaction entry count.')
            END;

            SELECT CASE
                WHEN NEW."TransactionTypeCode" = 'REVERSAL'
                 AND EXISTS (
                    SELECT 1
                    FROM "TransactionEntry" AS originalEntry
                    WHERE originalEntry."TransactionId"
                        = NEW."ReversalOfTransactionId"
                      AND (
                          originalEntry."QuantityDeltaE8"
                              = -9223372036854775808
                          OR NOT EXISTS (
                              SELECT 1
                              FROM "TransactionEntry" AS reversalEntry
                              WHERE reversalEntry."TransactionId" = NEW."Id"
                                AND reversalEntry."EntrySequence"
                                    = originalEntry."EntrySequence"
                                AND reversalEntry."PortfolioId"
                                    = originalEntry."PortfolioId"
                                AND reversalEntry."AccountId"
                                    = originalEntry."AccountId"
                                AND reversalEntry."AssetId"
                                    = originalEntry."AssetId"
                                AND reversalEntry."EntryRoleCode"
                                    = originalEntry."EntryRoleCode"
                                AND reversalEntry."QuantityDeltaE8"
                                    = -originalEntry."QuantityDeltaE8"
                                AND reversalEntry."UnitPriceE8"
                                    IS originalEntry."UnitPriceE8"
                                AND reversalEntry."PriceCurrencyCode"
                                    IS originalEntry."PriceCurrencyCode"
                          )
                      )
                )
                THEN RAISE(
                    ABORT,
                    'A reversal must mirror every original entry with the exact opposite quantity.')
            END;

            SELECT CASE
                WHEN NEW."TransactionTypeCode" = 'REVERSAL'
                 AND EXISTS (
                    SELECT 1
                    FROM "TransactionEntry" AS originalEntry
                    JOIN "LotEntryAllocation" AS originalAllocation
                      ON originalAllocation."TransactionEntryId"
                         = originalEntry."Id"
                    WHERE originalEntry."TransactionId"
                        = NEW."ReversalOfTransactionId"
                      AND (
                          originalAllocation."QuantityDeltaE8"
                              = -9223372036854775808
                          OR NOT EXISTS (
                              SELECT 1
                              FROM "TransactionEntry" AS reversalEntry
                              JOIN "LotEntryAllocation" AS reversalAllocation
                                ON reversalAllocation."TransactionEntryId"
                                   = reversalEntry."Id"
                              WHERE reversalEntry."TransactionId" = NEW."Id"
                                AND reversalEntry."EntrySequence"
                                    = originalEntry."EntrySequence"
                                AND reversalAllocation."AssetLotId"
                                    = originalAllocation."AssetLotId"
                                AND reversalAllocation."QuantityDeltaE8"
                                    = -originalAllocation."QuantityDeltaE8"
                          )
                      )
                )
                THEN RAISE(
                    ABORT,
                    'A reversal must mirror the original lot allocations on the same lots.')
            END;

            SELECT CASE
                WHEN NEW."TransactionTypeCode" = 'REVERSAL'
                 AND (
                    SELECT COUNT(*)
                    FROM "TransactionEntry" AS reversalEntry
                    JOIN "LotEntryAllocation" AS reversalAllocation
                      ON reversalAllocation."TransactionEntryId"
                         = reversalEntry."Id"
                    WHERE reversalEntry."TransactionId" = NEW."Id"
                 )
                 <>
                 (
                    SELECT COUNT(*)
                    FROM "TransactionEntry" AS originalEntry
                    JOIN "LotEntryAllocation" AS originalAllocation
                      ON originalAllocation."TransactionEntryId"
                         = originalEntry."Id"
                    WHERE originalEntry."TransactionId"
                        = NEW."ReversalOfTransactionId"
                 )
                THEN RAISE(
                    ABORT,
                    'A reversal must contain exactly the original lot allocation count.')
            END;

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
                    WHERE openingEntry."TransactionId" = NEW."ReversalOfTransactionId"
                      AND dependentTransaction."StatusCode" = 'POSTED'
                      AND dependentTransaction."Id" <> NEW."ReversalOfTransactionId"
                 )
                THEN RAISE(
                    ABORT,
                    'An acquisition cannot be reversed while later posted lot allocations depend on it.')
            END;

            SELECT CASE
                WHEN NEW."TransactionTypeCode" = 'REVERSAL'
                 AND (
                    EXISTS (
                        SELECT 1
                        FROM "TransactionCostComponent"
                        WHERE "TransactionId" = NEW."Id"
                    )
                    OR EXISTS (
                        SELECT 1
                        FROM "CashFlowDetail"
                        WHERE "TransactionId" = NEW."Id"
                    )
                 )
                THEN RAISE(
                    ABORT,
                    'A reversal cannot add cost or cash-flow metadata.')
            END;
        END;
        """;

    private static readonly string RevisedValidateBeforePostingTriggerSql =
        BuildRevisedValidateBeforePostingTriggerSql();

    private static string BuildRevisedValidateBeforePostingTriggerSql()
    {
        const string dependencyErrorMessage =
            "An acquisition cannot be reversed while later posted lot allocations depend on it.";

        var errorMessageIndex =
            LegacyValidateBeforePostingTriggerSql.IndexOf(
                dependencyErrorMessage,
                StringComparison.Ordinal);

        if (errorMessageIndex < 0)
        {
            throw new InvalidOperationException(
                "The legacy posting trigger does not contain the acquisition-dependency validation.");
        }

        var blockStartIndex =
            LegacyValidateBeforePostingTriggerSql.LastIndexOf(
                "SELECT CASE",
                errorMessageIndex,
                StringComparison.Ordinal);

        if (blockStartIndex < 0)
        {
            throw new InvalidOperationException(
                "The acquisition-dependency validation has no SELECT CASE start.");
        }

        var blockEndIndex =
            LegacyValidateBeforePostingTriggerSql.IndexOf(
                "END;",
                errorMessageIndex,
                StringComparison.Ordinal);

        if (blockEndIndex < 0)
        {
            throw new InvalidOperationException(
                "The acquisition-dependency validation has no END terminator.");
        }

        blockEndIndex +=
            "END;".Length;

        var secondErrorMessageIndex =
            LegacyValidateBeforePostingTriggerSql.IndexOf(
                dependencyErrorMessage,
                errorMessageIndex + dependencyErrorMessage.Length,
                StringComparison.Ordinal);

        if (secondErrorMessageIndex >= 0)
        {
            throw new InvalidOperationException(
                "The legacy posting trigger contains the acquisition-dependency validation more than once.");
        }

        var revised =
            string.Concat(
                LegacyValidateBeforePostingTriggerSql.AsSpan(
                    0,
                    blockStartIndex),
                RevisedDependencyBlock,
                LegacyValidateBeforePostingTriggerSql.AsSpan(
                    blockEndIndex));

        if (!revised.Contains(
                "dependentTransaction.\"TransactionTypeCode\" <> 'REVERSAL'",
                StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "The revised posting trigger does not exclude dependent reversal transactions.");
        }

        if (!revised.Contains(
                "dependentReversal.\"ReversalOfTransactionId\"",
                StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "The revised posting trigger does not recognize posted reversals of downstream dependencies.");
        }

        return revised;
    }
}