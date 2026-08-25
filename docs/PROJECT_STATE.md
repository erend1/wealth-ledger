# WealthLedger Project State

As of: 2026-08-24

Status source: verified against the repository, the generated EF model, and local .NET/SQLite test runs.

## Current checkpoint

The Domain v1 baseline, the `001_CoreLedger` persistence milestone, the first ASP.NET Core Minimal API slice, and an explicit first-run setup slice are implemented and verified. Starting from an empty SQLite file, the end-to-end path applies the migration only when opted in, initializes the required master data through HTTP, records a contribution and fund purchase, creates its acquisition lot, and derives positions from posted entry history.

The solution currently contains:

- `WealthLedger.Domain`
- `WealthLedger.Application`
- `WealthLedger.Infrastructure`
- `WealthLedger.Api`
- `WealthLedger.Api.Tests`
- `WealthLedger.Application.Tests`
- `WealthLedger.Domain.Tests`
- `WealthLedger.Infrastructure.Tests`

`WealthLedger.UI` and a UI technology decision do not yet exist.

## Delivery planning

Canonical product requirements, delivery roadmap, proposed MVP interaction
model, data-capture guide, security/operations baseline, and milestone workflow
now live under `docs`.

The only accepted delivery milestone is
[`M002: Retry-Safe Transaction Submission and Readback`](milestones/M002_transaction_submission_and_readback.md).
Its dedicated retry-identity contract and persistence boundary were accepted on
2026-08-24 and are recorded by ADR-006. No idempotency storage,
transaction-detail query, or related API behavior exists yet; implementation
and verification remain pending.

## Verified implementation

### Domain

The repository contains the accepted fixed-point value objects, assets and stable vocabulary, master data, ledger aggregate and children, reversal behavior, cost basis, asset lots, signed lot-entry allocations, physical-gold detail, and FIFO allocation planning described by the canonical domain documents and ADRs.

The persisted model does not contain a `Reversed` status, `LotDisposal`, lot custody fields, mutable remaining-quantity fields, binary floating-point financial fields, or authoritative position/value/allocation tables.

### Core persistence

- Runtime and target framework: .NET 10.
- EF Core SQLite and design packages: `10.0.11`.
- Local `dotnet-ef` tool: `10.0.11`.
- DbContext: `src/WealthLedger.Infrastructure/Persistence/WealthLedgerDbContext.cs`.
- Explicit configurations: `src/WealthLedger.Infrastructure/Persistence/Mapping/CoreLedgerConfigurations.cs`.
- Stable text-code mappings: `src/WealthLedger.Infrastructure/Persistence/Mapping/StableCodeMappings.cs`.
- Initial migration: `20260824074930_001_CoreLedger`.

The migration creates the normalized core master-data, ledger, lot, allocation, cash-flow, cost-component, and physical-gold tables. Financial values use signed integer minor units or signed E8 integers. GUIDs, business dates, UTC timestamps, and enum-like values have explicit SQLite representations.

SQLite foreign keys are enabled on every opened connection. Check constraints, restrictive foreign keys, indexes, and triggers protect household consistency, transaction date ordering, posted aggregate immutability, reversal uniqueness and exact inverse facts, required lot reconciliation, allocation sign and asset consistency, non-negative lot balances, known-versus-unknown cost basis, and physical-gold detail consistency.

The Infrastructure persistence rows remain internal implementation records. No generic repository abstraction has been introduced.

### First vertical slice

Application contains focused commands and ports for:

- recording a positive external contribution with cash-flow classification;
- recording a fund purchase with separate fund principal and cash consideration entries;
- creating the fund acquisition lot with known minor-unit cost basis and an exact opening allocation;
- querying a signed position for one household, portfolio, account, and asset.

Currency-asset quantities are converted deterministically from signed integer minor units to E8 using the persisted currency precision with checked arithmetic. Executed unit price remains a preserved source fact; cash consideration remains a separate entry.

Infrastructure resolves the required master references, maps Domain aggregate roots to internal rows, inserts the complete graph as Draft, and finalizes it as Posted inside one SQLite transaction. A failed final posting rolls back the transaction, entries, lots, and allocations together. The position adapter returns only posted entry facts; Application performs the ordered checked sum.

### Minimal API boundary

`WealthLedger.Api` exposes:

- `POST /api/ledger/contributions`;
- `POST /api/ledger/fund-purchases`;
- `GET /api/households/{householdId}/portfolios/{portfolioId}/accounts/{accountId}/positions/{assetId}`.

Transport contracts use signed integer minor units and raw E8 integers. Cash-flow categories have explicit stable text-code mapping. API contracts do not expose Domain aggregates or EF rows, and no portfolio arithmetic has moved into the delivery layer.

The composition root registers the focused Application use cases, the EF Core SQLite adapters, `TimeProvider`, Problem Details, and exception translation. Invalid transport values return 400, Domain/Application rule violations return 422, and wrapped persistence conflicts return a sanitized 409 without leaking SQLite or EF details. Database migration and master-data initialization remain explicit operational prerequisites rather than hidden endpoint side effects.

### First-run setup

Application provides one focused initialization command for a base currency, household, optional member, institution, portfolio, account, cash asset, and required-lot fund asset. It constructs the existing Domain master entities, generates their identities, and submits one setup graph through `ICoreLedgerSetupStore`; no generic master-data repository or service layer was introduced.

Infrastructure accepts setup only when all core master tables are empty and inserts the complete graph in one SQLite transaction. Existing master data returns a stable conflict, while a constraint or trigger failure rolls back currency, household, member, institution, portfolio, account, and assets together. The setup uses the existing `001_CoreLedger` schema and required no model or migration change.

`POST /api/setup/core-ledger` is mapped only when `Setup:Enabled` is explicitly true. `Database:ApplyMigrationsOnStartup` is also false by default and applies migrations only when explicitly enabled. API integration tests now initialize through HTTP rather than inserting internal persistence rows, and the setup endpoint returns stable IDs consumed by the existing ledger routes.

## Verification

Last verified commands:

```text
dotnet test WealthLedger.slnx --no-restore --verbosity minimal
dotnet format WealthLedger.slnx --verify-no-changes --no-restore --verbosity minimal
dotnet ef migrations has-pending-model-changes --project src/WealthLedger.Infrastructure/WealthLedger.Infrastructure.csproj --startup-project src/WealthLedger.Infrastructure/WealthLedger.Infrastructure.csproj --context WealthLedgerDbContext --no-build
```

Results:

- Domain tests: 76 passed, 0 failed.
- Application tests: 12 passed, 0 failed.
- Infrastructure tests against real SQLite files: 32 passed, 0 failed.
- API tests against real SQLite files: 7 passed, 0 failed.
- Total: 127 passed, 0 failed.
- Formatting drift: none.
- EF model drift: none.

The integration suite proves fixed-point and stable-code round trips, GUID/date/timestamp storage, foreign-key enforcement, posted graph immutability through EF and direct SQL, reversal behavior and dependency protection, effective lot balances that exclude drafts, acquisition-lineage and allocation invariants, cost-basis shape, transaction ordering, setup and posting rollback, and an HTTP setup/contribution/purchase/position round trip without authoritative balance tables. API tests also prove default-off setup gating, opt-in migration, repeat-setup conflict, transport-code validation, semantic rule mapping, sanitized persistence failures, and isolated SQLite databases under parallel execution.

## Next delivery candidate

M002 is the next accepted implementation milestone. It hardens the current HTTP
write boundary before another command endpoint is added:

1. Separate a client command's retry identity from optional external financial
   references.
2. Persist retry identity and posting result atomically so equivalent and
   concurrent replay cannot duplicate history.
3. Add transaction-detail readback and make existing transaction Locations
   resolvable.
4. Prove equivalent replay, conflicting replay, concurrent submission,
   rollback, restart, and sanitized not-found/conflict behavior.

Acceptance changed delivery status only and has not changed the verified runtime
checkpoint described above.

## Following coherent ledger slice

After write safety and readback, the next coherent ledger feature is the posted
reversal/correction workflow already accepted by the Domain and persistence
design:

1. Add a focused Application use case and query/store ports that load one posted original with its effective entries, verify reversal uniqueness and later lot dependencies, and create the Domain reversal.
2. Persist the reversal transaction and mirrored allocations atomically without changing the original transaction's Posted status.
3. Add an explicit API command and sanitized conflict/not-found behavior without exposing persistence rows.
4. Prove original-plus-reversal position netting, second-reversal rejection, dependent-lot rejection, restart behavior, and posted-history immutability through Application, SQLite, and HTTP tests.

Do not start live market data, provider-specific integration, optimization, AI/LLM integration, broad UI work, materialized analytics, microservices, messaging, caching, or CQRS infrastructure without a new accepted milestone need.

## Open decisions

- UI framework and MVP interaction model.
- Market/reference-data schemas and provider contracts.
- Authentication and authorization.
- Backup, encryption-at-rest, and deployment for the local database.
- Partial cost-basis rounding allocation if a concrete use case exposes a gap.
- SQLite concurrency policy if multiple writers become a demonstrated requirement.

Record a new ADR only when one of these or another cross-cutting architectural choice is accepted or superseded.
