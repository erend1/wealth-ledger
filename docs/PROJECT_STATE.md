# WealthLedger Project State

As of: 2026-09-03

Status source: verified against the repository, the generated EF model, and local .NET/SQLite test runs.

## Current checkpoint

The Domain v1 baseline, core SQLite persistence, Minimal API boundary,
first-run setup, M002 retry-safe transaction submission/readback, and M003
posted reversal and correction workflow are implemented and verified. M004 now
adds the verified fail-closed local database, ownership, backup, restore,
migration, and loopback-hosting operating boundary. M005 adds verified bounded
master/reference navigation, a recent Posted ledger feed, and valid-versus-
unknown position-scope behavior.

Starting without a database, the explicit operations CLI can initialize the
accepted migration chain and verify the resulting file. The default-off setup
endpoint can then initialize required master data. Supported ledger use cases
record retry-safe contributions and fund purchases, create acquisition lots,
read posted transactions through stable HTTP projections, derive positions from
immutable posted entry history, preview reversal eligibility, and post an exact
retry-safe reversal without editing or deleting the original transaction.
Callers can now discover the current stable identities and labels needed for
those operations without reading SQLite or retaining setup output.

Posted originals remain Posted. Reversals are separate immutable Posted
transactions linked through `ReversalOfTransactionId`; transaction readback
also derives `ReversedByTransactionId` and exposes ordered lot-allocation
effects. Where a corrected replacement is required, it remains a separate
normal submission with its own idempotency identity and no fabricated durable
replacement relationship.

## Delivery planning

Canonical product requirements, delivery roadmap, proposed MVP interaction
model, data-capture guide, security/operations baseline, and milestone workflow
live under `docs`.

[`M002: Retry-Safe Transaction Submission and Readback`](milestones/M002_transaction_submission_and_readback.md)
was accepted on 2026-08-24 and verified on 2026-08-27. ADR-006 records the
decision to keep command retry identity separate from external financial
references.

[`M003: Posted Reversal and Correction Workflow`](milestones/M003_posted_reversal_and_correction.md)
was accepted on 2026-08-28 and verified on 2026-08-31.

[`M004: Safe Local Data Operations`](milestones/M004_safe_local_data_operations.md)
was accepted on 2026-09-01 after the human owner approved all ten Recommended
decisions exactly as written. ADR-007 records the accepted local-data operating
model. M004 was verified on 2026-09-02 after its focused/full suites,
repository-protection checks, and synthetic recovery workflows passed.

[`M005: Master Data and Ledger Navigation`](milestones/M005_master_data_and_ledger_navigation.md)
was accepted and verified on 2026-09-02 after the human owner approved all ten
Recommended decisions exactly as written. It exposes read-only master/reference
pages, a bounded recent Posted ledger feed with current display context, scoped
restart-safe cursors, and sanitized invalid position scopes. No M006 or later
scope was implemented.

[`M006: Local UI Shell and Guided First Run`](milestones/M006_ui_shell_and_guided_first_run.md)
was accepted on 2026-09-03 after the human owner approved all eleven
Recommended decisions, with Decision 4 amended by the workspace-binding gate
found during pre-implementation reconciliation. ADR-008 records the accepted UI
and hosting architecture and the two explicit refinements it makes to ADR-007.
M006 is now In Progress and is the only In Progress milestone.

Its first commit boundary is complete and verified: local protection readiness
now requires a verified backup proved to belong to the configured live
database. The remaining boundaries — the UI assembly, exact value presentation,
fail-closed startup modes, guided first run, the read-only shell, and browser
verification — are not implemented. There is still no UI project, page,
static-asset pipeline, or browser test, and no UI behavior is claimed as
verified.

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
- M002 migration: `20260827072019_002_CommandReceipt`.
- M003 migration: `20260831113310_003_ReversalDependencySemantics`.
- M005 migration: `20260902112549_004_LedgerNavigationQueries`.
- M006 workspace-binding migration: `20260903075104_005_WorkspaceIdentity`.
- Local database ownership is one adjacent cross-process exclusive file lock;
  it is not a distributed or remote multi-writer policy. M002/M003 database
  constraints still arbitrate scoped submission and reversal races.

M002 adds dedicated `CommandReceipt` persistence. M003 is behavior-only: it
replaces the posting-validation trigger while preserving all previous guards
and changes only the acquisition-reversal dependency predicate. M005 adds only
the descending recent-ledger query index; it adds no table or authoritative
financial field.

M002 adds a second schema migration for dedicated `CommandReceipt` persistence.
A receipt is identified by household, stable operation code, and idempotency
key, and records fingerprint algorithm/version/value plus the resulting
transaction identity and optional acquisition-lot identity.

The receipt has restrictive relationships to the resulting ledger transaction
and optional asset lot. Receipt and ledger graph persistence are coordinated
inside one explicit SQLite transaction. Concurrent equivalent submissions are
arbitrated by the database uniqueness invariant rather than by an in-memory
lock.

The migration creates the normalized core master-data, ledger, lot, allocation, cash-flow, cost-component, and physical-gold tables. Financial values use signed integer minor units or signed E8 integers. GUIDs, business dates, UTC timestamps, and enum-like values have explicit SQLite representations.

SQLite foreign keys are enabled on every opened connection. Check constraints, restrictive foreign keys, indexes, and triggers protect household consistency, transaction date ordering, posted aggregate immutability, reversal uniqueness and exact inverse facts, required lot reconciliation, allocation sign and asset consistency, non-negative lot balances, known-versus-unknown cost basis, and physical-gold detail consistency.

The Infrastructure persistence rows remain internal implementation records. No generic repository abstraction has been introduced.

### First vertical slice

Application contains focused commands and ports for:

- recording a positive external contribution with cash-flow classification;
- recording a fund purchase with separate fund principal and cash consideration entries;
- creating the fund acquisition lot with known minor-unit cost basis and an exact opening allocation;
- querying a signed position for one household, portfolio, account, and asset.

The position use case first validates that every requested master exists and
that Portfolio and Account belong to the Household. A valid empty or net-zero
scope retains the existing 200 payload and arithmetic; an unknown or cross-
household scope is a sanitized `POSITION_SCOPE_NOT_FOUND` 404.

Currency-asset quantities are converted deterministically from signed integer minor units to E8 using the persisted currency precision with checked arithmetic. Executed unit price remains a preserved source fact; cash consideration remains a separate entry.

Infrastructure resolves the required master references, maps Domain aggregate roots to internal rows, inserts the complete graph as Draft, and finalizes it as Posted inside one SQLite transaction. A failed final posting rolls back the transaction, entries, lots, and allocations together. The position adapter returns only posted entry facts; Application performs the ordered checked sum.

### Retry-safe submission and transaction readback

Application defines dedicated retry-safe submission contracts rather than
overloading `ExternalReference`. Contribution and fund-purchase commands are
normalized and fingerprinted with explicit versioned canonical forms.

For both supported write commands:

- a new scoped idempotency key posts one ledger graph;
- an equivalent replay returns the previously recorded result;
- a conflicting replay is rejected;
- replay does not re-run current reference-data validation or Domain posting;
- a concurrent loser observes the winning receipt rather than creating
  duplicate history.

Contribution receipts preserve the original transaction identity.
Fund-purchase receipts preserve both the original transaction identity and
acquisition-lot identity.

Infrastructure implements the submission contract with dedicated
`CommandReceipt` persistence and the existing EF Core ledger writer. Real
file-backed SQLite tests cover receipt round trip, rollback, operation-code
scope separation, and concurrent equivalent submissions.

A dedicated read adapter reconstructs a stable transaction-detail projection
from normalized persisted facts. It returns transaction metadata, ordered
entries, optional cash-flow detail, typed cost components, and lots created by
the transaction.

### Minimal API boundary

`WealthLedger.Api` exposes:

- `POST /api/ledger/contributions`;
- `POST /api/ledger/fund-purchases`;
- `GET /api/ledger/transactions/{transactionId}`;
- `GET /api/households` and `GET /api/households/{householdId}`;
- `GET /api/households/{householdId}/members`;
- `GET /api/households/{householdId}/portfolios`;
- `GET /api/households/{householdId}/accounts`;
- `GET /api/institutions`, `GET /api/currencies`, and `GET /api/assets`;
- `GET /api/households/{householdId}/ledger/transactions`;
- `GET /api/households/{householdId}/portfolios/{portfolioId}/accounts/{accountId}/positions/{assetId}`.

Contribution and fund-purchase submissions require a bounded opaque
`Idempotency-Key`. Equivalent replay returns the original stable identities and
transaction Location. Reuse of a scoped key for a different semantic command
returns sanitized 409 Problem Details.

Every transaction Location emitted by the ledger write endpoints resolves
through the transaction-detail GET. Unknown transaction identities return a
sanitized 404.

Transport contracts continue to use signed integer minor units and raw E8
integers. Public enum-like values are mapped to explicit stable text codes
rather than exposing implementation enum names.

### Master and ledger navigation

Application defines explicit page queries, current-display results, use cases,
and narrow master, ledger, and scope read ports. Every collection uses the
same `{ items, nextCursor }` envelope, defaults to 50 items, accepts 1 through
100, and uses an opaque versioned cursor bound to its resource, household, and
active-filter state. Cursor and query-shape validation happens before
persistence access.

Infrastructure uses bounded `AsNoTracking` projections. Members, portfolios,
accounts, transactions, and position scopes are household-safe in SQLite;
Institution, Currency, and Asset remain global under the implemented schema.
Lifecycle-bearing master pages default to active rows and can include inactive,
closed, or archived current state. Account projections preserve nullable or
inactive Institution context.

The recent feed returns Posted transactions ordered by `PostedAtUtc` then
transaction identity, both descending. It selects bounded transaction keys and
loads every ordered entry/master effect in one batched query, so a non-empty
page uses three reader commands including household validation regardless of
item count. Effects retain exact raw E8 quantities, stable identities/codes,
and current Portfolio, Account, nullable Institution, and Asset context. Notes,
costs, cash-flow details, lots, and allocations remain on the existing detail
route and are absent from summaries.

### First-run setup

Application provides one focused initialization command for a base currency, household, optional member, institution, portfolio, account, cash asset, and required-lot fund asset. It constructs the existing Domain master entities, generates their identities, and submits one setup graph through `ICoreLedgerSetupStore`; no generic master-data repository or service layer was introduced.

Infrastructure accepts setup only when all core master tables are empty and inserts the complete graph in one SQLite transaction. Existing master data returns a stable conflict, while a constraint or trigger failure rolls back currency, household, member, institution, portfolio, account, and assets together. The setup uses the existing `001_CoreLedger` schema and required no model or migration change.

`POST /api/setup/core-ledger` is mapped only when `Setup:Enabled` is explicitly
true. Normal API startup no longer supports initialization or migration; those
are explicit operations commands. The setup endpoint returns stable initialized
identities and still emits no `Location`; M005 adds the matching household read
route without changing the verified setup response contract.

### Safe local data operations

Normal storage resolves to the absolute per-user local application-data path
`WealthLedger/data/wealthledger.db`; an advanced override must be absolute and
pass repository/build-root, broad-root, reparse-point, extension, and path-
overlap checks. Design-time tooling uses an explicit synthetic temp path.

`WealthLedger.Operations` exposes exactly seven lifecycle commands: status,
database initialize/migrate, backup create/verify, and restore stage/replace.
Application owns narrow operation use cases. Infrastructure owns canonical path
resolution, the adjacent exclusive ownership lock, SQLite integrity/schema and
representative-read verification, the online backup API, bounded versioned
archives, staging, replacement rollback, and explicit EF migration mechanics.

Normal API hosting validates loopback-only URLs, holds database ownership for
its service lifetime, and fails closed with sanitized guidance when the file is
missing, corrupt, incompatible, or needs migration. It exposes no lifecycle,
filesystem, or SQL HTTP route.

Every immutable `.wlbackup` contains one standalone SQLite snapshot and one
privacy-safe versioned manifest with SHA-256 corruption evidence. Packages are
plaintext at the application layer. Backup creation, pending migration, and
active replacement require a separate configured backup directory. The
separation and encryption acknowledgements determine real-data readiness; they
do not replace the operator's verification of those protections or gate the
local lifecycle command itself. Isolated restore never overwrites a target;
confirmed active replacement first creates a verified pre-restore package,
preserves the superseded database, and rolls a failed promotion back.

Migration `20260903075104_005_WorkspaceIdentity` adds a durable random
workspace identity to the database file, and protection readiness now requires
a verified package proved to carry that same identity. The identity survives
backup, isolated restore, active replacement, and a change of live path; it is
outside the EF model, referenced by no ledger row, and read with a bounded
direct query. A package predating the migration remains valid and restorable
but cannot prove its origin and is not protection, so one new backup is
required after upgrading. See the M006 Decision 4 amendment.

### Posted reversal and correction

Application exposes a read-only eligibility preview and a retry-safe reversal
command using stable operation code `REVERSE_POSTED_TRANSACTION`. The command
normalizes a required reversal reason, fingerprints the original transaction
identity plus normalized reason, and resolves an existing scoped receipt before
current eligibility. A second receipt lookup after candidate reconstruction
preserves equivalent same-key replay when another writer commits during that
multi-query read window.

Infrastructure reconstructs posted Domain transaction and lot aggregates from
persisted identities, derives deterministic dependency blockers, mirrors
reversal allocations onto existing lots, and persists the reversal graph,
receipt, and final Posted transition atomically. Database uniqueness remains
the final arbiter for concurrent reversal submissions. Same-key equivalent
writers converge on one receipt and reversal identity; different-key writers
produce one winner and a sanitized already-reversed result containing that
winner identity.

Migration `20260831113310_003_ReversalDependencySemantics` updates the posting
trigger without changing historical migration `001_CoreLedger`. A downstream
Posted non-reversal transaction remains an acquisition-reversal blocker only
while it lacks its own Posted reversal. Draft or Cancelled dependent reversals
do not unblock it, and unrelated quantity netting does not substitute for
reversal lineage.

The API additionally exposes:

- `GET /api/ledger/transactions/{transactionId}/reversal-preview`;
- `POST /api/ledger/transactions/{transactionId}/reversals`.

The existing transaction-detail GET now includes
`ReversalOfTransactionId`, derived `ReversedByTransactionId`, and ordered
lot-allocation effects so both sides of a reversal remain inspectable after a
fresh database context or process restart.

## Verification

Last verified commands:

```text
dotnet test WealthLedger.slnx --no-restore --verbosity minimal
dotnet format WealthLedger.slnx --verify-no-changes --no-restore --verbosity minimal
dotnet ef migrations has-pending-model-changes --project src/WealthLedger.Infrastructure/WealthLedger.Infrastructure.csproj --startup-project src/WealthLedger.Infrastructure/WealthLedger.Infrastructure.csproj --context WealthLedgerDbContext --no-build
```

Results:

- Domain tests: 83 passed, 0 failed.
- Application tests: 95 passed, 0 failed.
- Infrastructure tests against real SQLite files: 156 passed, 0 failed.
- API tests against real SQLite files: 71 passed, 0 failed.
- Operations process/contract tests: 23 passed, 0 failed.
- Total: 428 passed, 0 failed.
- Formatting drift: no committable content diff; see the SDK line-ending caveat
  below.
- EF model drift: none.

On the Windows .NET 10.0.400 SDK, a fresh LF checkout currently makes
`dotnet format --verify-no-changes` report comment-adjacent whitespace at
`LedgerTransaction.cs` lines 469, 471, and 472. Applying the formatter only
rewrites that file's raw line endings to CRLF; Git normalizes it back to the
same LF blob under the repository attributes. This tooling/configuration
discrepancy remains open and is not introduced by M005.

M004 focused verification passed 9 local-data Application tests, 42 backup-
related Infrastructure tests, 20 restore-related Infrastructure tests, 28
local-hosting API tests, all 23 Operations tests, and the Domain dependency
guard. The documented synthetic process workflows passed initialize, status,
backup create/verify, isolated restore and restart validation, mandatory
verified pre-migration backup, and confirmed active replacement with preserved
recovery evidence. Artifact ignore checks passed for all 15 representative
extensions, Git tracks none, and no generated local-data artifact exists in the
implementation worktree.

A focused regression additionally proves that a valid M001 database is
classified as `MigrationRequired`, receives an independently verified
pre-migration package, migrates through M002, M003, and the M005 index
migration, and preserves its synthetic data both in the upgraded live file and
an isolated restore of the M001 package. Representative table checks are
limited to tables introduced by the applied migration prefix.

M005 focused verification passed 16 Application navigation/position-scope
tests, 8 real-SQLite navigation/position-scope/query-plan tests, and 5 API
navigation tests. The exact pre-migration production query plan used
`IX_LedgerTransaction_Household_Status_Date` and a temporary B-tree for its
posting-time order; migration 004 changes that plan to
`IX_LedgerTransaction_Household_Status_Posted_Id` with no temporary sort. The
non-empty recent feed remains fixed at three reader commands for page sizes one
and two. A dedicated synthetic M003-to-M005 recovery test verifies the
pre-migration package, explicit migration, live integrity, data preservation,
and an isolated restore of the old schema.

The M006 workspace-binding correction adds 11 real-SQLite Infrastructure tests.
They prove that independently initialized databases receive distinct
identities, that an unrelated workspace's package is never protection, that a
newer unrelated package never displaces an older matching one, that a forged or
stripped manifest lineage is rejected against the snapshot, that a package
predating lineage stays valid but unknown, that a migrated database does not
accept its own pre-migration package until one new backup is taken, that the
identity survives isolated restore and a fresh verifier, and that confirmed
active replacement rebinds the live database to the promoted lineage. The
existing local-data, migration, restore, and operations suites pass unchanged
apart from the migration-chain head moving to 005.

The M003 suite proves exact Domain reversal and reconstitution, normalized
reason and deterministic fingerprinting, receipt-first replay, generic
eligibility preview, same-lot inverse allocation, atomic SQLite persistence and
rollback, neutralized downstream dependencies, direct-SQL trigger enforcement,
same-key and different-key concurrency, dependency races introduced after
eligibility evaluation, restart readback, exact position/allocation netting,
stable sanitized HTTP errors, and a separate corrected-replacement flow.

The integration suite proves fixed-point and stable-code round trips, GUID/date/timestamp storage, foreign-key enforcement, posted graph immutability through EF and direct SQL, reversal behavior and dependency protection, effective lot balances that exclude drafts, acquisition-lineage and allocation invariants, cost-basis shape, transaction ordering, setup and posting rollback, and an HTTP setup/contribution/purchase/position round trip without authoritative balance tables. API tests also prove default-off setup gating, fail-closed loopback startup without automatic migration, repeat-setup conflict, transport-code validation, semantic rule mapping, sanitized persistence failures, and isolated SQLite databases under parallel execution.

A planning-only M006 audit on 2026-08-31 originally inspected the stacked
M004/M005 proposal branch, then-current source, local .NET 10 templates, and
official ASP.NET Core UI/lifetime guidance. Its inherited pre-M003 suite passed
all 163 tests and the EF model-drift check. After rebasing the proposal onto the
verified M003 baseline and merged M004/M005 planning documents, that planning
checkpoint had 243 passing tests with no EF model drift; the same formatter
caveat remains.

There is still no UI project, page, static-asset pipeline, or browser test.
M006 remains Proposed and claims no UI behavior as implemented; its M003-M005
ordering prerequisites are now verified, but its decision gates and ADR remain
unaccepted.

## Next delivery candidate

M006 is In Progress and is the active delivery. Its eleven decisions were
accepted on 2026-09-03 and ADR-008 records the resulting architecture. The
workspace-binding prerequisite is verified; the UI assembly, presentation
formatters, startup modes, guided first run, read-only shell, and browser
verification remain to be delivered under that accepted contract.

M007 remains the next candidate after it.

Do not start live market data, provider-specific integration, optimization, AI/LLM integration, broad UI work, materialized analytics, microservices, messaging, caching, or CQRS infrastructure without a new accepted milestone need.

## Open decisions

- Market/reference-data schemas and provider contracts.
- Authentication, authorization, and transport security. The household intends
  to move the API to a private home server so the UI can be reached from other
  devices. ADR-008 keeps normal operation loopback-only; that move requires its
  own accepted milestone and ADR before any non-loopback binding.
- Application-level database or package encryption beyond M004's accepted
  OS/device-encryption reliance remains deferred to a separate ADR.
- Partial cost-basis rounding allocation if a concrete use case exposes a gap.
- SQLite concurrency beyond M004's single-machine ownership boundary and the
  scoped database collisions handled by M002/M003.

Record a new ADR only when one of these or another cross-cutting architectural choice is accepted or superseded.
