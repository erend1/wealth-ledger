# ADR-002: Make posted transactions immutable and correct them with reversals

- Status: Accepted
- Decision date: 2026-08-24 context-pack baseline

## Context

Editing or deleting a transaction after it has affected positions and lots destroys the audit trail and can invalidate downstream disposals, transfers, reconciliation, and analysis.

An earlier lifecycle idea treated Reversed as a transaction status. That creates a mathematical trap: excluding the original from queries while also including negative reversal entries corrects the effect twice.

## Decision

A posted LedgerTransaction and its effective child facts cannot be edited or deleted.

Correction is represented by:

1. a new posted Reversal transaction that references the original and contains its effective entries with opposite quantity deltas;
2. when appropriate, a separate corrected transaction.

The original remains Posted. The status vocabulary is Draft, Ordered, Posted, and Cancelled. Reversal is a TransactionType and relationship, not a status.

Only a posted non-reversal transaction may be reversed. One original may have at most one reversal. A reversal is not directly reversed.

A full reversal of an acquisition is permitted only if the lots it created have no later effective allocations. Dependent activity must be corrected in reverse chronological order first.

Database constraints/triggers reinforce aggregate guards so direct SQL or an ORM mistake cannot mutate posted history.

## Consequences

Positive:

- audit history remains complete;
- position math is simply original plus reversal plus correction;
- corrections are timestamped and attributable;
- downstream dependencies can be validated explicitly.

Costs:

- correction workflows create additional rows;
- Application logic must check reversal uniqueness and downstream lot dependencies;
- queries/UI must show reversal relationships clearly.

## Superseded ideas

- Updating or deleting a posted transaction.
- Marking the original Reversed and excluding it from effective-history queries.
- Mutating even apparently harmless posted fields such as Note.
- Reversing an old acquisition without checking later lot use.
