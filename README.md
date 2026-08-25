# WealthLedger

WealthLedger is a long-lived household multi-asset investment ledger built on .NET 10. It preserves economic events and acquisition lineage so that positions, cost basis, performance, allocation, and reconciliation remain derivable and auditable.

The ledger is the source of truth. Posted transactions are immutable, corrections use separate reversals, and authoritative financial values use integer minor units or signed E8 fixed-point representations.

## Repository structure

- `src/WealthLedger.Domain` contains the financial model and invariants.
- `src/WealthLedger.Application` contains focused use cases and persistence ports.
- `src/WealthLedger.Infrastructure` contains the EF Core SQLite implementation.
- `src/WealthLedger.Api` contains the ASP.NET Core Minimal API boundary.
- `tests` contains the unit and real-SQLite integration suites.
- `docs` contains product, delivery, architecture, domain, database, project-state, operations, milestone, and ADR material at each document's stated status.

See [docs/PROJECT_STATE.md](docs/PROJECT_STATE.md) for the verified checkpoint and next coherent slice.

## Documentation map

| Document | Purpose |
|---|---|
| [AGENTS.md](AGENTS.md) | Repository-wide rules for humans and agents |
| [docs/PRODUCT_REQUIREMENTS.md](docs/PRODUCT_REQUIREMENTS.md) | Durable product outcomes and boundaries |
| [docs/ROADMAP.md](docs/ROADMAP.md) | Intended delivery order, never proof of implementation |
| [docs/PROJECT_STATE.md](docs/PROJECT_STATE.md) | Concise verified repository checkpoint |
| [docs/UX_MVP.md](docs/UX_MVP.md) | Framework-independent proposed interaction model |
| [docs/DATA_CAPTURE.md](docs/DATA_CAPTURE.md) | Source facts each financial workflow should preserve |
| [docs/SECURITY_OPERATIONS.md](docs/SECURITY_OPERATIONS.md) | Proposed local-data, backup, restore, and privacy baseline |
| [docs/milestones/README.md](docs/milestones/README.md) | Milestone statuses, template, agent prompts, and definition of done |
| [docs/decisions/README.md](docs/decisions/README.md) | Accepted architectural decisions |

Roadmap items are implemented one bounded milestone at a time. A Proposed
milestone is a review contract, not authorization to fill unresolved decisions
silently. Conversation transcripts under `docs/history` are non-authoritative
reference material and are not required reading for routine development.

## Prerequisites

- .NET SDK 10

Restore the pinned local tools and dependencies:

```powershell
dotnet tool restore
dotnet restore WealthLedger.slnx
```

## Local database initialization

Database migration and the one-time setup endpoint are disabled by default. For an explicit first local initialization, run:

```powershell
dotnet run --project src/WealthLedger.Api/WealthLedger.Api.csproj -- --Database:ApplyMigrationsOnStartup=true --Setup:Enabled=true
```

Call `POST /api/setup/core-ledger` once to create the initial currency, household, institution, portfolio, account, cash asset, and fund asset. Stop the process afterward and restart without the setup flags for normal operation.

## Verification

Run the full test suite:

```powershell
dotnet test WealthLedger.slnx --no-restore
```

Check formatting and migration-model alignment:

```powershell
dotnet format WealthLedger.slnx --verify-no-changes --no-restore
dotnet ef migrations has-pending-model-changes --project src/WealthLedger.Infrastructure/WealthLedger.Infrastructure.csproj --startup-project src/WealthLedger.Infrastructure/WealthLedger.Infrastructure.csproj --context WealthLedgerDbContext --no-build
```

Development guidance and non-negotiable invariants are defined in [AGENTS.md](AGENTS.md).
