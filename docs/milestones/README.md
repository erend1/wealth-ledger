# WealthLedger Milestone Workflow

Status: Canonical delivery workflow

Last reviewed: 2026-08-24

## Purpose

A milestone is the bounded contract for one coherent delivery change. It turns
product intent into reviewable behavior, scope, invariants, acceptance criteria,
and verification without asking an agent to implement an entire roadmap.

`ROADMAP.md` establishes order. `PROJECT_STATE.md` establishes current verified
reality. A milestone establishes the next bounded change.

## Status lifecycle

```text
Draft -> Proposed -> Accepted -> In Progress -> Verified
                     \-> Rejected
Accepted/In Progress -> Superseded
```

- **Draft**: incomplete working notes.
- **Proposed**: ready for human review; implementation is not authorized by
  status alone.
- **Accepted**: scope and decision gates are approved for implementation.
- **In Progress**: an agent or human is currently implementing it.
- **Verified**: acceptance criteria and required checks pass and
  `PROJECT_STATE.md` reflects reality.
- **Rejected**: intentionally not proceeding.
- **Superseded**: replaced by a later milestone; history remains intact.

Keep at most one milestone In Progress. A direct, explicit user instruction may
authorize work on a Proposed milestone, but the implementing change should make
the resulting acceptance decisions explicit rather than silently guessing.

## Naming

Use:

```text
MNNN_short_descriptive_name.md
```

Milestone numbers describe delivery sequence and are independent from EF Core
migration numbers.

## Required milestone structure

Copy [`TEMPLATE.md`](TEMPLATE.md) when creating a milestone. Every milestone
should contain:

```markdown
# MNNN: Outcome name

Status: Proposed
Owner: Human and agent
Last reviewed: YYYY-MM-DD

## User outcome
## Current evidence
## Why now
## Decisions and decision gates
## In scope
## Out of scope
## Required behavior
## Invariants
## API or UI contract
## Persistence impact
## Acceptance criteria
## Test scenarios
## Verification commands
## Documentation updates
## Suggested commit boundaries
## Risks and rollback
```

Omit a section only when it genuinely does not apply. Unknowns that could change
the implementation remain decision gates; they are not hidden in prose.

## Agent start prompt

After human acceptance, a Zed agent can receive a short prompt:

```text
Implement the accepted milestone described in
docs/milestones/MNNN_short_descriptive_name.md.

Follow AGENTS.md. Read docs/PROJECT_STATE.md and the relevant accepted ADRs
before changing code.

First compare the milestone with the current source and tests. Report any
material conflict before implementation.

Keep all changes strictly within the milestone scope. Preserve existing
accounting invariants and unrelated user changes.

Run focused tests, the full test suite, formatting verification, and the EF
Core model drift check where applicable.

Update docs/PROJECT_STATE.md only after the implementation is verified. Do not
treat docs/history as authoritative and do not modify it.
Use small English conventional commits.
```

For planning-only work:

```text
Do not change code.

Review docs/milestones/MNNN_short_descriptive_name.md against the current
source, tests, PROJECT_STATE, and accepted ADRs.

Identify contradictions, missing decisions, migration risks, and required test
cases. Propose a bounded implementation plan for human approval.
```

## Definition of done

A milestone is Verified only when:

- implemented behavior satisfies every accepted criterion;
- focused tests and the appropriate full suite pass;
- formatting and migration-model checks pass where applicable;
- API/UI contracts and migrations are reviewed for compatibility;
- no real or synthetic-sensitive data was added accidentally;
- backup/restore verification is included when storage changes;
- `PROJECT_STATE.md` describes the new factual checkpoint without becoming a
  changelog;
- a new ADR records any accepted cross-cutting decision;
- unrelated working-tree changes remain untouched;
- commits are small, English, and describe one coherent concern.

## Human review questions

Before changing a milestone to Accepted, ask:

1. Does the user outcome solve a real next problem?
2. Are every in-scope and out-of-scope boundary clear?
3. Does it preserve ledger and lot invariants?
4. Is any product or architecture choice still implicit?
5. Can acceptance be proven by tests or a repeatable operational check?
6. Is recovery possible if a migration or operational change fails?
7. Does the milestone expose or require real household data during development?

## History rule

Never update a Verified milestone to make later behavior look original. Add a
new milestone and, when appropriate, mark the old one Superseded. Conversation
transcripts under `docs/history` are reference material only and never replace
an accepted milestone.
