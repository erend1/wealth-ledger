# WealthLedger Architecture

Status: Canonical architecture

Last distilled: 2026-09-08

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
and UI are delivery mechanisms; they do not create alternate financial-rule
paths. `WealthLedger.Api` is also the single local runtime composition root.

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
- the single ASP.NET Core loopback host and startup-mode composition;
- Razor Pages registration and fail-closed page mapping for the UI assembly;
- authentication/authorization wiring when introduced;
- request validation at the transport boundary;
- mapping between API contracts and Application requests/results;
- error-to-HTTP translation.

The API does not expose EF entities and does not contain portfolio mathematics.

Normal API/UI hosting is loopback-only. Before mapping routes, startup derives
exactly one of `Blocked`, `StorageUninitialized`, `WorkspaceUninitialized`,
`InitialBackupRequired`, or `Ready` without holding database ownership. Only a
`Ready` host then acquires the same authoritative ownership used by lifecycle
operations and retains it for the process lifetime. A failed acquisition becomes
`Blocked`. Startup never invokes EF migration APIs and exposes no browser backup
selection, restore, file-browser, migration, path override, or SQL endpoint.

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

`WealthLedger.UI` is the server-rendered Razor Class Library accepted by ADR-008.
It owns PageModels, Razor views, layouts, neutral Turkish-first resources, local
CSS and progressive enhancement, and exact presentation of Application results.
It references Application only: it does not reference Infrastructure, EF Core,
SQLite, Minimal API transport contracts, or endpoint implementations, and it
does not call the co-hosted JSON API over HTTP.

PageModels map human input and Application results but do not perform accounting
arithmetic or persistence. Exact money, E8 quantity, unit price, dates,
timestamps, and stable codes pass through the shared UI value presenter. Unknown,
Not applicable, recorded Zero, and unavailable values remain distinct.

Every Razor Page declares its accepted startup mode and whether it supports
POST. A centralized convention and middleware deny undeclared or mode-
inappropriate pages. Setup mutations are antiforgery-protected and use
Post/Redirect/Get; Ready pages are read-only GETs. All required assets are local,
and the UI emits the restrictive CSP and related security headers recorded by
ADR-008. See ADR-008 for the accepted topology and rejected alternatives.

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

    API request or accepted setup-page POST
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

M006 adds no Ready-mode UI write path. The only browser writes are bounded
first-run storage creation, atomic core setup, and creation of one new immutable
verified backup generation. Each calls the existing Application operation
directly through dependency injection and acquires lifecycle ownership only for
that request. Migration, restore, active replacement, arbitrary path selection,
and ordinary ledger posting are not browser surfaces.

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

The verified M006 UI read flow is:

    Razor PageModel
        ↓
    focused Application use case/query
        ↓
    narrow Application read port
        ↓
    bounded Infrastructure projection
        ↓
    Application result
        ↓
    exact UI presenter and encoded Razor view

Today, Ledger, transaction explanation, and Settings derive their state on each
request. They create no UI cache, session authority, browser storage, or
materialized balance. Transaction explanation composes M003 facts with current
M005 display labels in bounded queries; those labels are visibly current context,
not source-time history.

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

UI tests cover exact formatting, stable-code/resource completeness, dependency
direction, semantic markup, input labeling, validation focus, local assets,
responsive/focus CSS, and reduced-motion/forced-color rules. API-host tests cover
startup-mode route exposure, first-run mutation safety, Ready rendering, security
headers, and privacy-safe responses and logs against isolated SQLite files.

Operations tests use unique temporary directories and real processes to verify
path independence, ownership collisions, stable CLI parsing/exits, WAL and
rollback-journal backups, hostile archives, isolated restore, active rollback,
pre-migration protection, restart/readback, and privacy-safe diagnostics.

The Playwright xUnit suite starts real loopback host processes on ephemeral
ports and verifies the restart-delimited first run and Ready read navigation in
Chromium. It includes JavaScript-disabled and keyboard-only journeys, narrow and
desktop reflow, rejects all non-loopback requests, and proves browser, process,
and synthetic-file cleanup. Browser installation is an explicit prerequisite,
not a side effect of the test run.
