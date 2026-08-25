# ADR-006: Separate command idempotency from external financial references

- Status: Accepted
- Decision date: 2026-08-24

## Context

WealthLedger posts immutable financial history. An HTTP response can be lost or
delayed after a valid command commits, and a user, UI, script, or agent may
retry, double-click, or reconnect without knowing whether the first submission
succeeded. Posting the retry as a new transaction would create duplicate
history that then requires an auditable correction.

The existing `ExternalReference` belongs to the financial source. It may contain
a bank, asset-manager, receipt, or manually supplied reference. It is optional,
may be unavailable, and may collide across institutions, accounts, or operation
types. Reusing it as transport retry identity would conflate two different
meanings and make imports and manual entry fragile.

## Decision

Use a dedicated command idempotency identity for retry-safe write endpoints.

- The HTTP contract uses an opaque `Idempotency-Key` supplied by the client for
  one logical command.
- The key is scoped by Household identity and a stable operation code.
- The server fingerprints the normalized semantic Application command with an
  explicit, versioned canonicalization. Authoritative financial conversion does
  not use binary floating point.
- The first successful submission persists a dedicated command receipt and its
  stable result identities atomically with the ledger transaction graph.
- An equivalent replay returns the original result without invoking financial
  posting again.
- Reuse of the same scoped key for a different semantic command returns a stable
  sanitized conflict and changes no ledger facts.
- Concurrent equivalent submissions may create only one posted transaction
  graph.
- `ExternalReference` remains optional source metadata and receives no
  idempotency or uniqueness semantics from this decision.

Command receipts are operational Application/Infrastructure metadata. They are
not ledger transactions, positions, balances, or Domain authority. They are
accessed through narrow use-case-driven ports, not a generic repository or
command framework.

M002 applies this decision to contribution and fund-purchase endpoints. Applying
it to later write endpoints is expected but must be included in their accepted
milestones. The one-time setup endpoint remains outside M002.

## Consequences

Positive:

- uncertain retries and double-clicks do not duplicate immutable history;
- provider references retain their financial meaning;
- equivalent replay can return the same transaction and lot identities;
- conflicts become explicit rather than silently posting a different command;
- UI, API clients, scripts, and approved agents share one retry contract.

Costs:

- persistence needs a command-receipt region and a schema migration;
- fingerprint canonicalization becomes a versioned compatibility concern;
- receipt and ledger persistence must share an atomic transaction;
- concurrency and restart behavior require real-SQLite integration tests;
- clients must generate and retain a key across retries of one logical command.

## Rejected alternatives

### Reuse ExternalReference

Rejected because it is optional source metadata with different identity and
collision semantics.

### Detect duplicates from amount, date, and asset heuristics

Rejected because two legitimate transactions may share those values and
heuristics cannot provide deterministic replay semantics.

### Keep idempotency only in process memory

Rejected because it fails after restart, cannot coordinate independent
processes, and can diverge from the committed SQLite transaction.

### Let every retry post and rely on reversal

Rejected because reversal preserves auditability but is not a substitute for
preventing a transport-level duplicate.

## Compatibility and privacy

The fingerprint algorithm and canonical command shape must be versioned so a
later normalization change does not reinterpret an existing receipt silently.
Logs may use a bounded correlation representation but must not log normalized
request bodies or private financial values merely to diagnose idempotency.

