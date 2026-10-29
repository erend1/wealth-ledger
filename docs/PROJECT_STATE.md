# WealthLedger Project State

As of: 2026-09-16

Verified against: dd3ad33

Status source: verified against the repository, the generated EF model, local
.NET/SQLite test runs, real-process lifecycle smoke tests, and local Chromium
journeys over synthetic isolated data.

## Current checkpoint

The Domain v1 baseline, core SQLite persistence, Minimal API boundary,
first-run setup, M002 retry-safe transaction submission/readback, and M003
posted reversal and correction workflow are implemented and verified. M004 now
adds the verified fail-closed local database, ownership, backup, restore,
migration, and loopback-hosting operating boundary. M005 adds verified bounded
master/reference navigation, a recent Posted ledger feed, and valid-versus-
unknown position-scope behavior. M006 adds the verified local Razor Pages shell,
fail-closed guided first run, exact Turkish-first presentation, and critical
real-browser coverage. M007 adds the verified controlled opening-balance
cutover for cash, foreign currency, funds, equities, and physical-gold lots,
including persisted verification and bounded immutable correction. M008 adds
the verified complete investment-fund lifecycle: reviewed contribution, fund
purchase and fund sale workflows, separate fund and cash accounts, exact cost
treatment, custody-scoped FIFO, stale-plan protection, completeness-aware derived
realized cost, and a reviewed correction path through immutable reversal.
M009 adds the verified complete physical-gold lifecycle: reviewed cash purchase
into physical custody, exact gross/fineness/piece evidence, explicitly selected-
lot sale, same-lineage custody transfer, completeness-aware realized cost,
persisted verification/receipts, and exact immutable correction.

Starting without a database, the explicit operations CLI can initialize the
accepted migration chain and verify the resulting file. The default-off setup
endpoint can then initialize required master data. Supported ledger use cases
record retry-safe contributions, Fund purchases and sales, physical-gold
purchases and explicitly selected sales, exact gold custody transfers, and one-
scope opening balances. They create, consume, and move acquisition lots, read
posted transactions through stable HTTP projections, derive positions from
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

The M009 checkpoint suite results follow. `CHANGELOG.md` records the commands
and the full verification evidence:

- Domain tests: 193 passed, 0 failed.
- Application tests: 226 passed, 0 failed.
- Infrastructure tests against real SQLite files: 245 passed, 0 failed.
- UI presentation/contract tests: 81 passed, 0 failed.
- API/UI host tests against real SQLite files: 159 passed, 0 failed.
- Operations process/contract tests: 23 passed, 0 failed.
- Playwright Chromium browser tests: 3 passed, 0 failed.
- Total: 930 passed, 0 failed, 0 skipped.
- Formatting drift: no M009 drift; the check reports only the three documented
  pre-existing `LedgerTransaction.cs` findings described in `CHANGELOG.md`.
- EF model drift: none. The migration chain contains eight migrations and ends
  at `008_PhysicalGoldLifecycle`.

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
M006 was verified on 2026-09-08 after its accessibility/privacy checks, real-
browser critical journeys, complete repository verification, and disposable
M004 recovery smoke passed.

[`M007: Controlled Opening-Balance Cutover`](milestones/M007_opening_balance_cutover.md)
was accepted on 2026-09-10 after the human owners approved all fifteen
Recommended decisions exactly as written. M007 was verified on 2026-09-11 after
its Domain, Application, SQLite, API, Razor UI, real-browser, privacy, backup,
and restore evidence passed.

[`M008: Complete Investment-Fund Lifecycle`](milestones/M008_complete_investment_fund_lifecycle.md)
was accepted on 2026-09-12 after the human owner approved all eighteen
Recommended decisions and ADR-009, with four technical amendments and three
explicit resolutions recorded in the milestone's acceptance record. ADR-009
records the accepted deterministic realized-cost method. M008 was verified on
2026-09-15 after its Domain, Application, real-SQLite, API, Razor UI, real-
browser, privacy, migration, backup, and restore evidence passed, including
direct-SQL guard refusals and a real concurrent-sale race. M008 remains
Verified.

[`M009: Complete Physical-Gold Lifecycle`](milestones/M009_complete_physical_gold_lifecycle.md)
was accepted on 2026-09-14 after the human owners approved all twenty
Recommended decisions without amendment. ADR-010 records signed allocation-
level piece movement and the physical-gold extension of ADR-009 cumulative
realized-cost apportionment. M009 was verified on 2026-09-16 after its Domain,
Application, real-SQLite, API, Razor UI, real-browser, privacy, migration,
concurrency, backup, restore, and fresh-process readback evidence passed. M009
remains Verified while M010 is implemented.

[`M010: Searchable Inventory, Evidence, and Reconciliation`](milestones/M010_searchable_inventory_evidence_reconciliation.md)
was accepted on 2026-10-02 after the human owners approved all twenty-two
Recommended decisions without amendment. M010 became the sole In Progress
milestone on 2026-10-02, when #20 merged via PR #27. #21 merged via PR #28,
#22 via PR #32, #23 via PR #34, #24 via PR #36 and #25 via PR #39; parent
issue #19 tracks the open child #26. This adds no M010 Verified behavior;
implementation evidence will be recorded only after it is proved.

The verified M006 delivery includes workspace-bound protection readiness, the
`WealthLedger.UI` Razor Class Library with exact Turkish-first presentation, the
fail-closed startup-mode boundary, guided browser initialization for storage,
workspace setup and the required initial backup, and the Ready shell with its
complete read-only transaction explanation. Semantic landmarks, labels,
validation focus, keyboard focus, skip navigation, reduced-motion handling,
forced-color support, and responsive reflow have focused automated coverage;
this is not a claim of general WCAG conformance.

`InitialBackupRequired` maps the initial-backup review/action and completion
pages. The running setup process remains in that static mode after backup
creation and clearly requires one clean restart. `Ready` maps Today, Ledger
with direct transaction explanation, read-only Settings, and the dedicated
M007-M009 opening, Fund, and physical-gold review/receipt/reversal pages and
JSON adapters. The real-browser suite covers the complete restart-delimited
first run, Ready navigation, opening cutover, Fund trades, and the physical-
gold purchase/sale/transfer/correction lifecycle; JavaScript-disabled
operation, keyboard-only paths, narrow and desktop viewports, external-request
rejection, and process/file cleanup.

## Migrations

- Initial migration: `20260824074930_001_CoreLedger`.
- M002 migration: `20260827072019_002_CommandReceipt`.
- M003 migration: `20260831113310_003_ReversalDependencySemantics`.
- M005 migration: `20260902112549_004_LedgerNavigationQueries`.
- M006 workspace-binding migration: `20260903075104_005_WorkspaceIdentity`.
- M007 migration: `20260910101810_006_OpeningBalanceCutoverGuards`.
- M008 migration: `20260913054039_007_FundTradeLifecycleGuards`.
- M009 migration: `20260915082550_008_PhysicalGoldLifecycle`.
- M010 migrations (In Progress, not yet verified):
  `20261028065838_009_EvidencePersistence` and
  `20261028082512_010_ReconciliationPersistence`.

## Next delivery candidate

M009 is Verified. Its accepted contract and ADR-010 are implemented through
the exact piece-movement Domain model, reviewed Application workflows,
migration 008 asset-family dispatch and guards, API and Razor boundaries, three
real-browser journeys, and disposable backup/restore verification recorded
in `CHANGELOG.md`.

M010 is the sole In Progress delivery milestone under parent issue #19. Its
accepted contract bounds transaction search, position/lot inventory, evidence,
and reconciliation; no later milestone has started.

Do not start live market data, provider-specific integration, optimization, AI/LLM integration, broad UI work, materialized analytics, microservices, messaging, caching, or CQRS infrastructure without a new accepted milestone need.

## Open decisions

- Market/reference-data schemas and provider contracts.
- Authentication, authorization, and transport security. The household intends
  to move the API to a private home server so the UI can be reached from other
  devices. ADR-008 keeps normal operation loopback-only; that move requires its
  own accepted milestone and ADR before any non-loopback binding.
- Application-level database or package encryption beyond M004's accepted
  OS/device-encryption reliance remains deferred to a separate ADR.
- SQLite concurrency beyond M004's single-machine ownership boundary and the
  scoped database collisions handled through M009.

Record a new ADR only when one of these or another cross-cutting architectural choice is accepted or superseded.
