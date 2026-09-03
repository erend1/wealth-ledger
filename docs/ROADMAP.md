# WealthLedger Delivery Roadmap

Status: Canonical delivery intent

Last reviewed: 2026-09-02

## How to read this roadmap

This document records delivery order and outcome boundaries. It is not evidence
that a feature exists. `PROJECT_STATE.md`, source code, migrations, and passing
tests establish implemented reality.

A roadmap item does not authorize code changes by itself. Implementation is
governed by one bounded milestone document in `docs/milestones` and an explicit
human request or acceptance.

Statuses used here:

- **Verified**: present in source and verified by the repository's required
  checks.
- **Accepted**: the bounded milestone contract has human approval and may be
  implemented, but is not yet verified behavior.
- **Proposed**: specified for review but not yet accepted for implementation.
- **Planned**: ordered direction without a complete implementation contract.
- **Deferred**: intentionally outside the current delivery horizon.

## Delivery principles

1. Protect ledger correctness before adding analytical breadth.
2. Make writes retry-safe and facts readable before adding more write paths.
3. Establish backup and restore before importing real household history.
4. Deliver vertical user outcomes rather than horizontal framework layers.
5. Keep UI, provider, and agent integrations outside the core Domain.
6. Introduce one new architectural decision at a time and record it with an ADR
   only after acceptance.
7. Keep at most one milestone In Progress.

## Milestone sequence

| ID | Status | Outcome | Important dependency or gate |
|---|---|---|---|
| M001 | Verified | Core immutable ledger, fixed-point persistence, setup, contribution, fund purchase, lot creation, and one position query | Existing checkpoint in `PROJECT_STATE.md` |
| M002 | Verified | Retry-safe transaction submission and resolvable transaction readback | ADR-006; verified 2026-08-27 |
| M003 | Verified | [Posted reversal and correction workflow](milestones/M003_posted_reversal_and_correction.md) through Application, SQLite, and HTTP | Accepted 2026-08-28; verified 2026-08-31 |
| M004 | Verified | [Safe local data operations](milestones/M004_safe_local_data_operations.md): explicit data location, source-control exclusions, backup, restore verification, migration safety, and local exposure policy | Ten decision gates accepted 2026-09-01 and recorded by ADR-007; verified 2026-09-02 |
| M005 | Verified | [Master-data and ledger navigation](milestones/M005_master_data_and_ledger_navigation.md) with stable human-oriented pages, a recent Posted feed, and valid position scopes | Ten decision gates accepted and implementation verified 2026-09-02 |
| M006 | Proposed | [Local UI shell and guided first run](milestones/M006_ui_shell_and_guided_first_run.md) with fail-closed startup modes, exact value presentation, and browser verification | M003-M005 are Verified; amended Decision 4 accepted and its workspace-binding prerequisite verified on 2026-09-03; the remaining ten decision gates and the UI ADR are still required |
| M007 | Planned | Opening-balance cutover for cash, funds, equities, and physical-gold lots | M003 correction; M005 navigation; M006 shell |
| M008 | Planned | Complete investment-fund lifecycle, including fees, taxes, sale, FIFO allocation, and realized cost | Opening lots and correction path |
| M009 | Planned | Complete physical-gold lifecycle, including weight, fineness, pieces, making-charge treatment, custody, purchase, transfer, and sale | Opening lots and correction path |
| M010 | Planned | Transaction search, position inventory, reconciliation, and evidence capture | Core entry workflows |
| M011 | Planned | Market/reference observations, dated valuation, freshness, and source provenance | Accepted schema/provider boundary ADR if cross-cutting |
| M012 | Planned | Goal, reserve, allocation policy, deterministic performance, and monthly review | Reliable ledger and valuation data |
| M013 | Planned | Decision journal and governed read models for agent-assisted analysis | Stable deterministic analytical contracts |

Milestone identifiers express delivery order, not database migration numbers.
One milestone may produce no migration, and a migration name must describe its
own schema change.

## Current verified milestone and next candidate

[`M003_posted_reversal_and_correction.md`](milestones/M003_posted_reversal_and_correction.md)
was accepted on 2026-08-28 and verified on 2026-08-31.

M003 provides generic reversal eligibility preview, retry-safe exact reversal
submission, same-lot inverse allocation, bidirectional transaction readback,
and neutralized acquisition-dependency semantics. Equivalent concurrent
same-key submissions converge on the winning receipt and reversal identity;
different-key races create one reversal and return the winning identity as a
sanitized conflict. Corrected replacement remains a separate normal submission
without a structured replacement relationship.

[`M004_safe_local_data_operations.md`](milestones/M004_safe_local_data_operations.md)
was accepted on 2026-09-01 and verified on 2026-09-02. Its fail-closed local
operating model now covers the authoritative path, seven-command operations
surface, exclusive process ownership, consistent versioned backup and
verification, isolated and confirmed active restore, protected explicit
migration, loopback exposure, and the plaintext-package/external-encryption
boundary recorded by ADR-007.

[`M005_master_data_and_ledger_navigation.md`](milestones/M005_master_data_and_ledger_navigation.md)
was accepted and verified on 2026-09-02. It provides bounded master/reference
pages, a cursor-based recent Posted transaction feed with current display
context, and the distinction between a genuine zero position and an unknown or
cross-household scope. The production SQLite plan uses the dedicated posting-
time navigation index. Broad transaction search, position/lot inventory, and
reconciliation remain in M010.

[`M006_ui_shell_and_guided_first_run.md`](milestones/M006_ui_shell_and_guided_first_run.md)
is now the next Proposed candidate for planning and human review. It recommends
a Turkish-first,
server-rendered Razor Pages UI in the existing single loopback host, a
fail-closed blocked/setup/ready startup model, a bounded first-run wizard,
exact fixed-point presentation, and a small Today/Ledger/Settings shell. It
deliberately leaves opening positions and ordinary transaction-entry forms to
their ordered milestones.

Pre-implementation reconciliation on 2026-09-03 found that its Decision 4
readiness gate could not be satisfied by the verified M004 status contract,
which accepted any valid package in the configured backup directory as
protection. The human owner accepted the amended decision and authorized the
workspace-binding correction ahead of the UI; migration 005 and the bound
readiness contract are implemented and verified.

The rest of M006 must not become In Progress until its remaining ten decisions
are accepted and the accepted UI architecture is recorded in the next available
ADR.

No later roadmap item should be implemented merely because it appears in this
file.

## Real-data readiness gate

Do not treat WealthLedger as the sole record of real household assets until all
of the following are verified:

- duplicate submissions cannot create duplicate transactions — verified by M002;
- every posted transaction can be read back and inspected — verified for the
  currently supported contribution, fund-purchase, and reversal workflows by
  M002 and M003;
- posted mistakes can be reversed through the supported workflow — verified by
  M003;
- the live database is outside the repository and ignored by source control —
  verified by M004;
- backup and restore have a user-visible, tested workflow — verified locally by
  M004, while off-device protection and recurring drills remain operator duties;
- setup is default-off and normal startup cannot initialize or migrate —
  verified by M004;
- logs, errors, exports, and screenshots do not expose avoidable private data;
- the user can reconcile an imported or entered position with independent
  evidence.

This gate does not block development with synthetic test data.

## First usable product slice

The first user-operable release spans M002 through M009. It should support this
complete path:

1. Create or select protected local storage.
2. Initialize master data through a guided setup.
3. Verify a backup destination.
4. Import opening cash, fund, and physical-gold positions.
5. Record a contribution.
6. Record a fund or physical-gold purchase with all relevant costs.
7. Inspect the resulting transaction, lot, and position.
8. Correct a mistake through reversal and replacement.
9. Close the application and recover the same state after restart.

Analytics that cannot yet satisfy this path must not delay it.

## Decision gates

The following choices require explicit acceptance before their dependent
milestone becomes Accepted:

| Decision | Needed by | Expected record |
|---|---|---|
| Dedicated idempotency identity versus reusing ExternalReference | M002 | Resolved by ADR-006 |
| Separate reversal/replacement commands, required reason, structured replacement-link boundary, neutralized lot dependencies, and preview contract | M003 | Resolved by accepted M003 on 2026-08-28; new ADR only if a durable replacement relationship or another cross-cutting rule changes |
| Local database directory, operations surface, backup/restore format, local exposure, migration, and encryption-at-rest policy | M004 | Resolved by accepted M004 on 2026-09-01 and ADR-007 |
| Master projection fields, current-label semantics, cursor contract, recent-ledger boundary, and invalid position-scope behavior | M005 | Resolved by accepted M005 on 2026-09-02; no ADR was required |
| UI framework, single-host topology, readiness modes, direct Application boundary, exact presentation, and browser verification | M006 | Proposed M006 decision gates; next available ADR after human acceptance |
| Market/reference data schema and provider contracts | M011 | ADR when a provider-independent boundary is accepted |
| Performance methodologies and partial-cost rounding when exposed by a real use case | M008/M012 | Tests and ADR if cross-cutting |
| Agent read-contract and human approval boundary | M013 | ADR |

## Deferred capabilities

The following are not current roadmap commitments:

- automatic order execution;
- cloud multi-tenancy;
- social or shared portfolio features;
- real-time streaming market feeds;
- autonomous agent posting;
- microservices, messaging infrastructure, or distributed caching;
- tax filing or legally authoritative reporting.

They require a demonstrated product need and a new accepted milestone.

## Roadmap change protocol

When priorities change:

1. Preserve the verified checkpoint in `PROJECT_STATE.md`.
2. Edit this roadmap to reflect the newly accepted delivery order.
3. Create or revise only Proposed milestone documents; do not rewrite Verified
   milestone history.
4. Record an ADR only when a cross-cutting architectural decision is accepted.
5. Move a milestone to Verified only after code, tests, migrations, operational
   checks, and documentation agree.
