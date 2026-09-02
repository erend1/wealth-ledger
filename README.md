# WealthLedger

WealthLedger is a long-lived household multi-asset investment ledger built on .NET 10. It preserves economic events and acquisition lineage so that positions, cost basis, performance, allocation, and reconciliation remain derivable and auditable.

The ledger is the source of truth. Posted transactions are immutable, corrections use separate reversals, and authoritative financial values use integer minor units or signed E8 fixed-point representations.

## Repository structure

- `src/WealthLedger.Domain` contains the financial model and invariants.
- `src/WealthLedger.Application` contains focused use cases and persistence ports.
- `src/WealthLedger.Infrastructure` contains the EF Core SQLite implementation.
- `src/WealthLedger.Api` contains the ASP.NET Core Minimal API boundary.
- `src/WealthLedger.Operations` contains the explicit local data lifecycle CLI.
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
| [docs/SECURITY_OPERATIONS.md](docs/SECURITY_OPERATIONS.md) | Accepted security/operations requirements and implemented M004 boundary |
| [docs/OPERATIONS.md](docs/OPERATIONS.md) | Canonical database, backup, restore, migration, and recovery guide |
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

## Local database operations

Normal API startup never creates or migrates a database. Configure an absolute,
separated backup directory, then use the explicit operations project:

```powershell
$wlBackupDirectory = 'E:\Encrypted WealthLedger Backups'
$wlProtectionArgs = @(
  "--Backup:Directory=$wlBackupDirectory"
  '--Backup:DestinationSeparationConfirmed=true'
  '--Backup:DestinationEncryptionConfirmed=true'
)

dotnet run --project src/WealthLedger.Operations/WealthLedger.Operations.csproj -- status @wlProtectionArgs
dotnet run --project src/WealthLedger.Operations/WealthLedger.Operations.csproj -- database initialize @wlProtectionArgs
dotnet run --project src/WealthLedger.Operations/WealthLedger.Operations.csproj -- backup create @wlProtectionArgs
```

Set the confirmation values to `true` only after verifying the destination's
actual separation, encryption, and recovery-key custody. Application packages
are plaintext. See [docs/OPERATIONS.md](docs/OPERATIONS.md) before using real
data; it covers verification, restore drills, migration, active replacement,
failure recovery, and stable exit categories.

After database initialization, one-time master-data setup remains an explicit,
default-off API action. Start the loopback API with `Setup:Enabled=true`, call
`POST /api/setup/core-ledger` once, then restart without that flag.

## Read-only navigation API

The loopback API exposes current master display context without making it
ledger history. Stable identities always accompany names and codes:

```text
GET /api/households
GET /api/households/{householdId}
GET /api/households/{householdId}/members
GET /api/households/{householdId}/portfolios
GET /api/households/{householdId}/accounts
GET /api/institutions
GET /api/currencies
GET /api/assets
GET /api/households/{householdId}/ledger/transactions
```

Every collection accepts `pageSize` from 1 through 100 (default 50) and an
opaque continuation `cursor`. Members, portfolios, accounts, institutions, and
assets also accept `includeInactive=true`; active-only is the default. A
synthetic request and envelope are:

```http
GET /api/households/10000000-0000-0000-0000-000000000001/accounts?pageSize=25&includeInactive=true
```

```json
{
  "items": [
    {
      "accountId": "50000000-0000-0000-0000-000000000001",
      "householdId": "10000000-0000-0000-0000-000000000001",
      "institution": null,
      "code": "SYNTHETIC_ACCOUNT",
      "name": "Synthetic Account",
      "typeCode": "INVESTMENT",
      "isActive": true,
      "openedOn": "2026-01-01",
      "closedOn": null
    }
  ],
  "nextCursor": null
}
```

The household ledger route returns recently recorded Posted transactions by
`postedAtUtc` and transaction ID, descending. Its entry effects include exact
raw E8 quantities and current Portfolio, Account, nullable Institution, and
Asset context. Follow `transactionId` with
`GET /api/ledger/transactions/{transactionId}` for complete details; summaries
omit notes, costs, cash-flow expansion, lots, and allocations.

Malformed page/filter/cursor input returns a stable 400 navigation code.
Unknown nested households return `HOUSEHOLD_NOT_FOUND`. The existing point-
position route still returns a genuine zero for a valid empty scope, while an
unknown or cross-household scope returns the sanitized 404 code
`POSITION_SCOPE_NOT_FOUND`.

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
