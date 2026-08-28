# M002: Retry-Safe Transaction Submission and Readback

Status: Verified

Owner: Human and agent

Last reviewed: 2026-08-28

Accepted: 2026-08-24

Verified: 2026-08-27

## User outcome

A user can safely submit an existing contribution or fund-purchase command,
retry after an uncertain response without creating a duplicate, and immediately
open a stable read-only explanation of the posted transaction.

## Current evidence

M002 is implemented and verified.

Both existing ledger write endpoints accept a dedicated `Idempotency-Key`
that is separate from `ExternalReference`. The retry scope is household plus
stable operation code plus opaque key.

Application normalizes contribution and fund-purchase commands before computing
deterministic, versioned SHA-256 fingerprints. Equivalent replay returns the
original stable result identities without reconstructing or reposting the
Domain aggregate. Conflicting replay returns a sanitized idempotency conflict.

SQLite persists command receipts in dedicated `CommandReceipt` storage with a
unique household/operation/key scope. Receipt persistence and the corresponding
ledger graph participate in the same database transaction. Fund-purchase
receipts preserve both the transaction identity and acquisition-lot identity.

Real SQLite integration tests cover receipt round trip, atomic rollback, unique
scope, and concurrent equivalent submissions through independent DbContexts.
The persisted EF model contains the command-receipt table and its result
relationships in addition to the existing normalized ledger schema.

The API exposes:

- `POST /api/ledger/contributions`;
- `POST /api/ledger/fund-purchases`;
- `GET /api/ledger/transactions/{transactionId}`.

Contribution and fund-purchase Locations now resolve to stable read-only
transaction projections. Transaction readback includes transaction facts,
ordered entries, optional cash-flow detail, typed costs, and lots created by
the transaction.

The one-time setup endpoint retains its `201 Created` response but no longer
advertises a household Location for which no GET route exists.

Application, Infrastructure, and API tests cover first submission, equivalent
replay, conflicting replay, purchase replay with stable transaction and lot
identities, transaction readback, unknown-transaction 404 behavior, setup
Location behavior, persistence rollback, and concurrent receipt submission.

During final review, two additional API assertions were considered useful
hardening but not release blockers: directly asserting the serialized
idempotency-conflict error-code extension, and duplicating zero-row persistence
assertions at the API boundary for invalid/conflicting requests. The human
owner accepted the current verification evidence without expanding those tests.

## Why now

Duplicate immutable financial history is expensive to correct and easy to
create through double-click, retry, reconnect, automation, or an agent. Adding
more write workflows before safe retry and readback would multiply that risk.

This milestone is a transport/application/persistence safety slice. It does not
change ledger arithmetic or introduce a UI.

## Accepted decisions

Human approval on 2026-08-24 accepted the following decisions:

1. **Dedicated retry identity.** Use an `Idempotency-Key` supplied by the client
   for a logical command. Do not reinterpret `ExternalReference` as the retry
   identity. External references remain optional provider/source facts and may
   collide across institutions or operation types.
2. **Scope.** Scope a key to household plus stable operation code plus key. The
   same key may not identify two semantically different commands within that
   scope.
3. **Equivalent replay.** Persist a deterministic fingerprint of the normalized
   Application command and the stable result identities. An equivalent replay
   returns the original success result without posting again.
4. **Conflicting replay.** Reuse of a key with a different semantic command
   returns a stable sanitized conflict.
5. **Atomicity.** The idempotency record and ledger posting succeed or roll back
   together. Concurrent equal submissions produce one posted transaction.
6. **Setup Location.** Keep setup's 201 response but omit a non-resolvable
   `Location` until a household read endpoint exists. Every non-null Location
   emitted by the API must resolve.

Verification outcome: accepted as satisfied on 2026-08-27. The final human
review accepted the current automated evidence with the two non-blocking API
test-hardening notes recorded in Current evidence.

The cross-cutting rationale is recorded in
[`ADR-006`](../decisions/ADR-006-command-idempotency.md).

## In scope

- Add an explicit idempotency contract to the existing contribution and
  fund-purchase HTTP endpoints.
- Validate missing, malformed, and overlong keys at the transport boundary.
- Normalize the Application command before computing its deterministic
  fingerprint.
- Add a narrow Application/Infrastructure port for atomic command receipt and
  result replay; do not add a generic repository or command bus.
- Add the required SQLite persistence, uniqueness, and migration.
- Return the same status, response identities, and transaction Location for an
  equivalent replay.
- Add a transaction-detail query through Application and Infrastructure.
- Map `GET /api/ledger/transactions/{transactionId}` to a stable transport read
  model.
- Make existing contribution and fund-purchase Locations resolvable.
- Remove the unsupported setup Location without broadening setup into master
  data management.
- Add focused Application, SQLite, and API tests, including concurrent retry.
- Add the accepted idempotency ADR.

## Out of scope

- Posted reversal or corrected-transaction workflow.
- Transaction list/search and pagination.
- Household or master-data GET/CRUD endpoints.
- API route versioning or a general public API compatibility policy.
- OpenAPI UI, SDK generation, authentication, or authorization.
- User-interface implementation.
- Backup, restore, database-path, or encryption changes.
- Idempotency for the one-time setup endpoint.
- Changing the meaning or uniqueness of `ExternalReference`.
- Fund sale, physical gold, opening balance, valuation, or analytics.

## Required behavior

### First submission

For a valid new key and command, the API posts exactly one transaction and
returns the existing success contract plus a resolvable transaction Location.

### Equivalent replay

For the same household, operation code, key, and normalized semantic command,
the API returns the recorded result. It does not call Domain posting again,
create another transaction, create another lot, or change timestamps.

### Conflicting replay

For the same scoped key and a different normalized semantic command, the API
returns 409 Problem Details with a stable application error code and no
persistence internals.

### Concurrent submission

If equivalent requests race, one atomic posting wins and every successful
caller observes the same transaction and lot identities. SQLite busy/conflict
handling must not transform the race into duplicate history.

### Transaction readback

The transaction GET returns a read-only Application projection containing at
least:

- transaction identity, household identity, type, status, dates, external
  reference, note, reversal relationship, and audit timestamps;
- ordered entries with portfolio, account, asset, signed quantity, role, and
  optional unit price/currency;
- cash-flow detail when present;
- typed cost components when present;
- lots created by the transaction and their stable identities where applicable.

The projection is built from normalized facts and does not expose EF rows or
Domain mutation APIs.

### Not found

An unknown transaction identity returns sanitized 404 Problem Details. A
transaction outside an explicitly requested household scope must not be leaked
if household scoping is included in the accepted route contract.

## Invariants

- Ledger history remains the source of truth.
- Posted transactions and children remain immutable.
- Command receipts are operational metadata, not financial transactions or
  authoritative balances.
- ExternalReference retains source-reference semantics.
- An idempotency fingerprint uses deterministic, versioned canonicalization and
  no binary floating-point conversion.
- Idempotency persistence cannot commit without its ledger result, and a ledger
  result cannot commit without its receipt for an idempotent command.
- Existing setup, contribution, purchase, lot, and position behavior remains
  valid.

## Recommended API contract

```text
POST /api/ledger/contributions
Idempotency-Key: <opaque client-generated key>

POST /api/ledger/fund-purchases
Idempotency-Key: <opaque client-generated key>

GET /api/ledger/transactions/{transactionId}
```

The key should be treated as opaque, bounded text. A UUID is a suitable client
default, but the server contract should not depend on a particular UI library.

The exact response contract and error code names must be written into API tests
before implementation is considered complete.

## Persistence impact

The recommended implementation introduces dedicated command-receipt storage
rather than adding retry semantics to `LedgerTransaction.ExternalReference`.
The record needs, at minimum:

- household identity;
- stable operation code;
- idempotency key;
- fingerprint algorithm/version and fingerprint;
- result transaction identity;
- optional result lot identity for fund purchase;
- creation timestamp.

The unique constraint covers household, operation code, and key. Exact table and
migration names should follow repository conventions and be explicit in the
implementation plan.

The transaction query should reuse the existing normalized ledger tables and
add only query-driven indexes demonstrated by the actual access path.

## Acceptance criteria

- A first contribution request posts once and its Location returns 200.
- An equivalent contribution replay returns the original transaction identity
  and creates no new ledger rows.
- A first fund purchase posts once and its Location returns the transaction with
  the expected created lot identity.
- An equivalent fund-purchase replay returns the original transaction and lot
  identities and creates no new lot or allocation.
- Conflicting key reuse returns sanitized 409 and changes no ledger facts.
- Concurrent equivalent submissions create exactly one transaction graph.
- Invalid key input returns 400 and changes no data.
- An unknown transaction GET returns sanitized 404.
- Setup no longer advertises a route that does not exist.
- Existing 127 tests continue to pass, with new focused tests added.
- Formatting and EF model-drift checks pass.
- The accepted ADR and `PROJECT_STATE.md` match verified behavior.

## Test scenarios

### Application

- first command executes and stores its result;
- equivalent replay bypasses posting and returns recorded result;
- changed amount, date, asset, account, reference, or note conflicts under the
  same key according to the accepted semantic fingerprint;
- operation-code separation behaves as documented;
- receipt-store failure does not leave a posted transaction.

### SQLite integration

- receipt round trip and unique scope;
- receipt plus contribution atomic commit and rollback;
- receipt plus fund purchase/lot/allocation atomic commit and rollback;
- two independent DbContexts racing on one key produce one graph;
- transaction-detail projection returns ordered entries, details, costs, and
  created lots after restart;
- foreign keys and posted-history triggers remain effective.

### API

- required header validation;
- stable first and replay responses;
- conflicting replay Problem Details;
- resolvable Created Location;
- unknown transaction 404;
- setup response contains no false Location;
- no raw EF, SQLite, connection, or stack details in failures.

## Verification commands

```powershell
dotnet test WealthLedger.slnx --no-restore --verbosity minimal
dotnet format WealthLedger.slnx --verify-no-changes --no-restore --verbosity minimal
dotnet ef migrations has-pending-model-changes --project src/WealthLedger.Infrastructure/WealthLedger.Infrastructure.csproj --startup-project src/WealthLedger.Infrastructure/WealthLedger.Infrastructure.csproj --context WealthLedgerDbContext --no-build
```

The implementation plan should also name focused test filters used during
development.

## Documentation updates

After verification:

- add the accepted idempotency ADR and decision index entry;
- update `PROJECT_STATE.md` to the new factual checkpoint;
- update API examples in `README.md` only if they are introduced there;
- move this milestone to Verified;
- advance `ROADMAP.md` so M003 becomes the next candidate.

## Suggested commit boundaries

```text
docs(decisions): separate command idempotency from external references
feat(application): add transaction receipt and readback ports
feat(infrastructure): persist atomic command receipts and transaction queries
feat(api): add idempotent writes and transaction readback
test(api): cover retry conflict and resolvable locations
docs(state): record verified transaction safety milestone
```

Commit boundaries may be adjusted to keep every intermediate commit buildable,
but unrelated refactors must remain outside the milestone.

## Risks and rollback

- Canonical fingerprint changes could make old receipts appear conflicting.
  Version the fingerprint algorithm and test compatibility.
- A receipt committed before ledger posting would suppress a valid retry; atomic
  persistence is mandatory.
- A ledger post committed without a receipt would permit duplicate retry; the
  same transaction boundary is mandatory.
- Overloading ExternalReference would couple provider semantics to UI retries;
  the proposed design deliberately avoids it.
- If the migration fails, restore the pre-migration synthetic database during
  development. Real-data migration and backup procedures belong to M004 and
  must be verified before production-like use.
