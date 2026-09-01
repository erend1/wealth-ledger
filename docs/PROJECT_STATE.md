# WealthLedger Project State

As of: 2026-09-01

Status source: verified against the repository, the generated EF model, and local .NET/SQLite test runs.

## Current checkpoint

The Domain v1 baseline, core SQLite persistence, Minimal API boundary,
first-run setup, M002 retry-safe transaction submission/readback, and M003
posted reversal and correction workflow are implemented and verified.

Starting from an empty SQLite file, the supported synthetic-data path can apply
migrations when explicitly enabled, initialize required master data, record
retry-safe contributions and fund purchases, create acquisition lots, read
posted transactions through stable HTTP projections, derive positions from
immutable posted entry history, preview reversal eligibility, and post an exact
retry-safe reversal without editing or deleting the original transaction.

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
model. M004 is now the one In Progress milestone; its implementation remains
unverified until the complete acceptance and recovery workflow passes. M003's
verification satisfies M004's delivery-order prerequisite.

[`M005: Master Data and Ledger Navigation`](milestones/M005_master_data_and_ledger_navigation.md)
is also Proposed for parallel planning and human review only. It cannot become
In Progress until M003 and M004 are Verified. The proposal defines read-only
master/reference pages, a bounded recent Posted ledger feed, current-label
semantics, cursor pagination, and valid-versus-unknown point-position scopes;
none of those routes or behaviors is currently implemented.

[`M006: Local UI Shell and Guided First Run`](milestones/M006_ui_shell_and_guided_first_run.md)
is Proposed for parallel planning and human review only. It cannot become In
Progress until M003-M005 are Verified and its accepted architecture is recorded
in an ADR. The proposal recommends one Razor Pages UI assembly in the existing
loopback host, fail-closed readiness modes, a bounded first-run flow, exact
human value presentation, and Today/Ledger/Settings read pages; no UI project
or behavior is currently implemented.

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
- SQLite concurrency policy outside the scoped idempotent submission and
  reversal collision handling verified by M002 and M003.

M002 adds dedicated `CommandReceipt` persistence. M003 is behavior-only: it
replaces the posting-validation trigger while preserving all previous guards
and changes only the acquisition-reversal dependency predicate.

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

### First-run setup

Application provides one focused initialization command for a base currency, household, optional member, institution, portfolio, account, cash asset, and required-lot fund asset. It constructs the existing Domain master entities, generates their identities, and submits one setup graph through `ICoreLedgerSetupStore`; no generic master-data repository or service layer was introduced.

Infrastructure accepts setup only when all core master tables are empty and inserts the complete graph in one SQLite transaction. Existing master data returns a stable conflict, while a constraint or trigger failure rolls back currency, household, member, institution, portfolio, account, and assets together. The setup uses the existing `001_CoreLedger` schema and required no model or migration change.

`POST /api/setup/core-ledger` is mapped only when `Setup:Enabled` is explicitly true. `Database:ApplyMigrationsOnStartup` is also false by default and applies migrations only when explicitly enabled. The setup endpoint returns stable initialized identities but deliberately emits
no `Location` until a matching household read endpoint exists.

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

- Domain tests: 82 passed, 0 failed.
- Application tests: 70 passed, 0 failed.
- Infrastructure tests against real SQLite files: 53 passed, 0 failed.
- API tests against real SQLite files: 38 passed, 0 failed.
- Total: 243 passed, 0 failed.
- Formatting drift: no committable content diff; see the SDK line-ending caveat
  below.
- EF model drift: none.

On the Windows .NET 10.0.400 SDK, a fresh LF checkout currently makes
`dotnet format --verify-no-changes` report comment-adjacent whitespace at
`LedgerTransaction.cs` lines 469, 471, and 472. Applying the formatter only
rewrites that file's raw line endings to CRLF; Git normalizes it back to the
same LF blob under the repository attributes. This tooling/configuration
discrepancy remains open and is not introduced by the planning documents.

The M003 suite proves exact Domain reversal and reconstitution, normalized
reason and deterministic fingerprinting, receipt-first replay, generic
eligibility preview, same-lot inverse allocation, atomic SQLite persistence and
rollback, neutralized downstream dependencies, direct-SQL trigger enforcement,
same-key and different-key concurrency, dependency races introduced after
eligibility evaluation, restart readback, exact position/allocation netting,
stable sanitized HTTP errors, and a separate corrected-replacement flow.

The integration suite proves fixed-point and stable-code round trips, GUID/date/timestamp storage, foreign-key enforcement, posted graph immutability through EF and direct SQL, reversal behavior and dependency protection, effective lot balances that exclude drafts, acquisition-lineage and allocation invariants, cost-basis shape, transaction ordering, setup and posting rollback, and an HTTP setup/contribution/purchase/position round trip without authoritative balance tables. API tests also prove default-off setup gating, opt-in migration, repeat-setup conflict, transport-code validation, semantic rule mapping, sanitized persistence failures, and isolated SQLite databases under parallel execution.

A planning-only audit for the Proposed M004 document on 2026-08-31 originally
ran against the pre-M003 baseline: all 163 tests passed and the EF model-drift
check passed. The proposal changes no source file. After M003 verification and
this planning branch's rebase, the verified baseline is 243 passing tests with
no EF model drift; the line-ending-only formatter caveat above remains.

A planning-only M006 audit on 2026-08-31 originally inspected the stacked
M004/M005 proposal branch, then-current source, local .NET 10 templates, and
official ASP.NET Core UI/lifetime guidance. Its inherited pre-M003 suite passed
all 163 tests and the EF model-drift check. After rebasing the proposal onto the
verified M003 baseline and merged M004/M005 planning documents, the baseline is
243 passing tests with no EF model drift; the same formatter caveat remains.
There is still no UI project, page, static-asset pipeline, or browser test.
M006 changes documentation only and does not claim M004, M005, or any UI
behavior as implemented.

## Next delivery candidate

M004 is the active In Progress coherent slice: safe local data operations.

Its roadmap outcome is explicit local database location, source-control
exclusions, backup, restore verification, and local exposure policy. M004
records its accepted operations and encryption boundary in ADR-007; no M004
runtime behavior is claimed by the verified M003 checkpoint.

## Accepted M003 delivery outline

After write safety and readback, the next coherent ledger feature applies the
reversal rules already accepted by the Domain and ADRs:

1. Add focused preview and command use cases that load one immutable original,
   replay an existing receipt first, and validate reversal/dependency state.
2. Create the Domain reversal and mirror original allocations on their existing
   lots without changing the original transaction's Posted status.
3. Persist reversal, allocations, and receipt atomically, including explicit
   recovery from reversal-uniqueness races.
4. Align the posting trigger with neutralized downstream reversal pairs through
   a new migration rather than modifying `001_CoreLedger`.
5. Expose preview, command, reverse navigation, and allocation readback through
   sanitized HTTP contracts and prove them across Application, SQLite, restart,
   concurrency, and API tests.

Do not start live market data, provider-specific integration, optimization, AI/LLM integration, broad UI work, materialized analytics, microservices, messaging, caching, or CQRS infrastructure without a new accepted milestone need.

## Open decisions

- The Proposed M006 UI framework, hosting, readiness, interaction, and exact-
  presentation choices remain subject to human acceptance and an ADR.
- Market/reference-data schemas and provider contracts.
- Authentication and authorization.
- Application-level database or package encryption beyond M004's accepted
  OS/device-encryption reliance remains deferred to a separate ADR.
- Partial cost-basis rounding allocation if a concrete use case exposes a gap.
- SQLite concurrency policy outside the scoped idempotent-submission collision
  handled by M002.

Record a new ADR only when one of these or another cross-cutting architectural choice is accepted or superseded.
