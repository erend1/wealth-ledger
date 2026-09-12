# WealthLedger

WealthLedger is a long-lived household multi-asset investment ledger built on .NET 10. It preserves economic events and acquisition lineage so that positions, cost basis, performance, allocation, and reconciliation remain derivable and auditable.

The ledger is the source of truth. Posted transactions are immutable, corrections use separate reversals, and authoritative financial values use integer minor units or signed E8 fixed-point representations.

## Repository structure

- `src/WealthLedger.Domain` contains the financial model and invariants.
- `src/WealthLedger.Application` contains focused use cases and persistence ports.
- `src/WealthLedger.Infrastructure` contains the EF Core SQLite implementation.
- `src/WealthLedger.UI` contains the Razor Class Library, PageModels, resources,
  and local presentation assets; it references Application only.
- `src/WealthLedger.Api` contains the ASP.NET Core Minimal API boundary and the
  single loopback Razor Pages host/composition root.
- `src/WealthLedger.Operations` contains the explicit local data lifecycle CLI.
- `tests` contains the unit, real-SQLite integration, process, UI host, and
  Playwright browser suites.
- `docs` contains product, delivery, architecture, domain, database, project-state, operations, milestone, and ADR material at each document's stated status.

See [docs/PROJECT_STATE.md](docs/PROJECT_STATE.md) for the verified checkpoint and next coherent slice.

## Documentation map

| Document | Purpose |
|---|---|
| [AGENTS.md](AGENTS.md) | Repository-wide rules for humans and agents |
| [docs/PRODUCT_REQUIREMENTS.md](docs/PRODUCT_REQUIREMENTS.md) | Durable product outcomes and boundaries |
| [docs/ROADMAP.md](docs/ROADMAP.md) | Intended delivery order, never proof of implementation |
| [docs/PROJECT_STATE.md](docs/PROJECT_STATE.md) | Concise verified repository checkpoint |
| [docs/UX_MVP.md](docs/UX_MVP.md) | Proposed broader interaction model and verified M006/M007 subsets |
| [docs/DATA_CAPTURE.md](docs/DATA_CAPTURE.md) | Source facts each financial workflow should preserve |
| [docs/SECURITY_OPERATIONS.md](docs/SECURITY_OPERATIONS.md) | Accepted requirements and implemented M004-M007 operating boundaries |
| [docs/OPERATIONS.md](docs/OPERATIONS.md) | Canonical database, backup, restore, migration, and recovery guide |
| [docs/milestones/README.md](docs/milestones/README.md) | Milestone statuses, template, agent prompts, and definition of done |
| [docs/decisions/README.md](docs/decisions/README.md) | Accepted architectural decisions |

Roadmap items are implemented one bounded milestone at a time. A Proposed
milestone is a review contract, not authorization to fill unresolved decisions
silently. Conversation transcripts under `docs/history` are non-authoritative
reference material and are not required reading for routine development.

## Prerequisites

- .NET SDK 10
- PowerShell 7 (`pwsh`) for the generated Playwright installer

Restore the pinned local tools and dependencies:

```powershell
dotnet tool restore
dotnet restore WealthLedger.slnx
```

The browser tests pin `Microsoft.Playwright` at `1.62.0`, which pins Chromium
revision `1234` (Chrome for Testing `151.0.7922.34`) through the package tooling.
Build the project, then install that browser explicitly with the generated
script:

```powershell
dotnet build tests/WealthLedger.UI.BrowserTests/WealthLedger.UI.BrowserTests.csproj --no-restore
pwsh tests/WealthLedger.UI.BrowserTests/bin/Debug/net10.0/playwright.ps1 install chromium
```

Browser installation is a separate prerequisite. `dotnet test` never downloads
a browser or silently skips the suite when it is absent.

## Local database operations

Host startup never creates or migrates a database as a side effect. Configure
an absolute, separated backup directory before either the browser first run or
the explicit operations project:

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

Migration, restore, active replacement, backup-file selection, and detailed
recovery stay exclusively in the Operations CLI. The legacy one-time JSON core-
setup endpoint remains default-off and is separate from the browser flow; it is
available only in `WorkspaceUninitialized` when `Setup:Enabled=true`.

## Local UI and guided first run

Start the single host on an explicit loopback URL using the protection settings
above:

```powershell
$wlHostArgs = $wlProtectionArgs + @('--Urls=http://127.0.0.1:54876')
dotnet run --project src/WealthLedger.Api/WealthLedger.Api.csproj -- @wlHostArgs
```

Open `http://127.0.0.1:54876/setup` in a local browser for first run (or
`/blocked` if startup reported a blocked mode). The process selects one mode at
startup and never promotes itself dynamically:

1. `StorageUninitialized` reviews the server-resolved safe storage location and
   can create only the missing database. It offers no browser path control.
2. Stop the process with Ctrl+C and run the same command. In
   `WorkspaceUninitialized`, enter human-readable core master values and review
   stable codes; no GUID, E8 value, minor-unit integer, SQL, or connection string
   is required.
3. Restart again. In `InitialBackupRequired`, review the configured destination
   and create one new immutable generation through the verified M004 backup
   operation.
4. After the completion page, restart once more and open
   `http://127.0.0.1:54876/`. `Ready` exposes Today, recent Ledger and
   transaction explanations, read-only Settings, and the dedicated M007 opening-
   balance workflow under Record. Setup routes now return 404.

An unsafe path, incompatible or migration-required database, failed integrity
check, partial workspace, or other non-recoverable startup classification shows
only the sanitized `/blocked` guidance. Use the
[M004 operations guide](docs/OPERATIONS.md) for status, explicit migration,
backup verification, isolated restore, and recovery; none of those privileged
actions is reachable through the browser.

The UI uses only local CSS and a tiny optional local focus helper. First run and
core navigation remain functional with JavaScript disabled and without Internet
access. The host rejects wildcard, LAN, and public binding; loopback is not a
remote-access security design.

### Synthetic screenshots and traces

M006/M007 add no screenshot/export feature and the automated browser suite
creates no screenshot or trace baseline. If capturing an image or trace for a review,
use only an isolated synthetic database and backup directory. Inspect the
artifact before sharing or committing it, and do not include household/master
names, notes, references, exact financial values, resolved real paths, cookies,
request bodies, cursor payloads, SQL, connection strings, or stack traces.

## Controlled opening-balance cutover

In Ready, open `http://127.0.0.1:54876/record/opening-balance`. M007 records one
household/portfolio/account/asset/as-of scope per command for base cash, foreign
currency, fund units, equity shares, or physical-gold gross weight. It uses a
non-mutating review followed by an explicit final post and a persisted receipt.

Cash/Currency creates no lot and shows historical cost as Not applicable.
Fund/Equity/PhysicalGold requires complete opening lots and exact allocation;
lot cost is Known only with supporting amount/currency or remains Unknown.
Physical-gold input captures gross weight, fineness, pieces, and optional
provenance while deriving fine weight. Current value is never substituted for
historical cost.

The same page can narrowly create a missing Currency, Institution, Account, or
Asset, but offers no generic master-data edit/delete UI. A second effective
opening in the same exact scope is rejected. Correct an opening from its receipt
through a separate immutable reversal, then submit a separately reviewed
replacement.

Programmatic callers use the same Application behavior through these Ready-only
JSON adapters:

```text
POST /api/ledger/opening-balances/preview
POST /api/ledger/opening-balances
GET /api/households/{householdId}/ledger/opening-balances/{transactionId}/verification
PUT /api/opening-balance-reference-data/currencies/{currencyCode}
PUT /api/opening-balance-reference-data/institutions/{institutionCode}
PUT /api/households/{householdId}/opening-balance-reference-data/accounts/{accountCode}
PUT /api/opening-balance-reference-data/assets/{assetCode}
```

The posting request requires exactly one `Idempotency-Key` header. JSON
financial values use integer raw E8/minor-unit representations; the human UI
accepts exact localized decimals and performs deliberate checked conversion.
Created responses link to stable transaction and verification readback. See the
[M007 contract](docs/milestones/M007_opening_balance_cutover.md) for request,
validation, conflict, receipt, and lot/fineness details.

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

The verified M007 checkpoint contains 676 passing tests: Domain 98,
Application 156, Infrastructure 195, UI 71, API/UI host 130, Operations 23, and
Playwright browser 3. Run the browser project directly when iterating on UI
journeys:

```powershell
dotnet test tests/WealthLedger.UI.BrowserTests/WealthLedger.UI.BrowserTests.csproj --no-restore --verbosity minimal
```

Check formatting and migration-model alignment:

```powershell
dotnet format WealthLedger.slnx --verify-no-changes --no-restore
dotnet ef migrations has-pending-model-changes --project src/WealthLedger.Infrastructure/WealthLedger.Infrastructure.csproj --startup-project src/WealthLedger.Infrastructure/WealthLedger.Infrastructure.csproj --context WealthLedgerDbContext --no-build
```

Development guidance and non-negotiable invariants are defined in [AGENTS.md](AGENTS.md).
