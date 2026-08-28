# M003: Posted Reversal and Correction Workflow

Status: Accepted

Owner: Human and agent

Last reviewed: 2026-08-28

Accepted: 2026-08-28

## User outcome

A user can inspect whether an immutable posted transaction may be reversed,
understand any blocking downstream lot activity, post one exact retry-safe
reversal, and open both sides of the correction through stable HTTP readback.

When a corrected replacement is required, the user posts it as a separate
supported transaction after the reversal succeeds. The original remains
Posted and all three economic facts remain auditable.

## Current evidence

M001 already established the Domain and SQLite reversal invariants:

- `LedgerTransaction.CreateReversal` accepts only a posted non-reversal,
  preserves its effective business dates, and creates opposite entries in the
  same sequence;
- a partial unique index permits at most one reversal row per original;
- posting triggers require the same household, dates, entries, prices, roles,
  and opposite lot allocations on the same lots;
- a reversal cannot add cost-component or cash-flow metadata;
- the original remains Posted and posted graphs cannot be edited or deleted.

The existing Domain and real-SQLite tests prove inverse entry creation,
reversal posting, uniqueness, same-lot allocation mirroring, original-plus-
reversal netting, and rejection when an acquisition lot has downstream posted
allocations.

M002 added retry-safe contribution and fund-purchase submission, dedicated
`CommandReceipt` storage, and `GET /api/ledger/transactions/{transactionId}`.
The transaction read model exposes `ReversalOfTransactionId`, but an original
does not yet expose the transaction that reverses it and neither side exposes
its lot-allocation effects.

There is no Application reversal use case, reversal-specific persistence port,
eligibility preview, or HTTP reversal command. No current supported workflow
can therefore correct posted history without bypassing the Application layer.

One persistence mismatch must be resolved in this milestone. The current
`TR_LedgerTransaction_ValidateBeforePosting` trigger treats every posted lot
allocation after an acquisition as a permanent dependency. It still blocks the
acquisition after the downstream transaction has itself been validly reversed.
That behavior does not complete ADR-002's accepted workflow of correcting
dependent activity first and then reversing the acquisition.

The verified baseline before this proposal is 163 passing tests, no formatting
drift, and no pending EF model changes.

## Why now

M002 prevents accidental duplicate history and makes supported writes
inspectable. The next safety gap is correcting a genuine mistake without
editing SQLite or deleting posted facts.

Opening-balance import and the later fund and physical-gold lifecycles depend
on a correction path that works across transaction types and lot allocations.
Building more entry workflows before this path would leave users with more
immutable facts that the product cannot safely correct.

## Accepted decisions

Human approval on 2026-08-28 accepted the following decisions as written.

### 1. Reversal command and replacement remain separate

**Accepted:** M003 posts one reversal per command. A corrected contribution
or fund purchase is then submitted through its existing type-specific endpoint
with a new logical idempotency key. Future transaction types use their own
accepted writers.

M003 does not add an atomic `reverse-and-replace` union request. Such a request
would combine unrelated transport shapes, enlarge failure semantics, and
couple this safety slice to every future transaction type.

### 2. No structured original-to-replacement link yet

**Accepted:** model only the already accepted original-to-reversal
relationship in M003. Do not overload `ExternalReference` or command receipts
to identify a replacement. A human explanation belongs in the required
reversal reason; source references retain their existing meaning.

The schema currently has no accepted cardinality for one replacement, a split
replacement, or a merge correction. Adding `CorrectsTransactionId`, a
correction-case aggregate, or another durable relationship requires a separate
product decision and an ADR before implementation. The absence of that
structured link must remain explicit in API/UI documentation.

### 3. Eligibility is generic, not endpoint-type allowlisted

**Accepted:** any faithfully reconstructed posted non-reversal transaction
is eligible under the Domain and persisted-history rules. The use case must not
special-case only Contribution and Buy, and it must not revalidate current
master-data activity for immutable historical facts.

This lets later accepted opening-balance, sale, transfer, and physical-gold
writers use the same correction path. A persisted shape that cannot satisfy
the current Domain semantics is rejected safely rather than reversed by raw
row arithmetic.

### 4. A reason is required

**Accepted:** require trimmed non-empty, non-control-bearing `Reason` text of
at most 2,000 characters and store it as the reversal transaction's immutable
`Note`. `ExternalReference` remains null on the reversal.

This provides an audit explanation. M003 cannot attribute the action to a
person because authentication and authorization are not yet accepted; it must
not fabricate actor identity.

### 5. Apply ADR-006 idempotency to reversal

**Accepted:** require `Idempotency-Key` and use the stable operation code
`REVERSE_POSTED_TRANSACTION`. Scope remains household plus operation code plus
key. The original's immutable household identity supplies the scope; it need
not be repeated in the request body.

The versioned fingerprint contains the original transaction identity and the
normalized reason. Server time, generated reversal IDs, and current dependency
state are excluded.

### 6. A reversed downstream transaction is a neutralized dependency

**Accepted:** an acquisition remains blocked while any posted non-reversal
transaction that allocates one of its created lots lacks its own posted exact
reversal. A downstream original paired with its valid posted reversal is no
longer a blocker. The downstream original and its reversal remain in history.

Application eligibility and SQLite posting protection must implement the same
definition. This clarifies and completes ADR-002; it does not permit deletion,
mutation, or arbitrary netting between unrelated transactions.

### 7. Add a read-only eligibility preview

**Accepted:** expose a deterministic reversal preview before the command.
It reports eligibility, exact inverse entries and allocations, an existing
reversal identity, or resolvable blocking transaction identities. The preview
is advisory; the write rechecks inside its atomic persistence flow.

## In scope

- Add a focused reversal eligibility query and Application preview use case.
- Add a focused retry-safe `ReversePostedTransactionUseCase`.
- Reconstruct the persisted original through a narrow Domain-owned path so
  `LedgerTransaction.CreateReversal` remains authoritative.
- Reconstruct or otherwise mutate affected `AssetLot` aggregates through a
  narrow Domain-owned boundary; do not create allocation children directly in
  Application code.
- Mirror every original lot allocation onto the corresponding reversal entry,
  on the same lot, with the exact opposite signed E8 quantity.
- Persist the reversal transaction, mirrored allocations, and command receipt
  in one explicit SQLite transaction.
- Handle same-key and different-key reversal races without duplicate history.
- Add a raw-SQL migration that aligns acquisition-dependency protection with
  the accepted neutralized-dependency rule.
- Add reversal preview and reversal command HTTP endpoints.
- Extend transaction readback additively with reverse navigation and
  transaction allocation effects.
- Add stable sanitized error codes and blocking transaction identities where
  useful to the caller.
- Add focused Domain, Application, real-SQLite, and HTTP tests.

## Out of scope

- Editing, deleting, cancelling, or changing the status of a posted original.
- Directly reversing a reversal or creating more than one reversal per
  original.
- A composite reverse-and-replace endpoint.
- A generic transaction-entry endpoint or new contribution, purchase, sale,
  transfer, opening-balance, or physical-gold writer.
- A structured original-to-corrected-replacement relationship.
- Reusing `ExternalReference` as correction or retry identity.
- Copying original costs or cash-flow detail onto the reversal.
- Transaction list/search, evidence attachments, reconciliation, or analytics.
- UI implementation, authentication, authorization, or actor attribution.
- Database-path, backup, restore, encryption, deployment, or real-data import
  changes.
- Generic repositories, a command bus, CQRS framework, event bus, or messaging.

## Required behavior

### Eligibility preview

For a known target, the preview returns one stable eligibility result:

- `ELIGIBLE`;
- `NOT_POSTED`;
- `TARGET_IS_REVERSAL`;
- `ALREADY_REVERSED`;
- `BLOCKED_BY_DEPENDENCIES`;
- `UNSUPPORTED_PERSISTED_SHAPE`.

An eligible preview contains the exact inverse entry effects in original entry
sequence and the exact inverse lot allocations keyed to the same lot and entry
sequence. It does not allocate IDs for a future reversal or persist anything.

`ALREADY_REVERSED` returns the existing reversal transaction identity.
`BLOCKED_BY_DEPENDENCIES` returns distinct blocking transaction identities in a
deterministic order. Those identities resolve through the existing transaction
GET.

An unknown target returns sanitized 404. Preview never exposes EF rows, SQL,
trigger names, connection details, or stack traces.

Preview is not a reservation. A state change between preview and submission is
handled by command revalidation and database constraints.

### First reversal submission

For a valid new key, reason, and eligible original:

1. load only enough immutable target identity to establish existence and its
   household-scoped receipt identity;
2. check for an existing receipt and resolve replay or fingerprint conflict
   before reconstructing Domain state or applying current eligibility failures;
3. for a new command, load the original's effective entries, allocations,
   created lots, existing reversal relationship, and downstream dependency
   facts;
4. create the reversal through `LedgerTransaction.CreateReversal` with current
   UTC creation/posting time and the normalized reason;
5. create exact opposite allocations against the same existing lots through
   the AssetLot aggregate boundary;
6. post the Domain reversal;
7. insert the Draft graph, new allocations, receipt, and final Posted state in
   one SQLite transaction;
8. return `201 Created` with the reversal identity and a resolvable Location.

The original remains Posted. The reversal keeps the original order, execution,
and settlement dates, has no cash-flow detail or cost components, and creates
no new AssetLot.

### Equivalent replay

The same household, operation code, key, original identity, and normalized
reason returns the original reversal identity and Location. It does not run
Domain posting again, create allocations, change timestamps, or fail because
the original now has a reversal.

Whitespace-only differences around the reason normalize equivalently. A
different normalized reason or target under the same scoped key is an
idempotency conflict.

### Existing reversal and dependencies

A new key targeting an already reversed original returns a stable 409 and the
existing reversal transaction identity. It writes no second receipt or ledger
facts.

An acquisition with an outstanding downstream allocation returns a stable 409
with blocking transaction identities and writes nothing. After every blocker
has its own posted reversal, retrying the previously uncommitted command may
succeed.

### Concurrency

Two equivalent requests with the same key may create only one reversal graph
and receipt; both successful callers observe the same reversal identity.

Two different keys racing to reverse the same original produce one success and
one `TRANSACTION_ALREADY_REVERSED` conflict. The losing command persists no
receipt or partial graph.

The unique reversal index can collide before command-receipt insertion. The
Infrastructure implementation must therefore recover the winning receipt or
existing reversal explicitly; it cannot rely only on M002's receipt-uniqueness
collision path.

If a downstream allocation races with acquisition reversal, Application and
the posting trigger must agree: either the dependency commits first and the
reversal is rejected, or the reversal commits first. No partial graph or raw
SQLite error reaches the caller.

### Corrected replacement

After reversal succeeds, a corrected transaction is a new logical command.
For currently supported shapes, the caller uses the contribution or fund-
purchase endpoint with a new idempotency key. A replacement failure never
undoes the valid reversal; the caller safely retries the replacement command.

M003 does not claim that the replacement has a structured persisted link to
the original. The reversal's required reason can explain the correction, but
must not impersonate a source reference.

### Transaction readback

The existing transaction-detail projection gains:

- nullable `ReversedByTransactionId`, derived from the unique reverse
  relationship;
- ordered `LotAllocations` for entries in the requested transaction, including
  allocation identity, lot identity, entry identity, signed raw E8 quantity,
  and UTC creation time.

For the original, `ReversedByTransactionId` points to the reversal. For the
reversal, `ReversalOfTransactionId` points to the original. Both identities
resolve through the existing GET. A purchase original and its reversal expose
equal-and-opposite allocations on the same lot.

## Invariants

- Ledger history remains the only authoritative source of transaction effects.
- The original and reversal are distinct immutable Posted transactions.
- There is no `Reversed` status and effective queries include both transactions.
- One original has at most one reversal; a reversal is not directly reversed.
- Reversal entry sequence, location, asset, role, unit price, and price currency
  match the original; only quantity sign changes.
- Checked negation rejects `long.MinValue` rather than overflowing.
- Original effective business dates are preserved; reversal audit timestamps
  use the current UTC time.
- Original costs and cash-flow classification remain original facts and are not
  copied to the reversal.
- Allocation signs match their reversal entries, reconcile exactly, target the
  same lots, and cannot make a lot quantity negative.
- Acquisition lots and cost-basis history are retained even when their derived
  current quantity becomes zero.
- A downstream dependency is neutralized only by its own valid Posted reversal,
  never by coincidental netting with an unrelated transaction.
- Receipt metadata is operational, not a financial fact or balance.
- Receipt and reversal persistence commit or roll back together.
- No current authoritative position, lot quantity, value, cost, or allocation
  table is introduced.
- Historical reversal must not depend on a referenced account or asset still
  being active today.
- Fixed-point financial values never pass through binary floating point.

## API contract

Accepted routes:

```text
GET /api/ledger/transactions/{originalTransactionId}/reversal-preview

POST /api/ledger/transactions/{originalTransactionId}/reversals
Idempotency-Key: <opaque client-generated key>
Content-Type: application/json

{
  "reason": "Correcting an incorrectly recorded transaction."
}
```

First submission and equivalent replay return:

```text
201 Created
Location: /api/ledger/transactions/{reversalTransactionId}

{
  "reversalTransactionId": "...",
  "reversalOfTransactionId": "..."
}
```

The preview response uses stable explicit fields and raw signed E8 values. Its
exact contract must be frozen in API tests before M003 is Verified. It contains
at least:

- `originalTransactionId`;
- `canReverse`;
- `eligibilityCode`;
- nullable `existingReversalTransactionId`;
- `blockingTransactionIds`;
- ordered `inverseEntries`;
- ordered `inverseLotAllocations`.

Accepted command error mapping:

| HTTP | Stable code | Meaning |
|---|---|---|
| 400 | `IDEMPOTENCY_KEY_REQUIRED` / `IDEMPOTENCY_KEY_INVALID` | Existing M002 header contract |
| 400 | `REVERSAL_REASON_REQUIRED` / `REVERSAL_REASON_INVALID` | Missing, blank, control-bearing, or overlong reason |
| 404 | `LEDGER_TRANSACTION_NOT_FOUND` | Target does not exist |
| 409 | `IDEMPOTENCY_KEY_CONFLICT` | Same scoped key, different semantic command |
| 409 | `TRANSACTION_NOT_POSTED` | Target state is not Posted |
| 409 | `TRANSACTION_ALREADY_REVERSED` | Another reversal already owns the target |
| 409 | `REVERSAL_DEPENDENCY_CONFLICT` | Outstanding downstream lot activity blocks reversal |
| 422 | `REVERSAL_TARGET_IS_REVERSAL` | Direct reversal of a reversal is forbidden |
| 422 | `REVERSAL_SOURCE_UNSUPPORTED` | Persisted source cannot satisfy current Domain semantics or checked negation |

Conflict responses may include `existingReversalTransactionId` or
`blockingTransactionIds` as documented extensions. They must not include raw
persistence exception text.

## Persistence impact

No new authoritative financial table or reversal-link column is expected.
Existing `CommandReceipt` storage can record the new operation with a null
result lot identity. The existing unique `ReversalOfTransactionId` index
remains the final one-reversal arbiter.

A new migration is required because the already-applied `001_CoreLedger`
migration must not be edited to change deployed history. The migration should
have a behavior-specific name such as `003_ReversalDependencySemantics` and:

1. drop and recreate `TR_LedgerTransaction_ValidateBeforePosting` with the
   accepted neutralized-dependency predicate;
2. retain every other posting protection byte-for-behavior, including exact
   inverse entries/allocations, lot reconciliation, non-negative effective lot
   balance, and no reversal metadata;
3. block a posted non-reversal dependent transaction unless that transaction
   has its own Posted reversal;
4. ignore the matching dependent reversal row only as part of that explicit
   pair, not through aggregate quantity netting;
5. restore the previous trigger behavior in `Down` without dropping data.

Do not modify the historical `001_CoreLedger` migration to hide the change.
Add query-driven indexes only if the actual reversal candidate/dependency query
plan demonstrates a need.

The write transaction must persist:

- the new reversal transaction as Draft;
- its entries;
- mirrored allocations against existing lots;
- the command receipt;
- the final transition to Posted;

and then commit once. A posting, trigger, receipt, uniqueness, busy, or
concurrency failure rolls the whole unit back.

If existing aggregates cannot be reconstructed without inventing persisted
identity, add the narrowest explicit Domain-owned reconstitution path. Do not
expose EF rows outside Infrastructure, add EF annotations to Domain, grant
Application direct child-row writes, or introduce a generic repository.

## Acceptance criteria

- An eligible preview returns exact inverse entries and same-lot inverse
  allocations without writing data.
- Preview identifies an existing reversal and outstanding blockers through
  resolvable transaction IDs.
- A first valid command posts exactly one reversal and its Location returns
  200 through transaction readback.
- The original remains Posted and readback links original and reversal in both
  directions.
- Original plus reversal positions net to zero for every affected household,
  portfolio, account, and asset.
- A purchase reversal creates no lot, preserves the acquisition lot/cost basis,
  appends the exact opposite allocation, and leaves derived lot quantity zero.
- A sale or other non-acquisition allocation reversal restores the same lot and
  creates no replacement lot.
- An equivalent retry returns the original reversal identity and creates no
  new rows or timestamps.
- A changed reason or target under the same scoped key returns sanitized 409
  and changes no data.
- Concurrent equivalent submissions create one reversal graph and receipt.
- Concurrent different-key submissions create one reversal; the loser returns
  the winning reversal identity as a sanitized conflict and persists nothing.
- Draft, Cancelled, reversal, already-reversed, non-negatable, and unsupported
  source shapes are rejected without partial history.
- An outstanding downstream allocation blocks acquisition reversal and returns
  the blocker identity.
- After the downstream transaction receives its own Posted reversal, the
  acquisition reversal can succeed and final positions/lots remain coherent.
- A trigger catches a dependency introduced after Application eligibility
  evaluation, with full rollback and no raw SQLite leakage.
- Transaction readback includes ordered allocation effects and the reverse
  relationship after restart.
- Existing 163 tests continue to pass with focused M003 tests added.
- Formatting and EF migration-model drift checks pass.
- `PROJECT_STATE.md`, `ROADMAP.md`, and this milestone describe the verified
  behavior without rewriting M001/M002 history.

## Test scenarios

### Domain

- exact inverse preserves sequence, location, role, unit price, currency, and
  effective dates;
- reversal note is normalized and immutable after posting;
- Draft/Cancelled originals and reversal targets are rejected;
- checked negation rejects `long.MinValue`;
- restored/reconstituted transaction and lot aggregates retain persisted IDs
  and allocation history;
- inverse allocation against a reconstituted lot respects same-asset, sign,
  magnitude, uniqueness, and non-negative balance rules.

### Application

- eligible preview for contribution, acquisition, and allocated disposal;
- preview results for unknown, non-posted, reversal, already-reversed,
  dependent, and unsupported sources;
- first reversal calls Domain creation and one atomic store operation;
- equivalent receipt replay occurs before current eligibility rejection;
- changed reason/target conflicts under the same scoped key;
- normalization treats surrounding reason whitespace equivalently;
- dependency IDs are distinct and deterministically ordered;
- current inactive master data does not prevent reversal of valid history;
- commit-result scope, fingerprint, and transaction identity are validated;
- dependency and uniqueness race outcomes map to stable Application results.

### SQLite integration

- candidate reconstruction round trip with entries, costs, cash flow,
  allocations, created lots, existing reversal, and blockers;
- reversal transaction, existing-lot allocations, and receipt commit atomically;
- forced posting/receipt/allocation failure leaves no reversal, allocation, or
  receipt;
- exact inverse entry and allocation triggers remain effective;
- original Posted graph and pre-existing lot history remain immutable;
- second reversal uniqueness survives restart;
- same-key concurrency through independent DbContexts returns one receipt and
  reversal identity;
- different-key concurrency returns one reversal and no losing receipt;
- outstanding dependent transaction blocks acquisition reversal;
- Draft or Cancelled dependent reversal does not unblock it;
- Posted exact reversal of the dependent transaction unblocks it;
- unrelated coincidental lot netting does not unblock it;
- original plus reversal position and per-lot sums are correct after restart;
- direct SQL cannot bypass the revised trigger semantics.

### API

- preview success and every eligibility code;
- preview unknown-target 404 with stable code;
- required/invalid idempotency header and required/invalid reason validation;
- first command and equivalent replay return the same 201 body and Location;
- Location resolves with `ReversalOfTransactionId`, while original readback
  exposes `ReversedByTransactionId`;
- allocation readback shows exact opposite raw E8 values on the same lot;
- idempotency, already-reversed, and dependency conflicts use stable codes and
  documented identity extensions;
- a corrected contribution or fund purchase can be posted separately with its
  own key after reversal;
- no response leaks SQL, trigger, connection, EF row, or stack details.

## Verification commands

Focused filters should be named during implementation. At minimum:

```powershell
dotnet test tests/WealthLedger.Domain.Tests/WealthLedger.Domain.Tests.csproj --no-restore --filter FullyQualifiedName~Reversal --verbosity minimal
dotnet test tests/WealthLedger.Application.Tests/WealthLedger.Application.Tests.csproj --no-restore --filter FullyQualifiedName~Reversal --verbosity minimal
dotnet test tests/WealthLedger.Infrastructure.Tests/WealthLedger.Infrastructure.Tests.csproj --no-restore --filter FullyQualifiedName~Reversal --verbosity minimal
dotnet test tests/WealthLedger.Api.Tests/WealthLedger.Api.Tests.csproj --no-restore --filter FullyQualifiedName~Reversal --verbosity minimal
```

Final verification:

```powershell
dotnet test WealthLedger.slnx --no-restore --verbosity minimal
dotnet format WealthLedger.slnx --verify-no-changes --no-restore --verbosity minimal
dotnet ef migrations has-pending-model-changes --project src/WealthLedger.Infrastructure/WealthLedger.Infrastructure.csproj --startup-project src/WealthLedger.Infrastructure/WealthLedger.Infrastructure.csproj --context WealthLedgerDbContext --no-build
```

## Documentation updates

After verification:

- mark this milestone Verified and record acceptance/verification dates;
- update `PROJECT_STATE.md` to the factual reversal checkpoint;
- move M003 to Verified in `ROADMAP.md` and make M004 the next candidate;
- update `DATABASE_DESIGN.md` with the neutralized dependency predicate and
  migration name;
- update API examples in `README.md` only if reversal examples exist there;
- do not change ADR-002 or ADR-006 merely to record their implementation;
- add a new ADR only if the accepted scope introduces a structured correction
  relationship or changes another cross-cutting decision.

## Suggested commit boundaries

```text
feat(domain): add narrow aggregate reconstitution for reversal
feat(application): add reversal preview and retry-safe orchestration
feat(infrastructure): persist atomic reversal allocations and receipts
fix(persistence): recognize reversed downstream lot dependencies
feat(api): expose reversal preview and command
test(ledger): prove reversal retry dependency and readback behavior
docs(state): record verified reversal workflow
```

Keep intermediate commits buildable where practical. Do not mix later
transaction writers, UI, backup, or navigation work into M003.

## Risks and rollback

- **Trigger divergence:** Application and SQLite could disagree about a
  neutralized dependency. Use one documented predicate and prove both layers
  with paired tests.
- **Reversal uniqueness race:** the unique reversal index may fail before
  receipt insertion. Recover the committed winner explicitly and test both
  same-key and different-key races.
- **Partial existing-lot mutation:** inserting allocations outside the reversal
  transaction could corrupt lot history. One explicit SQLite transaction is
  mandatory.
- **Replay regression:** checking `ALREADY_REVERSED` before receipt replay would
  turn a successful retry into a conflict. Test the order directly.
- **Unchecked negation:** `long.MinValue` cannot be reversed. Reject it before
  persistence and return a sanitized supported-range failure.
- **Read-model compatibility:** new transaction response members are additive,
  but clients and tests must not silently reinterpret absent/null reverse links.
- **Correction-link ambiguity:** M003 deliberately has no structured replacement
  relationship. Do not imply one in UI or API prose.
- **Migration rollback:** `Down` restores the prior stricter trigger and drops
  no ledger rows. Roll back the application and migration together on a copy of
  synthetic data. M004 backup/restore readiness is still required before real
  household history becomes authoritative.
