using Microsoft.EntityFrameworkCore.Migrations;

namespace WealthLedger.Infrastructure.Persistence.Migrations;

public partial class _001_CoreLedger
{
    private static void CreateCoreLedgerProtections(MigrationBuilder migrationBuilder)
    {
        CreateLedgerTransactionTriggers(migrationBuilder);
        CreateTransactionEntryTriggers(migrationBuilder);
        CreateTransactionCostTriggers(migrationBuilder);
        CreateCashFlowDetailTriggers(migrationBuilder);
        CreateAssetLotTriggers(migrationBuilder);
        CreatePhysicalGoldDetailTriggers(migrationBuilder);
        CreateLotAllocationTriggers(migrationBuilder);
        CreateMasterConsistencyTriggers(migrationBuilder);
    }

    private static void DropCoreLedgerProtections(MigrationBuilder migrationBuilder)
    {
        foreach (var triggerName in TriggerNames)
        {
            migrationBuilder.Sql($"DROP TRIGGER IF EXISTS \"{triggerName}\";");
        }
    }

    private static void CreateLedgerTransactionTriggers(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            CREATE TRIGGER "TR_LedgerTransaction_PreventPostedInsert"
            BEFORE INSERT ON "LedgerTransaction"
            WHEN NEW."StatusCode" = 'POSTED'
            BEGIN
                SELECT RAISE(ABORT, 'Transactions must be assembled before they are posted.');
            END;
            """);

        migrationBuilder.Sql(
            """
            CREATE TRIGGER "TR_LedgerTransaction_PreventPostedUpdate"
            BEFORE UPDATE ON "LedgerTransaction"
            WHEN OLD."StatusCode" = 'POSTED'
            BEGIN
                SELECT RAISE(ABORT, 'Posted transactions are immutable.');
            END;
            """);

        migrationBuilder.Sql(
            """
            CREATE TRIGGER "TR_LedgerTransaction_PreventPostedDelete"
            BEFORE DELETE ON "LedgerTransaction"
            WHEN OLD."StatusCode" = 'POSTED'
            BEGIN
                SELECT RAISE(ABORT, 'Posted transactions cannot be deleted.');
            END;
            """);

        migrationBuilder.Sql(
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
                        JOIN "Asset" AS asset ON asset."Id" = entry."AssetId"
                        WHERE entry."TransactionId" = NEW."Id"
                          AND asset."LotTrackingModeCode" = 'REQUIRED'
                          AND COALESCE((
                              SELECT SUM(allocation."QuantityDeltaE8")
                              FROM "LotEntryAllocation" AS allocation
                              WHERE allocation."TransactionEntryId" = entry."Id"
                          ), 0) <> entry."QuantityDeltaE8"
                    )
                    THEN RAISE(ABORT, 'Required lot allocations must exactly match the transaction entry quantity.')
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
                    THEN RAISE(ABORT, 'A posted allocation must reference a lot with posted acquisition lineage.')
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
                    THEN RAISE(ABORT, 'Every lot must have a positive allocation from its opening entry.')
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
                              WHERE allocation."AssetLotId" = currentAllocation."AssetLotId"
                                AND (tx."StatusCode" = 'POSTED'
                                     OR tx."Id" = NEW."Id")
                          ), 0) < 0
                    )
                    THEN RAISE(ABORT, 'Posting the transaction would make an effective lot quantity negative.')
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
                    THEN RAISE(ABORT, 'A reversal must target a posted non-reversal transaction in the same household and preserve its effective dates.')
                END;

                SELECT CASE
                    WHEN NEW."TransactionTypeCode" = 'REVERSAL'
                     AND (
                        (SELECT COUNT(*) FROM "TransactionEntry" WHERE "TransactionId" = NEW."Id")
                        <>
                        (SELECT COUNT(*) FROM "TransactionEntry" WHERE "TransactionId" = NEW."ReversalOfTransactionId")
                     )
                    THEN RAISE(ABORT, 'A reversal must contain exactly the original transaction entry count.')
                END;

                SELECT CASE
                    WHEN NEW."TransactionTypeCode" = 'REVERSAL'
                     AND EXISTS (
                        SELECT 1
                        FROM "TransactionEntry" AS originalEntry
                        WHERE originalEntry."TransactionId" = NEW."ReversalOfTransactionId"
                          AND (
                              originalEntry."QuantityDeltaE8" = -9223372036854775808
                              OR NOT EXISTS (
                                  SELECT 1
                                  FROM "TransactionEntry" AS reversalEntry
                                  WHERE reversalEntry."TransactionId" = NEW."Id"
                                    AND reversalEntry."EntrySequence" = originalEntry."EntrySequence"
                                    AND reversalEntry."PortfolioId" = originalEntry."PortfolioId"
                                    AND reversalEntry."AccountId" = originalEntry."AccountId"
                                    AND reversalEntry."AssetId" = originalEntry."AssetId"
                                    AND reversalEntry."EntryRoleCode" = originalEntry."EntryRoleCode"
                                    AND reversalEntry."QuantityDeltaE8" = -originalEntry."QuantityDeltaE8"
                                    AND reversalEntry."UnitPriceE8" IS originalEntry."UnitPriceE8"
                                    AND reversalEntry."PriceCurrencyCode" IS originalEntry."PriceCurrencyCode"
                              )
                          )
                    )
                    THEN RAISE(ABORT, 'A reversal must mirror every original entry with the exact opposite quantity.')
                END;

                SELECT CASE
                    WHEN NEW."TransactionTypeCode" = 'REVERSAL'
                     AND EXISTS (
                        SELECT 1
                        FROM "TransactionEntry" AS originalEntry
                        JOIN "LotEntryAllocation" AS originalAllocation
                          ON originalAllocation."TransactionEntryId" = originalEntry."Id"
                        WHERE originalEntry."TransactionId" = NEW."ReversalOfTransactionId"
                          AND (
                              originalAllocation."QuantityDeltaE8" = -9223372036854775808
                              OR NOT EXISTS (
                                  SELECT 1
                                  FROM "TransactionEntry" AS reversalEntry
                                  JOIN "LotEntryAllocation" AS reversalAllocation
                                    ON reversalAllocation."TransactionEntryId" = reversalEntry."Id"
                                  WHERE reversalEntry."TransactionId" = NEW."Id"
                                    AND reversalEntry."EntrySequence" = originalEntry."EntrySequence"
                                    AND reversalAllocation."AssetLotId" = originalAllocation."AssetLotId"
                                    AND reversalAllocation."QuantityDeltaE8" = -originalAllocation."QuantityDeltaE8"
                              )
                          )
                    )
                    THEN RAISE(ABORT, 'A reversal must mirror the original lot allocations on the same lots.')
                END;

                SELECT CASE
                    WHEN NEW."TransactionTypeCode" = 'REVERSAL'
                     AND (
                        SELECT COUNT(*)
                        FROM "TransactionEntry" AS reversalEntry
                        JOIN "LotEntryAllocation" AS reversalAllocation
                          ON reversalAllocation."TransactionEntryId" = reversalEntry."Id"
                        WHERE reversalEntry."TransactionId" = NEW."Id"
                     )
                     <>
                     (
                        SELECT COUNT(*)
                        FROM "TransactionEntry" AS originalEntry
                        JOIN "LotEntryAllocation" AS originalAllocation
                          ON originalAllocation."TransactionEntryId" = originalEntry."Id"
                        WHERE originalEntry."TransactionId" = NEW."ReversalOfTransactionId"
                     )
                    THEN RAISE(ABORT, 'A reversal must contain exactly the original lot allocation count.')
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
                    THEN RAISE(ABORT, 'An acquisition cannot be reversed while later posted lot allocations depend on it.')
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
                    THEN RAISE(ABORT, 'A reversal cannot add cost or cash-flow metadata.')
                END;
            END;
            """);

        migrationBuilder.Sql(
            """
            CREATE TRIGGER "TR_LedgerTransaction_ValidateHouseholdUpdate"
            BEFORE UPDATE OF "HouseholdId" ON "LedgerTransaction"
            WHEN EXISTS (
                SELECT 1
                FROM "TransactionEntry" AS entry
                JOIN "Account" AS account ON account."Id" = entry."AccountId"
                JOIN "Portfolio" AS portfolio ON portfolio."Id" = entry."PortfolioId"
                WHERE entry."TransactionId" = OLD."Id"
                  AND (account."HouseholdId" <> NEW."HouseholdId"
                       OR portfolio."HouseholdId" <> NEW."HouseholdId")
            )
             OR EXISTS (
                SELECT 1
                FROM "CashFlowDetail" AS detail
                JOIN "HouseholdMember" AS member
                  ON member."Id" = detail."HouseholdMemberId"
                WHERE detail."TransactionId" = OLD."Id"
                  AND member."HouseholdId" <> NEW."HouseholdId"
             )
            BEGIN
                SELECT RAISE(ABORT, 'Transaction facts must belong to the same household.');
            END;
            """);

        migrationBuilder.Sql(
            """
            CREATE TRIGGER "TR_LedgerTransaction_ValidateTypeUpdate"
            BEFORE UPDATE OF "TransactionTypeCode" ON "LedgerTransaction"
            WHEN NEW."TransactionTypeCode" <> 'CONTRIBUTION'
             AND EXISTS (
                SELECT 1
                FROM "CashFlowDetail"
                WHERE "TransactionId" = OLD."Id"
             )
            BEGIN
                SELECT RAISE(ABORT, 'Cash-flow detail is supported only for contribution transactions.');
            END;
            """);
    }

    private static void CreateTransactionEntryTriggers(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            CREATE TRIGGER "TR_TransactionEntry_ValidateInsert"
            BEFORE INSERT ON "TransactionEntry"
            BEGIN
                SELECT CASE
                    WHEN EXISTS (
                        SELECT 1
                        FROM "LedgerTransaction"
                        WHERE "Id" = NEW."TransactionId" AND "StatusCode" = 'POSTED'
                    )
                    THEN RAISE(ABORT, 'Cannot append entries to a posted transaction.')
                END;

                SELECT CASE
                    WHEN (SELECT "HouseholdId" FROM "LedgerTransaction" WHERE "Id" = NEW."TransactionId")
                         <> (SELECT "HouseholdId" FROM "Account" WHERE "Id" = NEW."AccountId")
                      OR (SELECT "HouseholdId" FROM "LedgerTransaction" WHERE "Id" = NEW."TransactionId")
                         <> (SELECT "HouseholdId" FROM "Portfolio" WHERE "Id" = NEW."PortfolioId")
                    THEN RAISE(ABORT, 'Transaction, account and portfolio must belong to the same household.')
                END;
            END;
            """);

        migrationBuilder.Sql(
            """
            CREATE TRIGGER "TR_TransactionEntry_ValidateUpdate"
            BEFORE UPDATE ON "TransactionEntry"
            BEGIN
                SELECT CASE
                    WHEN EXISTS (
                        SELECT 1 FROM "LedgerTransaction"
                        WHERE "Id" IN (OLD."TransactionId", NEW."TransactionId")
                          AND "StatusCode" = 'POSTED'
                    )
                    THEN RAISE(ABORT, 'Entries of posted transactions are immutable.')
                END;

                SELECT CASE
                    WHEN (SELECT "HouseholdId" FROM "LedgerTransaction" WHERE "Id" = NEW."TransactionId")
                         <> (SELECT "HouseholdId" FROM "Account" WHERE "Id" = NEW."AccountId")
                      OR (SELECT "HouseholdId" FROM "LedgerTransaction" WHERE "Id" = NEW."TransactionId")
                         <> (SELECT "HouseholdId" FROM "Portfolio" WHERE "Id" = NEW."PortfolioId")
                    THEN RAISE(ABORT, 'Transaction, account and portfolio must belong to the same household.')
                END;

                SELECT CASE
                    WHEN EXISTS (
                        SELECT 1
                        FROM "LotEntryAllocation" AS allocation
                        JOIN "AssetLot" AS lot ON lot."Id" = allocation."AssetLotId"
                        WHERE allocation."TransactionEntryId" = OLD."Id"
                          AND (
                              lot."AssetId" <> NEW."AssetId"
                              OR (allocation."QuantityDeltaE8" > 0 AND NEW."QuantityDeltaE8" < 0)
                              OR (allocation."QuantityDeltaE8" < 0 AND NEW."QuantityDeltaE8" > 0)
                          )
                    )
                    THEN RAISE(ABORT, 'Existing lot allocations must remain asset- and sign-consistent with their entry.')
                END;

                SELECT CASE
                    WHEN EXISTS (
                        SELECT 1
                        FROM "AssetLot"
                        WHERE "OpeningTransactionEntryId" = OLD."Id"
                          AND "AssetId" <> NEW."AssetId"
                    )
                    THEN RAISE(ABORT, 'An opening transaction entry must retain the asset of its lots.')
                END;

                SELECT CASE
                    WHEN NEW."QuantityDeltaE8" > 0
                     AND COALESCE((
                        SELECT SUM("QuantityDeltaE8")
                        FROM "LotEntryAllocation"
                        WHERE "TransactionEntryId" = OLD."Id"
                     ), 0) > NEW."QuantityDeltaE8"
                    THEN RAISE(ABORT, 'Lot allocations cannot exceed their transaction entry quantity.')
                END;

                SELECT CASE
                    WHEN NEW."QuantityDeltaE8" < 0
                     AND COALESCE((
                        SELECT SUM("QuantityDeltaE8")
                        FROM "LotEntryAllocation"
                        WHERE "TransactionEntryId" = OLD."Id"
                     ), 0) < NEW."QuantityDeltaE8"
                    THEN RAISE(ABORT, 'Lot allocations cannot exceed their transaction entry quantity.')
                END;
            END;
            """);

        migrationBuilder.Sql(
            """
            CREATE TRIGGER "TR_TransactionEntry_PreventPostedDelete"
            BEFORE DELETE ON "TransactionEntry"
            WHEN EXISTS (
                SELECT 1
                FROM "LedgerTransaction"
                WHERE "Id" = OLD."TransactionId" AND "StatusCode" = 'POSTED'
            )
            BEGIN
                SELECT RAISE(ABORT, 'Entries of posted transactions cannot be deleted.');
            END;
            """);
    }

    private static void CreateTransactionCostTriggers(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            CREATE TRIGGER "TR_TransactionCostComponent_PreventPostedInsert"
            BEFORE INSERT ON "TransactionCostComponent"
            WHEN EXISTS (
                SELECT 1 FROM "LedgerTransaction"
                WHERE "Id" = NEW."TransactionId" AND "StatusCode" = 'POSTED'
            )
            BEGIN
                SELECT RAISE(ABORT, 'Cannot append costs to a posted transaction.');
            END;
            """);

        migrationBuilder.Sql(
            """
            CREATE TRIGGER "TR_TransactionCostComponent_PreventPostedUpdate"
            BEFORE UPDATE ON "TransactionCostComponent"
            WHEN EXISTS (
                SELECT 1 FROM "LedgerTransaction"
                WHERE "Id" IN (OLD."TransactionId", NEW."TransactionId")
                  AND "StatusCode" = 'POSTED'
            )
            BEGIN
                SELECT RAISE(ABORT, 'Costs of posted transactions are immutable.');
            END;
            """);

        migrationBuilder.Sql(
            """
            CREATE TRIGGER "TR_TransactionCostComponent_PreventPostedDelete"
            BEFORE DELETE ON "TransactionCostComponent"
            WHEN EXISTS (
                SELECT 1 FROM "LedgerTransaction"
                WHERE "Id" = OLD."TransactionId" AND "StatusCode" = 'POSTED'
            )
            BEGIN
                SELECT RAISE(ABORT, 'Costs of posted transactions cannot be deleted.');
            END;
            """);
    }

    private static void CreateCashFlowDetailTriggers(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            CREATE TRIGGER "TR_CashFlowDetail_ValidateInsert"
            BEFORE INSERT ON "CashFlowDetail"
            BEGIN
                SELECT CASE
                    WHEN EXISTS (
                        SELECT 1 FROM "LedgerTransaction"
                        WHERE "Id" = NEW."TransactionId" AND "StatusCode" = 'POSTED'
                    )
                    THEN RAISE(ABORT, 'Cannot append cash-flow detail to a posted transaction.')
                END;

                SELECT CASE
                    WHEN (SELECT "TransactionTypeCode" FROM "LedgerTransaction" WHERE "Id" = NEW."TransactionId") <> 'CONTRIBUTION'
                    THEN RAISE(ABORT, 'Cash-flow detail is supported only for contribution transactions.')
                END;

                SELECT CASE
                    WHEN NEW."HouseholdMemberId" IS NOT NULL
                     AND (SELECT "HouseholdId" FROM "HouseholdMember" WHERE "Id" = NEW."HouseholdMemberId")
                         <> (SELECT "HouseholdId" FROM "LedgerTransaction" WHERE "Id" = NEW."TransactionId")
                    THEN RAISE(ABORT, 'Cash-flow member and transaction must belong to the same household.')
                END;
            END;
            """);

        migrationBuilder.Sql(
            """
            CREATE TRIGGER "TR_CashFlowDetail_ValidateUpdate"
            BEFORE UPDATE ON "CashFlowDetail"
            BEGIN
                SELECT CASE
                    WHEN EXISTS (
                        SELECT 1 FROM "LedgerTransaction"
                        WHERE "Id" IN (OLD."TransactionId", NEW."TransactionId")
                          AND "StatusCode" = 'POSTED'
                    )
                    THEN RAISE(ABORT, 'Cash-flow detail of posted transactions is immutable.')
                END;

                SELECT CASE
                    WHEN (SELECT "TransactionTypeCode" FROM "LedgerTransaction" WHERE "Id" = NEW."TransactionId") <> 'CONTRIBUTION'
                    THEN RAISE(ABORT, 'Cash-flow detail is supported only for contribution transactions.')
                END;

                SELECT CASE
                    WHEN NEW."HouseholdMemberId" IS NOT NULL
                     AND (SELECT "HouseholdId" FROM "HouseholdMember" WHERE "Id" = NEW."HouseholdMemberId")
                         <> (SELECT "HouseholdId" FROM "LedgerTransaction" WHERE "Id" = NEW."TransactionId")
                    THEN RAISE(ABORT, 'Cash-flow member and transaction must belong to the same household.')
                END;
            END;
            """);

        migrationBuilder.Sql(
            """
            CREATE TRIGGER "TR_CashFlowDetail_PreventPostedDelete"
            BEFORE DELETE ON "CashFlowDetail"
            WHEN EXISTS (
                SELECT 1 FROM "LedgerTransaction"
                WHERE "Id" = OLD."TransactionId" AND "StatusCode" = 'POSTED'
            )
            BEGIN
                SELECT RAISE(ABORT, 'Cash-flow detail of posted transactions cannot be deleted.');
            END;
            """);
    }

    private static void CreateAssetLotTriggers(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            CREATE TRIGGER "TR_AssetLot_ValidateInsert"
            BEFORE INSERT ON "AssetLot"
            BEGIN
                SELECT CASE
                    WHEN NEW."AssetId" <> (
                        SELECT "AssetId" FROM "TransactionEntry"
                        WHERE "Id" = NEW."OpeningTransactionEntryId"
                    )
                    THEN RAISE(ABORT, 'Opening entry asset does not match lot asset.')
                END;

                SELECT CASE
                    WHEN (
                        SELECT "QuantityDeltaE8" FROM "TransactionEntry"
                        WHERE "Id" = NEW."OpeningTransactionEntryId"
                    ) <= 0
                    THEN RAISE(ABORT, 'A lot must be opened by a positive transaction entry.')
                END;

                SELECT CASE
                    WHEN (
                        SELECT "LotTrackingModeCode" FROM "Asset"
                        WHERE "Id" = NEW."AssetId"
                    ) = 'NONE'
                    THEN RAISE(ABORT, 'A lot cannot be created for an asset without lot tracking.')
                END;

                SELECT CASE
                    WHEN EXISTS (
                        SELECT 1
                        FROM "TransactionEntry" AS entry
                        JOIN "LedgerTransaction" AS tx ON tx."Id" = entry."TransactionId"
                        WHERE entry."Id" = NEW."OpeningTransactionEntryId"
                          AND tx."StatusCode" = 'POSTED'
                    )
                    THEN RAISE(ABORT, 'A lot must be created before its opening transaction is posted.')
                END;
            END;
            """);

        migrationBuilder.Sql(
            """
            CREATE TRIGGER "TR_AssetLot_ValidateUpdate"
            BEFORE UPDATE ON "AssetLot"
            BEGIN
                SELECT CASE
                    WHEN EXISTS (
                        SELECT 1
                        FROM "TransactionEntry" AS openingEntry
                        JOIN "LedgerTransaction" AS openingTransaction
                          ON openingTransaction."Id" = openingEntry."TransactionId"
                        WHERE openingEntry."Id" = OLD."OpeningTransactionEntryId"
                          AND openingTransaction."StatusCode" = 'POSTED'
                    )
                     OR EXISTS (
                        SELECT 1
                        FROM "LotEntryAllocation" AS allocation
                        JOIN "TransactionEntry" AS entry ON entry."Id" = allocation."TransactionEntryId"
                        JOIN "LedgerTransaction" AS tx ON tx."Id" = entry."TransactionId"
                        WHERE allocation."AssetLotId" = OLD."Id"
                          AND tx."StatusCode" = 'POSTED'
                    )
                    THEN RAISE(ABORT, 'Lots participating in posted history are immutable.')
                END;

                SELECT CASE
                    WHEN NEW."AssetId" <> (
                        SELECT "AssetId" FROM "TransactionEntry"
                        WHERE "Id" = NEW."OpeningTransactionEntryId"
                    )
                     OR EXISTS (
                        SELECT 1
                        FROM "LotEntryAllocation" AS allocation
                        JOIN "TransactionEntry" AS entry ON entry."Id" = allocation."TransactionEntryId"
                        WHERE allocation."AssetLotId" = OLD."Id"
                          AND entry."AssetId" <> NEW."AssetId"
                     )
                    THEN RAISE(ABORT, 'Lot, opening entry and allocated entries must reference the same asset.')
                END;

                SELECT CASE
                    WHEN (
                        SELECT "QuantityDeltaE8" FROM "TransactionEntry"
                        WHERE "Id" = NEW."OpeningTransactionEntryId"
                    ) <= 0
                    THEN RAISE(ABORT, 'A lot must be opened by a positive transaction entry.')
                END;

                SELECT CASE
                    WHEN (
                        SELECT "LotTrackingModeCode" FROM "Asset"
                        WHERE "Id" = NEW."AssetId"
                    ) = 'NONE'
                    THEN RAISE(ABORT, 'A lot cannot reference an asset without lot tracking.')
                END;

                SELECT CASE
                    WHEN EXISTS (
                        SELECT 1
                        FROM "TransactionEntry" AS entry
                        JOIN "LedgerTransaction" AS tx ON tx."Id" = entry."TransactionId"
                        WHERE entry."Id" = NEW."OpeningTransactionEntryId"
                          AND tx."StatusCode" = 'POSTED'
                    )
                    THEN RAISE(ABORT, 'A lot cannot be reassigned to an already posted opening entry.')
                END;
            END;
            """);

        migrationBuilder.Sql(
            """
            CREATE TRIGGER "TR_AssetLot_PreventPostedDelete"
            BEFORE DELETE ON "AssetLot"
            WHEN EXISTS (
                SELECT 1
                FROM "TransactionEntry" AS openingEntry
                JOIN "LedgerTransaction" AS openingTransaction
                  ON openingTransaction."Id" = openingEntry."TransactionId"
                WHERE openingEntry."Id" = OLD."OpeningTransactionEntryId"
                  AND openingTransaction."StatusCode" = 'POSTED'
            )
             OR EXISTS (
                SELECT 1
                FROM "LotEntryAllocation" AS allocation
                JOIN "TransactionEntry" AS entry ON entry."Id" = allocation."TransactionEntryId"
                JOIN "LedgerTransaction" AS tx ON tx."Id" = entry."TransactionId"
                WHERE allocation."AssetLotId" = OLD."Id"
                  AND tx."StatusCode" = 'POSTED'
            )
            BEGIN
                SELECT RAISE(ABORT, 'Lots participating in posted history cannot be deleted.');
            END;
            """);
    }

    private static void CreatePhysicalGoldDetailTriggers(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            CREATE TRIGGER "TR_PhysicalGoldLotDetail_ValidateInsert"
            BEFORE INSERT ON "PhysicalGoldLotDetail"
            BEGIN
                SELECT CASE
                    WHEN (
                        SELECT asset."AssetTypeCode"
                        FROM "AssetLot" AS lot
                        JOIN "Asset" AS asset ON asset."Id" = lot."AssetId"
                        WHERE lot."Id" = NEW."AssetLotId"
                    ) <> 'PHYSICAL_GOLD'
                    THEN RAISE(ABORT, 'Physical-gold detail can be attached only to a physical-gold lot.')
                END;

                SELECT CASE
                    WHEN EXISTS (
                        SELECT 1
                        FROM "AssetLot" AS lot
                        JOIN "TransactionEntry" AS entry ON entry."Id" = lot."OpeningTransactionEntryId"
                        JOIN "LedgerTransaction" AS tx ON tx."Id" = entry."TransactionId"
                        WHERE lot."Id" = NEW."AssetLotId"
                          AND tx."StatusCode" = 'POSTED'
                    )
                    THEN RAISE(ABORT, 'Physical-gold detail must be added before the opening transaction is posted.')
                END;
            END;
            """);

        migrationBuilder.Sql(
            """
            CREATE TRIGGER "TR_PhysicalGoldLotDetail_PreventPostedUpdate"
            BEFORE UPDATE ON "PhysicalGoldLotDetail"
            BEGIN
                SELECT CASE
                    WHEN EXISTS (
                        SELECT 1
                        FROM "AssetLot" AS lot
                        JOIN "TransactionEntry" AS openingEntry
                          ON openingEntry."Id" = lot."OpeningTransactionEntryId"
                        JOIN "LedgerTransaction" AS openingTransaction
                          ON openingTransaction."Id" = openingEntry."TransactionId"
                        WHERE lot."Id" IN (OLD."AssetLotId", NEW."AssetLotId")
                          AND openingTransaction."StatusCode" = 'POSTED'
                    )
                    THEN RAISE(ABORT, 'Physical-gold detail in posted history is immutable.')
                END;

                SELECT CASE
                    WHEN (
                        SELECT asset."AssetTypeCode"
                        FROM "AssetLot" AS lot
                        JOIN "Asset" AS asset ON asset."Id" = lot."AssetId"
                        WHERE lot."Id" = NEW."AssetLotId"
                    ) <> 'PHYSICAL_GOLD'
                    THEN RAISE(ABORT, 'Physical-gold detail can be attached only to a physical-gold lot.')
                END;
            END;
            """);

        migrationBuilder.Sql(
            """
            CREATE TRIGGER "TR_PhysicalGoldLotDetail_PreventPostedDelete"
            BEFORE DELETE ON "PhysicalGoldLotDetail"
            WHEN EXISTS (
                SELECT 1
                FROM "AssetLot" AS lot
                JOIN "TransactionEntry" AS entry ON entry."Id" = lot."OpeningTransactionEntryId"
                JOIN "LedgerTransaction" AS tx ON tx."Id" = entry."TransactionId"
                WHERE lot."Id" = OLD."AssetLotId"
                  AND tx."StatusCode" = 'POSTED'
            )
            BEGIN
                SELECT RAISE(ABORT, 'Physical-gold detail in posted history cannot be deleted.');
            END;
            """);
    }

    private static void CreateLotAllocationTriggers(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            CREATE TRIGGER "TR_LotEntryAllocation_ValidateInsert"
            BEFORE INSERT ON "LotEntryAllocation"
            BEGIN
                SELECT CASE
                    WHEN EXISTS (
                        SELECT 1
                        FROM "TransactionEntry" AS entry
                        JOIN "LedgerTransaction" AS tx ON tx."Id" = entry."TransactionId"
                        WHERE entry."Id" = NEW."TransactionEntryId"
                          AND tx."StatusCode" = 'POSTED'
                    )
                    THEN RAISE(ABORT, 'Cannot append lot allocations to a posted transaction.')
                END;

                SELECT CASE
                    WHEN (SELECT "AssetId" FROM "AssetLot" WHERE "Id" = NEW."AssetLotId")
                         <> (SELECT "AssetId" FROM "TransactionEntry" WHERE "Id" = NEW."TransactionEntryId")
                    THEN RAISE(ABORT, 'Lot and transaction entry asset mismatch.')
                END;

                SELECT CASE
                    WHEN (NEW."QuantityDeltaE8" > 0 AND (
                              SELECT "QuantityDeltaE8" FROM "TransactionEntry"
                              WHERE "Id" = NEW."TransactionEntryId"
                          ) < 0)
                      OR (NEW."QuantityDeltaE8" < 0 AND (
                              SELECT "QuantityDeltaE8" FROM "TransactionEntry"
                              WHERE "Id" = NEW."TransactionEntryId"
                          ) > 0)
                    THEN RAISE(ABORT, 'Lot allocation sign must match entry sign.')
                END;

                SELECT CASE
                    WHEN (SELECT "QuantityDeltaE8" FROM "TransactionEntry" WHERE "Id" = NEW."TransactionEntryId") > 0
                     AND COALESCE((
                        SELECT SUM("QuantityDeltaE8")
                        FROM "LotEntryAllocation"
                        WHERE "TransactionEntryId" = NEW."TransactionEntryId"
                     ), 0) + NEW."QuantityDeltaE8"
                         > (SELECT "QuantityDeltaE8" FROM "TransactionEntry" WHERE "Id" = NEW."TransactionEntryId")
                    THEN RAISE(ABORT, 'Lot allocations cannot exceed their transaction entry quantity.')
                END;

                SELECT CASE
                    WHEN (SELECT "QuantityDeltaE8" FROM "TransactionEntry" WHERE "Id" = NEW."TransactionEntryId") < 0
                     AND COALESCE((
                        SELECT SUM("QuantityDeltaE8")
                        FROM "LotEntryAllocation"
                        WHERE "TransactionEntryId" = NEW."TransactionEntryId"
                     ), 0) + NEW."QuantityDeltaE8"
                         < (SELECT "QuantityDeltaE8" FROM "TransactionEntry" WHERE "Id" = NEW."TransactionEntryId")
                    THEN RAISE(ABORT, 'Lot allocations cannot exceed their transaction entry quantity.')
                END;

                SELECT CASE
                    WHEN COALESCE((
                        SELECT SUM("QuantityDeltaE8")
                        FROM "LotEntryAllocation"
                        WHERE "AssetLotId" = NEW."AssetLotId"
                    ), 0) + NEW."QuantityDeltaE8" < 0
                    THEN RAISE(ABORT, 'Lot quantity cannot become negative.')
                END;
            END;
            """);

        migrationBuilder.Sql(
            """
            CREATE TRIGGER "TR_LotEntryAllocation_ValidateUpdate"
            BEFORE UPDATE ON "LotEntryAllocation"
            BEGIN
                SELECT CASE
                    WHEN EXISTS (
                        SELECT 1
                        FROM "TransactionEntry" AS entry
                        JOIN "LedgerTransaction" AS tx ON tx."Id" = entry."TransactionId"
                        WHERE entry."Id" IN (OLD."TransactionEntryId", NEW."TransactionEntryId")
                          AND tx."StatusCode" = 'POSTED'
                    )
                    THEN RAISE(ABORT, 'Lot allocations of posted transactions are immutable.')
                END;

                SELECT CASE
                    WHEN (SELECT "AssetId" FROM "AssetLot" WHERE "Id" = NEW."AssetLotId")
                         <> (SELECT "AssetId" FROM "TransactionEntry" WHERE "Id" = NEW."TransactionEntryId")
                    THEN RAISE(ABORT, 'Lot and transaction entry asset mismatch.')
                END;

                SELECT CASE
                    WHEN (NEW."QuantityDeltaE8" > 0 AND (
                              SELECT "QuantityDeltaE8" FROM "TransactionEntry"
                              WHERE "Id" = NEW."TransactionEntryId"
                          ) < 0)
                      OR (NEW."QuantityDeltaE8" < 0 AND (
                              SELECT "QuantityDeltaE8" FROM "TransactionEntry"
                              WHERE "Id" = NEW."TransactionEntryId"
                          ) > 0)
                    THEN RAISE(ABORT, 'Lot allocation sign must match entry sign.')
                END;

                SELECT CASE
                    WHEN (SELECT "QuantityDeltaE8" FROM "TransactionEntry" WHERE "Id" = NEW."TransactionEntryId") > 0
                     AND COALESCE((
                        SELECT SUM("QuantityDeltaE8")
                        FROM "LotEntryAllocation"
                        WHERE "TransactionEntryId" = NEW."TransactionEntryId"
                          AND "Id" <> OLD."Id"
                     ), 0) + NEW."QuantityDeltaE8"
                         > (SELECT "QuantityDeltaE8" FROM "TransactionEntry" WHERE "Id" = NEW."TransactionEntryId")
                    THEN RAISE(ABORT, 'Lot allocations cannot exceed their transaction entry quantity.')
                END;

                SELECT CASE
                    WHEN (SELECT "QuantityDeltaE8" FROM "TransactionEntry" WHERE "Id" = NEW."TransactionEntryId") < 0
                     AND COALESCE((
                        SELECT SUM("QuantityDeltaE8")
                        FROM "LotEntryAllocation"
                        WHERE "TransactionEntryId" = NEW."TransactionEntryId"
                          AND "Id" <> OLD."Id"
                     ), 0) + NEW."QuantityDeltaE8"
                         < (SELECT "QuantityDeltaE8" FROM "TransactionEntry" WHERE "Id" = NEW."TransactionEntryId")
                    THEN RAISE(ABORT, 'Lot allocations cannot exceed their transaction entry quantity.')
                END;

                SELECT CASE
                    WHEN OLD."AssetLotId" <> NEW."AssetLotId"
                     AND COALESCE((
                        SELECT SUM("QuantityDeltaE8")
                        FROM "LotEntryAllocation"
                        WHERE "AssetLotId" = OLD."AssetLotId"
                          AND "Id" <> OLD."Id"
                    ), 0) < 0
                    THEN RAISE(ABORT, 'Removing an allocation cannot make the old lot quantity negative.')
                END;

                SELECT CASE
                    WHEN COALESCE((
                        SELECT SUM("QuantityDeltaE8")
                        FROM "LotEntryAllocation"
                        WHERE "AssetLotId" = NEW."AssetLotId"
                          AND "Id" <> OLD."Id"
                    ), 0) + NEW."QuantityDeltaE8" < 0
                    THEN RAISE(ABORT, 'Lot quantity cannot become negative.')
                END;
            END;
            """);

        migrationBuilder.Sql(
            """
            CREATE TRIGGER "TR_LotEntryAllocation_ValidateDelete"
            BEFORE DELETE ON "LotEntryAllocation"
            BEGIN
                SELECT CASE
                    WHEN EXISTS (
                        SELECT 1
                        FROM "TransactionEntry" AS entry
                        JOIN "LedgerTransaction" AS tx ON tx."Id" = entry."TransactionId"
                        WHERE entry."Id" = OLD."TransactionEntryId"
                          AND tx."StatusCode" = 'POSTED'
                    )
                    THEN RAISE(ABORT, 'Lot allocations of posted transactions cannot be deleted.')
                END;

                SELECT CASE
                    WHEN COALESCE((
                        SELECT SUM("QuantityDeltaE8")
                        FROM "LotEntryAllocation"
                        WHERE "AssetLotId" = OLD."AssetLotId"
                          AND "Id" <> OLD."Id"
                    ), 0) < 0
                    THEN RAISE(ABORT, 'Removing an allocation cannot make the lot quantity negative.')
                END;
            END;
            """);
    }

    private static void CreateMasterConsistencyTriggers(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            CREATE TRIGGER "TR_Asset_ValidateClassificationUpdate"
            BEFORE UPDATE OF "AssetTypeCode", "LotTrackingModeCode" ON "Asset"
            BEGIN
                SELECT CASE
                    WHEN NEW."LotTrackingModeCode" = 'NONE'
                     AND EXISTS (
                        SELECT 1 FROM "AssetLot" WHERE "AssetId" = OLD."Id"
                     )
                    THEN RAISE(ABORT, 'An asset with lots cannot disable lot tracking.')
                END;

                SELECT CASE
                    WHEN NEW."AssetTypeCode" <> 'PHYSICAL_GOLD'
                     AND EXISTS (
                        SELECT 1
                        FROM "AssetLot" AS lot
                        JOIN "PhysicalGoldLotDetail" AS detail
                          ON detail."AssetLotId" = lot."Id"
                        WHERE lot."AssetId" = OLD."Id"
                     )
                    THEN RAISE(ABORT, 'An asset with physical-gold lot detail must remain physical gold.')
                END;
            END;
            """);

        migrationBuilder.Sql(
            """
            CREATE TRIGGER "TR_Account_ValidateHouseholdUpdate"
            BEFORE UPDATE OF "HouseholdId" ON "Account"
            WHEN EXISTS (
                SELECT 1
                FROM "TransactionEntry" AS entry
                JOIN "LedgerTransaction" AS tx ON tx."Id" = entry."TransactionId"
                WHERE entry."AccountId" = OLD."Id"
                  AND tx."HouseholdId" <> NEW."HouseholdId"
            )
            BEGIN
                SELECT RAISE(ABORT, 'An account cannot move to a household inconsistent with its entries.');
            END;
            """);

        migrationBuilder.Sql(
            """
            CREATE TRIGGER "TR_Portfolio_ValidateHouseholdUpdate"
            BEFORE UPDATE OF "HouseholdId" ON "Portfolio"
            WHEN EXISTS (
                SELECT 1
                FROM "TransactionEntry" AS entry
                JOIN "LedgerTransaction" AS tx ON tx."Id" = entry."TransactionId"
                WHERE entry."PortfolioId" = OLD."Id"
                  AND tx."HouseholdId" <> NEW."HouseholdId"
            )
            BEGIN
                SELECT RAISE(ABORT, 'A portfolio cannot move to a household inconsistent with its entries.');
            END;
            """);

        migrationBuilder.Sql(
            """
            CREATE TRIGGER "TR_HouseholdMember_ValidateHouseholdUpdate"
            BEFORE UPDATE OF "HouseholdId" ON "HouseholdMember"
            WHEN EXISTS (
                SELECT 1
                FROM "CashFlowDetail" AS detail
                JOIN "LedgerTransaction" AS tx ON tx."Id" = detail."TransactionId"
                WHERE detail."HouseholdMemberId" = OLD."Id"
                  AND tx."HouseholdId" <> NEW."HouseholdId"
            )
            BEGIN
                SELECT RAISE(ABORT, 'A household member cannot move outside the household of attributed cash flows.');
            END;
            """);
    }

    private static readonly string[] TriggerNames =
    [
        "TR_LedgerTransaction_PreventPostedInsert",
        "TR_LedgerTransaction_PreventPostedUpdate",
        "TR_LedgerTransaction_PreventPostedDelete",
        "TR_LedgerTransaction_ValidateBeforePosting",
        "TR_LedgerTransaction_ValidateHouseholdUpdate",
        "TR_LedgerTransaction_ValidateTypeUpdate",
        "TR_TransactionEntry_ValidateInsert",
        "TR_TransactionEntry_ValidateUpdate",
        "TR_TransactionEntry_PreventPostedDelete",
        "TR_TransactionCostComponent_PreventPostedInsert",
        "TR_TransactionCostComponent_PreventPostedUpdate",
        "TR_TransactionCostComponent_PreventPostedDelete",
        "TR_CashFlowDetail_ValidateInsert",
        "TR_CashFlowDetail_ValidateUpdate",
        "TR_CashFlowDetail_PreventPostedDelete",
        "TR_AssetLot_ValidateInsert",
        "TR_AssetLot_ValidateUpdate",
        "TR_AssetLot_PreventPostedDelete",
        "TR_PhysicalGoldLotDetail_ValidateInsert",
        "TR_PhysicalGoldLotDetail_PreventPostedUpdate",
        "TR_PhysicalGoldLotDetail_PreventPostedDelete",
        "TR_LotEntryAllocation_ValidateInsert",
        "TR_LotEntryAllocation_ValidateUpdate",
        "TR_LotEntryAllocation_ValidateDelete",
        "TR_Asset_ValidateClassificationUpdate",
        "TR_Account_ValidateHouseholdUpdate",
        "TR_Portfolio_ValidateHouseholdUpdate",
        "TR_HouseholdMember_ValidateHouseholdUpdate"
    ];
}
