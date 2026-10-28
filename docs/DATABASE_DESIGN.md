# WealthLedger Database Design

Status: Canonical implemented persistence design through M009

Target: EF Core with SQLite

Migrations:
- `20260824074930_001_CoreLedger`
- `20260827072019_002_CommandReceipt`
- `20260831113310_003_ReversalDependencySemantics`
- `20260902112549_004_LedgerNavigationQueries`
- `20260903075104_005_WorkspaceIdentity`
- `20260910101810_006_OpeningBalanceCutoverGuards`
- `20260913054039_007_FundTradeLifecycleGuards`
- `20260915082550_008_PhysicalGoldLifecycle`
- `20261028065838_009_EvidencePersistence`
- `20261028082512_010_ReconciliationPersistence`

Last distilled: 2026-09-16

## Design goals

- Normalize durable ledger facts.
- Make invalid states difficult at both application and database layers.
- Preserve posted history.
- Store exact financial representations.
- Keep positions and analytics derivable.
- Support funds, equities, cash/currency, and physical gold without separate transaction systems.
- Leave later asset detail, market data, goals, policies, reconciliation, and analysis regions additive.

## SQLite representation standards

| Concept | SQLite representation |
|---|---|
| Guid | TEXT primary/foreign key in canonical UUID form |
| Boolean | INTEGER constrained to 0 or 1 |
| Money | INTEGER signed minor units plus currency code |
| Quantity/Delta | INTEGER signed raw E8 |
| Unit price | INTEGER non-negative raw E8 plus price currency |
| Basis points | INTEGER |
| Fineness | INTEGER parts per million |
| Business date | TEXT ISO YYYY-MM-DD |
| Audit timestamp | TEXT normalized UTC ISO-8601 |
| Enum-like value | explicit stable uppercase TEXT code |

Do not persist CLR enum ordinals.

SQLite INTEGER is signed 64-bit. Calculations that can overflow, require scaling, or require rounding happen in checked C# logic rather than ad hoc SQL multiplication.

## Core relationship

    LedgerTransaction
        ├── TransactionEntry ──> LotEntryAllocation <── AssetLot
        ├── TransactionCostComponent
        ├── CashFlowDetail
        └── PhysicalGoldTradeDetail

    AssetLot
        └── PhysicalGoldLotDetail

    LotEntryAllocation
        └── PhysicalGoldLotAllocationDetail

Master references:

    Household
        ├── HouseholdMember
        ├── Portfolio
        ├── Account ──> Institution
        └── LedgerTransaction

    Asset
    Currency

## Core tables

The column list below is the canonical logical design. Exact EF-generated names should match it unless the real repository has already accepted a tested equivalent.

### Currency

| Column | Type | Rules |
|---|---|---|
| Code | TEXT | primary key; three uppercase ASCII letters |
| Name | TEXT | required |
| MinorUnitDigits | INTEGER | between 0 and 8 |

Currency reference data supplies display/conversion metadata. Domain Money remains minor-unit based.

### Household

| Column | Type | Rules |
|---|---|---|
| Id | TEXT | primary key |
| Name | TEXT | required |
| BaseCurrencyCode | TEXT | required FK to Currency, restrict delete |
| CreatedAtUtc | TEXT | required UTC timestamp |

### HouseholdMember

| Column | Type | Rules |
|---|---|---|
| Id | TEXT | primary key |
| HouseholdId | TEXT | required FK to Household, restrict delete |
| DisplayName | TEXT | required |
| IsActive | INTEGER | required, 0 or 1 |
| CreatedAtUtc | TEXT | required UTC timestamp |

Store only minimal display attribution. This is not a general personal-profile table.

### Institution

| Column | Type | Rules |
|---|---|---|
| Id | TEXT | primary key |
| Code | TEXT | required, unique |
| Name | TEXT | required |
| InstitutionTypeCode | TEXT | required stable code |
| IsActive | INTEGER | required, 0 or 1 |

Institution type codes:

    BANK
    BROKER
    ASSET_MANAGER
    JEWELER
    PENSION
    OTHER

### Portfolio

| Column | Type | Rules |
|---|---|---|
| Id | TEXT | primary key |
| HouseholdId | TEXT | required FK, restrict delete |
| Code | TEXT | required |
| Name | TEXT | required |
| StatusCode | TEXT | ACTIVE, CLOSED, or ARCHIVED |
| CreatedAtUtc | TEXT | required UTC timestamp |
| ClosedAtUtc | TEXT | nullable UTC timestamp |

Unique on HouseholdId plus Code.

### Account

| Column | Type | Rules |
|---|---|---|
| Id | TEXT | primary key |
| HouseholdId | TEXT | required FK, restrict delete |
| InstitutionId | TEXT | nullable FK, restrict delete |
| Code | TEXT | required |
| Name | TEXT | required |
| AccountTypeCode | TEXT | required stable code |
| IsActive | INTEGER | required, 0 or 1 |
| OpenedOn | TEXT | nullable business date |
| ClosedOn | TEXT | nullable business date |

Unique on HouseholdId plus Code.

Account type codes:

    CASH
    INVESTMENT
    PHYSICAL_VAULT
    PENSION
    PROPERTY_REGISTRY
    OTHER

### Asset

| Column | Type | Rules |
|---|---|---|
| Id | TEXT | primary key |
| Code | TEXT | required, unique |
| Name | TEXT | required |
| AssetTypeCode | TEXT | required stable code |
| BaseUnitCode | TEXT | required stable code |
| BaseCurrencyCode | TEXT | nullable FK to Currency, restrict delete |
| LotTrackingModeCode | TEXT | NONE, OPTIONAL, or REQUIRED |
| IsActive | INTEGER | required, 0 or 1 |
| CreatedAtUtc | TEXT | required UTC timestamp |

Asset type and unit codes map explicitly to Domain vocabulary.

### LedgerTransaction

| Column | Type | Rules |
|---|---|---|
| Id | TEXT | primary key |
| HouseholdId | TEXT | required FK, restrict delete |
| TransactionTypeCode | TEXT | required stable code |
| StatusCode | TEXT | DRAFT, ORDERED, POSTED, CANCELLED |
| OrderDate | TEXT | nullable business date |
| ExecutionDate | TEXT | nullable business date |
| SettlementDate | TEXT | nullable business date |
| ExternalReference | TEXT | nullable |
| Note | TEXT | nullable |
| ReversalOfTransactionId | TEXT | nullable self-FK, restrict delete |
| CreatedAtUtc | TEXT | required UTC timestamp |
| PostedAtUtc | TEXT | nullable UTC timestamp |

Checks:

- reversal target differs from the transaction itself;
- Posted requires PostedAtUtc;
- OrderDate is not after ExecutionDate;
- ExecutionDate is not after SettlementDate.

A partial unique index on ReversalOfTransactionId where non-null ensures one reversal per original.

There is no REVERSED status code.

### TransactionEntry

| Column | Type | Rules |
|---|---|---|
| Id | TEXT | primary key |
| TransactionId | TEXT | required FK to LedgerTransaction |
| EntrySequence | INTEGER | required, non-negative |
| PortfolioId | TEXT | required FK, restrict delete |
| AccountId | TEXT | required FK, restrict delete |
| AssetId | TEXT | required FK, restrict delete |
| QuantityDeltaE8 | INTEGER | required, non-zero |
| EntryRoleCode | TEXT | required stable code |
| UnitPriceE8 | INTEGER | nullable, non-negative |
| PriceCurrencyCode | TEXT | nullable FK to Currency, restrict delete |
| CreatedAtUtc | TEXT | required UTC timestamp |

Unique on TransactionId plus EntrySequence.

UnitPriceE8 and PriceCurrencyCode are either both null or both non-null.

The FK from entry to transaction may cascade only for deletion of a non-posted aggregate. Posted-history triggers must prevent cascade from deleting effective history.

### CashFlowDetail

| Column | Type | Rules |
|---|---|---|
| TransactionId | TEXT | primary key and FK to LedgerTransaction |
| CashFlowCategoryCode | TEXT | required stable code |
| HouseholdMemberId | TEXT | nullable FK, restrict delete |

The Application/Domain rule currently permits this detail only for Contribution. SQLite CHECK cannot validate a parent row's type, so enforce it in code and optionally a tested trigger.

### TransactionCostComponent

| Column | Type | Rules |
|---|---|---|
| Id | TEXT | primary key |
| TransactionId | TEXT | required FK to LedgerTransaction |
| CostTypeCode | TEXT | required stable code |
| TreatmentCode | TEXT | required stable code |
| AmountMinor | INTEGER | required, non-negative |
| CurrencyCode | TEXT | required FK to Currency, restrict delete |
| Note | TEXT | nullable |

The component is explanatory and must not double count a cash entry.

SPREAD is not a CostTypeCode.

### AssetLot

| Column | Type | Rules |
|---|---|---|
| Id | TEXT | primary key |
| AssetId | TEXT | required FK to Asset, restrict delete |
| OpeningTransactionEntryId | TEXT | required FK to TransactionEntry, restrict delete |
| AcquiredOn | TEXT | nullable business date |
| OriginalCostBasisMinor | INTEGER | nullable, non-negative |
| CostBasisCurrencyCode | TEXT | nullable FK to Currency, restrict delete |
| CostBasisStatusCode | TEXT | KNOWN, UNKNOWN, NOT_APPLICABLE |
| CreatedAtUtc | TEXT | required UTC timestamp |

Cost-basis combination check:

- KNOWN requires non-null amount and currency;
- UNKNOWN and NOT_APPLICABLE require null amount and currency.

OpeningTransactionEntryId is not unique because one acquisition entry can create multiple lots.

AssetLot has no AccountId, PortfolioId, OriginalQuantity, RemainingQuantity, or ClosedAt.

### LotEntryAllocation

| Column | Type | Rules |
|---|---|---|
| Id | TEXT | primary key |
| AssetLotId | TEXT | required FK to AssetLot, restrict delete |
| TransactionEntryId | TEXT | required FK to TransactionEntry, restrict delete |
| QuantityDeltaE8 | INTEGER | required, non-zero |
| CreatedAtUtc | TEXT | required UTC timestamp |

Unique on AssetLotId plus TransactionEntryId.

The database must reject a lot/entry asset mismatch and an allocation sign that differs from its entry. The Application layer additionally ensures exact reconciliation across all lots and prevents a negative resulting lot balance.

### PhysicalGoldLotDetail

| Column | Type | Rules |
|---|---|---|
| AssetLotId | TEXT | primary key and FK to AssetLot |
| ActualFinenessPpm | INTEGER | greater than 0 and at most 1,000,000 |
| PieceCount | INTEGER | greater than 0 |
| Hallmark | TEXT | nullable |
| CertificateReference | TEXT | nullable |
| Note | TEXT | nullable |

GrossWeight and FineGoldWeight are deliberately absent.

`PieceCount` is immutable acquisition evidence. Current global and
custody-scoped piece counts are derived from signed allocation details rather
than updating this row.

### PhysicalGoldLotAllocationDetail

| Column | Type | Rules |
|---|---|---|
| LotEntryAllocationId | TEXT | primary key and restrictive FK to LotEntryAllocation |
| PieceDelta | INTEGER | required, non-zero signed whole-piece movement |

Every allocation whose lot asset is PhysicalGold has exactly one row; every
other allocation has none. `PieceDelta` and `QuantityDeltaE8` have matching
signs but independent magnitudes. The database never infers average piece
weight or derives either fact from the other.

### PhysicalGoldTradeDetail

| Column | Type | Rules |
|---|---|---|
| LedgerTransactionId | TEXT | primary key and restrictive FK to LedgerTransaction |
| CounterpartyInstitutionId | TEXT | nullable restrictive FK to global Institution |

The row exists only for supported PhysicalGold Buy and Sell transactions. Its
optional counterparty records seller or buyer evidence and is separate from
the physical-vault custody account. Transfer and reversal do not fabricate a
trade-detail copy.

## Stable code sets

At minimum, explicit converters/checks are required for:

- AssetTypeCode;
- BaseUnitCode;
- LotTrackingModeCode;
- InstitutionTypeCode;
- Portfolio StatusCode;
- AccountTypeCode;
- TransactionTypeCode;
- Transaction StatusCode;
- EntryRoleCode;
- CashFlowCategoryCode;
- CostTypeCode;
- TreatmentCode;
- CostBasisStatusCode;
- Evidence SourceKindCode;
- Evidence attachment MediaTypeCode;
- Reconciliation run MethodCode;
- Reconciliation item ComparisonStatusCode;
- Reconciliation item ResolutionCode.

Adding or renaming a code is a migration/API compatibility decision. Never depend on enum numeric values or default ToString behavior without an explicit, tested mapping.

## Database-enforced protections

The first migration should include and integration-test:

1. foreign keys enabled for every connection;
2. checks listed above;
3. unique reversal index;
4. prevention of update/delete of a Posted LedgerTransaction;
5. prevention of insert/update/delete of its entry, cost, and cash-flow facts after posting;
6. household consistency between transaction, account, and portfolio;
7. lot/entry asset equality;
8. allocation/entry sign equality;
9. uniqueness of lot-entry allocation;
10. restrictive deletion of lots and allocations that participate in history;
11. exactly one signed piece detail for every PhysicalGold allocation and none
    for other asset families; and
12. non-negative global and custody-scoped physical-gold gross quantity and
    piece count.

SQLite triggers that read other tables must have both insert and relevant update variants. Trigger behavior and error messages are part of integration tests.

The exact save order must allow a complete valid aggregate and its lot effects to be inserted atomically before the transaction is finalized as Posted.

Some invariants are better enforced in Application code and tested against the database:

- semantic shape of every transaction type;
- exact sum of all allocations for one entry, with the M007 posting trigger
  independently enforcing complete opening allocation for Optional assets;
- lot balance never becoming negative across history;
- acquisition reversal eligibility is evaluated in Application and independently
  re-enforced by the SQLite posting trigger;
- FIFO selection;
- arithmetic/rounding.

### M003 reversal-dependency semantics

Migration `20260831113310_003_ReversalDependencySemantics` is a behavior-only
migration. It does not add an authoritative financial table and does not modify
historical migration `20260824074930_001_CoreLedger`.

The migration drops and recreates
`TR_LedgerTransaction_ValidateBeforePosting` with the complete existing
validation body intact except for the acquisition-reversal dependency
predicate.

When a Posted reversal targets an acquisition transaction, a downstream lot
allocation remains a blocker when all of the following are true:

- the downstream transaction is Posted;
- the downstream transaction is not itself a Reversal;
- it is not the acquisition being reversed; and
- no Posted Reversal exists whose `ReversalOfTransactionId` equals that
  downstream transaction's identity.

Therefore a downstream original paired with its own valid Posted reversal is
neutralized for acquisition-reversal eligibility. Draft or Cancelled reversals
do not neutralize it. A reversal transaction is not itself treated as a new
blocker, and unrelated transactions whose quantities happen to net the lot do
not substitute for explicit reversal lineage.

The Application candidate query implements the same predicate. SQLite remains
the final authority if a dependency is introduced after Application eligibility
has been evaluated. The reversal graph, mirrored allocations, command receipt,
and final Posted transition share one explicit database transaction, so a
trigger rejection rolls back the attempted reversal completely.

The migration `Down` path restores the previous stricter dependency predicate.

### M005 recent-ledger navigation index

Migration `20260902112549_004_LedgerNavigationQueries` adds only the index:

```text
IX_LedgerTransaction_Household_Status_Posted_Id
    (HouseholdId ASC, StatusCode ASC, PostedAtUtc DESC, Id DESC)
```

It supports the bounded household feed of Posted transactions ordered by
posting time and immutable transaction identity, both descending. The existing
household/status/execution-date index remains for business-date queries.

`EXPLAIN QUERY PLAN` is exercised against the exact command emitted by the
production EF read store. On the M003 schema, SQLite selects
`IX_LedgerTransaction_Household_Status_Date` and reports
`USE TEMP B-TREE FOR ORDER BY`. After migration 004, it selects the M005 index
and no temporary sort. The migration `Down` removes only the M005 index; real-
SQLite up/down tests preserve all seeded rows.

### M006 workspace identity

Migration `20260903075104_005_WorkspaceIdentity` creates one non-financial,
single-row `WorkspaceIdentity` table outside the EF model. SQLite generates a
random opaque UUID and UTC creation timestamp when the migration is applied.
No ledger or master row references it.

Backup creation records the identity and package verification compares the
manifest value with the snapshot value. A verified package counts as protection
for a live database only when the two identities match. The identity survives
backup, isolated restore, active replacement, and a live-path move; a package
from before migration 005 remains verifiable and restorable but has unknown
lineage and does not satisfy Ready protection.

### M007 opening-balance cutover guards

Migration `20260910101810_006_OpeningBalanceCutoverGuards` first validates any
existing Posted opening history, then adds:

```text
IX_TransactionEntry_OpeningBalanceScope
    (PortfolioId ASC, AccountId ASC, AssetId ASC, TransactionId ASC)

TR_LedgerTransaction_ValidateOpeningBalanceBeforePosting
```

The trigger runs on the Draft-to-Posted transition for `OPENING_BALANCE` and
rejects an invalid graph before it can become effective. It requires one
positive Principal entry, an execution/as-of date, a non-empty note, and no
order date, settlement date, unit price, cash-flow detail, or transaction cost.
It validates active and household-consistent references plus the accepted
asset/account combinations.

Cash and Currency must use `CURRENCY_UNIT` with `NONE` lot tracking and have no
lot or allocation; Cash uses the household base currency while Currency must
use a different currency. Fund and Equity accept `OPTIONAL` or `REQUIRED`, and
PhysicalGold accepts `REQUIRED`; all three must create opening-owned lots whose
positive allocations reconcile exactly to the entry. Opening lots use only
Known or Unknown cost, acquisition dates cannot follow the as-of date, and
every physical-gold lot requires its detail row.

The same trigger is the concurrency authority for two semantic rules: one
effective opening per exact household/portfolio/account/asset scope, and no
opening over other effective Posted history in that scope. A Posted reversal
neutralizes its original for those predicates. Stable SQLite abort codes allow
Infrastructure to distinguish an existing opening, prior effective history,
and a malformed opening without exposing SQL or stored values.

The transaction, entry, all new lots/details/allocations, command receipt, and
final Posted transition share one explicit SQLite transaction. Direct-SQL,
independent-connection race, rollback, up/down, and older-schema migration tests
exercise the trigger and index. The `Down` path removes only the M007 trigger
and scope index; it does not erase Posted facts.

## Indexes

Beyond PK/unique indexes, evaluate query-driven indexes for:

- LedgerTransaction by HouseholdId, StatusCode, ExecutionDate;
- LedgerTransaction by HouseholdId, StatusCode, PostedAtUtc descending, and Id
  descending for verified M005 recent navigation;
- TransactionEntry by PortfolioId, AccountId, AssetId, and TransactionId for
  M007 semantic opening eligibility;
- TransactionEntry by TransactionId;
- TransactionEntry by PortfolioId, AssetId;
- TransactionEntry by AccountId, AssetId;
- AssetLot by AssetId, AcquiredOn;
- LotEntryAllocation by AssetLotId;
- LotEntryAllocation by TransactionEntryId;
- EvidenceRecord by HouseholdId, EffectiveDate descending, CreatedAtUtc
  descending, and Id descending for the M010 evidence list and its cursor;
- EvidenceTransactionLink by LedgerTransactionId.

Add indexes in response to actual first-slice queries and query plans. Avoid speculative indexing of deferred tables.

## Authoritative tables that must not exist

Do not persist these as mutable truth:

- CurrentPosition;
- CurrentBalance;
- RemainingLotQuantity;
- CurrentPortfolioValue;
- AveragePurchasePrice;
- CurrentProfit;
- CurrentAllocationPercentage.

Views or rebuildable projections may use similar names only when their non-authoritative nature is unmistakable and rebuilding from source facts is tested.

M010 position and lot inventory is a query, not stored state. At read time it
sums effective Posted `TransactionEntry` deltas, signed `LotEntryAllocation`
quantities and `PhysicalGoldLotAllocationDetail` piece movement, optionally
bounded by `ExecutionDate <= asOf`. A lot is immutable lineage, so its original
quantity comes from its opening allocation whatever the as-of date; the bound
applies to allocations. Fine weight is derived from gross quantity and the
lot's immutable fineness.

The posting guards keep complete history non-negative, so a negative current
quantity is a persisted-shape fault and fails closed. They do not order
history by `ExecutionDate`: a disposal or transfer may be dated before the
acquisition or custody it consumed. An as-of reconstruction can therefore be
negative, and inventory reports it signed rather than hiding or rejecting it.

Inventory adds no table, column, index or migration. Real-SQLite query-plan
tests show that its position, lot and allocation queries, with and without an
as-of date and with account and asset filters, use only indexed searches on
existing indexes: no full table scan and no automatic index.

## Deferred database regions

Not part of 001_CoreLedger:

- market quotes and historical reference data;
- asset-family detail tables beyond the implemented physical-gold
  lot/allocation/trade details;
- goals and allocation policies;
- import staging (reconciliation runs arrived in migration 010);
- analysis and agent-decision history;
- materialized read models;
- UI preferences.

Their eventual schemas must reference the ledger rather than duplicating it and must be introduced by separate migrations and ADRs when cross-cutting choices arise.

## Minimum migration integration scenarios

- create master data, contribution, fund purchase, lot, and derive the position after a database round trip;
- transfer a lot-tracked asset between accounts without changing global lot quantity or cost basis;
- sell across two lots using FIFO and derive remaining quantities;
- record cash and foreign-currency openings without lots or acquisition cost;
- record fully reconciled fund/equity openings with mixed Known and Unknown
  cost and optional acquisition dates;
- store physical-gold opening lots with gross weight, fineness and piece count,
  then derive exact fine-gold quantity;
- reject an opening over effective history and a second effective opening,
  including independent-connection races and direct SQL;
- permit a corrected opening only after the original receives a Posted
  reversal;
- reverse a posted purchase and prove original plus reversal nets to zero;
- reject a second reversal;
- reject mutation/deletion of posted facts;
- reject allocation asset/sign mismatches;
- reject cross-household entries;
- round-trip the maximum supported fixed-point values and detect overflow in calculations.
- reject acquisition reversal while an outstanding Posted downstream lot
  transaction depends on its acquisition lot;
- permit acquisition reversal after that exact downstream transaction receives
  its own Posted reversal;
- reject unrelated quantity netting as a substitute for reversal lineage;
- reject and atomically roll back a reversal when a new dependency appears
  after Application eligibility evaluation.
- post a physical-gold purchase, explicitly selected-lot partial sale, custody
  transfer, and exact reversals, then reconstruct gross, pieces, fine weight,
  cash, cost, and custody after restart;
- reject direct-SQL physical-gold graphs with missing/forged piece detail,
  incompatible accounts or currency, negative scoped availability, or unequal
  transfer movement;
- migrate provable M007 opening and reversal history, reject unprovable piece
  history before mutation, and pass 007 -> 008 -> 007 -> 008;
- arbitrate concurrent physical-gold sale and transfer races from independent
  SQLite connections without over-consuming gross quantity or pieces.

## Fund-trade posting guards (migration 007)

At the migration-007 head, `007_FundTradeLifecycleGuards` adds one trigger,
`TR_LedgerTransaction_ValidateFundTradeBeforePosting`, on the draft-to-posted
transition of each Buy or Sell. Its accepted graph is the Fund-trade graph, so
other principal asset families fail closed. It governs new postings only and
never reinterprets history posted under M001-M007.

It enforces the fund-trade entry shape and signs, exactly one principal and
one consideration entry, at most one aggregated fee and one aggregated tax
entry, one portfolio, active same-household references, an investment or
pension account for the fund leg, one currency across the trade, the accepted
cost vocabulary, exact agreement between the fee and tax entries and the
additional-outflow components, allocation reconciliation, the purchase-lot
cost equation, and scope-correct sale availability.

Two candidate objects were measured and dropped rather than shipped. An index
on `LotEntryAllocation("AssetLotId")` is unnecessary because the query plan
shows the existing `UX_LotEntryAllocation_Lot_Entry` already makes the per-lot
sum an index search. A non-negative lot-balance trigger is unnecessary because
M001's `TR_LotEntryAllocation_ValidateInsert` already enforces it and fires
first.

The migration adds no realized-cost, remaining-quantity, current-position,
valuation or market-data table. Down removes only the trigger it created.

## Physical-gold lifecycle guards (migration 008)

`008_PhysicalGoldLifecycle` preflights existing PhysicalGold allocations before
creating either new table or replacing posting behavior. It backfills an M007
opening allocation from that lot's immutable positive `PieceCount`, and an
exact reversal only from the inverse opening relationship. Any other existing
allocation history without independently provable pieces aborts the migration;
the migration never divides gross weight by piece count or infers average piece
weight.

The migration replaces migration 007's all-Buy/Sell Fund-only behavior with
explicit principal-asset dispatch:

- Fund Buy and Sell preserve every corrected M008 shape, cost, cash, currency,
  account, allocation, and scoped-availability rule;
- PhysicalGold Buy and Sell use the M009 purchase or selected-sale graph;
- unsupported Buy/Sell principal asset families fail closed; and
- PhysicalGold Transfer has its own same-lot, equal-and-opposite gross and
  piece movement rules without consideration, new lot, or acquisition cost.

Companion triggers require piece-detail presence/absence and matching signs,
protect posted piece and counterparty evidence from mutation, reconcile exact
purchase/sale/transfer movement, enforce active same-household account and
currency compatibility, and prevent negative household-wide or
portfolio/account-scoped gross quantity and pieces. Purchase creates one
complete homogeneous Known-cost lot. Sale consumes only its persisted selected
allocations. Optional seller/buyer is a global Institution and is never
interpreted as custody or Household ownership.

Reversal continues to mirror entries and lot allocations through the existing
immutable reversal graph; every inverse PhysicalGold allocation carries the
exact opposite `PieceDelta`. Original cost rows and trade detail remain on the
original transaction, so reversal creates neither copied costs nor a
replacement link.

The migration adds no position, remaining-quantity, realized-cost, valuation,
or market-data authority. Its Down path removes only M009 tables and triggers,
then restores the exact corrected migration-007 Fund guard. Real-SQLite tests
cover fresh Up, M007 backfill, fail-closed preflight, direct-SQL refusal, Fund
regression, concurrent races, Down/Up, 007 -> 008 -> 007 -> 008, foreign-key
integrity, and EF model agreement.

## Evidence persistence (migration 009)

`009_EvidencePersistence` adds M010 evidence as adjunct audit data. Evidence
references ledger facts but is never one: no evidence write inserts, updates or
deletes a ledger row, and no evidence value enters ledger arithmetic.

| Table | Purpose |
|---|---|
| `EvidenceRecord` | One household-scoped source: `SourceKindCode`, business `EffectiveDate`, optional global `InstitutionId` and household `AccountId`, `Label` (1-256), optional `Reference` (1-256) and `Note` (1-2000), optional `SupersedesEvidenceId`, `CreatedAtUtc`. |
| `EvidenceAttachment` | At most one retained file per record, keyed by `EvidenceId`: sanitized `FileName` (1-255), `MediaTypeCode`, `ByteLength`, lowercase hex `Sha256`, `Content` BLOB, `CreatedAtUtc`. |
| `EvidenceTransactionLink` | One row per evidence/transaction pair, keyed by both, with `LinkedAtUtc`. |
| `EvidenceCommandReceipt` | ADR-006 retry receipts for evidence commands, shaped like `CommandReceipt` but resulting in `ResultEvidenceId`. |

`CommandReceipt` is not reused because its `ResultTransactionId` is a required
ledger reference. Evidence receipts therefore live in their own table and leave
the verified ledger receipt table unchanged.

Stable codes: `SourceKindCode` is `STATEMENT`, `RECEIPT`, `PHYSICAL_COUNT`,
`CONTRACT_OR_CERTIFICATE` or `OTHER`; `MediaTypeCode` is `PDF`, `PNG`, `JPEG`,
`PLAIN_TEXT` or `CSV`.

Retained bytes stay inside the SQLite file, so the M004 `.wlbackup` snapshot
captures evidence and ledger together, with no sidecar directory.

Database protections:

- every foreign key is restrictive, including the self-reference that forms a
  supersession chain;
- `CK_EvidenceAttachment_Content` requires a BLOB of 1 to 10,485,760 bytes whose
  length equals `ByteLength`, and `CK_EvidenceAttachment_Sha256` requires 64
  lowercase hex characters;
- `UX_EvidenceRecord_Supersedes` lets a record be superseded at most once, and a
  check forbids self-supersession;
- `TR_EvidenceRecord_ValidateInsert` keeps the account inside the record's
  household and, when both are named, inside the named institution; it also
  keeps the superseded record in the same household;
- `TR_EvidenceAttachment_ValidateInsert` requires an attachment to carry its
  record's `CreatedAtUtc`. SQLite cannot see transaction boundaries, so direct
  SQL that copies the timestamp can still attach a file to an existing record;
  the Application writes an attachment only in its record's own commit;
- `TR_EvidenceTransactionLink_ValidateInsert` admits only Posted transactions of
  the record's household and refuses new links on a superseded record;
- `TR_EvidenceCommandReceipt_ValidateInsert` requires a receipt's result record
  to belong to the receipt's household; and
- update and delete triggers on all four evidence tables, receipts included,
  raise `WL_M010_EVIDENCE_IMMUTABLE`, so corrections use supersession, a retry
  key keeps resolving to the same record, and there is no delete path.

Trust boundaries the database cannot carry: the Application computes the
SHA-256 from the retained bytes and accepts a file only when its extension and
content signature match the declared type. Before returning bytes for download
it checks length, digest, declared type and sanitized name again, so a row
written outside the Application still fails closed.

A same-key retry that loses a race returns the winner's receipt even when its
own record collides first, for example on `UX_EvidenceRecord_Supersedes`.

The household list reads through
`IX_EvidenceRecord_Household_Effective_Created_Id` in key order without a
temporary sort. List and detail projections never select `Content`; only the
explicit attachment read does.

The migration creates no evidence rows for existing history. Down drops the
twelve triggers and four tables and restores the exact migration-008 schema.
Real-SQLite tests cover byte-for-byte round trips, replay and lost-race
retries that retain one copy, every direct-SQL guard, the query plans, backup,
verify and stage readback, and 008 -> 009 -> 008 -> 009.

## Reconciliation persistence (migration 010)

`010_ReconciliationPersistence` adds M010 evidence observations and
deterministic reconciliation as audit data beside the ledger. No observation,
run, item or resolution write inserts, updates or deletes a ledger row, and
reconciliation never posts a transaction.

| Table | Purpose |
|---|---|
| `EvidenceObservation` | One observed quantity per exact `PortfolioId`/`AccountId`/`AssetId` scope of an evidence record: signed `ObservedQuantityE8`, non-negative `ToleranceE8`, optional `ObservedPieceCount` (physical gold only), `RecordedAtUtc`. |
| `ReconciliationRun` | One comparison of an evidence record's observations: `HouseholdId`, `EvidenceId`, the evidence `EvidenceEffectiveDate`, `RunAtUtc`, `MethodCode`, `MethodVersion`, `ItemCount`, and the retry `IdempotencyKey`. |
| `ReconciliationItem` | An immutable snapshot per observation: `Sequence`, `ObservationId`, the scope, `ObservedQuantityE8`, derived `LedgerQuantityE8`, `DeltaE8`, `ToleranceE8`, piece counts and `PieceDelta` when pieces were observed, and `ComparisonStatusCode`. |
| `ReconciliationItemResolution` | At most one resolution per out-of-tolerance item, keyed by `ItemId`: `ResolutionCode`, then either `CorrectiveTransactionId` or `Explanation` (1-2000), and `ResolvedAtUtc`. An item without a row is Open. |

Stable codes: `MethodCode` is `EXACT_SCOPE_QUANTITY_AS_OF` (version 1);
`ComparisonStatusCode` is `MATCHED` or `OUT_OF_TOLERANCE`; `ResolutionCode` is
`CORRECTED_BY_TRANSACTION` or `DOCUMENTED_EXCEPTION`.

The ledger quantity is derived, never stored as a balance: it is the exact
scope's effective Posted position on the evidence date, and for physical gold
the pieces are the scope's lot custody on that date. A stored item is an audit
snapshot of one comparison. Reading a run derives the ledger side again and
reports the run as stale when it no longer reproduces the snapshot; the rows
are never rewritten. Staleness is therefore a query result, not a column.

Database protections:

- every foreign key is restrictive, including those to masters and to the
  corrective ledger transaction;
- `UX_EvidenceObservation_Evidence_Scope` admits one observation per scope per
  record, and checks keep the tolerance and piece count non-negative;
- `TR_EvidenceObservation_ValidateInsert` keeps the portfolio and account in
  the record's household and inside its account and institution context,
  admits a piece count only for a `PHYSICAL_GOLD` asset, caps a record at 100
  observations, and refuses observations on a superseded record;
- `TR_ReconciliationRun_ValidateInsert` requires the run's household and
  evidence date to equal its evidence record's and refuses a run on a
  superseded record; `UX_ReconciliationRun_Household_IdempotencyKey` makes the
  retry key unique per household;
- `TR_ReconciliationItem_ValidateInsert` requires an item to repeat its
  observation's scope, observed quantity, tolerance and piece count, to belong
  to the run's evidence, and to fit the run's recorded `ItemCount`, so items
  cannot be added to a finished run; `UX_ReconciliationItem_Run_Sequence` and
  `UX_ReconciliationItem_Run_Observation` keep each run's items unique;
- `CK_ReconciliationItem_Delta` requires `DeltaE8 = ObservedQuantityE8 -
  LedgerQuantityE8`, `CK_ReconciliationItem_Pieces` keeps the piece columns all
  present or all absent with `PieceDelta` exact, and
  `CK_ReconciliationItem_ComparisonStatus` requires `MATCHED` exactly when the
  delta lies within the tolerance and the piece delta is zero;
- `CK_ReconciliationItemResolution_Shape` pairs `CORRECTED_BY_TRANSACTION`
  with a transaction only and `DOCUMENTED_EXCEPTION` with an explanation only,
  and `TR_ReconciliationItemResolution_ValidateInsert` admits a resolution only
  for an `OUT_OF_TOLERANCE` item and a correction only when it is Posted in the
  run's household; and
- update and delete triggers on all four tables raise
  `WL_M010_EVIDENCE_IMMUTABLE` for observations and
  `WL_M010_RECONCILIATION_IMMUTABLE` for runs, items and resolutions.

Trust boundaries the database cannot carry: the Application derives the ledger
side through the inventory read model with checked arithmetic and refuses a
difference that does not fit 64-bit fixed point. A same-key run retry that
loses a race returns the winner's run.

The household run list reads `IX_ReconciliationRun_Household_RunAt_Id`, and the
per-evidence list reads `IX_ReconciliationRun_Evidence_RunAt_Id`, both in key
order without a temporary sort; item counts use the run's item index and the
resolution key.

The migration creates no observation or reconciliation rows for existing
history. Down drops the twelve triggers and four tables and restores the exact
migration-009 schema with its evidence intact. Real-SQLite tests cover the
lifecycle through the real ledger use cases, reversal-induced staleness, retry
races, every direct-SQL guard, the query plans, backup, verify and stage
readback, and 009 -> 010 -> 009 -> 010.
