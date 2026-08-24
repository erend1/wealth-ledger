# WealthLedger Project State

As of: 2026-08-24

Status source: verified against the repository, the generated EF model, and local .NET/SQLite test runs.

## Current checkpoint

The Domain v1 baseline and the `001_CoreLedger` persistence milestone are implemented and verified. The first complete Application-to-SQLite slice records a contribution and a synthetic fund purchase, creates its acquisition lot, commits each posted graph atomically, and derives positions from posted entry history after the database is closed and reopened.

The solution currently contains:

- `WealthLedger.Domain`
- `WealthLedger.Application`
- `WealthLedger.Infrastructure`
- `WealthLedger.Application.Tests`
- `WealthLedger.Domain.Tests`
- `WealthLedger.Infrastructure.Tests`

`WealthLedger.Api`, `WealthLedger.UI`, and a UI technology decision do not yet exist.

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

## Verification

Last verified commands:

```text
dotnet test WealthLedger.slnx --no-restore --verbosity minimal
dotnet ef migrations has-pending-model-changes --project src/WealthLedger.Infrastructure/WealthLedger.Infrastructure.csproj --startup-project src/WealthLedger.Infrastructure/WealthLedger.Infrastructure.csproj --context WealthLedgerDbContext --no-build
```

Results:

- Domain tests: 76 passed, 0 failed.
- Application tests: 6 passed, 0 failed.
- Infrastructure tests against real SQLite files: 29 passed, 0 failed.
- Total: 111 passed, 0 failed.
- EF model drift: none.

The integration suite proves fixed-point and stable-code round trips, GUID/date/timestamp storage, foreign-key enforcement, posted graph immutability through EF and direct SQL, reversal behavior and dependency protection, effective lot balances that exclude drafts, acquisition-lineage and allocation invariants, cost-basis shape, transaction ordering, atomic rollback, and a contribution/purchase/position round trip without authoritative balance tables.

## Next coherent slice

The natural next delivery slice is the accepted ASP.NET Core Minimal API boundary for the verified use cases:

1. Add `WealthLedger.Api` with explicit contribution, fund-purchase, and position transport contracts.
2. Add ordinary dependency-injection composition for DbContext, Application use cases, and Infrastructure adapters.
3. Translate transport input deliberately into fixed-point Domain/Application values and map validation/storage failures without exposing EF rows.
4. Add HTTP integration tests against real SQLite while keeping UI technology undecided.

Do not start live market data, provider-specific integration, optimization, AI/LLM integration, broad UI work, materialized analytics, microservices, messaging, caching, or CQRS infrastructure without a new accepted milestone need.

## Open decisions

- UI framework and MVP interaction model.
- Market/reference-data schemas and provider contracts.
- Authentication and authorization.
- Backup, encryption-at-rest, and deployment for the local database.
- Partial cost-basis rounding allocation if a concrete use case exposes a gap.
- SQLite concurrency policy if multiple writers become a demonstrated requirement.

Record a new ADR only when one of these or another cross-cutting architectural choice is accepted or superseded.
