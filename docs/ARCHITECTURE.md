# WealthLedger Architecture

Status: Canonical architecture

Last distilled: 2026-09-01

## Dependency direction

    WealthLedger.UI ────────────────┐
                                    │
    WealthLedger.Api ───────────────┤
                                    ├──> WealthLedger.Application ──> WealthLedger.Domain
    WealthLedger.Operations ────────┤
                                    ▲
                                    │
    WealthLedger.Infrastructure ────┘

Domain has no outward dependency. Application depends on Domain. Infrastructure
implements Application ports and depends on both as needed. API, Operations,
and the later UI are delivery/composition mechanisms; they do not create
alternate financial-rule paths.

## Project responsibilities

### WealthLedger.Domain

Owns:

- financial value objects and exact arithmetic;
- asset vocabulary;
- transaction and lot aggregates;
- invariants that can be decided from aggregate state;
- deterministic lot-allocation behavior that does not require persistence;
- domain-specific exceptions.

Must not know:

- EF Core or SQLite;
- HTTP or transport DTOs;
- user-interface frameworks;
- market-data provider protocols;
- LLM or agent libraries.

### WealthLedger.Application

Owns:

- use-case orchestration;
- repository and query ports shaped around use cases;
- transaction boundaries;
- cross-aggregate validation requiring persisted history;
- authorization/policy hooks when introduced;
- DTOs/results for callers;
- deterministic query and calculation services;
- narrow local-data operation ports and orchestration results that contain no
  filesystem, SQLite, EF, HTTP, archive, or console implementation detail.

Examples of appropriate use cases:

- record a contribution;
- record a purchase and create its lots;
- reverse a posted transaction after dependency validation;
- query positions by portfolio/account/asset;
- reconcile recorded holdings with an external statement.

Avoid a generic command bus or generic service layer. A use case may be an ordinary class with an explicit method.

### WealthLedger.Infrastructure

Owns:

- WealthLedgerDbContext;
- EF Core entity configurations and value conversions;
- SQLite migration SQL, constraints, indexes, and triggers;
- repository/query implementations;
- canonical local-data path validation and exclusive process ownership;
- SQLite integrity/compatibility inspection and explicit EF migration mechanics;
- online backup, bounded archive verification, filesystem staging, journal
  normalization, active replacement, and rollback mechanics;
- external provider implementations introduced in later milestones;
- rebuildable read-model persistence if later justified.

Persistence needs do not dictate public Domain mutation APIs. Use explicit EF configuration, private fields/constructors, and tested materialization.

### WealthLedger.Api

Owns:

- Minimal API endpoints and route grouping;
- authentication/authorization wiring when introduced;
- request validation at the transport boundary;
- mapping between API contracts and Application requests/results;
- error-to-HTTP translation.

The API does not expose EF entities and does not contain portfolio mathematics.

Normal API hosting is loopback-only. Startup acquires the same authoritative
database ownership used by lifecycle operations, validates the current schema,
and fails closed when initialization or explicit migration is required. It does
not invoke EF migration APIs and exposes no backup, restore, file-browser,
migration, or SQL endpoint.

### WealthLedger.Operations

Owns:

- parsing the seven accepted local lifecycle commands;
- composition of the focused Application local-data use cases;
- privacy-safe text output and stable numeric exit categories;
- cancellation wiring for the console process.

It accepts no caller-supplied SQL. Filesystem, archive, SQLite, locking, backup,
restore, and migration work remains in Infrastructure. Destructive active
replacement requires the literal `--confirm-replace-active` option and still
passes through Application orchestration and Infrastructure safety checks.

### WealthLedger.UI

Owns presentation and interaction only. Its framework has not been accepted yet. The first end-to-end slice may be completed through the API before UI work.

### Future agent integration

A future agent integration may:

- query curated read models;
- explain portfolio state;
- propose a validated plan;
- prepare a draft command for review.

It may not write SQLite directly, post silently, or replace deterministic allocation/cost-basis logic.

## Aggregate model

    LedgerTransaction
    ├── TransactionEntry
    ├── TransactionCostComponent
    └── CashFlowDetail

    AssetLot
    ├── LotEntryAllocation
    └── PhysicalGoldLotDetail

LedgerTransaction protects the lifecycle and economic consistency of one event.

AssetLot protects acquisition lineage, signed allocation history, and the non-negative quantity invariant.

Asset, Household, HouseholdMember, Institution, Portfolio, and Account are master entities. Their precise aggregate grouping should follow the implemented Domain and use cases; do not invent child repositories merely because they map to tables.

## Repository boundaries

Repository ports belong in Application unless a concrete Domain service genuinely requires one. Prefer narrow capabilities, for example:

- load a transaction with its children for posting/reversal;
- determine whether an original transaction was already reversed;
- load open lots for one asset in FIFO order;
- save a transaction and related lot changes atomically;
- query derived positions;
- page current master display context or recent Posted effects;
- validate one household-safe position scope.

Do not expose IQueryable outside Infrastructure. Do not make one IRepository of T abstraction cover unrelated aggregate semantics.

Transaction posting and its associated lot changes must be committed atomically.

## Write flow

    API/UI request
        ↓
    transport validation and explicit mapping
        ↓
    Application use case
        ↓
    load required aggregates/history
        ↓
    Domain operations and validation
        ↓
    persist one atomic unit of work
        ↓
    return stable Application result

Database constraints form a second safety layer. They do not replace Domain and Application validation, and exceptions from raw SQLite should be translated at the Infrastructure/Application boundary.

## Read flow

Derived queries may read normalized tables efficiently without rehydrating aggregates when no domain mutation occurs.

    normalized posted ledger facts
        ↓
    deterministic query/projection
        ↓
    position, cost basis, value, performance, or allocation result

M005 navigation follows the same inward boundary without creating a
materialized read model:

    API page/filter text
        ↓
    Application validation and scoped versioned cursor
        ↓
    narrow Application read port
        ↓
    bounded Infrastructure AsNoTracking projection
        ↓
    stable current-display or exact ledger-effect result

Household predicates remain in SQLite. Cursor content selects only a frozen
keyset shape; it never supplies a column name, SQL fragment, or free-form
predicate. The recent ledger projection batches effects for the bounded page
rather than resolving each transaction or master row separately.

A later materialized read model is allowed only when:

- it is clearly non-authoritative;
- it can be rebuilt from source facts;
- update/rebuild semantics are tested;
- callers cannot mistake it for ledger truth.

## Cross-aggregate invariants

Some rules require persisted context and therefore cannot live entirely inside one aggregate:

- an original transaction can have at most one reversal;
- an acquisition cannot be fully reversed while later effective lot allocations depend on it;
- all lots used by a lot-tracked entry must reconcile exactly to that entry;
- account, portfolio, and transaction must belong to the same household;
- a lot and its allocated entry must reference the same asset;
- internal transfer entries must net to zero by asset.

Enforce these in Application orchestration and, where feasible, again with unique indexes, foreign keys, checks, or SQLite triggers.

## Value and identity strategy

Entity identity uses Guid. Do not introduce a separate strong-ID value type for every entity unless a demonstrated benefit outweighs mapping and API noise.

Financial semantics use strong value objects:

- CurrencyCode;
- Money;
- Quantity;
- QuantityDelta;
- UnitPrice;
- BasisPoints;
- Fineness;
- CostBasis.

Invalid-default-sensitive value objects such as CurrencyCode, Money, UnitPrice, and Fineness are reference records/classes in the reported Domain design. Zero-valid types such as Quantity, QuantityDelta, and BasisPoints may remain value types.

## Failure and concurrency

Domain-rule failures should be explicit and stable enough for Application/API translation.

SQLite writes must use transactions. M004 adds one local, adjacent,
cross-process ownership lock for the authoritative database: the API holds it
during normal write service, while initialize, backup creation, migration, and
active replacement require exclusive lifecycle ownership. The open exclusive
file handle is authoritative; an unlocked stale marker is not. This is not a
distributed lock or remote multi-writer design.

## Testing layers

Domain tests exercise value objects, aggregate transitions, transaction semantics, lot allocation, FIFO behavior, reversal creation, and invalid states.

Application tests exercise use-case orchestration and cross-aggregate rules.

Integration tests use real SQLite to verify mappings, constraints, triggers, transactions, and derived queries. Do not rely only on EF's in-memory provider for SQLite behavior.

API tests cover transport mapping and status/error behavior after the first slice exists.

Operations tests use unique temporary directories and real processes to verify
path independence, ownership collisions, stable CLI parsing/exits, WAL and
rollback-journal backups, hostile archives, isolated restore, active rollback,
pre-migration protection, restart/readback, and privacy-safe diagnostics.
