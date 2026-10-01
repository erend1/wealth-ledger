# M010: Searchable Inventory, Evidence, and Reconciliation

Status: Proposed

Owner: Human and agent

Last reviewed: 2026-10-01

Planning note: this document is a review contract only. M010 implementation is
not authorized until the human owners accept the decisions below or explicitly
amend them.

## Objective

Make the verified M002-M009 ledger usable as an independently checkable record:
search the durable history, see current positions and acquisition lots without
introducing mutable balance tables, attach retained source evidence, compare
recorded holdings with a dated statement or physical count, and keep unresolved
differences visible until they are explained or corrected through normal ledger
workflows.

M010 closes the remaining real-data readiness gap identified by the roadmap. It
does not add market prices, valuation, performance, goals, allocation policy,
automatic imports, trade execution, or agent autonomy.

## User outcome

After M010, a household operator or reviewer in Ready mode can:

1. search ledger history with bounded filters for financial date, transaction
   type, asset, institution, account, portfolio, external reference, status, and
   reversal relationship;
2. open every search result in the existing complete transaction explanation;
3. view a derived current inventory grouped by portfolio, account, institution,
   and asset without storing `CurrentPosition` or `RemainingQuantity` as truth;
4. drill from a position into the effective transactions and, for lot-tracked
   assets, the acquisition lots that explain it;
5. inspect physical-gold custody with exact gross weight, whole pieces,
   immutable fineness, derived fine weight, acquisition lineage, and custody;
6. record a dated external evidence source such as a statement, receipt, or
   physical count, optionally retaining one bounded source file with a stable
   digest;
7. link evidence to one or more existing transactions without editing their
   Posted financial facts;
8. enter exact observed quantities from a statement or physical count and run a
   deterministic comparison against ledger-derived quantities as of the same
   effective date;
9. see exact ledger quantity, observed quantity, delta, explicit tolerance, and
   comparison status for every reconciled item;
10. leave an out-of-tolerance item open, link a later Posted corrective
    transaction, or record a documented exception without pretending the values
    matched;
11. reopen the reconciliation after restart and see whether later ledger history
    made the stored comparison stale; and
12. back up, stage, restore, and independently read back the evidence and
    reconciliation state together with the ledger.

## Current evidence

Repository inspection at verified M009 establishes these facts:

- M009 is Verified and M010 is the next Planned candidate. No M010 code or
  milestone contract exists on `main` at the drafting baseline.
- `GetPositionUseCase` already derives one exact
  Household/Portfolio/Account/Asset position by summing only Posted entry facts
  in deterministic order. It validates the full scope first, so a valid zero is
  distinct from an unknown or cross-household scope.
- `IPostedEntrySource` and `EfCorePostedEntrySource` read Posted transaction
  entries rather than a stored balance. This is the pattern M010 inventory must
  generalize rather than replace.
- M008 fund-sale custody already derives scope-aware available lot quantity from
  Posted entries and signed lot allocations. Its Application port explicitly
  rejects a stored current-position or remaining-quantity model.
- M009 physical-gold custody already derives current gross and piece movement
  from effective allocation history and exposes exact custody projections for
  its bounded workflows.
- M005 provides a bounded recent Posted feed ordered by `PostedAtUtc` and
  `TransactionId`. Its opaque cursor is versioned, bounded, household-scoped,
  and rejects scope mismatch.
- the recent feed intentionally omits broad filters. `UX_MVP.md` reserves date,
  type, asset, institution, account, portfolio, external reference, status, and
  reversal relationship for M010.
- the existing navigation index is optimized for recent Posted history, not all
  combinations of M010 filters. M010 must add only query support justified by
  the actual accepted search plan and real-SQLite evidence.
- PR-008 requires traceable ledger exploration; PR-009 requires position and
  inventory views; PR-013 requires statement/physical-count reconciliation and
  visible data-quality gaps.
- `DATA_CAPTURE.md` defines reconciliation evidence as the effective date,
  source institution or custody location, observed cash/quantity, comparison
  timestamp, difference and tolerance, evidence reference, resolution status,
  and linked correction or explanation.
- M007 deliberately stores text provenance only. Its accepted contract defers
  document/blob upload, hashing, statement lineage, reconciliation cases,
  discrepancy resolution, and evidence retention to M010.
- the M004 `.wlbackup` format contains one standalone SQLite snapshot plus one
  UTF-8 JSON manifest. Evidence stored in an unrelated filesystem tree would
  therefore not automatically participate in the verified backup/restore
  boundary.
- application backup packages remain plaintext by design; external destination
  separation/encryption remains an operator responsibility. M010 must not make
  retained evidence less private than the database that references it.

## Why now

M002-M009 can record, inspect, reverse, and recover the supported financial
history, but the application still cannot answer the ordinary review questions
that determine whether the record is trustworthy:

- "Show me every transaction involving this asset or account."
- "What does the ledger say I own in each custody location?"
- "Which acquisition lots explain this position?"
- "Does this broker statement or physical count agree with the ledger?"
- "If not, is the difference still unresolved?"

Without those capabilities, WealthLedger should not become the sole record of
real household assets. M010 is therefore a correctness and auditability
milestone, not an analytics milestone.

## Terminology

**Financial date** means the transaction `ExecutionDate` used to place an
effective economic event in history. `CreatedAtUtc` and `PostedAtUtc` remain
audit/ordering facts and are not substituted for the financial date.

**Current position** is the checked sum of effective Posted entry quantities in
one valid scope. It is a query result, not a mutable entity.

**Inventory** is a read model over current positions, acquisition lots,
allocations, and asset-specific detail. It never becomes a second source of
truth.

**Evidence record** is retained external support for ledger or reconciliation
facts. It is not a `LedgerTransaction` and cannot change a balance.

**Evidence attachment** is the optional retained byte content of one source
file plus integrity metadata. Its digest proves byte identity, not financial
truth.

**Evidence observation** is one externally observed quantity for one explicit
ledger scope on one evidence effective date.

**Reconciliation run** is a deterministic comparison of evidence observations
with ledger-derived quantities as of the evidence effective date.

**Reconciliation item** is one observed scope, its ledger quantity, delta,
tolerance, status, and resolution state within a run.

**Matched** means the absolute delta is within the explicitly recorded
tolerance. It does not mean that all historical cost or valuation evidence is
complete.

**Documented exception** means an out-of-tolerance difference has an explicit
human explanation and has been consciously accepted for review purposes. It is
not relabeled as Matched.

**Stale reconciliation** means effective ledger history at or before the
comparison date no longer reproduces the ledger quantity captured by the run.
The run remains audit history and must be rerun rather than silently rewritten.

## Decisions and decision gates

Every item below is a recommendation for human review. None is accepted merely
because it appears in this Proposed document.

### Decision 1: deliver four coherent capabilities in one milestone

**Recommended:** M010 contains four bounded vertical capabilities:

1. broad transaction search;
2. derived position and lot inventory;
3. retained evidence; and
4. dated reconciliation with explicit resolution.

Implement them in small commit boundaries, but keep one M010 milestone because
reconciliation is not useful without inventory and evidence, and the roadmap's
real-data gate closes only when the four parts work together.

Do not split M010 into independent simultaneously In-Progress milestones. The
repository rule of at most one In-Progress milestone remains intact.

### Decision 2: search never creates a second transaction model

**Recommended:** search projects existing normalized ledger facts and current
master display context. It does not create a materialized authoritative search
row, denormalized transaction table, or generic repository.

Every result keeps the stable TransactionId and opens the existing transaction
explanation/readback rather than reimplementing a competing detail model.

### Decision 3: search the financial date and bind cursors to the exact filter set

**Recommended:** the primary date range filters `ExecutionDate` inclusively.
Results order by:

1. `ExecutionDate` descending;
2. `PostedAtUtc` descending when present; and
3. `TransactionId` descending.

Search uses a new versioned opaque cursor whose payload is bound to Household
and a deterministic fingerprint of the normalized filters. Reusing a cursor
with changed filters is a stable 400 rather than silently continuing a
different query.

The recent-ledger cursor contract remains unchanged for the existing unfiltered
M005 endpoint.

### Decision 4: use AND across filter categories and exact stable identities for masters

**Recommended:** the accepted filter contract is:

- optional inclusive `executedFrom` and `executedTo`;
- zero or more transaction types;
- zero or more transaction statuses subject to Decision 5;
- optional exact AssetId;
- optional exact InstitutionId derived through entry Account context;
- optional exact AccountId;
- optional exact PortfolioId;
- optional bounded external-reference text query; and
- optional reversal relationship: original-only, reversal-only, reversed
  original, or any.

Different categories combine with AND. Multiple values within the same enum
category combine with OR. Invalid or cross-household identities fail with a
sanitized scope/filter error instead of producing a misleading empty result.

External-reference matching should be a trimmed, bounded case-insensitive
contains search using explicit SQLite semantics; it is convenience search, not
identity. Exact internal IDs remain the only identity keys.

### Decision 5: make effective Posted history the normal product surface

**Recommended:** M010's ordinary Ledger search defaults to `POSTED` and only
`POSTED` contributes to inventory and reconciliation.

The repository still persists the stable Draft/Ordered/Posted/Cancelled
vocabulary, but exposing non-Posted history as a normal household financial
search could make incomplete state look effective. If the owners want
Draft/Ordered/Cancelled diagnostics in M010, accept that explicitly and keep
those rows visibly non-effective. Otherwise diagnostic non-Posted exploration
remains out of the user-facing M010 surface.

This is a material product decision because `UX_MVP.md` named `status` among the
future filters without defining whether every persisted status should be user
searchable.

### Decision 6: generalize position inventory as a derived query only

**Recommended:** add a bounded inventory query that derives signed quantities
from effective Posted entries and returns stable Household/Portfolio/Account/
Institution/Asset context.

Default inventory shows non-zero positions. An explicit `includeZero=true` may
show valid known scopes whose effective sum is exactly zero when useful for
history/reconciliation. Unknown or cross-household scopes never masquerade as
zero.

Do not add `CurrentPosition`, `CurrentCash`, `CurrentPortfolioValue`, or another
authoritative balance table. If later scale requires a rebuildable read model,
that is a separate performance decision and remains non-authoritative.

### Decision 7: support deterministic as-of positions for reconciliation

**Recommended:** inventory exposes an optional `asOf` financial date. An as-of
position includes only effective Posted entries with `ExecutionDate <= asOf`.

Current inventory is equivalent to no upper date bound. Reconciliation always
uses the evidence effective date explicitly and never compares a historical
statement with today's quantity by accident.

The result states the as-of date, source-entry count, and stable scope so the
number is explainable.

### Decision 8: lot inventory remains acquisition lineage plus derived custody

**Recommended:** for lot-tracked assets, expose each acquisition lot with:

- AssetLotId and creating TransactionId;
- Asset identity;
- optional evidenced acquisition date;
- original exact quantity;
- Known/Unknown cost status and supported cost currency/amount when Known;
- current effective global quantity;
- current effective quantity by Portfolio/Account custody scope; and
- links to effective acquisition, disposal, transfer, and reversal allocations.

Exhausted lots are hidden by default but may be included explicitly.

`AssetLot` remains acquisition lineage and must not gain AccountId,
PortfolioId, mutable remaining quantity, or mutable custody.

### Decision 9: physical-gold inventory preserves three independent physical facts

**Recommended:** physical-gold inventory extends the generic lot projection with
immutable fineness, original piece count, current effective gross quantity,
current effective whole pieces, and derived fine weight by custody scope.

Never infer piece movement from average piece weight, never average fineness
across lots, and never persist aggregate fine weight. Existing M009 signed
allocation-level piece movement remains authoritative evidence for pieces.

### Decision 10: introduce evidence as adjunct audit data, not ledger facts

**Recommended:** add a separate evidence aggregate with a stable EvidenceId,
HouseholdId, source kind, effective date, optional InstitutionId, optional
AccountId or custody context, bounded human label/reference, optional note,
CreatedAtUtc, and optional supersession relationship.

Initial source kinds should be a small stable vocabulary:

- Statement;
- Receipt;
- PhysicalCount;
- ContractOrCertificate; and
- Other.

Evidence may link to zero or more existing ledger transactions through a
restrictive link table. Creating or linking evidence does not mutate the Posted
transaction graph, its command fingerprint, or its economics.

### Decision 11: retain at most one bounded attachment per evidence record in M010

**Recommended:** metadata-only evidence is valid. When the operator elects to
retain the source file, M010 accepts at most one attachment per EvidenceRecord,
with:

- immutable bytes stored inside SQLite;
- SHA-256 digest;
- byte length;
- sanitized original file name;
- declared/validated media type; and
- CreatedAtUtc.

Recommended maximum size: **10 MiB** per attachment.

Recommended initial accepted formats: PDF, PNG, JPEG, UTF-8 plain text, and CSV.
SVG, HTML, executable/script formats, Office macro formats, and arbitrary binary
content are rejected. Downloads use `Content-Disposition: attachment`,
`X-Content-Type-Options: nosniff`, and never execute or render active content in
the application.

Why SQLite: the accepted M004 backup package already snapshots SQLite as one
consistent unit. A sidecar evidence directory would require a new atomic backup,
restore, path-ownership, deletion, and recovery protocol before the application
could claim evidence was recoverable.

**Decision gate:** the owners may instead choose metadata/hash-only evidence in
M010 and defer retained bytes. If so, remove attachment storage and all upload/
download criteria rather than silently using a sidecar directory.

### Decision 12: evidence bytes are immutable and correction uses supersession

**Recommended:** after creation, attachment bytes, digest, Household, source
kind, and effective date are immutable. A mistaken evidence record is
superseded by a new EvidenceRecord linked through `SupersedesEvidenceId`; the
old record remains readable.

A bounded label/note may either follow the same immutable policy or be treated
as non-authoritative annotation. The Recommended default is full record
immutability except for adding transaction links and resolution links through
separate relationship records.

Deleting retained evidence is not an M010 convenience feature. A future data-
retention/privacy deletion policy must account for backup generations and audit
links explicitly.

### Decision 13: reconciliation observations use exact ledger scopes

**Recommended:** one EvidenceRecord may carry one or more EvidenceObservations.
Each observation names exactly:

- HouseholdId;
- PortfolioId;
- AccountId;
- AssetId;
- observed signed raw-E8 quantity; and
- for PhysicalGold, observed whole-piece count when the source evidence supports
  it.

The evidence record's effective date applies to all observations in that
record. Cross-household or incompatible scopes fail before persistence.

A statement or physical count is not forced to contain every account. Missing
scopes remain "not observed" rather than fabricated zero.

### Decision 14: tolerance is explicit, per observation, and never hidden

**Recommended:** every reconciliation item records a non-negative raw-E8
quantity tolerance, default zero. Physical-gold piece tolerance is always zero
in M010.

Comparison is deterministic:

```text
delta = observed quantity - ledger quantity
matched = abs(delta) <= tolerance
```

The displayed result includes all three values and the tolerance. There is no
percentage tolerance, market-value tolerance, or provider-specific fuzzy rule
in M010.

### Decision 15: persist reconciliation runs as audit history and detect staleness

**Recommended:** a ReconciliationRun stores:

- ReconciliationRunId;
- HouseholdId;
- EvidenceId;
- evidence effective date;
- comparison run timestamp;
- method/version code; and
- immutable item snapshots containing scope, observed quantity, derived ledger
  quantity, delta, tolerance, and initial comparison result.

On later read, deterministic code re-derives each ledger quantity as of the same
effective date. If it differs from the stored ledger quantity, the run is shown
as stale and requires a new run. The old run is not rewritten.

This allows a later reversal/correction to invalidate an earlier reconciliation
honestly without mutating reconciliation history.

### Decision 16: distinguish financial match from resolution state

**Recommended:** comparison status and resolution status are separate.

Comparison:

- `Matched`;
- `OutOfTolerance`.

Resolution for an out-of-tolerance item:

- `Open`;
- `CorrectedByTransaction`; or
- `DocumentedException`.

`CorrectedByTransaction` requires a linked Posted transaction and does not
retroactively change the old run's delta. The UI offers a rerun after the
corrective transaction so the new comparison can become Matched.

`DocumentedException` requires a non-empty bounded explanation. It remains
visibly out of tolerance and is never relabeled Matched.

M010 does not add a generic Adjustment posting shortcut merely to make a
reconciliation green. Corrections must use the accepted financial workflows
and immutable reversal/replacement semantics available for the affected facts.

### Decision 17: evidence and reconciliation have strict privacy/logging boundaries

**Recommended:** routine logs contain only stable route/operation/outcome
categories, item counts, size classes, and duration. They must not contain:

- evidence file names;
- evidence labels/notes/references;
- attachment bytes or digest values;
- observed or ledger financial quantities;
- master names/codes;
- resolved filesystem paths;
- cursor payloads;
- raw request bodies;
- SQL, connection strings, or stack traces.

Evidence download is an explicit user action. JSON read contracts expose
metadata and digest only when the caller specifically reads evidence metadata;
they never embed source bytes by default.

Automated tests use synthetic evidence only. Browser screenshots/traces remain
o-default artifacts and must never capture real evidence.

### Decision 18: no valuation or market-reference semantics leak into M010

**Recommended:** M010 compares quantities and pieces only. It does not ask for
current market price, statement market value, unrealized gain, allocation,
performance, FX conversion, or price freshness.

A broker statement's market value may be retained inside the original evidence
file, but it is not parsed into authoritative M010 fields. Dated market/reference
observations remain M011.

### Decision 19: preserve the M006 single-host UI boundary

**Recommended:** implement Ready-mode Razor Pages in the existing UI assembly
and single loopback host. PageModels call Application use cases directly, as
accepted by ADR-008; they do not call the co-hosted JSON API.

Recommended navigation:

- `/ledger` becomes the broad search surface while retaining direct transaction
  explanation;
- `/assets` becomes the derived position/inventory surface;
- `/reconcile` lists evidence, reconciliation state, and create/review flows.

No new SPA, Blazor, desktop shell, file-system browser, remote access, or auth
model is introduced.

### Decision 20: expose only bounded programmatic contracts needed by M010

**Recommended:** add stable JSON read contracts for:

- filtered transaction search;
- derived position inventory;
- lot/custody inventory;
- evidence metadata; and
- reconciliation metadata/results.

Evidence byte download/upload, if accepted by Decision 11, uses dedicated
bounded endpoints rather than embedding base64 in JSON.

Do not introduce a generic query language, generic `/search`, SQL exposure,
OData/GraphQL, or the governed agent-specific read contract reserved for M013.

### Decision 21: add schema and indexes only for durable evidence and measured query needs

**Recommended:** M010 requires a forward EF Core migration for evidence and
reconciliation persistence. The migration number should be the next repository
migration number (currently expected `009_...`), independent from milestone
numbering.

Provisional persisted concepts:

- EvidenceRecord;
- EvidenceAttachment if Decision 11 accepts retained bytes;
- EvidenceTransactionLink;
- EvidenceObservation;
- ReconciliationRun;
- ReconciliationItem; and
- reconciliation resolution links/explanation fields where the final mapping
  keeps constraints clearest.

Use restrictive relationships to durable ledger/master history. Do not cascade
through Posted financial history.

Add transaction/entry/allocation search indexes only after the accepted query
shape is known and real-SQLite query-plan/integration evidence demonstrates the
need. Do not create an index for every possible filter mechanically.

### Decision 22: backup/restore verification is part of M010 definition of done

**Recommended:** because M010 adds durable evidence/reconciliation data and may
add BLOB content, milestone verification must create synthetic evidence, run a
reconciliation, create and independently verify a `.wlbackup`, stage it to a
separate target, start a fresh process against the staged copy, and read back:

- evidence metadata;
- attachment byte length and SHA-256 when attachments are accepted;
- evidence observations;
- reconciliation run/items/resolution;
- the same underlying ledger-derived position and lot facts.

No M010 change weakens M004 workspace binding, package integrity, plaintext
boundary, path safety, exclusive ownership, or explicit restore semantics.

## In scope

- bounded broad transaction search over existing durable ledger history;
- opaque filter-bound pagination;
- derived current and as-of position inventory;
- derived lot and custody inventory for currently supported asset families;
- exact physical-gold gross/piece/fineness/fine-weight inventory projection;
- evidence metadata and transaction links;
- optional retained bounded source attachment if Decision 11 is accepted;
- exact evidence observations;
- deterministic quantity reconciliation;
- explicit tolerance, mismatch, staleness, correction links, and documented
  exceptions;
- Ready-mode Ledger, Assets, and Reconcile UI;
- bounded programmatic read contracts;
- required EF migration/indexes;
- focused privacy, real-SQLite, browser, migration, backup, and restore tests.

## Out of scope

- market prices, FX observations, NAVs, physical-gold bid/ask, or source
  providers (M011);
- valuation, current market value, realized/unrealized gain presentation,
  performance, time-weighted or money-weighted return (M011/M012);
- goal, reserve, allocation policy, drift, monthly plan, optimization, or
  forecasting (M012);
- decision journal and agent-specific governed read models (M013);
- autonomous or automatic correction/posting;
- broker/bank API synchronization, credential scraping, email ingestion, OCR,
  CSV/Excel bulk transaction import, or generic import framework;
- generic Adjustment workflow or new financial transaction types solely for
  reconciliation;
- master-data edit/delete/archive administration;
- remote/home-server access, authentication, authorization, or transport
  security changes;
- evidence OCR, semantic indexing, full-text document search, malware scanning
  service integration, or cloud object storage;
- evidence deletion/retention policy across historical backup generations;
- generic export/reporting framework;
- materialized authoritative balances or analytics caches.

## Required behavior

### Transaction search

- Validate Household and every supplied exact master scope before querying.
- Normalize bounded filter input deterministically.
- Reject invalid date ranges, unsupported statuses/types, oversized text, and
  cursor/filter mismatch with stable sanitized errors.
- Return a bounded page and opaque continuation cursor.
- Keep each result's stable TransactionId and complete current display context.
- Preserve direct navigation to the existing transaction explanation.
- Ensure entry-level filters such as Asset/Account/Portfolio/Institution select
  a transaction once even when several entries match.
- Never change ledger facts as a consequence of search.

### Position and lot inventory

- Derive quantity only from effective Posted history.
- Use checked fixed-point arithmetic.
- Distinguish valid zero, unknown scope, and missing evidence.
- Support current and accepted as-of queries without persisting snapshots as
  truth.
- Return enough source identities/counts for a reviewer to trace the result.
- Derive lot current/global/custody quantity from signed allocations.
- Preserve Known/Unknown/NotApplicable semantics where applicable.
- For gold, derive pieces from signed piece movement and fine weight from gross
  quantity plus immutable fineness.

### Evidence

- Evidence creation is explicit and household-scoped.
- Evidence never posts or edits a LedgerTransaction.
- Transaction links cannot cross households.
- If attachments are accepted: size/type validation occurs before persistence;
  SHA-256 is computed by trusted code over the stored bytes; download does not
  execute active content; retry never creates duplicate retained content
  accidentally.
- Superseded evidence remains inspectable.
- Sensitive content is absent from operational logs.

### Reconciliation

- Compare exact observation scopes against ledger-derived quantities at the
  evidence effective date.
- Persist the method/version and exact comparison inputs/results.
- Never infer an unobserved scope as zero.
- Never modify a Posted transaction to resolve a difference.
- Keep out-of-tolerance cases visible until explicitly resolved.
- Keep documented exceptions visibly different from matches.
- Detect stale persisted runs by re-deriving their ledger side.
- Link corrective transactions only when they are Posted and household-safe.

## Invariants

M010 must preserve all accepted ledger/lot invariants, including:

- ledger facts remain the source of truth;
- Posted transactions and their effective facts are immutable;
- reversal remains a separate Posted transaction;
- positions and lot remaining quantities are derived;
- financial quantities use fixed-point integer storage and checked arithmetic;
- Unknown is never converted to zero;
- AssetLot remains acquisition lineage, not custody;
- lot allocations reconcile exactly and cannot produce negative effective
  quantity;
- PhysicalGold piece movement remains independent from gross movement and cannot
  become negative globally or by custody scope;
- evidence and reconciliation data can reference ledger facts but cannot become
  financial entries by side effect;
- no external document, market value, or user explanation overrides ledger
  arithmetic.

## API and UI contract

Exact route names remain subject to implementation review, but the accepted
contract should retain these shapes:

```text
GET  /api/households/{householdId}/ledger/search
GET  /api/households/{householdId}/inventory/positions
GET  /api/households/{householdId}/inventory/lots
GET  /api/households/{householdId}/evidence
GET  /api/households/{householdId}/evidence/{evidenceId}
GET  /api/households/{householdId}/reconciliations
GET  /api/households/{householdId}/reconciliations/{reconciliationRunId}
```

If retained attachments are accepted, use dedicated upload/download routes with
bounded multipart/body handling. Mutation contracts for evidence/reconciliation
remain explicit Application use cases and do not become a generic CRUD API.

Human UI follows the existing Turkish-first presentation/localization boundary.
Exact identifiers, raw E8 values, hashes, and diagnostic metadata may be shown
under progressive disclosure but are not the primary interaction language.

## Persistence impact

Expected forward migration work:

- add normalized evidence/reconciliation tables with stable explicit text codes;
- add optional BLOB storage only if Decision 11 is accepted;
- add restrictive foreign keys and household-consistency protections;
- add uniqueness/idempotency constraints required by the accepted evidence
  command contract;
- add search/inventory indexes only where the final query plan needs them;
- preserve every existing M001-M009 table, guard, trigger, and migration
  behavior unless a tested forward change is explicitly required.

The migration must apply cleanly to an M009 database and Down must remove only
M010-owned schema/index objects while restoring the exact preceding behavior.
No migration may fabricate evidence or reconciliation rows for existing ledger
history.

## Acceptance criteria

M010 may become Verified only when all accepted decisions are implemented and
at minimum the repository proves:

1. broad search returns the correct immutable transaction set for every accepted
   filter individually and in representative combinations;
2. cursor pagination has no duplicate/omitted rows across same-date ties and
   refuses reuse with altered filters/household;
3. entry-level Asset/Account/Portfolio/Institution filters do not duplicate a
   transaction when multiple entries match;
4. search preserves current display context and exact stable transaction links;
5. current inventory equals deterministic sums from Posted history for cash,
   Currency, Fund, Equity, and PhysicalGold synthetic cases;
6. as-of inventory excludes later effective transactions and handles reversals
   according to their preserved effective dates;
7. valid exact zero is distinct from unknown/cross-household scope;
8. lot inventory reproduces original quantity/cost status and current effective
   global/custody quantities without stored remaining balances;
9. gold inventory reproduces exact current gross, pieces, fineness, and derived
   fine weight after purchase/opening/sale/transfer/reversal history;
10. metadata-only evidence round-trips and links only to same-household
    transactions;
11. if attachments are accepted, allowed synthetic files round-trip byte-for-
    byte with stable SHA-256 and disallowed/oversized content is rejected before
    mutation;
12. evidence supersession preserves the original record and does not rewrite
    attachment bytes;
13. a matched reconciliation records exact observed/ledger/delta/tolerance data;
14. an out-of-tolerance reconciliation remains visible as open;
15. documented exception requires explanation and remains visibly not Matched;
16. corrective-transaction resolution accepts only a Posted same-household
    transaction and a subsequent rerun reflects its effective history;
17. a later reversal or other effective-history change makes the earlier run
    visibly stale rather than silently updating its stored result;
18. reconciliation never writes a financial transaction itself;
19. direct SQL attempts cannot bypass required evidence/reconciliation
    household/shape constraints where practical;
20. APIs and pages return sanitized errors and logs contain no evidence content,
    master labels/references, financial values, raw cursors, paths, SQL, or
    request bodies;
21. JavaScript-disabled and keyboard navigation remain functional for the core
    Ledger/Assets/Reconcile paths;
22. a narrow viewport remains usable without horizontal data loss in the
    critical flows;
23. the full M001-M009 regression suite remains green;
24. EF reports no pending model changes;
25. forward/down/forward migration testing preserves M009 behavior;
26. a synthetic backup independently verifies, stages, and fresh-process reads
    back the complete M010 state; and
27. `PROJECT_STATE.md`, `ROADMAP.md`, schema docs, data-capture docs, and UX docs
    agree with verified implementation reality.

## Test scenarios

Focused implementation suites should cover at least:

### Application

- search filter validation and normalized filter fingerprint;
- filter-bound cursor behavior;
- position current/as-of checked sums;
- lot inventory derivation and zero/exhausted handling;
- evidence create/link/supersede rules;
- attachment size/type/hash rules if accepted;
- reconciliation comparison, tolerance, resolution, and staleness;
- cross-household rejection and stable sanitized exceptions.

### Infrastructure / real SQLite

- search joins, duplicate suppression, deterministic ordering, and pagination;
- current/as-of position queries over mixed history;
- lot/custody projections over purchase, sale, transfer, opening, and reversal;
- PhysicalGold gross/piece custody consistency;
- evidence/reconciliation mappings, stable codes, FKs, uniqueness, and direct-
  SQL guard tests;
- BLOB round trip and SHA verification if accepted;
- representative query-plan/index coverage;
- migration M009 -> M010 -> M009 -> M010;
- preservation of every M008/M009 database guard.

### API/UI

- every accepted search filter and malformed query handling;
- Assets position -> lot -> transaction traceability;
- evidence metadata create/read/link and attachment upload/download if accepted;
- matched and mismatched reconciliation review;
- documented exception and corrective-transaction linkage;
- stale-run warning after later effective history;
- privacy-safe error/log capture;
- restart readback.

### Browser and recovery

At minimum one real-Chromium journey should prove:

1. Ready startup with synthetic M009 history;
2. filtered Ledger search;
3. Assets position and lot drilldown;
4. synthetic evidence capture;
5. reconciliation producing one Matched and one OutOfTolerance item;
6. explicit documented-exception or correction linkage;
7. clean restart and exact readback;
8. no external request; and
9. artifact cleanup.

A separate fresh-process recovery test must prove the accepted M010 evidence and
reconciliation data survives the normal M004 backup/verify/stage path.

## Verification commands

Implementation should name focused filters as they are created. The milestone
still requires the repository-standard full checks:

```powershell
dotnet tool restore
dotnet restore WealthLedger.slnx --verbosity minimal
dotnet test WealthLedger.slnx --no-restore --verbosity minimal
dotnet format WealthLedger.slnx --verify-no-changes --no-restore --verbosity minimal
dotnet ef migrations has-pending-model-changes --project src/WealthLedger.Infrastructure/WealthLedger.Infrastructure.csproj --startup-project src/WealthLedger.Infrastructure/WealthLedger.Infrastructure.csproj --context WealthLedgerDbContext --no-build
```

Build the BrowserTests project and install the repository-pinned Chromium before
browser verification exactly as documented in `README.md`.

When the GitHub Actions verification workflow is merged, its pull-request result
is additional reproducible evidence; it does not replace the milestone-specific
migration and disposable recovery checks.

## Documentation updates

When implementation is actually verified:

- update `docs/PROJECT_STATE.md` with the factual M010 checkpoint;
- change M010 in `docs/ROADMAP.md` to Verified and link this milestone;
- update `docs/UX_MVP.md` for implemented Ledger/Assets/Reconcile behavior;
- update `docs/DATA_CAPTURE.md` with the accepted evidence and reconciliation
  persistence rules;
- update `docs/DATABASE_DESIGN.md` with the new schema/indexes and explicit
  non-authoritative inventory boundary;
- update `docs/SECURITY_OPERATIONS.md` for evidence privacy and backup coverage;
- update `docs/OPERATIONS.md` only where backup verification/recovery evidence
  materially changes;
- add a new ADR only if acceptance creates a genuinely cross-cutting decision
  beyond this bounded milestone, such as a durable attachment-storage boundary
  that should govern later document classes.

Do not rewrite Verified M001-M009 milestone history.

## Suggested commit boundaries

After acceptance, a practical serial implementation order is:

1. `docs(m010): start accepted searchable inventory and reconciliation work`
2. `feat(search): add bounded ledger search contracts`
3. `feat(inventory): add derived position and lot inventory`
4. `feat(evidence): persist evidence and transaction links`
5. `feat(reconciliation): add deterministic comparison and resolution`
6. `feat(api): expose M010 bounded contracts`
7. `feat(ui): add ledger assets and reconciliation workflows`
8. `test(m010): add browser privacy migration and recovery evidence`
9. `docs(state): record verified M010 checkpoint`

If Decision 11 accepts retained attachment bytes and the persistence change is
large enough to review independently, split `feat(evidence)` into metadata/link
and attachment-storage commits while keeping one M010 milestone.

## Risks and rollback

- Broad search can become an accidental performance project. Keep the query
  bounded and add measured indexes rather than a new search subsystem.
- Inventory can accidentally become a second balance system. Reject persistent
  current/remaining fields and prove derivation from ledger/allocation history.
- Evidence attachments can enlarge the database and backups. Bound each file,
  test backup size/restore behavior, and keep attachments optional.
- User-supplied files can become an active-content risk. Restrict formats and
  force download rather than inline execution.
- A reconciliation record can be mistaken for a ledger correction. Keep the
  comparison aggregate completely separate and never let it post implicitly.
- Historical statements can be compared to current holdings accidentally. Make
  the effective evidence date mandatory and use deterministic as-of queries.
- A later reversal can invalidate old comparisons. Preserve old runs and detect
  staleness instead of mutating audit history.
- New FKs/indexes can damage M009 migration reversibility. Test full
  forward/down/forward behavior on real SQLite before verification.
- If the migration or recovery work fails, the existing M004 process remains
  the rollback boundary: preserve the live M009 database and last verified
  backup, repair the forward migration on an isolated copy, and do not hand-edit
  real ledger or evidence rows.
