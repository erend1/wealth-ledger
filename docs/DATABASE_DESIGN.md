# WealthLedger Database Design

Status: Canonical target for the first persistence milestone

Target: EF Core with SQLite

Migrations:
- `20260824074930_001_CoreLedger`
- `20260827072019_002_CommandReceipt`
- `20260831113310_003_ReversalDependencySemantics`

Last distilled: 2026-08-31

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
        └── CashFlowDetail

    AssetLot
        └── PhysicalGoldLotDetail

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
- CostBasisStatusCode.

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
10. restrictive deletion of lots and allocations that participate in history.

SQLite triggers that read other tables must have both insert and relevant update variants. Trigger behavior and error messages are part of integration tests.

The exact save order must allow a complete valid aggregate and its lot effects to be inserted atomically before the transaction is finalized as Posted.

Some invariants are better enforced in Application code and tested against the database:

- semantic shape of every transaction type;
- exact sum of all allocations for one entry;
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

## Indexes

Beyond PK/unique indexes, evaluate query-driven indexes for:

- LedgerTransaction by HouseholdId, StatusCode, ExecutionDate;
- TransactionEntry by TransactionId;
- TransactionEntry by PortfolioId, AssetId;
- TransactionEntry by AccountId, AssetId;
- AssetLot by AssetId, AcquiredOn;
- LotEntryAllocation by AssetLotId;
- LotEntryAllocation by TransactionEntryId.

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

## Deferred database regions

Not part of 001_CoreLedger:

- market quotes and historical reference data;
- asset-family detail tables beyond physical-gold lot detail;
- goals and allocation policies;
- reconciliation/import staging;
- analysis and agent-decision history;
- materialized read models;
- UI preferences.

Their eventual schemas must reference the ledger rather than duplicating it and must be introduced by separate migrations and ADRs when cross-cutting choices arise.

## Minimum migration integration scenarios

- create master data, contribution, fund purchase, lot, and derive the position after a database round trip;
- transfer a lot-tracked asset between accounts without changing global lot quantity or cost basis;
- sell across two lots using FIFO and derive remaining quantities;
- import an opening balance with Unknown cost;
- store a physical-gold lot and derive fine-gold quantity;
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
