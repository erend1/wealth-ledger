using Microsoft.EntityFrameworkCore.Migrations;

namespace WealthLedger.Infrastructure.Persistence.Migrations;

/*
 * M008 database guards.
 *
 * These protect the accepted fund-trade shape against a writer that bypasses
 * the application, and against two concurrent writers that each believed they
 * had enough quantity.
 *
 * They deliberately do not try to judge plan freshness. A trigger has no
 * knowledge of what a user reviewed, so that check belongs to the application
 * inside its write transaction. What the database can guarantee, and does
 * guarantee here, is that quantity never goes negative and that the posted
 * shape matches the accepted contract.
 *
 * Every guard fires only on the draft-to-posted transition, so it applies to
 * new postings and never reinterprets history posted under M001-M007.
 */
public partial class _007_FundTradeLifecycleGuards
{
    private const string TradeTriggerName =
        "TR_LedgerTransaction_ValidateFundTradeBeforePosting";

    /*
     * One trigger, no index, and no duplicate of an existing guard.
     *
     * Two candidates were dropped after being measured rather than assumed:
     *
     * An index on LotEntryAllocation("AssetLotId") looked necessary because
     * the guard sums a lot's allocations. The actual SQLite query plan showed
     * the existing UX_LotEntryAllocation_Lot_Entry already turns that sum
     * into an index search, and scoped availability already uses
     * IX_TransactionEntry_OpeningBalanceScope from migration 006.
     *
     * A non-negative lot-balance trigger looked necessary because Decision 18
     * requires that protection at the posting boundary. It already exists:
     * M001's TR_LotEntryAllocation_ValidateInsert enforces it on insert, and
     * its update and delete counterparts cover the other paths. A drill
     * against real SQLite confirmed those fire first and refuse both
     * over-consumption and any sale against an emptied lot.
     *
     * Shipping either would have cost write throughput on every allocation
     * for no additional protection.
     */
    private static void AddFundTradeGuards(
        MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(FundTradePostingTriggerSql);
    }

    private static void DropFundTradeGuards(
        MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            $"""DROP TRIGGER IF EXISTS "{TradeTriggerName}";""");
    }

    private const string FundTradePostingTriggerSql =
        $"""
        CREATE TRIGGER "{TradeTriggerName}"
        BEFORE UPDATE OF "StatusCode" ON "LedgerTransaction"
        WHEN NEW."StatusCode" = 'POSTED'
         AND OLD."StatusCode" <> 'POSTED'
         AND NEW."TransactionTypeCode" IN ('BUY', 'SELL')
         AND EXISTS (
            SELECT 1
            FROM "TransactionEntry" AS entry
            JOIN "Asset" AS asset
              ON asset."Id" = entry."AssetId"
            WHERE entry."TransactionId" = NEW."Id"
              AND entry."EntryRoleCode" = 'PRINCIPAL'
              AND asset."AssetTypeCode" = 'FUND'
         )
        BEGIN
            -- Exactly one fund principal entry.
            SELECT CASE
                WHEN (
                    SELECT COUNT(*)
                    FROM "TransactionEntry" AS entry
                    WHERE entry."TransactionId" = NEW."Id"
                      AND entry."EntryRoleCode" = 'PRINCIPAL'
                ) <> 1
                THEN RAISE(
                    ABORT,
                    'FUND_TRADE_SHAPE_INVALID: a fund trade must contain exactly one principal entry.')
            END;

            -- Exactly one cash consideration entry.
            SELECT CASE
                WHEN (
                    SELECT COUNT(*)
                    FROM "TransactionEntry" AS entry
                    WHERE entry."TransactionId" = NEW."Id"
                      AND entry."EntryRoleCode" = 'CONSIDERATION'
                ) <> 1
                THEN RAISE(
                    ABORT,
                    'FUND_TRADE_SHAPE_INVALID: a fund trade must contain exactly one consideration entry.')
            END;

            -- Only principal, consideration, fee and tax entries.
            SELECT CASE
                WHEN EXISTS (
                    SELECT 1
                    FROM "TransactionEntry" AS entry
                    WHERE entry."TransactionId" = NEW."Id"
                      AND entry."EntryRoleCode" NOT IN (
                        'PRINCIPAL', 'CONSIDERATION', 'FEE', 'TAX')
                )
                THEN RAISE(
                    ABORT,
                    'FUND_TRADE_SHAPE_INVALID: a fund trade may contain only principal, consideration, fee and tax entries.')
            END;

            -- At most one aggregated fee entry and one aggregated tax entry.
            SELECT CASE
                WHEN (
                    SELECT COUNT(*)
                    FROM "TransactionEntry" AS entry
                    WHERE entry."TransactionId" = NEW."Id"
                      AND entry."EntryRoleCode" = 'FEE'
                ) > 1
                OR (
                    SELECT COUNT(*)
                    FROM "TransactionEntry" AS entry
                    WHERE entry."TransactionId" = NEW."Id"
                      AND entry."EntryRoleCode" = 'TAX'
                ) > 1
                THEN RAISE(
                    ABORT,
                    'FUND_TRADE_SHAPE_INVALID: a fund trade may contain at most one fee entry and one tax entry.')
            END;

            -- Principal and consideration signs follow the trade direction.
            SELECT CASE
                WHEN EXISTS (
                    SELECT 1
                    FROM "TransactionEntry" AS entry
                    WHERE entry."TransactionId" = NEW."Id"
                      AND entry."EntryRoleCode" = 'PRINCIPAL'
                      AND (
                        (NEW."TransactionTypeCode" = 'BUY'
                            AND entry."QuantityDeltaE8" <= 0)
                        OR (NEW."TransactionTypeCode" = 'SELL'
                            AND entry."QuantityDeltaE8" >= 0)
                      )
                )
                THEN RAISE(
                    ABORT,
                    'FUND_TRADE_SHAPE_INVALID: the fund principal sign does not match the trade direction.')
            END;

            SELECT CASE
                WHEN EXISTS (
                    SELECT 1
                    FROM "TransactionEntry" AS entry
                    WHERE entry."TransactionId" = NEW."Id"
                      AND entry."EntryRoleCode" = 'CONSIDERATION'
                      AND (
                        (NEW."TransactionTypeCode" = 'BUY'
                            AND entry."QuantityDeltaE8" >= 0)
                        OR (NEW."TransactionTypeCode" = 'SELL'
                            AND entry."QuantityDeltaE8" <= 0)
                      )
                )
                THEN RAISE(
                    ABORT,
                    'FUND_TRADE_SHAPE_INVALID: the cash consideration sign does not match the trade direction.')
            END;

            -- Fee and tax always reduce cash, in either direction.
            SELECT CASE
                WHEN EXISTS (
                    SELECT 1
                    FROM "TransactionEntry" AS entry
                    WHERE entry."TransactionId" = NEW."Id"
                      AND entry."EntryRoleCode" IN ('FEE', 'TAX')
                      AND entry."QuantityDeltaE8" >= 0
                )
                THEN RAISE(
                    ABORT,
                    'FUND_TRADE_SHAPE_INVALID: fee and tax entries must decrease cash.')
            END;

            -- The executed price is preserved on the principal entry only.
            SELECT CASE
                WHEN EXISTS (
                    SELECT 1
                    FROM "TransactionEntry" AS entry
                    WHERE entry."TransactionId" = NEW."Id"
                      AND entry."EntryRoleCode" = 'PRINCIPAL'
                      AND (
                        entry."UnitPriceE8" IS NULL
                        OR entry."UnitPriceE8" <= 0
                        OR entry."PriceCurrencyCode" IS NULL
                      )
                )
                THEN RAISE(
                    ABORT,
                    'FUND_TRADE_SHAPE_INVALID: a fund trade must preserve a positive executed unit price.')
            END;

            -- One portfolio across every entry.
            SELECT CASE
                WHEN (
                    SELECT COUNT(DISTINCT entry."PortfolioId")
                    FROM "TransactionEntry" AS entry
                    WHERE entry."TransactionId" = NEW."Id"
                ) <> 1
                THEN RAISE(
                    ABORT,
                    'FUND_TRADE_SHAPE_INVALID: a fund trade must use one portfolio.')
            END;

            -- Compatible, active, same-household references.
            SELECT CASE
                WHEN EXISTS (
                    SELECT 1
                    FROM "TransactionEntry" AS entry
                    JOIN "Asset" AS asset
                      ON asset."Id" = entry."AssetId"
                    JOIN "Account" AS account
                      ON account."Id" = entry."AccountId"
                    JOIN "Portfolio" AS portfolio
                      ON portfolio."Id" = entry."PortfolioId"
                    WHERE entry."TransactionId" = NEW."Id"
                      AND (
                        account."HouseholdId" <> NEW."HouseholdId"
                        OR portfolio."HouseholdId" <> NEW."HouseholdId"
                        OR account."IsActive" = 0
                        OR portfolio."StatusCode" <> 'ACTIVE'
                        OR asset."IsActive" = 0
                        OR asset."BaseCurrencyCode" IS NULL
                      )
                )
                THEN RAISE(
                    ABORT,
                    'FUND_TRADE_REFERENCE_INVALID: a fund trade requires active same-household references with a base currency.')
            END;

            -- The fund leg needs an investment or pension account.
            SELECT CASE
                WHEN EXISTS (
                    SELECT 1
                    FROM "TransactionEntry" AS entry
                    JOIN "Asset" AS asset
                      ON asset."Id" = entry."AssetId"
                    JOIN "Account" AS account
                      ON account."Id" = entry."AccountId"
                    WHERE entry."TransactionId" = NEW."Id"
                      AND entry."EntryRoleCode" = 'PRINCIPAL'
                      AND (
                        asset."AssetTypeCode" <> 'FUND'
                        OR asset."BaseUnitCode" <> 'FUND_UNIT'
                        OR asset."LotTrackingModeCode" NOT IN (
                            'OPTIONAL', 'REQUIRED')
                        OR account."AccountTypeCode" NOT IN (
                            'INVESTMENT', 'PENSION')
                      )
                )
                THEN RAISE(
                    ABORT,
                    'FUND_TRADE_REFERENCE_INVALID: a fund position requires a lot-tracked fund in an investment or pension account.')
            END;

            -- Every cash leg uses one compatible cash asset and account.
            SELECT CASE
                WHEN EXISTS (
                    SELECT 1
                    FROM "TransactionEntry" AS entry
                    JOIN "Asset" AS asset
                      ON asset."Id" = entry."AssetId"
                    JOIN "Account" AS account
                      ON account."Id" = entry."AccountId"
                    WHERE entry."TransactionId" = NEW."Id"
                      AND entry."EntryRoleCode" IN (
                        'CONSIDERATION', 'FEE', 'TAX')
                      AND (
                        asset."AssetTypeCode" NOT IN ('CASH', 'CURRENCY')
                        OR asset."BaseUnitCode" <> 'CURRENCY_UNIT'
                        OR asset."LotTrackingModeCode" <> 'NONE'
                        OR account."AccountTypeCode" NOT IN (
                            'CASH', 'INVESTMENT', 'PENSION')
                      )
                )
                THEN RAISE(
                    ABORT,
                    'FUND_TRADE_REFERENCE_INVALID: the cash legs require a non-lot-tracked cash or currency asset.')
            END;

            -- One currency across the fund, the cash legs and the price.
            SELECT CASE
                WHEN (
                    SELECT COUNT(DISTINCT asset."BaseCurrencyCode")
                    FROM "TransactionEntry" AS entry
                    JOIN "Asset" AS asset
                      ON asset."Id" = entry."AssetId"
                    WHERE entry."TransactionId" = NEW."Id"
                ) <> 1
                OR EXISTS (
                    SELECT 1
                    FROM "TransactionEntry" AS entry
                    JOIN "Asset" AS asset
                      ON asset."Id" = entry."AssetId"
                    WHERE entry."TransactionId" = NEW."Id"
                      AND entry."PriceCurrencyCode" IS NOT NULL
                      AND entry."PriceCurrencyCode"
                          <> asset."BaseCurrencyCode"
                )
                THEN RAISE(
                    ABORT,
                    'FUND_TRADE_CURRENCY_MISMATCH: a fund trade must use one currency.')
            END;

            -- Only the accepted fund cost vocabulary.
            SELECT CASE
                WHEN EXISTS (
                    SELECT 1
                    FROM "TransactionCostComponent" AS cost
                    WHERE cost."TransactionId" = NEW."Id"
                      AND (
                        cost."CostTypeCode" NOT IN (
                            'COMMISSION',
                            'BROKERAGE',
                            'WITHHOLDING_TAX',
                            'OTHER_TAX',
                            'OTHER')
                        OR cost."AmountMinor" <= 0
                      )
                )
                THEN RAISE(
                    ABORT,
                    'FUND_TRADE_COST_INVALID: a fund trade carries only positive amounts in the accepted cost vocabulary.')
            END;

            -- A purchase has no proceeds to withhold from.
            SELECT CASE
                WHEN NEW."TransactionTypeCode" = 'BUY'
                 AND EXISTS (
                    SELECT 1
                    FROM "TransactionCostComponent" AS cost
                    WHERE cost."TransactionId" = NEW."Id"
                      AND cost."TreatmentCode" = 'WITHHELD_FROM_PROCEEDS'
                )
                THEN RAISE(
                    ABORT,
                    'FUND_TRADE_COST_INVALID: a purchase cannot withhold cost from proceeds.')
            END;

            /*
             * The fee and tax entries must equal the additional-outflow
             * components exactly. This is the guard against counting a cost
             * that is already inside the consideration a second time.
             */
            SELECT CASE
                WHEN COALESCE((
                    SELECT SUM(-entry."QuantityDeltaE8")
                    FROM "TransactionEntry" AS entry
                    WHERE entry."TransactionId" = NEW."Id"
                      AND entry."EntryRoleCode" = 'FEE'
                ), 0) <> COALESCE((
                    SELECT SUM(
                        cost."AmountMinor"
                        * CAST(
                            POWER(10, 8 - currency."MinorUnitDigits")
                            AS INTEGER))
                    FROM "TransactionCostComponent" AS cost
                    JOIN "Currency" AS currency
                      ON currency."Code" = cost."CurrencyCode"
                    WHERE cost."TransactionId" = NEW."Id"
                      AND cost."TreatmentCode" = 'ADDITIONAL_CASH_OUTFLOW'
                      AND cost."CostTypeCode" IN (
                        'COMMISSION', 'BROKERAGE', 'OTHER')
                ), 0)
                THEN RAISE(
                    ABORT,
                    'FUND_TRADE_COST_ENTRY_MISMATCH: the fee entry must equal the additional-outflow fee components.')
            END;

            SELECT CASE
                WHEN COALESCE((
                    SELECT SUM(-entry."QuantityDeltaE8")
                    FROM "TransactionEntry" AS entry
                    WHERE entry."TransactionId" = NEW."Id"
                      AND entry."EntryRoleCode" = 'TAX'
                ), 0) <> COALESCE((
                    SELECT SUM(
                        cost."AmountMinor"
                        * CAST(
                            POWER(10, 8 - currency."MinorUnitDigits")
                            AS INTEGER))
                    FROM "TransactionCostComponent" AS cost
                    JOIN "Currency" AS currency
                      ON currency."Code" = cost."CurrencyCode"
                    WHERE cost."TransactionId" = NEW."Id"
                      AND cost."TreatmentCode" = 'ADDITIONAL_CASH_OUTFLOW'
                      AND cost."CostTypeCode" IN (
                        'WITHHOLDING_TAX', 'OTHER_TAX')
                ), 0)
                THEN RAISE(
                    ABORT,
                    'FUND_TRADE_COST_ENTRY_MISMATCH: the tax entry must equal the additional-outflow tax components.')
            END;

            -- Allocations reconcile exactly to the fund principal quantity.
            SELECT CASE
                WHEN COALESCE((
                    SELECT SUM(allocation."QuantityDeltaE8")
                    FROM "LotEntryAllocation" AS allocation
                    JOIN "TransactionEntry" AS entry
                      ON entry."Id" = allocation."TransactionEntryId"
                    WHERE entry."TransactionId" = NEW."Id"
                      AND entry."EntryRoleCode" = 'PRINCIPAL'
                ), 0) <> COALESCE((
                    SELECT SUM(entry."QuantityDeltaE8")
                    FROM "TransactionEntry" AS entry
                    WHERE entry."TransactionId" = NEW."Id"
                      AND entry."EntryRoleCode" = 'PRINCIPAL'
                ), 0)
                THEN RAISE(
                    ABORT,
                    'FUND_TRADE_ALLOCATION_MISMATCH: lot allocations must reconcile exactly to the fund principal quantity.')
            END;

            /*
             * A purchase opens exactly one lot, dated on the execution date,
             * whose known cost is the consideration plus any separately
             * settled outflow. Included costs are already inside the
             * consideration and must not be added again.
             */
            SELECT CASE
                WHEN NEW."TransactionTypeCode" = 'BUY'
                 AND (
                    SELECT COUNT(*)
                    FROM "AssetLot" AS lot
                    JOIN "TransactionEntry" AS entry
                      ON entry."Id" = lot."OpeningTransactionEntryId"
                    WHERE entry."TransactionId" = NEW."Id"
                 ) <> 1
                THEN RAISE(
                    ABORT,
                    'FUND_TRADE_LOT_INVALID: a fund purchase must open exactly one acquisition lot.')
            END;

            SELECT CASE
                WHEN NEW."TransactionTypeCode" = 'BUY'
                 AND EXISTS (
                    SELECT 1
                    FROM "AssetLot" AS lot
                    JOIN "TransactionEntry" AS entry
                      ON entry."Id" = lot."OpeningTransactionEntryId"
                    JOIN "Currency" AS currency
                      ON currency."Code" = lot."CostBasisCurrencyCode"
                    WHERE entry."TransactionId" = NEW."Id"
                      AND (
                        lot."CostBasisStatusCode" <> 'KNOWN'
                        OR lot."AcquiredOn" IS NOT NULL
                           AND lot."AcquiredOn" <> NEW."ExecutionDate"
                        OR lot."AcquiredOn" IS NULL
                        OR lot."OriginalCostBasisMinor" IS NULL
                        OR lot."OriginalCostBasisMinor" * CAST(
                            POWER(10, 8 - currency."MinorUnitDigits")
                            AS INTEGER)
                           <> (
                            SELECT COALESCE(SUM(-cash."QuantityDeltaE8"), 0)
                            FROM "TransactionEntry" AS cash
                            WHERE cash."TransactionId" = NEW."Id"
                              AND cash."EntryRoleCode" IN (
                                'CONSIDERATION', 'FEE', 'TAX')
                           )
                      )
                 )
                THEN RAISE(
                    ABORT,
                    'FUND_TRADE_LOT_INVALID: the acquisition lot must carry the known acquisition cash outflow on the execution date.')
            END;

            -- A sale consumes only lots of the same fund.
            SELECT CASE
                WHEN NEW."TransactionTypeCode" = 'SELL'
                 AND EXISTS (
                    SELECT 1
                    FROM "LotEntryAllocation" AS allocation
                    JOIN "AssetLot" AS lot
                      ON lot."Id" = allocation."AssetLotId"
                    JOIN "TransactionEntry" AS entry
                      ON entry."Id" = allocation."TransactionEntryId"
                    WHERE entry."TransactionId" = NEW."Id"
                      AND lot."AssetId" <> entry."AssetId"
                 )
                THEN RAISE(
                    ABORT,
                    'FUND_TRADE_LOT_INVALID: a sale can only consume lots of the fund being sold.')
            END;

            /*
             * Scope-correct availability. A sale may only consume quantity the
             * selected portfolio and account actually hold, so a stale or
             * direct-SQL writer cannot reach another account's custody.
             */
            SELECT CASE
                WHEN NEW."TransactionTypeCode" = 'SELL'
                 AND EXISTS (
                    SELECT 1
                    FROM "LotEntryAllocation" AS allocation
                    JOIN "TransactionEntry" AS saleEntry
                      ON saleEntry."Id" = allocation."TransactionEntryId"
                    WHERE saleEntry."TransactionId" = NEW."Id"
                      AND (
                        SELECT COALESCE(SUM(scoped."QuantityDeltaE8"), 0)
                        FROM "LotEntryAllocation" AS scoped
                        JOIN "TransactionEntry" AS scopedEntry
                          ON scopedEntry."Id" = scoped."TransactionEntryId"
                        JOIN "LedgerTransaction" AS scopedTx
                          ON scopedTx."Id" = scopedEntry."TransactionId"
                        WHERE scoped."AssetLotId" = allocation."AssetLotId"
                          AND scopedEntry."PortfolioId"
                              = saleEntry."PortfolioId"
                          AND scopedEntry."AccountId"
                              = saleEntry."AccountId"
                          AND (
                            scopedTx."StatusCode" = 'POSTED'
                            OR scopedTx."Id" = NEW."Id"
                          )
                      ) < 0
                 )
                THEN RAISE(
                    ABORT,
                    'FUND_TRADE_SCOPE_OVERDRAWN: a sale cannot consume more of a lot than the selected portfolio and account hold.')
            END;
        END;
        """;
}
