using Microsoft.EntityFrameworkCore.Migrations;

namespace WealthLedger.Infrastructure.Persistence.Migrations;

public partial class _006_OpeningBalanceCutoverGuards
{
    private const string TriggerName =
        "TR_LedgerTransaction_ValidateOpeningBalanceBeforePosting";

    private const string ScopeIndexName =
        "IX_TransactionEntry_OpeningBalanceScope";

    private static void ValidateExistingOpeningBalances(
        MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(ExistingOpeningBalanceValidationSql);
    }

    private static void AddOpeningBalanceScopeIndex(
        MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            $"""
            CREATE INDEX "{ScopeIndexName}"
            ON "TransactionEntry" (
                "PortfolioId",
                "AccountId",
                "AssetId",
                "TransactionId");
            """);
    }

    private static void DropOpeningBalanceScopeIndex(
        MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            $"""DROP INDEX IF EXISTS "{ScopeIndexName}";""");
    }

    private static void AddOpeningBalancePostingTrigger(
        MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(OpeningBalancePostingTriggerSql);
    }

    private static void DropOpeningBalancePostingTrigger(
        MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            $"""DROP TRIGGER IF EXISTS "{TriggerName}";""");
    }

    private const string ExistingOpeningBalanceValidationSql =
        """
        CREATE TEMP TABLE "__M007OpeningBalanceValidation" (
            "IsValid" INTEGER NOT NULL CHECK ("IsValid" = 1)
        );

        INSERT INTO "__M007OpeningBalanceValidation" ("IsValid")
        SELECT CASE
            WHEN EXISTS (
                SELECT 1
                FROM "LedgerTransaction" AS tx
                WHERE tx."TransactionTypeCode" = 'OPENING_BALANCE'
                  AND tx."StatusCode" = 'POSTED'
                  AND (
                    tx."OrderDate" IS NOT NULL
                    OR tx."SettlementDate" IS NOT NULL
                    OR trim(COALESCE(tx."Note", '')) = ''
                    OR (
                        SELECT COUNT(*)
                        FROM "TransactionEntry" AS entry
                        WHERE entry."TransactionId" = tx."Id"
                    ) <> 1
                    OR EXISTS (
                        SELECT 1
                        FROM "TransactionEntry" AS entry
                        WHERE entry."TransactionId" = tx."Id"
                          AND (
                            entry."EntryRoleCode" <> 'PRINCIPAL'
                            OR entry."QuantityDeltaE8" <= 0
                            OR entry."UnitPriceE8" IS NOT NULL
                            OR entry."PriceCurrencyCode" IS NOT NULL
                          )
                    )
                    OR EXISTS (
                        SELECT 1
                        FROM "TransactionCostComponent" AS cost
                        WHERE cost."TransactionId" = tx."Id"
                    )
                    OR EXISTS (
                        SELECT 1
                        FROM "CashFlowDetail" AS cashFlow
                        WHERE cashFlow."TransactionId" = tx."Id"
                    )
                    OR NOT EXISTS (
                        SELECT 1
                        FROM "TransactionEntry" AS entry
                        JOIN "Household" AS household
                          ON household."Id" = tx."HouseholdId"
                        JOIN "Portfolio" AS portfolio
                          ON portfolio."Id" = entry."PortfolioId"
                         AND portfolio."HouseholdId" = tx."HouseholdId"
                        JOIN "Account" AS account
                          ON account."Id" = entry."AccountId"
                         AND account."HouseholdId" = tx."HouseholdId"
                        JOIN "Asset" AS asset
                          ON asset."Id" = entry."AssetId"
                        WHERE entry."TransactionId" = tx."Id"
                          AND asset."BaseCurrencyCode" IS NOT NULL
                          AND (
                            (
                              asset."AssetTypeCode" = 'CASH'
                              AND asset."BaseUnitCode" = 'CURRENCY_UNIT'
                              AND asset."LotTrackingModeCode" = 'NONE'
                              AND asset."BaseCurrencyCode"
                                  = household."BaseCurrencyCode"
                              AND account."AccountTypeCode"
                                  IN ('CASH', 'INVESTMENT', 'PENSION')
                            )
                            OR (
                              asset."AssetTypeCode" = 'CURRENCY'
                              AND asset."BaseUnitCode" = 'CURRENCY_UNIT'
                              AND asset."LotTrackingModeCode" = 'NONE'
                              AND asset."BaseCurrencyCode"
                                  <> household."BaseCurrencyCode"
                              AND account."AccountTypeCode"
                                  IN ('CASH', 'INVESTMENT', 'PENSION')
                            )
                            OR (
                              asset."AssetTypeCode" = 'FUND'
                              AND asset."BaseUnitCode" = 'FUND_UNIT'
                              AND asset."LotTrackingModeCode"
                                  IN ('OPTIONAL', 'REQUIRED')
                              AND account."AccountTypeCode"
                                  IN ('INVESTMENT', 'PENSION')
                            )
                            OR (
                              asset."AssetTypeCode" = 'EQUITY'
                              AND asset."BaseUnitCode" = 'SHARE'
                              AND asset."LotTrackingModeCode"
                                  IN ('OPTIONAL', 'REQUIRED')
                              AND account."AccountTypeCode"
                                  IN ('INVESTMENT', 'PENSION')
                            )
                            OR (
                              asset."AssetTypeCode" = 'PHYSICAL_GOLD'
                              AND asset."BaseUnitCode" = 'GROSS_GRAM'
                              AND asset."LotTrackingModeCode" = 'REQUIRED'
                              AND account."AccountTypeCode" = 'PHYSICAL_VAULT'
                            )
                          )
                    )
                    OR EXISTS (
                        SELECT 1
                        FROM "TransactionEntry" AS entry
                        JOIN "Asset" AS asset
                          ON asset."Id" = entry."AssetId"
                        WHERE entry."TransactionId" = tx."Id"
                          AND asset."AssetTypeCode" IN ('CASH', 'CURRENCY')
                          AND (
                            EXISTS (
                                SELECT 1
                                FROM "AssetLot" AS lot
                                WHERE lot."OpeningTransactionEntryId"
                                    = entry."Id"
                            )
                            OR EXISTS (
                                SELECT 1
                                FROM "LotEntryAllocation" AS allocation
                                WHERE allocation."TransactionEntryId"
                                    = entry."Id"
                            )
                          )
                    )
                    OR EXISTS (
                        SELECT 1
                        FROM "TransactionEntry" AS entry
                        JOIN "Asset" AS asset
                          ON asset."Id" = entry."AssetId"
                        WHERE entry."TransactionId" = tx."Id"
                          AND asset."AssetTypeCode"
                              IN ('FUND', 'EQUITY', 'PHYSICAL_GOLD')
                          AND (
                            NOT EXISTS (
                                SELECT 1
                                FROM "AssetLot" AS lot
                                WHERE lot."OpeningTransactionEntryId"
                                    = entry."Id"
                            )
                            OR COALESCE((
                                SELECT SUM(allocation."QuantityDeltaE8")
                                FROM "LotEntryAllocation" AS allocation
                                WHERE allocation."TransactionEntryId"
                                    = entry."Id"
                            ), 0) <> entry."QuantityDeltaE8"
                            OR EXISTS (
                                SELECT 1
                                FROM "LotEntryAllocation" AS allocation
                                JOIN "AssetLot" AS lot
                                  ON lot."Id" = allocation."AssetLotId"
                                WHERE allocation."TransactionEntryId"
                                    = entry."Id"
                                  AND lot."OpeningTransactionEntryId"
                                      <> entry."Id"
                            )
                            OR EXISTS (
                                SELECT 1
                                FROM "AssetLot" AS lot
                                WHERE lot."OpeningTransactionEntryId"
                                    = entry."Id"
                                  AND (
                                    lot."CostBasisStatusCode"
                                        NOT IN ('KNOWN', 'UNKNOWN')
                                    OR (
                                      lot."AcquiredOn" IS NOT NULL
                                      AND lot."AcquiredOn" > tx."ExecutionDate"
                                    )
                                    OR NOT EXISTS (
                                        SELECT 1
                                        FROM "LotEntryAllocation" AS allocation
                                        WHERE allocation."AssetLotId" = lot."Id"
                                          AND allocation."TransactionEntryId"
                                              = entry."Id"
                                          AND allocation."QuantityDeltaE8" > 0
                                    )
                                  )
                            )
                            OR (
                              asset."AssetTypeCode" = 'PHYSICAL_GOLD'
                              AND EXISTS (
                                SELECT 1
                                FROM "AssetLot" AS lot
                                WHERE lot."OpeningTransactionEntryId"
                                    = entry."Id"
                                  AND NOT EXISTS (
                                    SELECT 1
                                    FROM "PhysicalGoldLotDetail" AS gold
                                    WHERE gold."AssetLotId" = lot."Id"
                                  )
                              )
                            )
                          )
                    )
                  )
            )
            OR EXISTS (
                SELECT 1
                FROM "LedgerTransaction" AS openingTx
                JOIN "TransactionEntry" AS openingEntry
                  ON openingEntry."TransactionId" = openingTx."Id"
                WHERE openingTx."TransactionTypeCode" = 'OPENING_BALANCE'
                  AND openingTx."StatusCode" = 'POSTED'
                  AND NOT EXISTS (
                    SELECT 1
                    FROM "LedgerTransaction" AS openingReversal
                    WHERE openingReversal."TransactionTypeCode" = 'REVERSAL'
                      AND openingReversal."StatusCode" = 'POSTED'
                      AND openingReversal."ReversalOfTransactionId"
                          = openingTx."Id"
                  )
                GROUP BY
                    openingTx."HouseholdId",
                    openingEntry."PortfolioId",
                    openingEntry."AccountId",
                    openingEntry."AssetId"
                HAVING COUNT(*) > 1
            )
            OR EXISTS (
                SELECT 1
                FROM "LedgerTransaction" AS openingTx
                JOIN "TransactionEntry" AS openingEntry
                  ON openingEntry."TransactionId" = openingTx."Id"
                JOIN "TransactionEntry" AS priorEntry
                  ON priorEntry."PortfolioId" = openingEntry."PortfolioId"
                 AND priorEntry."AccountId" = openingEntry."AccountId"
                 AND priorEntry."AssetId" = openingEntry."AssetId"
                JOIN "LedgerTransaction" AS priorTx
                  ON priorTx."Id" = priorEntry."TransactionId"
                WHERE openingTx."TransactionTypeCode" = 'OPENING_BALANCE'
                  AND openingTx."StatusCode" = 'POSTED'
                  AND priorTx."HouseholdId" = openingTx."HouseholdId"
                  AND priorTx."StatusCode" = 'POSTED'
                  AND priorTx."TransactionTypeCode"
                      NOT IN ('REVERSAL', 'OPENING_BALANCE')
                  AND priorTx."PostedAtUtc" <= openingTx."PostedAtUtc"
                  AND NOT EXISTS (
                    SELECT 1
                    FROM "LedgerTransaction" AS priorReversal
                    WHERE priorReversal."TransactionTypeCode" = 'REVERSAL'
                      AND priorReversal."StatusCode" = 'POSTED'
                      AND priorReversal."ReversalOfTransactionId" = priorTx."Id"
                  )
            )
            THEN 0
            ELSE 1
        END;

        DROP TABLE "__M007OpeningBalanceValidation";
        """;

    private const string OpeningBalancePostingTriggerSql =
        """
        CREATE TRIGGER "TR_LedgerTransaction_ValidateOpeningBalanceBeforePosting"
        BEFORE UPDATE OF "StatusCode" ON "LedgerTransaction"
        WHEN NEW."StatusCode" = 'POSTED'
         AND OLD."StatusCode" <> 'POSTED'
         AND NEW."TransactionTypeCode" = 'OPENING_BALANCE'
        BEGIN
            SELECT CASE
                WHEN NEW."OrderDate" IS NOT NULL
                  OR NEW."SettlementDate" IS NOT NULL
                  OR trim(COALESCE(NEW."Note", '')) = ''
                THEN RAISE(ABORT, 'WL_M007_INVALID_OPENING_BALANCE')
            END;

            SELECT CASE
                WHEN (
                    SELECT COUNT(*)
                    FROM "TransactionEntry" AS entry
                    WHERE entry."TransactionId" = NEW."Id"
                ) <> 1
                THEN RAISE(ABORT, 'WL_M007_INVALID_OPENING_BALANCE')
            END;

            SELECT CASE
                WHEN EXISTS (
                    SELECT 1
                    FROM "TransactionEntry" AS entry
                    WHERE entry."TransactionId" = NEW."Id"
                      AND (
                        entry."EntryRoleCode" <> 'PRINCIPAL'
                        OR entry."QuantityDeltaE8" <= 0
                        OR entry."UnitPriceE8" IS NOT NULL
                        OR entry."PriceCurrencyCode" IS NOT NULL
                      )
                )
                THEN RAISE(ABORT, 'WL_M007_INVALID_OPENING_BALANCE')
            END;

            SELECT CASE
                WHEN EXISTS (
                    SELECT 1
                    FROM "TransactionCostComponent" AS cost
                    WHERE cost."TransactionId" = NEW."Id"
                )
                  OR EXISTS (
                    SELECT 1
                    FROM "CashFlowDetail" AS cashFlow
                    WHERE cashFlow."TransactionId" = NEW."Id"
                  )
                THEN RAISE(ABORT, 'WL_M007_INVALID_OPENING_BALANCE')
            END;

            SELECT CASE
                WHEN NOT EXISTS (
                    SELECT 1
                    FROM "TransactionEntry" AS entry
                    JOIN "Household" AS household
                      ON household."Id" = NEW."HouseholdId"
                    JOIN "Portfolio" AS portfolio
                      ON portfolio."Id" = entry."PortfolioId"
                     AND portfolio."HouseholdId" = NEW."HouseholdId"
                    JOIN "Account" AS account
                      ON account."Id" = entry."AccountId"
                     AND account."HouseholdId" = NEW."HouseholdId"
                    JOIN "Asset" AS asset
                      ON asset."Id" = entry."AssetId"
                    LEFT JOIN "Institution" AS institution
                      ON institution."Id" = account."InstitutionId"
                    WHERE entry."TransactionId" = NEW."Id"
                      AND portfolio."StatusCode" = 'ACTIVE'
                      AND account."IsActive" = 1
                      AND account."ClosedOn" IS NULL
                      AND (
                        account."OpenedOn" IS NULL
                        OR account."OpenedOn" <= NEW."ExecutionDate"
                      )
                      AND asset."IsActive" = 1
                      AND asset."BaseCurrencyCode" IS NOT NULL
                      AND (
                        account."InstitutionId" IS NULL
                        OR institution."IsActive" = 1
                      )
                      AND (
                        account."AccountTypeCode"
                            NOT IN ('INVESTMENT', 'PENSION')
                        OR account."InstitutionId" IS NOT NULL
                      )
                      AND (
                        (
                          asset."AssetTypeCode" = 'CASH'
                          AND asset."BaseUnitCode" = 'CURRENCY_UNIT'
                          AND asset."LotTrackingModeCode" = 'NONE'
                          AND asset."BaseCurrencyCode"
                              = household."BaseCurrencyCode"
                          AND account."AccountTypeCode"
                              IN ('CASH', 'INVESTMENT', 'PENSION')
                        )
                        OR (
                          asset."AssetTypeCode" = 'CURRENCY'
                          AND asset."BaseUnitCode" = 'CURRENCY_UNIT'
                          AND asset."LotTrackingModeCode" = 'NONE'
                          AND asset."BaseCurrencyCode"
                              <> household."BaseCurrencyCode"
                          AND account."AccountTypeCode"
                              IN ('CASH', 'INVESTMENT', 'PENSION')
                        )
                        OR (
                          asset."AssetTypeCode" = 'FUND'
                          AND asset."BaseUnitCode" = 'FUND_UNIT'
                          AND asset."LotTrackingModeCode"
                              IN ('OPTIONAL', 'REQUIRED')
                          AND account."AccountTypeCode"
                              IN ('INVESTMENT', 'PENSION')
                        )
                        OR (
                          asset."AssetTypeCode" = 'EQUITY'
                          AND asset."BaseUnitCode" = 'SHARE'
                          AND asset."LotTrackingModeCode"
                              IN ('OPTIONAL', 'REQUIRED')
                          AND account."AccountTypeCode"
                              IN ('INVESTMENT', 'PENSION')
                        )
                        OR (
                          asset."AssetTypeCode" = 'PHYSICAL_GOLD'
                          AND asset."BaseUnitCode" = 'GROSS_GRAM'
                          AND asset."LotTrackingModeCode" = 'REQUIRED'
                          AND account."AccountTypeCode" = 'PHYSICAL_VAULT'
                        )
                      )
                )
                THEN RAISE(ABORT, 'WL_M007_INVALID_OPENING_BALANCE')
            END;

            SELECT CASE
                WHEN EXISTS (
                    SELECT 1
                    FROM "TransactionEntry" AS entry
                    JOIN "Asset" AS asset
                      ON asset."Id" = entry."AssetId"
                    WHERE entry."TransactionId" = NEW."Id"
                      AND asset."AssetTypeCode" IN ('CASH', 'CURRENCY')
                      AND (
                        EXISTS (
                            SELECT 1
                            FROM "AssetLot" AS lot
                            WHERE lot."OpeningTransactionEntryId" = entry."Id"
                        )
                        OR EXISTS (
                            SELECT 1
                            FROM "LotEntryAllocation" AS allocation
                            WHERE allocation."TransactionEntryId" = entry."Id"
                        )
                      )
                )
                THEN RAISE(ABORT, 'WL_M007_INVALID_OPENING_BALANCE')
            END;

            SELECT CASE
                WHEN EXISTS (
                    SELECT 1
                    FROM "TransactionEntry" AS entry
                    JOIN "Asset" AS asset
                      ON asset."Id" = entry."AssetId"
                    WHERE entry."TransactionId" = NEW."Id"
                      AND asset."AssetTypeCode"
                          IN ('FUND', 'EQUITY', 'PHYSICAL_GOLD')
                      AND (
                        (
                          asset."LotTrackingModeCode" = 'OPTIONAL'
                          AND (
                            NOT EXISTS (
                                SELECT 1
                                FROM "AssetLot" AS lot
                                WHERE lot."OpeningTransactionEntryId"
                                    = entry."Id"
                            )
                            OR COALESCE((
                                SELECT SUM(allocation."QuantityDeltaE8")
                                FROM "LotEntryAllocation" AS allocation
                                WHERE allocation."TransactionEntryId"
                                    = entry."Id"
                            ), 0) <> entry."QuantityDeltaE8"
                          )
                        )
                        OR EXISTS (
                            SELECT 1
                            FROM "LotEntryAllocation" AS allocation
                            JOIN "AssetLot" AS lot
                              ON lot."Id" = allocation."AssetLotId"
                            WHERE allocation."TransactionEntryId" = entry."Id"
                              AND lot."OpeningTransactionEntryId" <> entry."Id"
                        )
                        OR EXISTS (
                            SELECT 1
                            FROM "AssetLot" AS lot
                            WHERE lot."OpeningTransactionEntryId" = entry."Id"
                              AND (
                                lot."CostBasisStatusCode"
                                    NOT IN ('KNOWN', 'UNKNOWN')
                                OR (
                                  lot."AcquiredOn" IS NOT NULL
                                  AND lot."AcquiredOn" > NEW."ExecutionDate"
                                )
                              )
                        )
                        OR (
                          asset."AssetTypeCode" = 'PHYSICAL_GOLD'
                          AND EXISTS (
                            SELECT 1
                            FROM "AssetLot" AS lot
                            WHERE lot."OpeningTransactionEntryId" = entry."Id"
                              AND NOT EXISTS (
                                SELECT 1
                                FROM "PhysicalGoldLotDetail" AS gold
                                WHERE gold."AssetLotId" = lot."Id"
                              )
                          )
                        )
                      )
                )
                THEN RAISE(ABORT, 'WL_M007_INVALID_OPENING_BALANCE')
            END;

            SELECT CASE
                WHEN EXISTS (
                    SELECT 1
                    FROM "TransactionEntry" AS candidateEntry
                    JOIN "TransactionEntry" AS existingEntry
                      ON existingEntry."PortfolioId"
                          = candidateEntry."PortfolioId"
                     AND existingEntry."AccountId"
                          = candidateEntry."AccountId"
                     AND existingEntry."AssetId"
                          = candidateEntry."AssetId"
                    JOIN "LedgerTransaction" AS existingTx
                      ON existingTx."Id" = existingEntry."TransactionId"
                    WHERE candidateEntry."TransactionId" = NEW."Id"
                      AND existingTx."Id" <> NEW."Id"
                      AND existingTx."HouseholdId" = NEW."HouseholdId"
                      AND existingTx."StatusCode" = 'POSTED'
                      AND existingTx."TransactionTypeCode" = 'OPENING_BALANCE'
                      AND NOT EXISTS (
                        SELECT 1
                        FROM "LedgerTransaction" AS reversal
                        WHERE reversal."TransactionTypeCode" = 'REVERSAL'
                          AND reversal."StatusCode" = 'POSTED'
                          AND reversal."ReversalOfTransactionId"
                              = existingTx."Id"
                      )
                )
                THEN RAISE(ABORT, 'WL_M007_ALREADY_EXISTS')
            END;

            SELECT CASE
                WHEN EXISTS (
                    SELECT 1
                    FROM "TransactionEntry" AS candidateEntry
                    JOIN "TransactionEntry" AS existingEntry
                      ON existingEntry."PortfolioId"
                          = candidateEntry."PortfolioId"
                     AND existingEntry."AccountId"
                          = candidateEntry."AccountId"
                     AND existingEntry."AssetId"
                          = candidateEntry."AssetId"
                    JOIN "LedgerTransaction" AS existingTx
                      ON existingTx."Id" = existingEntry."TransactionId"
                    WHERE candidateEntry."TransactionId" = NEW."Id"
                      AND existingTx."Id" <> NEW."Id"
                      AND existingTx."HouseholdId" = NEW."HouseholdId"
                      AND existingTx."StatusCode" = 'POSTED'
                      AND existingTx."TransactionTypeCode"
                          NOT IN ('REVERSAL', 'OPENING_BALANCE')
                      AND NOT EXISTS (
                        SELECT 1
                        FROM "LedgerTransaction" AS reversal
                        WHERE reversal."TransactionTypeCode" = 'REVERSAL'
                          AND reversal."StatusCode" = 'POSTED'
                          AND reversal."ReversalOfTransactionId"
                              = existingTx."Id"
                      )
                )
                THEN RAISE(ABORT, 'WL_M007_SCOPE_HAS_EFFECTIVE_HISTORY')
            END;
        END;
        """;
}
