# WealthLedger Project State

As of: 2026-08-24

Status source: verified against the repository, the generated EF model, and local .NET/SQLite test runs.

## Current checkpoint

The Domain v1 baseline, the `001_CoreLedger` persistence milestone, and the first ASP.NET Core Minimal API slice are implemented and verified. The end-to-end slice records a contribution and a synthetic fund purchase, creates its acquisition lot, commits each posted graph atomically, and derives positions from posted entry history through HTTP against real SQLite.

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

## Verification

Last verified commands:

```text
dotnet test WealthLedger.slnx --no-restore --verbosity minimal
dotnet format WealthLedger.slnx --verify-no-changes --no-restore --verbosity minimal
dotnet ef migrations has-pending-model-changes --project src/WealthLedger.Infrastructure/WealthLedger.Infrastructure.csproj --startup-project src/WealthLedger.Infrastructure/WealthLedger.Infrastructure.csproj --context WealthLedgerDbContext --no-build
```

Results:

- Domain tests: 76 passed, 0 failed.
- Application tests: 6 passed, 0 failed.
- Infrastructure tests against real SQLite files: 29 passed, 0 failed.
- API tests against real SQLite files: 4 passed, 0 failed.
- Total: 115 passed, 0 failed.
- Formatting drift: none.
- EF model drift: none.

The integration suite proves fixed-point and stable-code round trips, GUID/date/timestamp storage, foreign-key enforcement, posted graph immutability through EF and direct SQL, reversal behavior and dependency protection, effective lot balances that exclude drafts, acquisition-lineage and allocation invariants, cost-basis shape, transaction ordering, atomic rollback, and an HTTP contribution/purchase/position round trip without authoritative balance tables. API tests also prove transport-code validation, semantic rule mapping, sanitized persistence failures, and isolated migrated SQLite databases under parallel execution.

## Next coherent slice

The smallest operational follow-on is a focused setup slice for the master/reference data required by the API:

1. Accept explicit Application use cases for initializing the first household, currencies, institution, portfolio, account, cash asset, and fund asset without introducing generic repositories.
2. Add transport contracts only for those accepted setup operations and keep persistence rows internal.
3. Define and test a deliberate local database migration/initialization workflow rather than seeding durable data from ledger endpoints.
4. Keep authentication, authorization, and UI technology as separate decisions.

Do not start live market data, provider-specific integration, optimization, AI/LLM integration, broad UI work, materialized analytics, microservices, messaging, caching, or CQRS infrastructure without a new accepted milestone need.

## Open decisions

- UI framework and MVP interaction model.
- Market/reference-data schemas and provider contracts.
- Authentication and authorization.
- Backup, encryption-at-rest, and deployment for the local database.
- Partial cost-basis rounding allocation if a concrete use case exposes a gap.
- SQLite concurrency policy if multiple writers become a demonstrated requirement.

Record a new ADR only when one of these or another cross-cutting architectural choice is accepted or superseded.
