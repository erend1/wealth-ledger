# WealthLedger

WealthLedger is a long-lived household multi-asset investment ledger built on .NET 10. It preserves economic events and acquisition lineage so that positions, cost basis, performance, allocation, and reconciliation remain derivable and auditable.

The ledger is the source of truth. Posted transactions are immutable, corrections use separate reversals, and authoritative financial values use integer minor units or signed E8 fixed-point representations.

## Repository structure

- `src/WealthLedger.Domain` contains the financial model and invariants.
- `src/WealthLedger.Application` contains focused use cases and persistence ports.
- `src/WealthLedger.Infrastructure` contains the EF Core SQLite implementation.
- `tests` contains the unit and real-SQLite integration suites.
- `docs` contains the canonical architecture, domain, database, project-state, and ADR material.

See [docs/PROJECT_STATE.md](docs/PROJECT_STATE.md) for the verified checkpoint and next coherent slice.

## Prerequisites

- .NET SDK 10

Restore the pinned local tools and dependencies:

```powershell
dotnet tool restore
dotnet restore WealthLedger.slnx
```

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
