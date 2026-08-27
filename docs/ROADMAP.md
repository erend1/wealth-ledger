# WealthLedger Delivery Roadmap

Status: Canonical delivery intent

Last reviewed: 2026-08-27

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
| M003 | Planned | Posted reversal and correction workflow through Application, SQLite, and HTTP | M002 readback and duplicate-write safety |
| M004 | Planned | Safe local data operations: explicit data location, source-control exclusions, backup, restore verification, and local exposure policy | Accepted operations and encryption decisions where needed |
| M005 | Planned | Master-data and ledger navigation queries with stable, human-oriented contracts | M002 transaction read model |
| M006 | Planned | UI architecture decision, application shell, first-run experience, and formatted value components | Accepted UI ADR; M004 local safety |
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

[`M002_transaction_submission_and_readback.md`](milestones/M002_transaction_submission_and_readback.md)
was accepted on 2026-08-24 and verified on 2026-08-27.

M002 separates client retry identity from provider/source references, persists
submission receipts atomically with ledger results, protects contribution and
fund-purchase writes from equivalent retry duplication, and makes transaction
Locations resolvable through a stable read model.

M003 is the next planned candidate. It introduces the posted
reversal/correction workflow required before broader real-history entry and
must be explicitly reviewed and accepted before implementation starts.

No later roadmap item should be implemented merely because it appears in this
file.

## Real-data readiness gate

Do not treat WealthLedger as the sole record of real household assets until all
of the following are verified:

- duplicate submissions cannot create duplicate transactions — verified by M002;
- every posted transaction can be read back and inspected — verified for the
  currently supported contribution and fund-purchase workflows by M002;
- posted mistakes can be reversed through the supported workflow;
- the live database is outside the repository and ignored by source control;
- backup and restore have a user-visible, tested workflow;
- setup and migration switches are safe for normal startup;
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
| Local database directory, backup format, and encryption-at-rest policy | M004 | ADR and operations documentation |
| UI framework and hosting model | M006 | ADR |
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
