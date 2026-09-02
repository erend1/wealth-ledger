# M005: Master Data and Ledger Navigation

Status: Verified

Owner: Human and agent

Accepted: 2026-09-02 (all ten Recommended decisions exactly as written)

Verified: 2026-09-02

Last reviewed: 2026-09-02

## User outcome

The household can discover the identifiers and current human-readable labels
needed to operate WealthLedger without reading SQLite, setup output, source
code, or copied GUIDs. It can browse households, members, institutions,
portfolios, accounts, currencies, and assets; open a bounded recent-posted
ledger page; follow a transaction to the existing complete readback; and use an
entry's household/portfolio/account/asset scope to open the existing point
position query.

A valid scope with no posted history returns a genuine zero position. An
unknown or cross-household scope no longer masquerades as zero.

M005 is a read-only navigation slice for later UI and opening-balance work. It
does not add master-data editing, broad transaction search, position or lot
inventory, valuation, reconciliation, or a graphical interface.

## Planning-time current evidence

This proposal was audited on 2026-08-31 against the documentation branch at
commit `1f56dec`, which is based on `origin/main` commit `1fec722`. The full
baseline has 163 passing tests and no EF model drift. Formatting verification
reports the pre-existing `LedgerTransaction.cs` whitespace finding already
recorded in `PROJECT_STATE.md`; M005 changes no source file.

The repository currently proves these facts:

- the setup response returns newly created identifiers, but there is no
  supported way to list or resolve master data after setup;
- no HTTP read route lists households, household members, institutions,
  portfolios, accounts, currencies, or assets;
- current master rows already preserve stable identifiers plus the names,
  codes, type/status codes, active state, dates, base units, base currencies,
  and lot-tracking mode needed by navigation;
- HouseholdMember, Portfolio, and Account are household-scoped; Institution,
  Currency, and Asset are global reference/master rows in the accepted current
  schema. M005 must not fabricate household ownership for the latter group;
- the only ledger navigation route is
  `GET /api/ledger/transactions/{transactionId}`. It requires the caller to
  know a transaction ID and returns exact normalized facts, but no transaction
  collection exists;
- the current transaction detail returns stable IDs and raw financial values.
  M003 is Accepted to add reversal navigation and allocation effects, but those
  additions are not implemented on this proposal's source base;
- the only position route requires four identifiers. `GetPositionUseCase`
  queries matching posted entries and returns zero when none exist; it does not
  first distinguish a valid empty scope from an unknown household, portfolio,
  account, asset, or cross-household combination;
- `EfCorePostedEntrySource` and `EfCoreLedgerTransactionReadStore` already use
  no-tracking projections and keep EF rows inside Infrastructure;
- the current ledger index is keyed by household, status, and execution date.
  It supports business-date queries but does not directly cover a deterministic
  recently-posted cursor ordered by posting time and identity;
- current master-code uniqueness and household/code indexes can support most
  proposed master pages. Any additional index must be justified by a real
  SQLite query plan rather than added speculatively;
- there is no materialized balance, position, search, or navigation table, and
  M005 must not create one.

M003 is being implemented separately by the human owner and M004 remains
Proposed. Both source realities must be re-audited before M005 implementation;
this document claims neither milestone's runtime behavior.

## Implementation reconciliation

Implementation began from clean commit `31c61b0`, where final M003 and M004
were already Verified and the complete baseline contained 388 passing tests.
That source and its tests superseded the older planning-time evidence above.
No material contract contradiction was found.

The verified implementation provides every accepted route and field, scoped
versioned keyset cursors, active filtering, current display context, a fixed
three-reader-command non-empty ledger feed, and position-scope validation. It
changes no Domain behavior and adds no write route, UI, broad search, inventory,
valuation, or later-milestone capability.

The exact production query used the existing execution-date index and
`USE TEMP B-TREE FOR ORDER BY` before migration. Migration
`20260902112549_004_LedgerNavigationQueries` adds only
`IX_LedgerTransaction_Household_Status_Posted_Id`; the same plan then uses that
index with no temporary sort. Its `Down` removes only that index, and the
up/down test preserves seeded rows.

Focused verification passed 16 Application tests, 8 real-SQLite
Infrastructure tests, and 5 API tests. The full suite passed 417 tests. EF
reports no pending model changes. The accepted M004 workflow created and
verified a synthetic M003 pre-migration package, explicitly migrated the live
copy, verified its integrity and preserved data, and staged an isolated restore
of the old copy. Formatting verification reports only the pre-existing,
byte-for-byte non-committable `LedgerTransaction.cs` Windows line-ending caveat
documented in `PROJECT_STATE.md`; no M005 file is reported.

## Why now

M002 makes transaction identities resolvable, and M003 makes corrections
navigable. M004 protects the local data lifecycle. The next coherent product
gap is discoverability: the current API can write and retrieve exact facts, but
a non-developer still cannot find the IDs required to use it.

M006 needs stable read contracts before choosing a UI framework or building
selectors and navigation. M007 opening-balance workflows likewise need current
household, account, portfolio, currency, and asset choices. Defining those
queries first keeps UI code from reading SQLite, calling setup internals, or
inventing its own financial projections.

Planning may proceed in parallel. Implementation may not begin until M003 and
M004 are Verified, M005 is explicitly Accepted, and its implementation branch
has been rebased onto that verified checkpoint. At most one milestone remains
In Progress.

## Decisions and decision gates

The human owner accepted all ten Recommended decisions exactly as written on
2026-09-02. Their wording is preserved below as the implementation contract.

### Decision 1: delivery sequencing

**Recommended:** review M005 now, but do not implement it until M003 and M004
are merged and Verified. Rebase onto that checkpoint, rerun the full suite, and
compare the final M003 transaction-detail contract and M004 hosting/operations
composition roots before changing source.

This avoids parallel edits to transaction projections, API registration,
Infrastructure dependency injection, migrations, and project-state documents.

### Decision 2: bounded read-only scope

**Recommended:** M005 delivers three related capabilities only:

1. paged master/reference-data discovery;
2. a paged default feed of recently posted transactions with current display
   labels and exact entry effects;
3. valid-versus-unknown scope validation for the existing point-position query.

It adds no write command. Master-data create, rename, activate, deactivate,
close, and archive behavior needs a later accepted workflow with its own
history and validation decisions. Full ledger search, position/lot inventory,
and reconciliation remain M010. M005 does not move those outcomes earlier by
calling them navigation.

### Decision 3: resource and household scoping

**Recommended:** expose explicit resources that follow the implemented schema:

```text
GET /api/households
GET /api/households/{householdId}
GET /api/households/{householdId}/members
GET /api/households/{householdId}/portfolios
GET /api/households/{householdId}/accounts
GET /api/institutions
GET /api/currencies
GET /api/assets
GET /api/households/{householdId}/ledger/transactions
```

Members, portfolios, accounts, transactions, and position validation are
strictly household-scoped. A nested collection first proves the household
exists; unknown household is 404 rather than an ambiguous empty page.

Institution, Currency, and Asset remain global because that is their current
schema meaning. Account results include their nullable current Institution
reference so callers need not infer custody from a name. A future authenticated
multi-user or multi-tenant deployment must revisit global reference visibility;
M005 relies on M004's accepted local-only operating model and does not invent
authorization.

### Decision 4: human-oriented current master projections

**Recommended:** every master item returns its stable identity and all current
display/selection facts required by the later UI:

- Household: identity, name, base-currency code/name/minor-unit digits, and UTC
  creation time;
- HouseholdMember: identity, household identity, display name, active state,
  and UTC creation time;
- Institution: identity, immutable code, current name, stable type code, and
  active state;
- Portfolio: identity, household identity, immutable code, current name,
  stable status code, UTC creation time, and nullable UTC close time;
- Account: identity, household identity, nullable Institution summary,
  immutable code, current name, stable type code, active state, and nullable
  opening/closing business dates;
- Currency: code, name, and minor-unit digits;
- Asset: identity, immutable code, current name, stable type and base-unit codes,
  nullable base-currency code, stable lot-tracking-mode code, active state, and
  UTC creation time.

Transport never exposes CLR enum names or Infrastructure rows. IDs remain
present even when labels are included. API values use stable explicit codes and
ISO date/time representations. M005 does not format monetary or quantity values
for humans; M006 presentation components will format exact transport values.

### Decision 5: current labels are not historical snapshots

**Recommended:** master names/codes shown in navigation and transaction entry
effects are explicitly current master data. Renaming a master later may change
the displayed label of old history, but never its stable ID or financial fact.
Inactive, closed, or archived state is returned visibly.

M005 does not add duplicate name snapshots to LedgerTransaction or
TransactionEntry. If source-time legal naming is later required, it must be
captured as evidence/source metadata through a separately accepted schema, not
inferred from current labels.

Historical transaction effects always resolve their referenced master rows
regardless of current active state. Restrictive foreign keys remain the final
protection against dangling referenced history.

### Decision 6: active filtering and deterministic cursor pages

**Recommended:** every collection uses one envelope:

```json
{
  "items": [],
  "nextCursor": null
}
```

`pageSize` defaults to 50 and accepts 1 through 100. A cursor is opaque,
versioned, self-contained, restart-safe, and bound to resource type, household
scope where applicable, and filter state. There is no server cursor cache and
no `totalCount`. A malformed, unsupported, or scope/filter-mismatched cursor is
a stable 400; it never becomes raw SQL or a free-form sort expression.

Collections with lifecycle state accept `includeInactive`, default false:

- members, accounts, institutions, and assets include only `IsActive = true` by
  default;
- portfolios include only `ACTIVE` by default;
- `includeInactive=true` includes every current state so historical references
  and Settings screens remain resolvable;
- households and currencies have no current inactive state.

Page ordering uses immutable keys:

- Household and HouseholdMember: creation UTC ascending, then identity;
- Institution, Portfolio, Account, and Asset: immutable code ascending, then
  identity;
- Currency: code ascending.

Ordering is database-deterministic rather than locale-sensitive. The later UI
may apply locale-aware presentation to a fully loaded small selector, but API
cursors never depend on machine culture. Valid empty pages return 200.

The cursor is not an authentication or authenticity token. It is bounded and
validated input. Routine logs do not record it because it contains navigation
keys already visible to the local caller.

### Decision 7: recent posted ledger feed

**Recommended:**
`GET /api/households/{householdId}/ledger/transactions` returns Posted
transactions only, ordered by `PostedAtUtc` descending and TransactionId
descending. It is explicitly a “recently recorded” feed, not business-date
search. This keeps a newly posted reversal visible even though its effective
dates mirror an older original.

The endpoint uses the same bounded cursor envelope. Each summary contains:

- transaction and household identities;
- stable transaction type and status codes;
- order, execution, and settlement dates;
- external reference when present, but not the free-text Note;
- `ReversalOfTransactionId` and the final M003
  `ReversedByTransactionId` contract;
- creation and posting UTC timestamps;
- ordered entry effects with entry identity/sequence, exact signed raw E8
  quantity, stable role code, and current Portfolio, Account, nullable
  Institution, and Asset identity/code/name/type/status/unit context.

The feed does not duplicate cash-flow detail, costs, notes, created lots, lot
allocations, physical details, or reversal preview. Selecting a summary follows
the existing transaction-detail route, which remains authoritative for the
complete explanation.

The first M005 feed has no date, type, status, asset, account, portfolio,
institution, reference, or reversal filter. M010 may add those parameters to
the same route without changing the default recent-posted behavior.

Keyset pagination uses the last posting time and transaction identity. A new
transaction posted after page one appears on a refreshed first page and does
not duplicate an older continuation. Cursor traversal does not promise a
multi-request SQLite snapshot; immutable posted rows and server-generated
posting time provide the accepted local-feed semantics.

### Decision 8: valid zero versus unknown position scope

**Recommended:** before summing posted entries, the existing point-position
use case validates that:

- Household, Portfolio, Account, and Asset all exist;
- Portfolio and Account belong to the requested Household.

Closed/archived portfolios, inactive accounts, and inactive assets remain
valid historical scopes. A valid scope with no matching posted entries returns
quantity zero and source-entry count zero. Any unknown or cross-household shape
returns one sanitized 404 `POSITION_SCOPE_NOT_FOUND`; it must not disclose
which private identifier failed.

The arithmetic and ordering of existing posted entry facts do not change. The
validation uses a narrow read port, not a generic master-data repository or
direct API access to EF.

### Decision 9: query implementation and persistence

**Recommended:** Application defines explicit query/result records and focused
read ports for master navigation, ledger navigation, and position-scope
existence. Infrastructure implements bounded `AsNoTracking` projections. It
does not expose `IQueryable`, rehydrate Domain aggregates for read-only display,
or create a generic repository/query framework.

Master pages use the current normalized tables and indexes. The ledger page
first selects at most `pageSize + 1` transaction keys, then loads its bounded
entry/master effects in a fixed number of batched queries. It must not issue one
query per transaction, entry, account, asset, or institution.

A query-plan review after rebasing is mandatory. The expected persistence
change is one descriptive migration adding a composite navigation index
equivalent to:

```text
LedgerTransaction(HouseholdId, StatusCode, PostedAtUtc, Id)
```

The exact ascending/descending declaration follows the verified SQLite plan.
Do not duplicate an equivalent index introduced by M003/M004, and do not edit a
historical migration. No new table, current-balance column, search cache, or
materialized read model is accepted.

### Decision 10: privacy, errors, and compatibility

**Recommended:** navigation responses contain private labels and references and
therefore inherit M004's loopback-only policy. Routine logs record route,
duration, item count, and stable outcome only; they omit names, codes,
references, cursor contents, exact quantities, notes, and response bodies.

Accepted stable error codes are:

| HTTP | Code | Meaning |
|---|---|---|
| 400 | `NAVIGATION_PAGE_SIZE_INVALID` | Page size is outside 1-100 or malformed |
| 400 | `NAVIGATION_FILTER_INVALID` | `includeInactive` is malformed |
| 400 | `NAVIGATION_CURSOR_INVALID` | Cursor is malformed or unsupported |
| 400 | `NAVIGATION_CURSOR_SCOPE_MISMATCH` | Cursor belongs to another resource, household, or filter |
| 404 | `HOUSEHOLD_NOT_FOUND` | A requested household or nested collection scope does not exist |
| 404 | `POSITION_SCOPE_NOT_FOUND` | The point-position master scope is unknown or cross-household |

Problem Details never includes SQL, EF types, cursor payload internals, stack
traces, or a private-field value. Existing successful transaction-detail and
position payload shapes remain compatible. M005 adds routes and makes only
invalid point-position scopes return 404 instead of fabricated zero.

## In scope

- stable Application DTOs/use cases and narrow read ports for all accepted
  master/reference pages;
- current human-readable names/codes/status alongside stable identities;
- deterministic, versioned cursor pagination with bounded page sizes;
- active-only defaults and explicit inactive/history inclusion;
- strict household scoping for members, portfolios, accounts, recent ledger,
  and point-position validation;
- global Institution, Currency, and Asset reads matching the current schema;
- account-to-institution current display context;
- a recent Posted transaction page with exact ordered entry effects and current
  master labels;
- M003 original/reversal navigation in summaries after its final contract is
  verified;
- validation that distinguishes a genuine zero position from an invalid scope;
- no-tracking, bounded, non-N+1 SQLite projections;
- a query-plan-backed transaction navigation index if still required after
  rebase;
- stable sanitized page/filter/cursor/not-found API errors;
- additive API documentation and end-to-end synthetic tests.

## Out of scope

- creation, rename, activation/deactivation, closure, archival, deletion, or
  merging of any master entity;
- a generic CRUD API, generic repository, generic query bus, CQRS framework, or
  exposing `IQueryable`;
- a combined “load the entire application” navigation endpoint;
- transaction posting, reversal implementation, replacement linking, or any
  mutation of posted history;
- business-date/type/status/asset/account/portfolio/institution/reference text
  search, arbitrary sorting, saved filters, or result export;
- a complete transaction timeline snapshot across multiple HTTP requests;
- position inventory, grouping, non-zero position lists, institution totals,
  current custody rollups, or lot inventory;
- cost basis, realized/unrealized gain, value, price, return, allocation, goal,
  or performance calculation;
- physical-gold detail/inventory views;
- reconciliation, evidence storage, import, or data-quality warnings;
- historical snapshots of master labels;
- formatted currency, unit, gram, or date presentation;
- UI components, framework selection, local caching, offline synchronization,
  or client state management;
- authentication, authorization, remote exposure, tenancy, or user-specific
  visibility rules;
- provider, market-data, AI/LLM, or agent-specific endpoints;
- materialized navigation/search/position tables.

## Required behavior

### Master and reference pages

Each list validates page/filter/cursor input before accessing persistence. A
valid first request returns the first deterministic page. `nextCursor` is null
exactly when no later item exists. A continuation returns only rows after its
last immutable key and can be used after process restart. The store fetches one
extra row to determine continuation but never returns more than the requested
page size.

Nested household routes return 404 for an unknown household even if the child
table is empty. Known households with no matching children return 200 with an
empty page. Household boundary predicates are applied in SQLite, not by
filtering a global in-memory result.

Default pages omit inactive/closed/archived choices. `includeInactive=true`
does not alter identities or relabel state; it only expands the eligible set.
An item that becomes inactive between requests may disappear from a default
continuation. A fresh page is the supported way to refresh mutable master state.

Accounts with no Institution return a null Institution summary. Accounts with
one return its current code/name/type/active state in the same bounded
projection. A referenced inactive Institution remains resolvable.

### Cursor behavior

The cursor codec has an explicit version and resource discriminator. It uses
invariant date, time, code, and identity representations and checked length
limits. It accepts no SQL column name, expression, direction, or raw predicate.

Changing household, resource, or `includeInactive` while reusing a cursor
returns `NAVIGATION_CURSOR_SCOPE_MISMATCH`. Unsupported version, invalid base64
or JSON, missing/extra-invalid required state, empty identities, out-of-range
timestamps, oversized payload, and invalid ordering key return
`NAVIGATION_CURSOR_INVALID` without persistence access.

Cursors are compatibility contracts only within their accepted version. A
future breaking ordering change introduces a new cursor version and treats an
old unsupported cursor as invalid; it never guesses a new position.

### Recent ledger page

The query proves the household exists, selects only its Posted transactions,
orders by posting UTC then identity descending, and applies the opaque keyset.
Every returned transaction has non-null posting time. Encountering persisted
Posted history without a posting time or required master row is a sanitized
persistence-compatibility failure, not a partially populated response.

Entry effects are ordered by transaction page order and EntrySequence. Their
exact signed raw E8 values and stable codes match transaction detail. Current
Portfolio, Account, Institution, and Asset display fields are resolved in
batch. A master rename or state change alters only display context; stored entry
identity and effect remain unchanged.

The page excludes Notes and expanded detail children. ExternalReference is
returned because it is a primary human source locator, but logging and future
screenshots must treat it as private. Each transaction identity resolves
through the existing detail GET after restart.

An equivalent request with the same cursor returns the same continuation when
the relevant older history and master labels have not changed. A new Posted
transaction appears on a refreshed first page. Read requests create no receipt,
transaction, lot, allocation, or operational fact.

### Position scope

Position scope validation happens before entry summation. It is read-only and
uses one household-safe result. A cross-household portfolio/account combination
is indistinguishable from an unknown scope at the transport boundary.

For a valid scope, the existing deterministic entry order and checked addition
remain unchanged. Posted reversal entries participate normally. Draft, Ordered,
and Cancelled entries remain excluded. Zero with no matching entries and zero
after equal-and-opposite Posted history are both genuine derived zero results,
distinguished by `SourceEntryCount` as today.

### Failure, cancellation, and restart

Cancellation reaches EF queries and returns no partial page. Persistence errors
are sanitized through the accepted API boundary. A cursor does not depend on
in-memory state and remains parseable after restart against the same compatible
data. No query performs a write or changes timestamps.

## Invariants

- Ledger history remains the only authoritative source of transaction effects.
- Master/navigation projections are current read models, not historical facts
  or duplicate authority.
- Stable identities accompany human labels everywhere.
- Current labels never overwrite or reinterpret persisted transaction facts.
- Institution, Currency, and Asset retain their implemented global scope;
  household ownership is not inferred.
- Nested household reads apply their scope in persistence and never leak another
  household's members, portfolios, accounts, transactions, or position facts.
- Unknown scope is not represented as a known zero position.
- Position arithmetic remains ordered, checked, and derived only from Posted
  entries; no binary floating point is introduced.
- Transaction feed effects preserve exact signed raw E8 values and explicit
  stable codes.
- Reversal pairs remain separate Posted facts and no corrected-replacement link
  is invented by navigation.
- Inactive and archived masters remain resolvable for historical explanation.
- Cursors are bounded transport state, never SQL, authentication, or ledger
  data.
- Every collection order has a deterministic immutable tie-breaker.
- No authoritative current-position, remaining-lot, value, cost, or allocation
  table is introduced.
- Application query contracts do not expose EF rows or `IQueryable`.
- Query count is bounded by page shape rather than item count.
- Read endpoints write no ledger, receipt, backup, or operations state.
- Routine logs and errors omit private labels, references, cursor contents,
  financial values, SQL, and stack traces.
- Tests and documentation use synthetic names and values only.

## API or UI contract

No UI is added. M005 establishes API contracts that M006 can consume.

### Collection query

All collection routes accept:

```text
pageSize=<integer 1..100, default 50>
cursor=<opaque optional string>
```

State-bearing master routes also accept:

```text
includeInactive=<true|false, default false>
```

All collection responses use:

```json
{
  "items": [],
  "nextCursor": "opaque-or-null"
}
```

`totalCount`, arbitrary sort, and arbitrary filter expressions are absent.

### Representative master contracts

The exact DTO names may follow repository naming conventions, but JSON fields
and semantics are frozen in API tests. Representative shapes are:

```json
{
  "householdId": "...",
  "name": "Synthetic Household",
  "baseCurrency": {
    "code": "TRY",
    "name": "Synthetic Currency",
    "minorUnitDigits": 2
  },
  "createdAtUtc": "2026-01-01T00:00:00Z"
}
```

```json
{
  "accountId": "...",
  "householdId": "...",
  "institution": {
    "institutionId": "...",
    "code": "SYNTHETIC_BANK",
    "name": "Synthetic Bank",
    "typeCode": "BANK",
    "isActive": true
  },
  "code": "INVESTMENT",
  "name": "Synthetic Investment Account",
  "typeCode": "INVESTMENT",
  "isActive": true,
  "openedOn": "2026-01-01",
  "closedOn": null
}
```

```json
{
  "assetId": "...",
  "code": "SYNTHETIC_FUND",
  "name": "Synthetic Fund",
  "typeCode": "FUND",
  "baseUnitCode": "FUND_UNIT",
  "baseCurrencyCode": "TRY",
  "lotTrackingModeCode": "REQUIRED",
  "isActive": true,
  "createdAtUtc": "2026-01-01T00:00:00Z"
}
```

Member, Institution, Portfolio, and Currency shapes contain the fields listed
in Decision 4 with the same stable code/date conventions. A null account
Institution is represented as JSON null, not an empty identity or invented
“unknown” institution.

### Recent transaction summary

A representative item is:

```json
{
  "transactionId": "...",
  "householdId": "...",
  "typeCode": "BUY",
  "statusCode": "POSTED",
  "orderDate": null,
  "executionDate": "2026-01-15",
  "settlementDate": null,
  "externalReference": "SYNTHETIC-REF",
  "reversalOfTransactionId": null,
  "reversedByTransactionId": null,
  "createdAtUtc": "2026-01-15T10:00:00Z",
  "postedAtUtc": "2026-01-15T10:00:00Z",
  "entryEffects": [
    {
      "entryId": "...",
      "entrySequence": 0,
      "portfolioId": "...",
      "portfolioCode": "HOME_GOAL",
      "portfolioName": "Synthetic Goal",
      "portfolioStatusCode": "ACTIVE",
      "accountId": "...",
      "accountCode": "INVESTMENT",
      "accountName": "Synthetic Investment Account",
      "accountTypeCode": "INVESTMENT",
      "accountIsActive": true,
      "institutionId": "...",
      "institutionCode": "SYNTHETIC_BANK",
      "institutionName": "Synthetic Bank",
      "institutionTypeCode": "BANK",
      "institutionIsActive": true,
      "assetId": "...",
      "assetCode": "SYNTHETIC_FUND",
      "assetName": "Synthetic Fund",
      "assetTypeCode": "FUND",
      "assetBaseUnitCode": "FUND_UNIT",
      "assetBaseCurrencyCode": "TRY",
      "assetLotTrackingModeCode": "REQUIRED",
      "assetIsActive": true,
      "quantityDeltaRawE8": 125000000,
      "roleCode": "PRINCIPAL"
    }
  ]
}
```

All Institution effect fields are nullable together when the Account has no
Institution. Entry effect arrays retain original sequence. The response does
not include Note, costs, cash-flow detail, lots, or allocations; callers use
`GET /api/ledger/transactions/{transactionId}` for those facts.

### Existing point position

The success payload remains unchanged. The behavioral contract becomes:

- valid master scope plus no entries: 200 with exact zero and count zero;
- valid master scope plus posted history: existing 200 derived result;
- unknown/cross-household master scope: sanitized 404 with
  `POSITION_SCOPE_NOT_FOUND`.

## Persistence impact

No authoritative financial schema changes are accepted. No master or
transaction row receives a duplicated display field.

One non-authoritative query index migration is expected after rebase if the
verified SQLite plan still needs it. The migration:

1. has a descriptive name such as `LedgerNavigationQueries`, with its numeric
   prefix selected from the actual post-M003 chain;
2. adds only the household/status/posting-time/identity navigation index needed
   by the recent feed;
3. preserves the existing household/status/execution-date index for later
   business-date search unless measured evidence supports a separate accepted
   change;
4. drops only its own index in `Down`;
5. creates no row, trigger, view, or cached total;
6. is applied through the verified M004 explicit migration and backup workflow.

If the post-M004 schema already contains an equivalent index and the query plan
proves it adequate, document that evidence and keep M005 migration-neutral
rather than creating a duplicate.

## Acceptance criteria

- Every accepted master/reference route returns stable IDs plus the complete
  current human-oriented fields in Decision 4.
- Household-scoped collections never return another household's rows.
- Unknown nested household scope returns sanitized 404; a known empty scope
  returns an empty 200 page.
- Active-only defaults and `includeInactive=true` behave consistently for all
  lifecycle-bearing masters.
- Every collection uses the same bounded page envelope, returns at most the
  requested size, and produces a null cursor exactly at the end.
- Cursor continuations survive process restart and reject malformed,
  unsupported, or mismatched state before querying SQLite.
- Deterministic ordering and immutable tie-breakers prevent duplicates across
  an unchanged dataset, including equal timestamps/codes.
- The recent ledger feed returns only the requested household's Posted
  transactions in posting-time/identity descending order.
- A newly posted reversal appears according to posting time even when its
  execution date is historical.
- Every recent transaction ID resolves through the existing detail GET.
- Summary entry effects match detail IDs, order, stable codes, and exact raw E8
  quantities and include current Portfolio/Account/Institution/Asset context.
- Inactive/closed masters referenced by history remain visible in summaries
  with their current state.
- Renaming a master changes current display context but never IDs, entry
  effects, detail facts, or ledger rows.
- The recent feed omits free-text Note and expanded child facts; no N+1 query
  behavior grows with page item count.
- A valid point-position scope with no history returns genuine zero; unknown or
  cross-household scope returns `POSITION_SCOPE_NOT_FOUND` and no quantity.
- Existing position arithmetic, reversal participation, exact raw transport,
  and source-entry counting remain unchanged for valid scopes.
- Query-plan evidence either proves the accepted navigation index is used or
  documents why no new index is needed.
- No Domain behavior, generic repository, materialized balance/navigation
  model, or master/ledger write endpoint is added.
- All invalid query/cursor/not-found responses use the accepted stable codes and
  leak no SQL, EF, cursor payload, stack, or private value.
- Existing post-M004 tests plus focused M005 Application, real-SQLite, restart,
  pagination, and API tests pass.
- Formatting and EF migration-model drift checks pass.
- `PROJECT_STATE.md`, `ROADMAP.md`, and this milestone claim M005 behavior only
  after verification.

## Test scenarios

### Domain

No Domain behavior should change. Dependency and source-review checks prove
that navigation, cursor, EF, and HTTP concerns did not enter Domain.

### Application

- each master query validates page size, filter, and cursor before invoking its
  port;
- first page, continuation, final page, and empty page results;
- cursor version/resource/household/filter mismatch and malformed ordering key;
- deterministic page ordering for equal timestamps and synthetic code sets;
- active-only versus include-inactive semantics for every supported master;
- unknown household versus known empty nested collection;
- recent feed requests only Posted, bounded transaction/effect facts;
- summary mapping preserves exact values and current label/status context;
- valid empty position scope returns zero while unknown/cross-household scope
  returns one non-disclosing result;
- overflow behavior in existing position addition remains unchanged;
- cancellation is propagated and no use case calls a write port.

### Infrastructure

- real SQLite round trip for every master projection and stable code;
- household isolation with multiple synthetic households, including accounts
  sharing one global Institution/Asset reference;
- nullable and inactive Institution account projections;
- active, inactive, closed, and archived list behavior after restart;
- keyset pages with more than two page sizes, identical creation timestamps,
  code boundaries, and deterministic identity tie-breakers;
- cursor continuation after a new master row and after restart;
- recent feed ordering by posting time rather than execution date;
- a reversal with historical effective date appears by new posting time;
- batched entry/master effects preserve transaction and EntrySequence order;
- external reference is present, Note/expanded children are absent;
- current rename/state changes update labels without modifying historical IDs or
  raw effects;
- direct query/command interception proves a fixed bounded query count rather
  than per-row N+1 behavior;
- point-position existence validation distinguishes unknown, cross-household,
  valid empty, and valid net-zero history;
- Draft/Ordered/Cancelled entry effects do not enter position or recent Posted
  results;
- `EXPLAIN QUERY PLAN` and integration timing-independent assertions prove the
  selected navigation index path;
- restart preserves cursor decoding and all exact results;
- migration `Up`/`Down` changes only the accepted index and no data;
- no unexpected EF model drift remains.

### API or UI

- every route, default query, include-inactive query, page continuation, and
  empty response shape;
- current names and explicit stable type/status/unit/lot-tracking codes never
  expose CLR enum names;
- unknown household and position scope return the accepted 404 code;
- invalid page size, boolean filter, cursor, and cursor scope return the
  accepted 400 code;
- malformed values never expose model-binding internals, cursor payload, SQL,
  EF row names, or stack traces;
- transaction page includes exact ordered effects and excludes Note/cost/lot
  expansions;
- each transaction summary identity resolves through transaction detail after
  restart and retains M003 reversal links;
- effect IDs are sufficient to call the existing point-position route;
- valid zero and non-zero point-position success payloads remain compatible;
- responses and captured logs are inspected for forbidden private diagnostics;
- no master write, broad search, inventory, generic SQL, or admin HTTP route is
  exposed.

## Verification commands

Focused names should be finalized during implementation. At minimum:

```powershell
dotnet test tests/WealthLedger.Application.Tests/WealthLedger.Application.Tests.csproj --no-restore --filter FullyQualifiedName~Navigation --verbosity minimal
dotnet test tests/WealthLedger.Application.Tests/WealthLedger.Application.Tests.csproj --no-restore --filter FullyQualifiedName~PositionScope --verbosity minimal
dotnet test tests/WealthLedger.Infrastructure.Tests/WealthLedger.Infrastructure.Tests.csproj --no-restore --filter FullyQualifiedName~Navigation --verbosity minimal
dotnet test tests/WealthLedger.Infrastructure.Tests/WealthLedger.Infrastructure.Tests.csproj --no-restore --filter FullyQualifiedName~PositionScope --verbosity minimal
dotnet test tests/WealthLedger.Api.Tests/WealthLedger.Api.Tests.csproj --no-restore --filter FullyQualifiedName~Navigation --verbosity minimal
```

Final verification:

```powershell
dotnet test WealthLedger.slnx --no-restore --verbosity minimal
dotnet format WealthLedger.slnx --verify-no-changes --no-restore --verbosity minimal
dotnet ef migrations has-pending-model-changes --project src/WealthLedger.Infrastructure/WealthLedger.Infrastructure.csproj --startup-project src/WealthLedger.Infrastructure/WealthLedger.Infrastructure.csproj --context WealthLedgerDbContext --no-build
```

Review the generated migration and run the accepted M004 pre-migration backup,
explicit migration, integrity, and isolated restore smoke workflow against a
synthetic database. Query-plan evidence must be attached to the implementation
review rather than inferred from index existence.

## Documentation updates

After human acceptance, mark this milestone Accepted and record the accepted
date/decisions. No ADR is expected if the accepted implementation stays within
the current normalized-read architecture and explicit route/cursor decisions.
Add an ADR only if review introduces a materialized read model, changes master
scope, or establishes another cross-cutting architecture rule.

After implementation verification:

- mark M005 Verified and record the verification date;
- update `PROJECT_STATE.md` with the factual master navigation, recent ledger,
  and valid-position-scope checkpoint;
- move M005 to Verified in `ROADMAP.md` and make M006 the next candidate;
- update `README.md` with compact synthetic examples for each route family and
  pagination/error semantics;
- update `ARCHITECTURE.md` only if the verified read-flow boundary needs
  additional canonical detail;
- update `DATABASE_DESIGN.md` with the actual navigation index/migration and
  query purpose;
- update `UX_MVP.md` only to map later selectors/navigation onto verified M005
  contracts without claiming a UI exists;
- preserve M001-M004 and accepted ADR history.

## Suggested commit boundaries

```text
feat(application): define bounded master navigation queries
feat(application): distinguish valid and unknown position scopes
feat(infrastructure): project paged master navigation data
feat(infrastructure): query recent posted ledger effects
perf(persistence): index recent ledger navigation
feat(api): expose master and ledger navigation routes
test(navigation): prove paging scope privacy and restart behavior
docs(state): record verified navigation contracts
```

Keep intermediate commits buildable. Do not mix M006 UI, M007 opening writes,
M010 broad search/inventory, master-data writes, market data, export, or
analytics into M005.

## Risks and rollback

- **Scope creep:** a navigation feed can grow into full search, inventory, or
  analytics. Freeze M005's default recent-posted page and leave filters and
  derived inventories to M010.
- **Unknown-as-zero:** omitting existence validation preserves a dangerous
  ambiguity. Prove valid empty and invalid scope separately at Application,
  SQLite, and API layers.
- **Household leakage:** global joins or in-memory filtering could return
  another household's labels or transaction effects. Apply and test household
  predicates in SQLite and use one non-disclosing scope error.
- **Current-label confusion:** renamed labels are not transaction-time facts.
  Preserve stable IDs, expose lifecycle state, and document current-label
  semantics without adding fabricated snapshots.
- **Cursor instability:** mutable labels, culture sorting, or offset pagination
  can duplicate/skip rows. Use immutable ordering keys, versioned scoped
  keysets, and restart/concurrency tests.
- **Recent-date ambiguity:** execution date would hide a newly posted reversal
  in old history. Name the feed “recently recorded” and order by posting time;
  defer business-date search to M010.
- **N+1 load:** resolving labels per row can make a page issue hundreds of
  queries. Page transaction keys first, batch effects, and test bounded command
  count.
- **Oversized or malicious cursor:** bound encoded/decoded size, accept only the
  frozen payload shape, and never turn cursor contents into SQL identifiers or
  expressions.
- **Index drift:** M003/M004 may change migrations before M005 starts. Rebase,
  inspect the actual chain and plan, and create no duplicate index or historical
  migration edit.
- **API privacy:** names and references are useful but private. Rely on verified
  local-only hosting, omit Notes from the feed, and prohibit value/cursor/body
  logging.
- **Compatibility:** the point-position 404 corrects invalid behavior but can
  surprise a client that relied on fabricated zero. Document and test the
  change; valid success payloads remain unchanged.
- **Rollback:** M005 read code can be reverted without changing ledger rows. If
  its index migration exists, roll back code and drop only that index through
  the verified M004 migration/recovery workflow. Never restore or delete ledger
  data merely to roll back a read feature.

All rollback and pagination tests use synthetic copies. M005 never needs the
household's real database or labels during development.
