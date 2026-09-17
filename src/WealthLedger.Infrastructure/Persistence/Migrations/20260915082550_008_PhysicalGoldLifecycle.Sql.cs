using Microsoft.EntityFrameworkCore.Migrations;

namespace WealthLedger.Infrastructure.Persistence.Migrations;

/// <summary>
/// M009 physical-gold migration validation, backfill, and database guards.
/// </summary>
/// <remarks>
/// Migration 007's trigger is already scoped to Fund principal assets in the
/// repository's corrected M008 starting point. M009 therefore preserves it
/// byte-for-byte and completes explicit dispatch with a PhysicalGold trigger
/// and a fail-closed unsupported-family trigger. Down removes only these
/// M009 objects, leaving the exact M008 guard in place.
/// </remarks>
public partial class _008_PhysicalGoldLifecycle
{
    private static readonly string[] TriggerNames =
    [
        "TR_PhysicalGoldAllocationDetail_ValidateInsert",
        "TR_PhysicalGoldAllocationDetail_ValidateUpdate",
        "TR_PhysicalGoldAllocationDetail_ValidateDelete",
        "TR_PhysicalGoldTradeDetail_ValidateInsert",
        "TR_PhysicalGoldTradeDetail_ValidateUpdate",
        "TR_PhysicalGoldTradeDetail_ValidateDelete",
        "TR_LedgerTransaction_ValidateBuySellDispatch",
        "TR_LedgerTransaction_ValidatePhysicalGoldAllocationPosting",
        "TR_LedgerTransaction_ValidatePhysicalGoldTradePosting",
        "TR_LedgerTransaction_ValidatePhysicalGoldTransferPosting"
    ];

    private static void ValidateExistingPhysicalGoldHistory(
        MigrationBuilder migrationBuilder)
        => migrationBuilder.Sql(ExistingHistoryPreflightSql);

    private static void BackfillPhysicalGoldPieceMovement(
        MigrationBuilder migrationBuilder)
        => migrationBuilder.Sql(BackfillSql);

    private static void AddPhysicalGoldLifecycleGuards(
        MigrationBuilder migrationBuilder)
    {
        foreach (var sql in GuardSql)
        {
            migrationBuilder.Sql(sql);
        }
    }

    private static void DropPhysicalGoldLifecycleGuards(
        MigrationBuilder migrationBuilder)
    {
        foreach (var name in TriggerNames.Reverse())
        {
            migrationBuilder.Sql($"DROP TRIGGER IF EXISTS \"{name}\";");
        }
    }

    private const string ExistingHistoryPreflightSql =
        """
        CREATE TEMP TABLE "__M009PhysicalGoldHistoryValidation" (
            "IsValid" INTEGER NOT NULL CHECK ("IsValid" = 1)
        );

        INSERT INTO "__M009PhysicalGoldHistoryValidation" ("IsValid")
        SELECT CASE WHEN EXISTS (
            SELECT 1
            FROM "LotEntryAllocation" AS allocation
            JOIN "AssetLot" AS lot
              ON lot."Id" = allocation."AssetLotId"
            JOIN "PhysicalGoldLotDetail" AS gold
              ON gold."AssetLotId" = lot."Id"
            JOIN "TransactionEntry" AS allocated_entry
              ON allocated_entry."Id" = allocation."TransactionEntryId"
            JOIN "LedgerTransaction" AS allocated_tx
              ON allocated_tx."Id" = allocated_entry."TransactionId"
            JOIN "TransactionEntry" AS opening_entry
              ON opening_entry."Id" = lot."OpeningTransactionEntryId"
            JOIN "LedgerTransaction" AS opening_tx
              ON opening_tx."Id" = opening_entry."TransactionId"
            WHERE allocation."TransactionEntryId" <> lot."OpeningTransactionEntryId"
              AND NOT (
                  allocated_tx."TransactionTypeCode" = 'REVERSAL'
                  AND allocated_tx."ReversalOfTransactionId" = opening_tx."Id"
                  AND allocated_entry."EntrySequence" = opening_entry."EntrySequence"
                  AND allocation."QuantityDeltaE8" = -(
                      SELECT opening_allocation."QuantityDeltaE8"
                      FROM "LotEntryAllocation" AS opening_allocation
                      WHERE opening_allocation."AssetLotId" = lot."Id"
                        AND opening_allocation."TransactionEntryId" = lot."OpeningTransactionEntryId"
                  )
              )
        ) OR EXISTS (
            SELECT 1
            FROM "AssetLot" AS lot
            JOIN "PhysicalGoldLotDetail" AS gold
              ON gold."AssetLotId" = lot."Id"
            WHERE (
                SELECT COUNT(*)
                FROM "LotEntryAllocation" AS opening_allocation
                WHERE opening_allocation."AssetLotId" = lot."Id"
                  AND opening_allocation."TransactionEntryId" = lot."OpeningTransactionEntryId"
                  AND opening_allocation."QuantityDeltaE8" > 0
            ) <> 1
        ) THEN 0 ELSE 1 END;

        DROP TABLE "__M009PhysicalGoldHistoryValidation";
        """;

    private const string BackfillSql =
        """
        INSERT INTO "PhysicalGoldLotAllocationDetail" (
            "LotEntryAllocationId",
            "PieceDelta"
        )
        SELECT
            allocation."Id",
            CASE
                WHEN allocation."TransactionEntryId" = lot."OpeningTransactionEntryId"
                    THEN gold."PieceCount"
                ELSE -gold."PieceCount"
            END
        FROM "LotEntryAllocation" AS allocation
        JOIN "AssetLot" AS lot
          ON lot."Id" = allocation."AssetLotId"
        JOIN "PhysicalGoldLotDetail" AS gold
          ON gold."AssetLotId" = lot."Id";
        """;

    private static readonly string[] GuardSql =
    [
        PhysicalAllocationDetailInsertSql,
        PhysicalAllocationDetailUpdateSql,
        PhysicalAllocationDetailDeleteSql,
        PhysicalTradeDetailInsertSql,
        PhysicalTradeDetailUpdateSql,
        PhysicalTradeDetailDeleteSql,
        BuySellDispatchSql,
        PhysicalAllocationPostingSql,
        PhysicalTradePostingSql,
        PhysicalTransferPostingSql
    ];

    private const string PhysicalAllocationDetailInsertSql =
        """
        CREATE TRIGGER "TR_PhysicalGoldAllocationDetail_ValidateInsert"
        BEFORE INSERT ON "PhysicalGoldLotAllocationDetail"
        BEGIN
            SELECT CASE WHEN NOT EXISTS (
                SELECT 1
                FROM "LotEntryAllocation" AS allocation
                JOIN "AssetLot" AS lot
                  ON lot."Id" = allocation."AssetLotId"
                JOIN "Asset" AS asset
                  ON asset."Id" = lot."AssetId"
                JOIN "TransactionEntry" AS entry
                  ON entry."Id" = allocation."TransactionEntryId"
                JOIN "LedgerTransaction" AS tx
                  ON tx."Id" = entry."TransactionId"
                WHERE allocation."Id" = NEW."LotEntryAllocationId"
                  AND asset."AssetTypeCode" = 'PHYSICAL_GOLD'
                  AND tx."StatusCode" <> 'POSTED'
                  AND (
                      (allocation."QuantityDeltaE8" > 0 AND NEW."PieceDelta" > 0)
                      OR (allocation."QuantityDeltaE8" < 0 AND NEW."PieceDelta" < 0)
                  )
            ) THEN RAISE(ABORT, 'WL_M009_PHYSICAL_GOLD_PIECE_DETAIL_INVALID') END;
        END;
        """;

    private const string PhysicalAllocationDetailUpdateSql =
        """
        CREATE TRIGGER "TR_PhysicalGoldAllocationDetail_ValidateUpdate"
        BEFORE UPDATE ON "PhysicalGoldLotAllocationDetail"
        BEGIN
            SELECT CASE WHEN EXISTS (
                SELECT 1
                FROM "LotEntryAllocation" AS allocation
                JOIN "TransactionEntry" AS entry
                  ON entry."Id" = allocation."TransactionEntryId"
                JOIN "LedgerTransaction" AS tx
                  ON tx."Id" = entry."TransactionId"
                WHERE allocation."Id" = OLD."LotEntryAllocationId"
                  AND tx."StatusCode" = 'POSTED'
            ) THEN RAISE(ABORT, 'WL_M009_PHYSICAL_GOLD_PIECE_HISTORY_IMMUTABLE') END;

            SELECT CASE WHEN OLD."LotEntryAllocationId" <> NEW."LotEntryAllocationId"
              OR NOT EXISTS (
                SELECT 1
                FROM "LotEntryAllocation" AS allocation
                JOIN "AssetLot" AS lot ON lot."Id" = allocation."AssetLotId"
                JOIN "Asset" AS asset ON asset."Id" = lot."AssetId"
                WHERE allocation."Id" = NEW."LotEntryAllocationId"
                  AND asset."AssetTypeCode" = 'PHYSICAL_GOLD'
                  AND (
                      (allocation."QuantityDeltaE8" > 0 AND NEW."PieceDelta" > 0)
                      OR (allocation."QuantityDeltaE8" < 0 AND NEW."PieceDelta" < 0)
                  )
              )
            THEN RAISE(ABORT, 'WL_M009_PHYSICAL_GOLD_PIECE_DETAIL_INVALID') END;
        END;
        """;

    private const string PhysicalAllocationDetailDeleteSql =
        """
        CREATE TRIGGER "TR_PhysicalGoldAllocationDetail_ValidateDelete"
        BEFORE DELETE ON "PhysicalGoldLotAllocationDetail"
        BEGIN
            SELECT CASE WHEN EXISTS (
                SELECT 1
                FROM "LotEntryAllocation" AS allocation
                JOIN "TransactionEntry" AS entry
                  ON entry."Id" = allocation."TransactionEntryId"
                JOIN "LedgerTransaction" AS tx
                  ON tx."Id" = entry."TransactionId"
                WHERE allocation."Id" = OLD."LotEntryAllocationId"
                  AND tx."StatusCode" = 'POSTED'
            ) THEN RAISE(ABORT, 'WL_M009_PHYSICAL_GOLD_PIECE_HISTORY_IMMUTABLE') END;
        END;
        """;

    private const string PhysicalTradeDetailInsertSql =
        """
        CREATE TRIGGER "TR_PhysicalGoldTradeDetail_ValidateInsert"
        BEFORE INSERT ON "PhysicalGoldTradeDetail"
        BEGIN
            SELECT CASE WHEN NOT EXISTS (
                SELECT 1
                FROM "LedgerTransaction" AS tx
                WHERE tx."Id" = NEW."LedgerTransactionId"
                  AND tx."StatusCode" <> 'POSTED'
                  AND tx."TransactionTypeCode" IN ('BUY', 'SELL')
            ) THEN RAISE(ABORT, 'WL_M009_PHYSICAL_GOLD_TRADE_DETAIL_INVALID') END;

            SELECT CASE WHEN NEW."CounterpartyInstitutionId" IS NOT NULL
              AND NOT EXISTS (
                SELECT 1 FROM "Institution" AS institution
                WHERE institution."Id" = NEW."CounterpartyInstitutionId"
                  AND institution."IsActive" = 1
                  AND institution."InstitutionTypeCode" IN ('JEWELER', 'BANK', 'OTHER')
              )
            THEN RAISE(ABORT, 'WL_M009_PHYSICAL_GOLD_COUNTERPARTY_INVALID') END;
        END;
        """;

    private const string PhysicalTradeDetailUpdateSql =
        """
        CREATE TRIGGER "TR_PhysicalGoldTradeDetail_ValidateUpdate"
        BEFORE UPDATE ON "PhysicalGoldTradeDetail"
        BEGIN
            SELECT CASE WHEN EXISTS (
                SELECT 1 FROM "LedgerTransaction" AS tx
                WHERE tx."Id" = OLD."LedgerTransactionId"
                  AND tx."StatusCode" = 'POSTED'
            ) THEN RAISE(ABORT, 'WL_M009_PHYSICAL_GOLD_TRADE_HISTORY_IMMUTABLE') END;

            SELECT CASE WHEN OLD."LedgerTransactionId" <> NEW."LedgerTransactionId"
              OR (NEW."CounterpartyInstitutionId" IS NOT NULL AND NOT EXISTS (
                SELECT 1 FROM "Institution" AS institution
                WHERE institution."Id" = NEW."CounterpartyInstitutionId"
                  AND institution."IsActive" = 1
                  AND institution."InstitutionTypeCode" IN ('JEWELER', 'BANK', 'OTHER')
              ))
            THEN RAISE(ABORT, 'WL_M009_PHYSICAL_GOLD_TRADE_DETAIL_INVALID') END;
        END;
        """;

    private const string PhysicalTradeDetailDeleteSql =
        """
        CREATE TRIGGER "TR_PhysicalGoldTradeDetail_ValidateDelete"
        BEFORE DELETE ON "PhysicalGoldTradeDetail"
        BEGIN
            SELECT CASE WHEN EXISTS (
                SELECT 1 FROM "LedgerTransaction" AS tx
                WHERE tx."Id" = OLD."LedgerTransactionId"
                  AND tx."StatusCode" = 'POSTED'
            ) THEN RAISE(ABORT, 'WL_M009_PHYSICAL_GOLD_TRADE_HISTORY_IMMUTABLE') END;
        END;
        """;

    private const string BuySellDispatchSql =
        """
        CREATE TRIGGER "TR_LedgerTransaction_ValidateBuySellDispatch"
        BEFORE UPDATE OF "StatusCode" ON "LedgerTransaction"
        WHEN NEW."StatusCode" = 'POSTED'
         AND OLD."StatusCode" <> 'POSTED'
         AND NEW."TransactionTypeCode" IN ('BUY', 'SELL')
        BEGIN
            SELECT CASE WHEN (
                SELECT COUNT(*)
                FROM "TransactionEntry" AS entry
                WHERE entry."TransactionId" = NEW."Id"
                  AND entry."EntryRoleCode" = 'PRINCIPAL'
            ) <> 1
            THEN RAISE(ABORT, 'WL_M009_BUY_SELL_PRINCIPAL_DISPATCH_INVALID') END;

            SELECT CASE WHEN NOT EXISTS (
                SELECT 1
                FROM "TransactionEntry" AS entry
                JOIN "Asset" AS asset ON asset."Id" = entry."AssetId"
                WHERE entry."TransactionId" = NEW."Id"
                  AND entry."EntryRoleCode" = 'PRINCIPAL'
                  AND asset."AssetTypeCode" IN ('FUND', 'PHYSICAL_GOLD')
            ) THEN RAISE(ABORT, 'WL_M009_BUY_SELL_ASSET_FAMILY_UNSUPPORTED') END;

            SELECT CASE WHEN EXISTS (
                SELECT 1
                FROM "PhysicalGoldTradeDetail" AS detail
                JOIN "TransactionEntry" AS principal
                  ON principal."TransactionId" = NEW."Id"
                 AND principal."EntryRoleCode" = 'PRINCIPAL'
                JOIN "Asset" AS asset ON asset."Id" = principal."AssetId"
                WHERE detail."LedgerTransactionId" = NEW."Id"
                  AND asset."AssetTypeCode" <> 'PHYSICAL_GOLD'
            ) THEN RAISE(ABORT, 'WL_M009_PHYSICAL_GOLD_TRADE_DETAIL_WRONG_FAMILY') END;
        END;
        """;

    private const string PhysicalAllocationPostingSql =
        """
        CREATE TRIGGER "TR_LedgerTransaction_ValidatePhysicalGoldAllocationPosting"
        BEFORE UPDATE OF "StatusCode" ON "LedgerTransaction"
        WHEN NEW."StatusCode" = 'POSTED'
         AND OLD."StatusCode" <> 'POSTED'
         AND EXISTS (
            SELECT 1
            FROM "LotEntryAllocation" AS allocation
            JOIN "TransactionEntry" AS entry
              ON entry."Id" = allocation."TransactionEntryId"
            JOIN "AssetLot" AS lot ON lot."Id" = allocation."AssetLotId"
            JOIN "Asset" AS asset ON asset."Id" = lot."AssetId"
            WHERE entry."TransactionId" = NEW."Id"
              AND asset."AssetTypeCode" = 'PHYSICAL_GOLD'
         )
        BEGIN
            SELECT CASE WHEN EXISTS (
                SELECT 1
                FROM "LotEntryAllocation" AS allocation
                JOIN "TransactionEntry" AS entry
                  ON entry."Id" = allocation."TransactionEntryId"
                JOIN "AssetLot" AS lot ON lot."Id" = allocation."AssetLotId"
                JOIN "Asset" AS asset ON asset."Id" = lot."AssetId"
                JOIN "PhysicalGoldLotDetail" AS gold
                  ON gold."AssetLotId" = lot."Id"
                LEFT JOIN "PhysicalGoldLotAllocationDetail" AS piece
                  ON piece."LotEntryAllocationId" = allocation."Id"
                WHERE entry."TransactionId" = NEW."Id"
                  AND asset."AssetTypeCode" = 'PHYSICAL_GOLD'
                  AND (piece."LotEntryAllocationId" IS NULL
                       OR (allocation."QuantityDeltaE8" > 0) <> (piece."PieceDelta" > 0))
            ) THEN RAISE(ABORT, 'WL_M009_PHYSICAL_GOLD_PIECE_DETAIL_REQUIRED') END;

            SELECT CASE WHEN EXISTS (
                SELECT 1
                FROM "PhysicalGoldLotAllocationDetail" AS piece
                JOIN "LotEntryAllocation" AS allocation
                  ON allocation."Id" = piece."LotEntryAllocationId"
                JOIN "TransactionEntry" AS entry
                  ON entry."Id" = allocation."TransactionEntryId"
                JOIN "AssetLot" AS lot ON lot."Id" = allocation."AssetLotId"
                JOIN "Asset" AS asset ON asset."Id" = lot."AssetId"
                WHERE entry."TransactionId" = NEW."Id"
                  AND asset."AssetTypeCode" <> 'PHYSICAL_GOLD'
            ) THEN RAISE(ABORT, 'WL_M009_PHYSICAL_GOLD_PIECE_DETAIL_FORBIDDEN') END;

            SELECT CASE WHEN EXISTS (
                SELECT 1
                FROM "AssetLot" AS lot
                JOIN "PhysicalGoldLotDetail" AS gold
                  ON gold."AssetLotId" = lot."Id"
                JOIN "LotEntryAllocation" AS allocation
                  ON allocation."AssetLotId" = lot."Id"
                 AND allocation."TransactionEntryId" = lot."OpeningTransactionEntryId"
                JOIN "PhysicalGoldLotAllocationDetail" AS piece
                  ON piece."LotEntryAllocationId" = allocation."Id"
                JOIN "TransactionEntry" AS entry
                  ON entry."Id" = allocation."TransactionEntryId"
                WHERE entry."TransactionId" = NEW."Id"
                  AND piece."PieceDelta" <> gold."PieceCount"
            ) THEN RAISE(ABORT, 'WL_M009_PHYSICAL_GOLD_OPENING_PIECES_INVALID') END;

            SELECT CASE WHEN EXISTS (
                SELECT 1
                FROM "LotEntryAllocation" AS touched_allocation
                JOIN "TransactionEntry" AS touched_entry
                  ON touched_entry."Id" = touched_allocation."TransactionEntryId"
                JOIN "AssetLot" AS touched_lot
                  ON touched_lot."Id" = touched_allocation."AssetLotId"
                JOIN "Asset" AS touched_asset
                  ON touched_asset."Id" = touched_lot."AssetId"
                WHERE touched_entry."TransactionId" = NEW."Id"
                  AND touched_asset."AssetTypeCode" = 'PHYSICAL_GOLD'
                  AND (
                    (SELECT COALESCE(SUM(allocation."QuantityDeltaE8"), 0)
                     FROM "LotEntryAllocation" AS allocation
                     JOIN "TransactionEntry" AS entry
                       ON entry."Id" = allocation."TransactionEntryId"
                     JOIN "LedgerTransaction" AS tx
                       ON tx."Id" = entry."TransactionId"
                     WHERE allocation."AssetLotId" = touched_lot."Id"
                       AND (tx."StatusCode" = 'POSTED' OR tx."Id" = NEW."Id")) < 0
                    OR
                    (SELECT COALESCE(SUM(piece."PieceDelta"), 0)
                     FROM "LotEntryAllocation" AS allocation
                     JOIN "PhysicalGoldLotAllocationDetail" AS piece
                       ON piece."LotEntryAllocationId" = allocation."Id"
                     JOIN "TransactionEntry" AS entry
                       ON entry."Id" = allocation."TransactionEntryId"
                     JOIN "LedgerTransaction" AS tx
                       ON tx."Id" = entry."TransactionId"
                     WHERE allocation."AssetLotId" = touched_lot."Id"
                       AND (tx."StatusCode" = 'POSTED' OR tx."Id" = NEW."Id")) < 0
                  )
            ) THEN RAISE(ABORT, 'WL_M009_PHYSICAL_GOLD_GLOBAL_QUANTITY_OR_PIECE_NEGATIVE') END;

            SELECT CASE WHEN EXISTS (
                SELECT 1
                FROM "LotEntryAllocation" AS touched_allocation
                JOIN "TransactionEntry" AS touched_entry
                  ON touched_entry."Id" = touched_allocation."TransactionEntryId"
                JOIN "AssetLot" AS touched_lot
                  ON touched_lot."Id" = touched_allocation."AssetLotId"
                JOIN "Asset" AS touched_asset
                  ON touched_asset."Id" = touched_lot."AssetId"
                WHERE touched_entry."TransactionId" = NEW."Id"
                  AND touched_asset."AssetTypeCode" = 'PHYSICAL_GOLD'
                  AND EXISTS (
                    SELECT 1
                    FROM "LotEntryAllocation" AS allocation
                    JOIN "PhysicalGoldLotAllocationDetail" AS piece
                      ON piece."LotEntryAllocationId" = allocation."Id"
                    JOIN "TransactionEntry" AS entry
                      ON entry."Id" = allocation."TransactionEntryId"
                    JOIN "LedgerTransaction" AS tx
                      ON tx."Id" = entry."TransactionId"
                    WHERE allocation."AssetLotId" = touched_lot."Id"
                      AND (tx."StatusCode" = 'POSTED' OR tx."Id" = NEW."Id")
                    GROUP BY entry."PortfolioId", entry."AccountId"
                    HAVING SUM(allocation."QuantityDeltaE8") < 0
                        OR SUM(piece."PieceDelta") < 0
                  )
            ) THEN RAISE(ABORT, 'WL_M009_PHYSICAL_GOLD_SCOPED_QUANTITY_OR_PIECE_NEGATIVE') END;

            SELECT CASE WHEN NEW."TransactionTypeCode" = 'REVERSAL'
              AND EXISTS (
                SELECT 1
                FROM "LotEntryAllocation" AS reversed_allocation
                JOIN "PhysicalGoldLotAllocationDetail" AS reversed_piece
                  ON reversed_piece."LotEntryAllocationId" = reversed_allocation."Id"
                JOIN "TransactionEntry" AS reversed_entry
                  ON reversed_entry."Id" = reversed_allocation."TransactionEntryId"
                JOIN "AssetLot" AS lot
                  ON lot."Id" = reversed_allocation."AssetLotId"
                JOIN "Asset" AS asset ON asset."Id" = lot."AssetId"
                WHERE reversed_entry."TransactionId" = NEW."Id"
                  AND asset."AssetTypeCode" = 'PHYSICAL_GOLD'
                  AND NOT EXISTS (
                    SELECT 1
                    FROM "TransactionEntry" AS original_entry
                    JOIN "LotEntryAllocation" AS original_allocation
                      ON original_allocation."TransactionEntryId" = original_entry."Id"
                     AND original_allocation."AssetLotId" = reversed_allocation."AssetLotId"
                    JOIN "PhysicalGoldLotAllocationDetail" AS original_piece
                      ON original_piece."LotEntryAllocationId" = original_allocation."Id"
                    WHERE original_entry."TransactionId" = NEW."ReversalOfTransactionId"
                      AND original_entry."EntrySequence" = reversed_entry."EntrySequence"
                      AND reversed_allocation."QuantityDeltaE8" = -original_allocation."QuantityDeltaE8"
                      AND reversed_piece."PieceDelta" = -original_piece."PieceDelta"
                  )
              )
            THEN RAISE(ABORT, 'WL_M009_PHYSICAL_GOLD_REVERSAL_PIECES_INVALID') END;

            SELECT CASE WHEN NEW."TransactionTypeCode" NOT IN ('BUY', 'SELL')
              AND EXISTS (
                SELECT 1 FROM "PhysicalGoldTradeDetail" AS detail
                WHERE detail."LedgerTransactionId" = NEW."Id"
              )
            THEN RAISE(ABORT, 'WL_M009_PHYSICAL_GOLD_TRADE_DETAIL_FORBIDDEN') END;
        END;
        """;

    private const string PhysicalTradePostingSql =
        """
        CREATE TRIGGER "TR_LedgerTransaction_ValidatePhysicalGoldTradePosting"
        BEFORE UPDATE OF "StatusCode" ON "LedgerTransaction"
        WHEN NEW."StatusCode" = 'POSTED'
         AND OLD."StatusCode" <> 'POSTED'
         AND NEW."TransactionTypeCode" IN ('BUY', 'SELL')
         AND EXISTS (
            SELECT 1
            FROM "TransactionEntry" AS principal
            JOIN "Asset" AS asset ON asset."Id" = principal."AssetId"
            WHERE principal."TransactionId" = NEW."Id"
              AND principal."EntryRoleCode" = 'PRINCIPAL'
              AND asset."AssetTypeCode" = 'PHYSICAL_GOLD'
         )
        BEGIN
            SELECT CASE WHEN NEW."ExecutionDate" IS NULL
              OR NEW."ExecutionDate" > date('now', 'localtime')
              OR (NEW."OrderDate" IS NOT NULL AND NEW."OrderDate" > NEW."ExecutionDate")
              OR (NEW."SettlementDate" IS NOT NULL AND NEW."SettlementDate" < NEW."ExecutionDate")
              OR trim(COALESCE(NEW."ExternalReference", '')) = ''
                 AND trim(COALESCE(NEW."Note", '')) = ''
            THEN RAISE(ABORT, 'WL_M009_PHYSICAL_GOLD_TRADE_DATE_OR_PROVENANCE_INVALID') END;

            SELECT CASE WHEN (
                SELECT COUNT(*) FROM "TransactionEntry" AS entry
                WHERE entry."TransactionId" = NEW."Id"
                  AND entry."EntryRoleCode" = 'PRINCIPAL'
            ) <> 1 OR (
                SELECT COUNT(*) FROM "TransactionEntry" AS entry
                WHERE entry."TransactionId" = NEW."Id"
                  AND entry."EntryRoleCode" = 'CONSIDERATION'
            ) <> 1 OR EXISTS (
                SELECT 1 FROM "TransactionEntry" AS entry
                WHERE entry."TransactionId" = NEW."Id"
                  AND entry."EntryRoleCode" NOT IN ('PRINCIPAL', 'CONSIDERATION', 'FEE', 'TAX')
            )
            THEN RAISE(ABORT, 'WL_M009_PHYSICAL_GOLD_TRADE_ENTRY_SHAPE_INVALID') END;

            SELECT CASE WHEN EXISTS (
                SELECT 1
                FROM "TransactionEntry" AS principal
                JOIN "Asset" AS asset ON asset."Id" = principal."AssetId"
                JOIN "Account" AS account ON account."Id" = principal."AccountId"
                JOIN "Portfolio" AS portfolio ON portfolio."Id" = principal."PortfolioId"
                WHERE principal."TransactionId" = NEW."Id"
                  AND principal."EntryRoleCode" = 'PRINCIPAL'
                  AND (
                    asset."AssetTypeCode" <> 'PHYSICAL_GOLD'
                    OR asset."BaseUnitCode" <> 'GROSS_GRAM'
                    OR asset."LotTrackingModeCode" <> 'REQUIRED'
                    OR asset."BaseCurrencyCode" IS NULL
                    OR asset."IsActive" <> 1
                    OR account."AccountTypeCode" <> 'PHYSICAL_VAULT'
                    OR account."IsActive" <> 1
                    OR account."ClosedOn" IS NOT NULL
                    OR account."HouseholdId" <> NEW."HouseholdId"
                    OR (account."OpenedOn" IS NOT NULL AND account."OpenedOn" > NEW."ExecutionDate")
                    OR (account."InstitutionId" IS NOT NULL AND NOT EXISTS (
                        SELECT 1 FROM "Institution" AS institution
                        WHERE institution."Id" = account."InstitutionId"
                          AND institution."IsActive" = 1))
                    OR portfolio."HouseholdId" <> NEW."HouseholdId"
                    OR portfolio."StatusCode" <> 'ACTIVE'
                    OR (NEW."TransactionTypeCode" = 'BUY' AND principal."QuantityDeltaE8" <= 0)
                    OR (NEW."TransactionTypeCode" = 'SELL' AND principal."QuantityDeltaE8" >= 0)
                    OR (principal."UnitPriceE8" IS NULL) <> (principal."PriceCurrencyCode" IS NULL)
                    OR (principal."UnitPriceE8" IS NOT NULL AND principal."UnitPriceE8" <= 0)
                    OR (principal."PriceCurrencyCode" IS NOT NULL
                        AND principal."PriceCurrencyCode" <> asset."BaseCurrencyCode")
                  )
            ) THEN RAISE(ABORT, 'WL_M009_PHYSICAL_GOLD_PRINCIPAL_INVALID') END;

            SELECT CASE WHEN EXISTS (
                SELECT 1
                FROM "TransactionEntry" AS cash_entry
                JOIN "Account" AS account ON account."Id" = cash_entry."AccountId"
                JOIN "Asset" AS cash_asset ON cash_asset."Id" = cash_entry."AssetId"
                JOIN "Portfolio" AS portfolio ON portfolio."Id" = cash_entry."PortfolioId"
                JOIN "TransactionEntry" AS principal
                  ON principal."TransactionId" = NEW."Id"
                 AND principal."EntryRoleCode" = 'PRINCIPAL'
                JOIN "Asset" AS gold_asset ON gold_asset."Id" = principal."AssetId"
                WHERE cash_entry."TransactionId" = NEW."Id"
                  AND cash_entry."EntryRoleCode" IN ('CONSIDERATION', 'FEE', 'TAX')
                  AND (
                    cash_asset."AssetTypeCode" NOT IN ('CASH', 'CURRENCY')
                    OR cash_asset."BaseUnitCode" <> 'CURRENCY_UNIT'
                    OR cash_asset."LotTrackingModeCode" <> 'NONE'
                    OR cash_asset."IsActive" <> 1
                    OR cash_asset."BaseCurrencyCode" <> gold_asset."BaseCurrencyCode"
                    OR account."AccountTypeCode" NOT IN ('CASH', 'INVESTMENT', 'PENSION')
                    OR account."IsActive" <> 1
                    OR account."ClosedOn" IS NOT NULL
                    OR account."HouseholdId" <> NEW."HouseholdId"
                    OR (account."OpenedOn" IS NOT NULL AND account."OpenedOn" > NEW."ExecutionDate")
                    OR (account."AccountTypeCode" IN ('INVESTMENT', 'PENSION')
                        AND (account."InstitutionId" IS NULL OR NOT EXISTS (
                            SELECT 1 FROM "Institution" AS institution
                            WHERE institution."Id" = account."InstitutionId"
                              AND institution."IsActive" = 1)))
                    OR portfolio."HouseholdId" <> NEW."HouseholdId"
                    OR portfolio."StatusCode" <> 'ACTIVE'
                    OR cash_entry."PortfolioId" <> principal."PortfolioId"
                    OR cash_entry."UnitPriceE8" IS NOT NULL
                    OR cash_entry."PriceCurrencyCode" IS NOT NULL
                    OR (cash_entry."EntryRoleCode" = 'CONSIDERATION'
                        AND ((NEW."TransactionTypeCode" = 'BUY' AND cash_entry."QuantityDeltaE8" >= 0)
                             OR (NEW."TransactionTypeCode" = 'SELL' AND cash_entry."QuantityDeltaE8" <= 0)))
                    OR (cash_entry."EntryRoleCode" IN ('FEE', 'TAX')
                        AND cash_entry."QuantityDeltaE8" >= 0)
                  )
            ) THEN RAISE(ABORT, 'WL_M009_PHYSICAL_GOLD_CASH_SCOPE_INVALID') END;

            SELECT CASE WHEN EXISTS (
                SELECT 1
                FROM "TransactionEntry" AS cash_entry
                JOIN "TransactionEntry" AS consideration
                  ON consideration."TransactionId" = NEW."Id"
                 AND consideration."EntryRoleCode" = 'CONSIDERATION'
                WHERE cash_entry."TransactionId" = NEW."Id"
                  AND cash_entry."EntryRoleCode" IN ('FEE', 'TAX')
                  AND (cash_entry."PortfolioId" <> consideration."PortfolioId"
                       OR cash_entry."AccountId" <> consideration."AccountId"
                       OR cash_entry."AssetId" <> consideration."AssetId")
            ) THEN RAISE(ABORT, 'WL_M009_PHYSICAL_GOLD_CASH_SCOPE_INVALID') END;

            SELECT CASE WHEN (
                SELECT COUNT(*) FROM "PhysicalGoldTradeDetail" AS detail
                WHERE detail."LedgerTransactionId" = NEW."Id"
            ) <> 1 OR EXISTS (
                SELECT 1
                FROM "PhysicalGoldTradeDetail" AS detail
                JOIN "Institution" AS institution
                  ON institution."Id" = detail."CounterpartyInstitutionId"
                WHERE detail."LedgerTransactionId" = NEW."Id"
                  AND (institution."IsActive" <> 1
                       OR institution."InstitutionTypeCode" NOT IN ('JEWELER', 'BANK', 'OTHER'))
            )
            THEN RAISE(ABORT, 'WL_M009_PHYSICAL_GOLD_COUNTERPARTY_INVALID') END;

            SELECT CASE WHEN (
                SELECT COUNT(*) FROM "TransactionCostComponent" AS cost
                WHERE cost."TransactionId" = NEW."Id"
            ) > 16 OR EXISTS (
                SELECT 1
                FROM "TransactionCostComponent" AS cost
                JOIN "TransactionEntry" AS principal
                  ON principal."TransactionId" = NEW."Id"
                 AND principal."EntryRoleCode" = 'PRINCIPAL'
                JOIN "Asset" AS gold_asset ON gold_asset."Id" = principal."AssetId"
                WHERE cost."TransactionId" = NEW."Id"
                  AND (cost."AmountMinor" <= 0
                       OR cost."CurrencyCode" <> gold_asset."BaseCurrencyCode"
                       OR cost."CostTypeCode" NOT IN ('MAKING_CHARGE', 'COMMISSION', 'OTHER_TAX', 'OTHER')
                       OR cost."TreatmentCode" NOT IN ('ADDITIONAL_CASH_OUTFLOW', 'INCLUDED_IN_CONSIDERATION', 'INFORMATIONAL_ONLY', 'WITHHELD_FROM_PROCEEDS')
                       OR (NEW."TransactionTypeCode" = 'BUY'
                           AND (cost."CostTypeCode" NOT IN ('MAKING_CHARGE', 'COMMISSION', 'OTHER_TAX', 'OTHER')
                                OR cost."TreatmentCode" = 'WITHHELD_FROM_PROCEEDS'))
                       OR (NEW."TransactionTypeCode" = 'SELL'
                           AND cost."CostTypeCode" NOT IN ('COMMISSION', 'OTHER_TAX', 'OTHER'))
                       OR (cost."CostTypeCode" = 'OTHER'
                           AND trim(COALESCE(cost."Note", '')) = ''))
            ) THEN RAISE(ABORT, 'WL_M009_PHYSICAL_GOLD_COST_INVALID') END;

            SELECT CASE WHEN EXISTS (
                SELECT 1
                FROM "TransactionEntry" AS consideration
                JOIN "Asset" AS cash_asset ON cash_asset."Id" = consideration."AssetId"
                JOIN "Currency" AS currency ON currency."Code" = cash_asset."BaseCurrencyCode"
                WHERE consideration."TransactionId" = NEW."Id"
                  AND consideration."EntryRoleCode" = 'CONSIDERATION'
                  AND abs(consideration."QuantityDeltaE8") % CASE currency."MinorUnitDigits"
                    WHEN 0 THEN 100000000 WHEN 1 THEN 10000000
                    WHEN 2 THEN 1000000 WHEN 3 THEN 100000
                    WHEN 4 THEN 10000 WHEN 5 THEN 1000
                    WHEN 6 THEN 100 WHEN 7 THEN 10 WHEN 8 THEN 1
                    ELSE 0 END <> 0
            ) THEN RAISE(ABORT, 'WL_M009_PHYSICAL_GOLD_CASH_PRECISION_INVALID') END;

            SELECT CASE WHEN (
                SELECT COUNT(*) FROM "TransactionEntry" AS entry
                WHERE entry."TransactionId" = NEW."Id" AND entry."EntryRoleCode" = 'FEE'
            ) <> CASE WHEN EXISTS (
                SELECT 1 FROM "TransactionCostComponent" AS cost
                WHERE cost."TransactionId" = NEW."Id"
                  AND cost."TreatmentCode" = 'ADDITIONAL_CASH_OUTFLOW'
                  AND cost."CostTypeCode" <> 'OTHER_TAX') THEN 1 ELSE 0 END
            OR (
                SELECT COUNT(*) FROM "TransactionEntry" AS entry
                WHERE entry."TransactionId" = NEW."Id" AND entry."EntryRoleCode" = 'TAX'
            ) <> CASE WHEN EXISTS (
                SELECT 1 FROM "TransactionCostComponent" AS cost
                WHERE cost."TransactionId" = NEW."Id"
                  AND cost."TreatmentCode" = 'ADDITIONAL_CASH_OUTFLOW'
                  AND cost."CostTypeCode" = 'OTHER_TAX') THEN 1 ELSE 0 END
            THEN RAISE(ABORT, 'WL_M009_PHYSICAL_GOLD_COST_CASH_ENTRY_MISSING') END;

            SELECT CASE WHEN EXISTS (
                SELECT 1
                FROM "TransactionEntry" AS support
                JOIN "Asset" AS cash_asset ON cash_asset."Id" = support."AssetId"
                JOIN "Currency" AS currency ON currency."Code" = cash_asset."BaseCurrencyCode"
                WHERE support."TransactionId" = NEW."Id"
                  AND support."EntryRoleCode" IN ('FEE', 'TAX')
                  AND support."QuantityDeltaE8" <> -(
                    SELECT COALESCE(SUM(cost."AmountMinor"), 0)
                    FROM "TransactionCostComponent" AS cost
                    WHERE cost."TransactionId" = NEW."Id"
                      AND cost."TreatmentCode" = 'ADDITIONAL_CASH_OUTFLOW'
                      AND ((support."EntryRoleCode" = 'TAX' AND cost."CostTypeCode" = 'OTHER_TAX')
                           OR (support."EntryRoleCode" = 'FEE' AND cost."CostTypeCode" <> 'OTHER_TAX'))
                  ) * CASE currency."MinorUnitDigits"
                    WHEN 0 THEN 100000000 WHEN 1 THEN 10000000
                    WHEN 2 THEN 1000000 WHEN 3 THEN 100000
                    WHEN 4 THEN 10000 WHEN 5 THEN 1000
                    WHEN 6 THEN 100 WHEN 7 THEN 10 WHEN 8 THEN 1
                    ELSE 0 END
            ) THEN RAISE(ABORT, 'WL_M009_PHYSICAL_GOLD_COST_CASH_EQUATION_INVALID') END;

            SELECT CASE WHEN NEW."TransactionTypeCode" = 'BUY' AND (
                (SELECT COUNT(*)
                 FROM "AssetLot" AS lot
                 JOIN "TransactionEntry" AS opening_entry
                   ON opening_entry."Id" = lot."OpeningTransactionEntryId"
                 WHERE opening_entry."TransactionId" = NEW."Id") <> 1
                OR (SELECT COUNT(*)
                    FROM "AssetLot" AS lot
                    JOIN "TransactionEntry" AS opening_entry
                      ON opening_entry."Id" = lot."OpeningTransactionEntryId"
                    JOIN "PhysicalGoldLotDetail" AS gold
                      ON gold."AssetLotId" = lot."Id"
                    WHERE opening_entry."TransactionId" = NEW."Id") <> 1
                OR (SELECT COUNT(*)
                    FROM "AssetLot" AS lot
                    JOIN "TransactionEntry" AS opening_entry
                      ON opening_entry."Id" = lot."OpeningTransactionEntryId"
                    JOIN "LotEntryAllocation" AS allocation
                      ON allocation."AssetLotId" = lot."Id"
                     AND allocation."TransactionEntryId" = opening_entry."Id"
                    JOIN "PhysicalGoldLotAllocationDetail" AS piece
                      ON piece."LotEntryAllocationId" = allocation."Id"
                    WHERE opening_entry."TransactionId" = NEW."Id") <> 1
                OR EXISTS (
                    SELECT 1
                    FROM "AssetLot" AS lot
                    JOIN "TransactionEntry" AS opening_entry
                      ON opening_entry."Id" = lot."OpeningTransactionEntryId"
                    JOIN "TransactionEntry" AS consideration
                      ON consideration."TransactionId" = NEW."Id"
                     AND consideration."EntryRoleCode" = 'CONSIDERATION'
                    JOIN "Asset" AS cash_asset ON cash_asset."Id" = consideration."AssetId"
                    JOIN "Currency" AS currency ON currency."Code" = cash_asset."BaseCurrencyCode"
                    JOIN "PhysicalGoldLotDetail" AS gold ON gold."AssetLotId" = lot."Id"
                    JOIN "LotEntryAllocation" AS allocation
                      ON allocation."AssetLotId" = lot."Id"
                     AND allocation."TransactionEntryId" = opening_entry."Id"
                    JOIN "PhysicalGoldLotAllocationDetail" AS piece
                      ON piece."LotEntryAllocationId" = allocation."Id"
                    WHERE opening_entry."TransactionId" = NEW."Id"
                      AND (lot."AssetId" <> opening_entry."AssetId"
                           OR lot."AcquiredOn" <> NEW."ExecutionDate"
                           OR lot."CostBasisStatusCode" <> 'KNOWN'
                           OR lot."CostBasisCurrencyCode" <> cash_asset."BaseCurrencyCode"
                           OR lot."OriginalCostBasisMinor" <> abs(consideration."QuantityDeltaE8") /
                              CASE currency."MinorUnitDigits"
                                WHEN 0 THEN 100000000 WHEN 1 THEN 10000000
                                WHEN 2 THEN 1000000 WHEN 3 THEN 100000
                                WHEN 4 THEN 10000 WHEN 5 THEN 1000
                                WHEN 6 THEN 100 WHEN 7 THEN 10 WHEN 8 THEN 1
                                ELSE 0 END
                              + (SELECT COALESCE(SUM(cost."AmountMinor"), 0)
                                 FROM "TransactionCostComponent" AS cost
                                 WHERE cost."TransactionId" = NEW."Id"
                                   AND cost."TreatmentCode" = 'ADDITIONAL_CASH_OUTFLOW')
                           OR allocation."QuantityDeltaE8" <> opening_entry."QuantityDeltaE8"
                           OR piece."PieceDelta" <> gold."PieceCount")
                )
            ) THEN RAISE(ABORT, 'WL_M009_PHYSICAL_GOLD_PURCHASE_LOT_INVALID') END;

            SELECT CASE WHEN NEW."TransactionTypeCode" = 'SELL' AND (
                EXISTS (
                    SELECT 1 FROM "AssetLot" AS lot
                    JOIN "TransactionEntry" AS opening_entry
                      ON opening_entry."Id" = lot."OpeningTransactionEntryId"
                    WHERE opening_entry."TransactionId" = NEW."Id")
                OR (SELECT COUNT(*)
                    FROM "LotEntryAllocation" AS allocation
                    JOIN "TransactionEntry" AS entry
                      ON entry."Id" = allocation."TransactionEntryId"
                    WHERE entry."TransactionId" = NEW."Id"
                      AND entry."EntryRoleCode" = 'PRINCIPAL') < 1
                OR EXISTS (
                    SELECT 1
                    FROM "TransactionEntry" AS principal
                    WHERE principal."TransactionId" = NEW."Id"
                      AND principal."EntryRoleCode" = 'PRINCIPAL'
                      AND (SELECT COALESCE(SUM(allocation."QuantityDeltaE8"), 0)
                           FROM "LotEntryAllocation" AS allocation
                           WHERE allocation."TransactionEntryId" = principal."Id")
                          <> principal."QuantityDeltaE8")
            ) THEN RAISE(ABORT, 'WL_M009_PHYSICAL_GOLD_SALE_ALLOCATION_INVALID') END;
        END;
        """;

    private const string PhysicalTransferPostingSql =
        """
        CREATE TRIGGER "TR_LedgerTransaction_ValidatePhysicalGoldTransferPosting"
        BEFORE UPDATE OF "StatusCode" ON "LedgerTransaction"
        WHEN NEW."StatusCode" = 'POSTED'
         AND OLD."StatusCode" <> 'POSTED'
         AND NEW."TransactionTypeCode" = 'TRANSFER'
         AND EXISTS (
            SELECT 1
            FROM "TransactionEntry" AS entry
            JOIN "Asset" AS asset ON asset."Id" = entry."AssetId"
            WHERE entry."TransactionId" = NEW."Id"
              AND asset."AssetTypeCode" = 'PHYSICAL_GOLD'
         )
        BEGIN
            SELECT CASE WHEN NEW."ExecutionDate" IS NULL
              OR NEW."OrderDate" IS NOT NULL
              OR NEW."SettlementDate" IS NOT NULL
              OR NEW."ExecutionDate" > date('now', 'localtime')
              OR (trim(COALESCE(NEW."ExternalReference", '')) = ''
                  AND trim(COALESCE(NEW."Note", '')) = '')
            THEN RAISE(ABORT, 'WL_M009_PHYSICAL_GOLD_TRANSFER_DATE_OR_PROVENANCE_INVALID') END;

            SELECT CASE WHEN (
                SELECT COUNT(*)
                FROM "TransactionEntry" AS entry
                JOIN "Asset" AS asset ON asset."Id" = entry."AssetId"
                WHERE entry."TransactionId" = NEW."Id"
                  AND entry."EntryRoleCode" = 'TRANSFER'
                  AND asset."AssetTypeCode" = 'PHYSICAL_GOLD'
            ) <> 2 OR EXISTS (
                SELECT 1 FROM "TransactionEntry" AS entry
                WHERE entry."TransactionId" = NEW."Id"
                  AND entry."EntryRoleCode" NOT IN ('TRANSFER', 'FEE', 'TAX')
            ) OR EXISTS (
                SELECT 1 FROM "PhysicalGoldTradeDetail" AS detail
                WHERE detail."LedgerTransactionId" = NEW."Id"
            )
            THEN RAISE(ABORT, 'WL_M009_PHYSICAL_GOLD_TRANSFER_ENTRY_SHAPE_INVALID') END;

            SELECT CASE WHEN NOT EXISTS (
                SELECT 1
                FROM "TransactionEntry" AS source
                JOIN "TransactionEntry" AS destination
                  ON destination."TransactionId" = source."TransactionId"
                 AND destination."Id" <> source."Id"
                 AND destination."EntryRoleCode" = 'TRANSFER'
                JOIN "Asset" AS asset ON asset."Id" = source."AssetId"
                JOIN "Account" AS source_account ON source_account."Id" = source."AccountId"
                JOIN "Account" AS destination_account ON destination_account."Id" = destination."AccountId"
                JOIN "Portfolio" AS source_portfolio ON source_portfolio."Id" = source."PortfolioId"
                JOIN "Portfolio" AS destination_portfolio ON destination_portfolio."Id" = destination."PortfolioId"
                WHERE source."TransactionId" = NEW."Id"
                  AND source."EntryRoleCode" = 'TRANSFER'
                  AND source."QuantityDeltaE8" < 0
                  AND destination."QuantityDeltaE8" = -source."QuantityDeltaE8"
                  AND destination."AssetId" = source."AssetId"
                  AND (destination."PortfolioId" <> source."PortfolioId"
                       OR destination."AccountId" <> source."AccountId")
                  AND source."UnitPriceE8" IS NULL
                  AND source."PriceCurrencyCode" IS NULL
                  AND destination."UnitPriceE8" IS NULL
                  AND destination."PriceCurrencyCode" IS NULL
                  AND asset."AssetTypeCode" = 'PHYSICAL_GOLD'
                  AND asset."BaseUnitCode" = 'GROSS_GRAM'
                  AND asset."LotTrackingModeCode" = 'REQUIRED'
                  AND asset."BaseCurrencyCode" IS NOT NULL
                  AND asset."IsActive" = 1
                  AND source_account."AccountTypeCode" = 'PHYSICAL_VAULT'
                  AND destination_account."AccountTypeCode" = 'PHYSICAL_VAULT'
                  AND source_account."HouseholdId" = NEW."HouseholdId"
                  AND destination_account."HouseholdId" = NEW."HouseholdId"
                  AND source_account."IsActive" = 1
                  AND destination_account."IsActive" = 1
                  AND source_account."ClosedOn" IS NULL
                  AND destination_account."ClosedOn" IS NULL
                  AND (source_account."InstitutionId" IS NULL OR EXISTS (
                      SELECT 1 FROM "Institution" AS source_institution
                      WHERE source_institution."Id" = source_account."InstitutionId"
                        AND source_institution."IsActive" = 1))
                  AND (destination_account."InstitutionId" IS NULL OR EXISTS (
                      SELECT 1 FROM "Institution" AS destination_institution
                      WHERE destination_institution."Id" = destination_account."InstitutionId"
                        AND destination_institution."IsActive" = 1))
                  AND (source_account."OpenedOn" IS NULL OR source_account."OpenedOn" <= NEW."ExecutionDate")
                  AND (destination_account."OpenedOn" IS NULL OR destination_account."OpenedOn" <= NEW."ExecutionDate")
                  AND source_portfolio."HouseholdId" = NEW."HouseholdId"
                  AND destination_portfolio."HouseholdId" = NEW."HouseholdId"
                  AND source_portfolio."StatusCode" = 'ACTIVE'
                  AND destination_portfolio."StatusCode" = 'ACTIVE'
            ) THEN RAISE(ABORT, 'WL_M009_PHYSICAL_GOLD_TRANSFER_PRINCIPAL_INVALID') END;

            SELECT CASE WHEN EXISTS (
                SELECT 1
                FROM "TransactionEntry" AS source
                JOIN "LotEntryAllocation" AS source_allocation
                  ON source_allocation."TransactionEntryId" = source."Id"
                JOIN "PhysicalGoldLotAllocationDetail" AS source_piece
                  ON source_piece."LotEntryAllocationId" = source_allocation."Id"
                WHERE source."TransactionId" = NEW."Id"
                  AND source."EntryRoleCode" = 'TRANSFER'
                  AND source."QuantityDeltaE8" < 0
                  AND NOT EXISTS (
                    SELECT 1
                    FROM "TransactionEntry" AS destination
                    JOIN "LotEntryAllocation" AS destination_allocation
                      ON destination_allocation."TransactionEntryId" = destination."Id"
                     AND destination_allocation."AssetLotId" = source_allocation."AssetLotId"
                    JOIN "PhysicalGoldLotAllocationDetail" AS destination_piece
                      ON destination_piece."LotEntryAllocationId" = destination_allocation."Id"
                    WHERE destination."TransactionId" = NEW."Id"
                      AND destination."EntryRoleCode" = 'TRANSFER'
                      AND destination."QuantityDeltaE8" > 0
                      AND destination_allocation."QuantityDeltaE8" = -source_allocation."QuantityDeltaE8"
                      AND destination_piece."PieceDelta" = -source_piece."PieceDelta"
                  )
            ) OR EXISTS (
                SELECT 1
                FROM "TransactionEntry" AS principal
                WHERE principal."TransactionId" = NEW."Id"
                  AND principal."EntryRoleCode" = 'TRANSFER'
                  AND (SELECT COALESCE(SUM(allocation."QuantityDeltaE8"), 0)
                       FROM "LotEntryAllocation" AS allocation
                       WHERE allocation."TransactionEntryId" = principal."Id")
                      <> principal."QuantityDeltaE8"
            ) OR EXISTS (
                SELECT 1 FROM "AssetLot" AS lot
                JOIN "TransactionEntry" AS opening_entry
                  ON opening_entry."Id" = lot."OpeningTransactionEntryId"
                WHERE opening_entry."TransactionId" = NEW."Id"
            )
            THEN RAISE(ABORT, 'WL_M009_PHYSICAL_GOLD_TRANSFER_ALLOCATION_INVALID') END;

            SELECT CASE WHEN (
                SELECT COUNT(*) FROM "TransactionCostComponent" AS cost
                WHERE cost."TransactionId" = NEW."Id"
            ) > 16 OR EXISTS (
                SELECT 1
                FROM "TransactionCostComponent" AS cost
                JOIN "TransactionEntry" AS principal
                  ON principal."TransactionId" = NEW."Id"
                 AND principal."EntryRoleCode" = 'TRANSFER'
                 AND principal."QuantityDeltaE8" < 0
                JOIN "Asset" AS gold_asset ON gold_asset."Id" = principal."AssetId"
                WHERE cost."TransactionId" = NEW."Id"
                  AND (cost."AmountMinor" <= 0
                       OR cost."CurrencyCode" <> gold_asset."BaseCurrencyCode"
                       OR cost."CostTypeCode" NOT IN ('COMMISSION', 'INSURANCE', 'OTHER_TAX', 'OTHER')
                       OR cost."TreatmentCode" NOT IN ('ADDITIONAL_CASH_OUTFLOW', 'INFORMATIONAL_ONLY')
                       OR (cost."CostTypeCode" = 'OTHER'
                           AND trim(COALESCE(cost."Note", '')) = ''))
            ) THEN RAISE(ABORT, 'WL_M009_PHYSICAL_GOLD_TRANSFER_COST_INVALID') END;

            SELECT CASE WHEN EXISTS (
                SELECT 1
                FROM "TransactionEntry" AS cash_entry
                JOIN "Account" AS account ON account."Id" = cash_entry."AccountId"
                JOIN "Asset" AS cash_asset ON cash_asset."Id" = cash_entry."AssetId"
                JOIN "Portfolio" AS portfolio ON portfolio."Id" = cash_entry."PortfolioId"
                JOIN "TransactionEntry" AS principal
                  ON principal."TransactionId" = NEW."Id"
                 AND principal."EntryRoleCode" = 'TRANSFER'
                 AND principal."QuantityDeltaE8" < 0
                JOIN "Asset" AS gold_asset ON gold_asset."Id" = principal."AssetId"
                WHERE cash_entry."TransactionId" = NEW."Id"
                  AND cash_entry."EntryRoleCode" IN ('FEE', 'TAX')
                  AND (cash_entry."QuantityDeltaE8" >= 0
                       OR cash_entry."UnitPriceE8" IS NOT NULL
                       OR cash_entry."PriceCurrencyCode" IS NOT NULL
                       OR cash_asset."AssetTypeCode" NOT IN ('CASH', 'CURRENCY')
                       OR cash_asset."BaseUnitCode" <> 'CURRENCY_UNIT'
                       OR cash_asset."LotTrackingModeCode" <> 'NONE'
                       OR cash_asset."BaseCurrencyCode" <> gold_asset."BaseCurrencyCode"
                       OR cash_asset."IsActive" <> 1
                       OR account."AccountTypeCode" NOT IN ('CASH', 'INVESTMENT', 'PENSION')
                       OR account."HouseholdId" <> NEW."HouseholdId"
                       OR account."IsActive" <> 1
                       OR account."ClosedOn" IS NOT NULL
                       OR (account."OpenedOn" IS NOT NULL AND account."OpenedOn" > NEW."ExecutionDate")
                       OR (account."AccountTypeCode" IN ('INVESTMENT', 'PENSION')
                           AND (account."InstitutionId" IS NULL OR NOT EXISTS (
                               SELECT 1 FROM "Institution" AS institution
                               WHERE institution."Id" = account."InstitutionId"
                                 AND institution."IsActive" = 1)))
                       OR portfolio."HouseholdId" <> NEW."HouseholdId"
                       OR portfolio."StatusCode" <> 'ACTIVE')
            ) OR EXISTS (
                SELECT 1
                FROM "TransactionEntry" AS left_entry
                JOIN "TransactionEntry" AS right_entry
                  ON right_entry."TransactionId" = left_entry."TransactionId"
                 AND right_entry."Id" <> left_entry."Id"
                 AND right_entry."EntryRoleCode" IN ('FEE', 'TAX')
                WHERE left_entry."TransactionId" = NEW."Id"
                  AND left_entry."EntryRoleCode" IN ('FEE', 'TAX')
                  AND (left_entry."PortfolioId" <> right_entry."PortfolioId"
                       OR left_entry."AccountId" <> right_entry."AccountId"
                       OR left_entry."AssetId" <> right_entry."AssetId")
            ) THEN RAISE(ABORT, 'WL_M009_PHYSICAL_GOLD_TRANSFER_CASH_SCOPE_INVALID') END;

            SELECT CASE WHEN (
                SELECT COUNT(*) FROM "TransactionEntry" AS entry
                WHERE entry."TransactionId" = NEW."Id" AND entry."EntryRoleCode" = 'FEE'
            ) <> CASE WHEN EXISTS (
                SELECT 1 FROM "TransactionCostComponent" AS cost
                WHERE cost."TransactionId" = NEW."Id"
                  AND cost."TreatmentCode" = 'ADDITIONAL_CASH_OUTFLOW'
                  AND cost."CostTypeCode" <> 'OTHER_TAX') THEN 1 ELSE 0 END
            OR (
                SELECT COUNT(*) FROM "TransactionEntry" AS entry
                WHERE entry."TransactionId" = NEW."Id" AND entry."EntryRoleCode" = 'TAX'
            ) <> CASE WHEN EXISTS (
                SELECT 1 FROM "TransactionCostComponent" AS cost
                WHERE cost."TransactionId" = NEW."Id"
                  AND cost."TreatmentCode" = 'ADDITIONAL_CASH_OUTFLOW'
                  AND cost."CostTypeCode" = 'OTHER_TAX') THEN 1 ELSE 0 END
            THEN RAISE(ABORT, 'WL_M009_PHYSICAL_GOLD_TRANSFER_COST_CASH_ENTRY_MISSING') END;

            SELECT CASE WHEN EXISTS (
                SELECT 1
                FROM "TransactionEntry" AS support
                JOIN "Asset" AS cash_asset ON cash_asset."Id" = support."AssetId"
                JOIN "Currency" AS currency ON currency."Code" = cash_asset."BaseCurrencyCode"
                WHERE support."TransactionId" = NEW."Id"
                  AND support."EntryRoleCode" IN ('FEE', 'TAX')
                  AND support."QuantityDeltaE8" <> -(
                    SELECT COALESCE(SUM(cost."AmountMinor"), 0)
                    FROM "TransactionCostComponent" AS cost
                    WHERE cost."TransactionId" = NEW."Id"
                      AND cost."TreatmentCode" = 'ADDITIONAL_CASH_OUTFLOW'
                      AND ((support."EntryRoleCode" = 'TAX' AND cost."CostTypeCode" = 'OTHER_TAX')
                           OR (support."EntryRoleCode" = 'FEE' AND cost."CostTypeCode" <> 'OTHER_TAX'))
                  ) * CASE currency."MinorUnitDigits"
                    WHEN 0 THEN 100000000 WHEN 1 THEN 10000000
                    WHEN 2 THEN 1000000 WHEN 3 THEN 100000
                    WHEN 4 THEN 10000 WHEN 5 THEN 1000
                    WHEN 6 THEN 100 WHEN 7 THEN 10 WHEN 8 THEN 1
                    ELSE 0 END
            ) THEN RAISE(ABORT, 'WL_M009_PHYSICAL_GOLD_TRANSFER_COST_CASH_EQUATION_INVALID') END;
        END;
        """;
}
