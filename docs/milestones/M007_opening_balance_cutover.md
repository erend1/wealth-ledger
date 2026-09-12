# M007: Controlled Opening-Balance Cutover

Status: Verified

Owner: Human and agent

Accepted: 2026-09-10 (all fifteen Recommended decisions exactly as written)

Started: 2026-09-10

Verified: 2026-09-11

Last reviewed: 2026-09-11

The human owners explicitly accepted the complete bounded contract and all
fifteen Recommended decisions exactly as written on 2026-09-10. Implementation
and verification completed on 2026-09-11 directly on `main`. The repository's
no-commit and no-remote-mutation boundary remains in force until separately
authorized.

## Objective

Let a household start using WealthLedger without inventing historical purchase
transactions. The user records one explicit opening-balance fact for one asset
in one account and portfolio as of one business date, with exact lot lineage
when applicable and with historical cost represented as Known only when
supported or explicitly Unknown when it is not.

M007 covers cash and foreign currency, investment-fund units, equity shares,
and physical-gold lots. It preserves the ledger as the source of truth:
positions remain sums of effective Posted entries, lot quantities remain sums
of allocations, and fine-gold weight remains derived from gross weight and
fineness.

## User outcome

A user can:

1. select an existing household and portfolio;
2. select or narrowly create the account, institution, currency, or asset
   needed for this opening balance;
3. enter an exact positive quantity and an opening as-of date;
4. describe one or more acquisition-lineage lots for funds, equities, and
   physical gold;
5. record a supported historical cost or deliberately mark it Unknown without
   substituting current market value;
6. enter physical-gold gross weight, fineness, piece count, and optional
   identifying facts while seeing exact derived fine-gold weight;
7. review every normalized source fact and exact total before posting;
8. post once through a retry-safe, atomic command;
9. inspect the persisted transaction, lots, allocations, and resulting derived
   position; and
10. correct a mistake through an immutable reversal followed by a separately
    posted replacement.

The workflow never asks the user for a GUID, raw E8 quantity, integer minor
units, or a fabricated unit price. The JSON API continues to expose exact
integer transport representations for programmatic callers.

## Current repository evidence

The proposal began from the clean `main` checkpoint at commit `2f0565b`, where
M006 is Verified. Baseline verification on 2026-09-08 completed with 580
passing tests:

- Domain: 83;
- Application: 122;
- Infrastructure: 172;
- UI: 63;
- API/UI host: 114;
- Operations: 23;
- Playwright browser: 3.

The first `--no-restore` run found a missing generated
`WealthLedger.UI/obj/project.assets.json`. A normal solution restore completed,
and the subsequent full test run passed. This was environment preparation, not
a product-test failure.

On 2026-09-09, documentation-only merge commit `3586550` was fast-forwarded
from `origin/main` and reconciled before this proposal's final planning review.
It adds privacy-sensitive Playwright artifact exclusions and refreshes the
ROADMAP real-data readiness gate. It changes no production source, migration,
package, or test behavior, so the 580-test behavioral baseline remains the
applicable source checkpoint. The refreshed ROADMAP recognizes M006 coverage
for rendered-page, error-page, and captured-log privacy, notes that governed
exports do not exist, and leaves independent position reconciliation as the
one open real-data readiness bullet.

Repository inspection establishes the following implementation facts:

- `LedgerTransaction` already has an `OpeningBalance` type. Posting currently
  requires every opening entry to be positive and Principal, forbids a unit
  price and `CashFlowDetail`, but does not require one entry, forbid transaction
  costs, restrict asset families, require lot reconciliation for Optional
  assets, require physical-gold detail, or prevent semantic duplicates.
- `AssetLot` represents acquisition lineage and has no Account or Portfolio.
  It can be created only for Optional or Required lot-tracked assets, attaches
  its positive opening allocation, and derives current quantity from signed
  allocations.
- `CostBasis` already represents Known with a non-negative `Money` amount,
  Unknown with no amount, and NotApplicable with no amount. Cost basis exists
  only on `AssetLot`.
- `PhysicalGoldLotDetail` already stores integer-parts-per-million fineness,
  positive piece count, and optional hallmark, certificate reference, and
  note. It does not calculate fine-gold weight or map human karat choices.
- Quantity and unit-price values use signed 64-bit E8 storage. Money uses
  signed integer minor units. Existing boundary code deliberately uses
  `decimal` and checked arithmetic rather than binary floating point.
- the SQLite graph contains restrictive foreign keys, posted-history guards,
  lot/entry asset and sign checks, non-negative lot-balance protection, and
  exact allocation reconciliation for Required lot-tracked entries. Optional
  lot tracking currently permits an absent or partial allocation.
- `ILedgerSubmissionStore` can atomically persist one transaction, all its
  entries, multiple new lots, allocations, physical-gold detail rows, and one
  command receipt. The current receipt has one optional `AssetLotId`, which is
  insufficient to enumerate a multi-lot result but can remain null when the
  transaction identity is the canonical opening result.
- ADR-006 and the M002 writers already implement receipt-first idempotent
  replay, versioned canonical fingerprints, same-key race recovery, stable
  transaction readback, and `201 Created` responses with resolvable locations.
- M003 already provides generic reversal preview and posting, exact inverse
  entries and same-lot allocations, one reversal per original, and downstream
  acquisition-dependency protection. The original remains Posted.
- M005 provides current master-data navigation, recent Posted activity, and a
  point-position query for an exact household, portfolio, account, and asset
  scope.
- M006 provides one loopback host, server-rendered Razor Pages, direct
  UI-to-Application invocation, exact Turkish-first presentation, fail-closed
  startup modes, guided initial setup, transaction explanation, and read-only
  Today, Ledger, and Settings pages.
- the M006 initial setup creates only one base currency, household, optional
  member, institution, portfolio, account, cash asset, and required-lot fund
  asset. There is no later master-data write workflow.
- current transaction readback and UI transaction detail do not expose
  `PhysicalGoldLotDetail`. Ready UI has no financial write or reversal form.
- there is no semantic opening-balance uniqueness constraint or query and no
  accepted formal cutover-completion state.

At planning time, `DATABASE_DESIGN.md` described implemented persistence through
M005 but its migration summary stopped at `004_LedgerNavigationQueries` even
though source and `PROJECT_STATE.md` also contained `005_WorkspaceIdentity`.
M007 documentation reconciliation corrected that omission and added the actual
006 trigger/index design; the original mismatch was never evidence that
migration 005 was absent.

The isolated learning environment prepared during planning remains outside the
repository:

```text
%LOCALAPPDATA%\WealthLedger-LearningLabs\M007-20260908-153913
%LOCALAPPDATA%\WealthLedger-LearningBackups\M007-20260908-153913
```

At the recorded checkpoint its configured database file did not exist, no
workspace data had been submitted, and no backup had been created. The paths
must be revalidated before a later process is started; ambient configuration
must never be trusted as a continuation mechanism.

## Why now

M003 makes immutable mistakes correctable, M004 protects the local database,
M005 exposes stable selection and readback contracts, and M006 provides the
human shell and first-run safety boundary. Opening positions are therefore the
smallest next vertical slice that can make a new ledger useful without
pretending historical buys occurred.

M008 and M009 depend on trustworthy opening lots for later sales, transfers,
and remaining-lot calculations. M010 depends on recorded opening facts before
it can reconcile them against independent statements or physical inventory.
Adding normal trading, valuation, or reconciliation first would either leave
pre-existing holdings absent or invite fabricated history.

## Terminology

**Opening balance** is a Posted `OpeningBalance` transaction that states a
positive holding at a cutover business date. It is not a Contribution, Buy,
Adjustment, valuation, or imported approximation of a missing trade.

**As-of date** is the opening transaction's `ExecutionDate`. It says when the
opening quantity is effective in the ledger.

**Acquisition date** is an optional `AssetLot.AcquiredOn` source fact. It may
precede or equal the as-of date and remains absent when unknown.

**Posting timestamp** is the current UTC audit time supplied by `TimeProvider`.
It is not entered by the user and is not an economic date.

**Known cost** is a non-negative historical total cost supported by evidence
for one opening lot. It is not current value and is not reverse-engineered from
today's price.

**Unknown cost** records that no supported historical cost is available. It
has no amount or currency and is distinct from zero.

**Not applicable cost** describes an un-lotted monetary opening for which
acquisition cost is not an economic fact. Because cost basis is stored only on
lots, this state is presented and validated for cash/currency but does not
create a synthetic lot or cost row.

**Effective transaction** is a Posted non-reversal transaction without a
Posted reversal. Its Posted reversal is the audit fact that neutralizes it;
Draft or Cancelled rows do not neutralize it.

**Semantic duplicate** is a second logical opening for the same household,
portfolio, account, and asset. It is distinct from retrying the same command
with the same idempotency key.

**Exact lot** is one evidence-supported acquisition lineage with its own
quantity, optional acquisition date, cost-basis state, and provenance-relevant
physical details.

**Aggregate opening lot** is one evidence-supported aggregate quantity and
total cost, such as a broker-reported position cost whose constituent
acquisition dates are unavailable. It is not a fabricated trade or fabricated
average price.

**Gross weight** is the authoritative quantity of a physical-gold lot.
**Fine-gold weight** is an exact derived display result from gross weight and
fineness and is not independently editable or persisted as a balance.

## Decisions and decision gates

Every decision below was explicitly accepted by the human owners on
2026-09-10 exactly as recommended.

### Decision 1: narrowly create missing reference data

**Accepted:** keep Household and Portfolio as required existing choices.
Within the opening-balance flow, permit narrowly scoped create-and-use actions
for a missing Currency, Institution, Account, or Asset. Do not add generic
master-data administration, editing, deletion, archival, or reactivation.

Reference creation is an explicit action before financial review. It commits
only the selected master fact and cannot create a holding. Stable code is its
natural retry identity: an equivalent existing record is returned, while the
same code with different normalized facts is a conflict. Unique SQLite keys
remain the concurrency authority.

This deliberately allows an unused reference to remain if the user later
cancels the opening form. Hiding reference creation inside the final financial
transaction would enlarge its fingerprint and rollback surface, obscure what
the first step changed, and couple reusable master facts to one ledger command.

Foreign-currency support explicitly includes the otherwise hidden Currency
reference prerequisite. An asset cannot name a base or cost currency that does
not exist.

### Decision 2: one scope and asset per opening command

**Accepted:** one opening command contains exactly one Household, one
Portfolio, one Account, one Asset, one as-of date, one positive entry, and zero
or more lots as dictated below. A command never mixes accounts, portfolios,
assets, or dates.

The selected asset may have multiple lots. This is smaller to review and
reverse, gives clearer provenance, isolates failures, produces a stable retry
fingerprint, and permits each entered position to be reconciled independently.

A multi-asset opening batch is out of scope. UI convenience may retain a link
to start another opening after receipt, but each submission has a new command
identity and transaction.

### Decision 3: one effective opening and no earlier effective scope history

**Accepted:** permit at most one effective opening balance for an exact
Household, Portfolio, Account, and Asset tuple. The as-of date is not part of
the uniqueness scope. An incorrect opening must be reversed before a corrected
replacement is posted.

Additionally, the first opening is allowed only when that exact tuple has no
other effective Posted entry history. Fully reversed transaction pairs do not
count as effective history. This keeps opening cutover from silently adding a
snapshot to an already active ledger and makes the immediate expected position
manually understandable.

Application preflight gives a useful error. An additive SQLite posting guard
must enforce the same rule at the Draft-to-Posted boundary so concurrent
different-key requests cannot both succeed. The losing request returns a
sanitized conflict and, when an existing effective opening won, its transaction
identity and readback location.

Idempotent replay is evaluated before current semantic eligibility. A
previously successful request with the same scope, key, and fingerprint always
returns its original receipt even though the posted transaction now makes a
new submission ineligible.

### Decision 4: exact behavior for every lot-tracking mode

**Accepted:** apply these M007 rules:

| Asset family | Accepted stored mode | Opening behavior |
|---|---|---|
| Cash or Currency | None | One positive entry, no AssetLot or allocation; cost is presented as NotApplicable |
| Fund or Equity | Optional or Required | One or more opening lots are mandatory; positive allocations reconcile exactly to the entry |
| PhysicalGold | Required | One or more opening lots are mandatory; positive gross-weight allocations reconcile exactly to the entry and every lot has gold detail |

Fund or Equity with `LotTrackingMode.None` is rejected by M007. Allowing it
would make Known versus Unknown acquisition cost impossible to record because
the current model stores cost basis only on AssetLot. For opening cutover,
Optional means that the asset supports optional lot use in other future
workflows; M007 still chooses complete lineage and exact reconciliation.

The command cannot create a zero-quantity lot, a lot larger than the entry, an
unallocated remainder, a partial Optional allocation, or an allocation with a
different asset or sign.

### Decision 5: fund and equity cost evidence

**Accepted:** support these explicit shapes:

1. exact historical lots: one command lot per evidence-supported lineage, each
   with its own quantity, optional acquisition date, and Known or Unknown cost;
2. supported aggregate broker cost with unknown acquisition dates: one
   aggregate lot for the full reported quantity, `AcquiredOn = null`, and
   `CostBasis.Known(total broker cost)`;
3. completely unknown cost: one or more lots with `CostBasis.Unknown()`, no
   amount, and no currency.

Mixed Known and Unknown lots are valid when evidence differs by lot. A Known
zero amount remains valid because zero may be a supported fact, but the UI must
call it out in review and require the ordinary explicit final posting action.

No opening entry has `UnitPrice`. The workflow never divides aggregate cost by
quantity to manufacture an acquisition price and never uses a current market
quote as cost.

### Decision 6: physical-gold lot grouping

**Accepted:** pieces may share one lot only when the user asserts they are
one evidence/inventory group and all relevant characteristics match:

- Asset product/form identity;
- fineness;
- acquisition-date knowledge and value;
- cost-basis status and, for Known cost, currency and group total;
- hallmark;
- certificate/reference;
- provenance expressed by the transaction and lot notes.

When evidence lists different individual weights, fineness, dates, costs, or
identifiers, create separate lots. When evidence supplies only one aggregate
gross weight for genuinely identical pieces, store the aggregate gross weight
and total piece count in one lot. Per-piece weights are not inferred from the
average and are not introduced as a new M007 table.

Product/form identity belongs in the selected PhysicalGold Asset code and
name. M007 does not add a second generic product-name column to the gold detail.

### Decision 7: karat, fineness, and fine-gold-weight presentation

**Accepted:** store only exact integer parts per million. The UI offers
these explicit convenience pairs and always shows both labels:

| UI preset | Stored fineness |
|---|---:|
| 24K / 999.9 per mille | 999,900 ppm |
| 22K / 916 per mille | 916,000 ppm |
| 18K / 750 per mille | 750,000 ppm |
| 14K / 585 per mille | 585,000 ppm |
| 8K / 333 per mille | 333,000 ppm |

The UI also accepts precise fineness from `0.001` through `1000` per mille with
at most three fractional digits. Conversion is exact `perMille * 1,000`; extra
precision is rejected rather than rounded. Pure `1000 per mille` therefore
maps to `1,000,000 ppm` even though the 24K convenience preset maps to the
explicitly labelled `999.9 per mille` convention.

Do not infer arbitrary fineness from `karat / 24`; hallmark conventions and
the mathematical ratio are not universally interchangeable.

Derived fine-gold grams are calculated from positive gross E8 quantity and
fineness ppm with checked integer/decimal arithmetic. The exact result can have
up to fourteen fractional gram digits and is presented as an exact decimal
string with insignificant trailing zeros removed. It is not rounded into or
persisted as another E8 Quantity and never uses `double` or `float`.

### Decision 8: three distinct date meanings

**Accepted:** map the entered opening as-of date only to
`LedgerTransaction.ExecutionDate`. Leave OrderDate and SettlementDate null.

Each lot's optional `AcquiredOn` remains null when unknown and, when supplied,
must be no later than the opening as-of date. The as-of date must be no later
than the host's current local operating date obtained through `TimeProvider`;
tests use an explicit time zone and clock. If an Account has `OpenedOn`, the
opening as-of date cannot precede it.

`CreatedAtUtc` and `PostedAtUtc` are generated from one current UTC instant and
are never supplied by the caller. A future historical acquisition date is not
silently replaced by the as-of or posting date.

### Decision 9: minimum provenance and the M010 boundary

**Accepted:** require a trimmed, non-empty transaction Note of at most 2,000
characters explaining the opening source, such as a synthetic statement or
physical inventory count. Reject control characters. ExternalReference is
optional, trimmed, and at most 256 characters.

Gold hallmark, certificate reference, and lot note keep their existing maximum
lengths and meanings. They supplement rather than replace the required
transaction note.

M007 stores text references only. Document/blob upload, hashes, statement
lineage, reconciliation cases, discrepancy resolution, and evidence retention
workflows remain M010. The receipt says that the position has not thereby been
independently reconciled.

### Decision 10: persisted readback and derived-position verification

**Accepted:** after commit, reconstruct the result from persisted ledger
history rather than returning only the in-memory graph. Verification reports:

- transaction identity, type, status, scope, and as-of date;
- requested and persisted entry quantity;
- created lot identities and persisted opening allocations;
- allocation total and exact reconciliation result for tracked assets;
- Known, Unknown, or NotApplicable cost presentation;
- physical-gold facts and exact derived fine-gold weight;
- the current derived position for the exact scope and asset;
- whether another later effective ledger entry now contributes to that current
  position.

The opening effect must match the submitted quantity exactly. Tracked
allocations must match that entry exactly. Under the no-prior-effective-history
rule, the position at the opening as-of date initially equals the opening
quantity. If another valid transaction is posted after the opening before a
later receipt read, the page shows the newer derived position and explains the
additional history rather than claiming corruption.

No verification result is stored as an authoritative balance or receipt
snapshot. Refresh recomputes it from persisted facts.

### Decision 11: Application, API, UI, and receipt boundaries

**Accepted:** add focused Application use cases and ports, not a generic
repository or service layer:

- `ListOpeningBalanceChoicesUseCase`;
- `CreateOpeningBalanceCurrencyUseCase`;
- `CreateOpeningBalanceInstitutionUseCase`;
- `CreateOpeningBalanceAccountUseCase`;
- `CreateOpeningBalanceAssetUseCase`;
- `PreviewOpeningBalanceUseCase`;
- `RecordOpeningBalanceUseCase`;
- `GetOpeningBalanceVerificationUseCase`;
- an opening-specific reference write port and effective-history query port.

Reuse `ILedgerSubmissionStore`, command receipts, transaction readback,
position derivation, and M003 reversal services where their accepted contracts
fit. Do not add child repositories or expose EF rows.

Use operation code `RECORD_OPENING_BALANCE`. The canonical versioned
fingerprint includes all normalized source facts and a deterministic sorted lot
representation. Generated transaction/lot/allocation IDs, server timestamps,
derived fine weight, current position, and presentation strings are excluded.
Semantically indistinguishable duplicate lot rows are rejected with guidance
to group them, making lot order irrelevant to replay identity.

Keep the current optional receipt `AssetLotId` null for this multi-lot command.
The receipt's TransactionId is canonical; persisted readback supplies all lot
IDs. Do not add one arbitrarily selected lot ID or widen the receipt schema
solely for M007.

The JSON API accepts raw E8 quantities, integer minor units, integer ppm, ISO
dates, and stable text codes. The Razor UI parses human decimal quantities,
money, and per-mille fineness, calls Application directly, and never sends an
HTTP request to its co-hosted API.

### Decision 12: no formal cutover-complete state

**Accepted:** do not add a household-wide, portfolio-wide, or account-wide
`OpeningCutoverComplete` flag or table. Keep the opening workflow available.
Per-scope semantic protection prevents accidental repetition, while explicit
warnings explain that every asset must be entered and independently checked.

A formal completion state would require knowing the intended inventory set and
its reconciliation evidence. Those are M010 concerns, not facts derivable from
the mere presence of one or more opening transactions.

### Decision 13: bounded reversal and replacement interaction

**Accepted:** reuse M003 without changing its accounting semantics. The
opening receipt and transaction detail expose a bounded Reverse action for an
opening transaction. The page loads the existing eligibility preview, shows
the exact inverse entry and allocation effects, requires a reason, and submits
the existing retry-safe reversal command.

Do not build a generic transaction editor or an atomic reverse-and-replace
request. After successful reversal, offer a link that starts a fresh opening
workflow with a new idempotency key. There is no structured replacement link;
the reversal reason and new opening provenance explain the correction.

If a later effective transaction has allocated one of the opening lots, the
existing M003 dependency rules block reversal until that dependent transaction
is itself reversed. No raw deletion or mutation is offered.

### Decision 14: accepted asset and account combinations

**Accepted:** constrain the narrow reference creator and posting use case to
these combinations:

| Asset type | Derived base unit | Base currency | Lot mode | Compatible account types |
|---|---|---|---|---|
| Cash | CurrencyUnit | Required and equal to household base currency | None | Cash, Investment, Pension |
| Currency | CurrencyUnit | Required and different from household base currency | None | Cash, Investment, Pension |
| Fund | FundUnit | Required | Optional or Required | Investment, Pension |
| Equity | Share | Required | Optional or Required | Investment, Pension |
| PhysicalGold | GrossGram | Required | Required | PhysicalVault |

The user chooses the economic type, base currency, and allowed lot mode; the
Application derives and validates the base unit. It does not accept arbitrary
type/unit combinations even though the general `Asset` constructor currently
can represent them.

Investment and Pension accounts require an active Institution. Cash and
PhysicalVault accounts may use an Institution but do not require one. M007 does
not use AccountType.Other to bypass compatibility. A bank “gold account” whose
legal asset is not possessed physical gold is outside this PhysicalGold shape
and requires a later explicit product decision.

The selected Portfolio must be Active; Account, optional Institution, and Asset
must be active and household relationships must match. Historical readback
continues to show inactive current references, but new opening writes cannot
use them.

### Decision 15: additive database protection and migration shape

**Accepted:** add one forward migration after `005_WorkspaceIdentity`, named
for the opening-balance guards rather than for roadmap numbering, expected as
`006_OpeningBalanceCutoverGuards`.

Use additive named SQLite posting triggers and supporting indexes where query
plans demonstrate a need. Do not edit a historical migration and do not
replace the M003 consolidated trigger unless preserving every existing clause
is proved necessary. The new guard applies at the Draft-to-Posted transition
and protects:

- the one-entry opening shape;
- no cash flow, transaction costs, or unit price;
- asset/account compatibility;
- cash/currency no-lot behavior;
- exact allocation for M007 fund, equity, and gold openings, including assets
  whose general mode is Optional;
- required physical-gold detail;
- acquisition-date ordering; and
- semantic duplicate/no-effective-history concurrency.

Migration application must fail closed if incompatible legacy opening rows
make the new guarantee unprovable. The `Down` path drops only the new M007
objects and restores the exact pre-M007 guard set. No authoritative position,
remaining quantity, average cost, value, return, or completion table is added.

## In scope

- a dedicated one-asset opening-balance Domain/Application workflow;
- positive cash and foreign-currency opening entries without lots;
- positive investment-fund and equity openings with one or more exact lots;
- physical-gold openings with gross weight, fineness, piece count, optional
  hallmark/certificate/note, and exact derived fine weight;
- Known and Unknown historical lot cost with no acquisition unit price;
- optional historical acquisition dates;
- narrow create-and-use Currency, Institution, Account, and Asset behavior;
- exact human decimal parsing and review-time normalization;
- command receipt, canonical fingerprint, equivalent replay, conflicting
  replay, double-submit, and concurrency behavior;
- semantic duplicate and pre-existing effective-history protection;
- atomic persistence of transaction, entry, all lots, gold details,
  allocations, and receipt;
- JSON preview, post, verification, and narrow reference-data routes;
- direct Application-driven Razor Pages Identify, facts, review, post, receipt,
  and bounded opening reversal interaction;
- transaction readback and UI detail extension for physical-gold facts;
- focused current derived-position verification after posting;
- database migration and direct-SQL protection for accepted cross-aggregate
  rules;
- synthetic browser and operational verification; and
- canonical documentation reconciliation after verification.

## Out of scope

- ordinary contributions or withdrawals from the UI;
- normal fund, equity, or physical-gold purchases or sales;
- transfers;
- FIFO sale allocation or realized-gain behavior;
- making-charge lifecycle accounting;
- current market prices, opening valuation observations, FX conversion, or
  price-provider integration;
- current portfolio value, performance, profit/loss, allocation, or dashboard
  totals;
- CSV, Excel, broker, or multi-asset bulk import;
- document/blob upload and evidence hashing;
- broad search, position/lot inventory, reconciliation workbench, or formal
  cutover completion;
- generic master-data CRUD, reactivation, renaming, closing, deletion, or
  archival;
- a generic transaction editor or general-purpose correction UI;
- per-piece physical-gold weight storage;
- bank/digital gold product semantics;
- authentication, remote access, multi-user concurrency policy, or role-based
  authorization;
- AI recommendations, portfolio optimization, or direct agent writes; and
- any M008-M012 lifecycle or analysis behavior.

## Domain rules

An M007 opening transaction:

- has `TransactionType.OpeningBalance` and becomes Posted only through Domain
  validation;
- belongs to one non-empty Household identity;
- has null OrderDate and SettlementDate and one non-future ExecutionDate;
- has a required normalized Note and optional normalized ExternalReference;
- has exactly one Principal entry;
- has one positive non-zero QuantityDelta;
- has no UnitPrice, Consideration entry, CashFlowDetail, or
  TransactionCostComponent;
- never contains a balancing cash entry; and
- cannot be edited or deleted after posting.

For Cash and Currency:

- asset unit is CurrencyUnit and lot tracking is None;
- asset base currency exists and matches the economic denomination;
- no AssetLot, allocation, or cost amount is created;
- cost is presented as NotApplicable; and
- the entered monetary decimal is converted first to currency minor units using
  that Currency's `MinorUnitDigits`, then deliberately to the E8 quantity
  representation. A programmatic raw E8 input must map back to an exact integer
  minor-unit amount for that currency. Unsupported fractional minor units and
  overflow are rejected.

For Fund and Equity:

- entry quantity and every lot quantity use positive exact E8;
- at least one lot is created even when the asset's general mode is Optional;
- the sum of opening allocations equals the entry quantity exactly;
- each lot cost is Known or Unknown, never NotApplicable;
- Known cost has an existing currency and non-negative integer minor-unit
  amount;
- Unknown cost has neither amount nor currency;
- acquisition date is optional and cannot follow the as-of date; and
- no price is inferred from lot or aggregate cost.

For PhysicalGold:

- gross grams are the entry and lot quantities;
- all lots have Required tracking and exact allocation reconciliation;
- every lot has `PhysicalGoldLotDetail`;
- fineness is from 1 through 1,000,000 ppm;
- piece count is positive;
- hallmark, certificate reference, and note retain their existing normalization
  and size rules;
- cost is Known or Unknown, never NotApplicable;
- fine-gold weight is derived and never independently entered; and
- grouping obeys Decision 6.

Cross-aggregate rules are orchestrated by Application with explicit persisted
queries and reinforced by SQLite at posting. AssetLot never gains AccountId or
PortfolioId. Allocation remains the signed relationship between a lot and an
entry.

## Application commands and queries

### Reference choices and focused creation

`ListOpeningBalanceChoicesUseCase` returns current active households,
portfolios, accounts, currencies, institutions, and only the M007-compatible
assets required to render selections. It may compose existing M005 queries but
does not expose `IQueryable` or persistence rows.

The four focused create use cases accept normalized stable code and the minimum
facts defined in Decision 14. Each returns `WasCreated` plus the stable identity
or Currency code. Equivalent retry returns the existing fact. A mismatched
code, inactive collision, or incompatible shape is a conflict. They cannot
update or reactivate an existing row.

### Preview

`PreviewOpeningBalanceUseCase` accepts the same economic payload as the record
command without an idempotency key. It:

1. validates identities, activity, household scope, account dates, asset shape,
   and currency references;
2. parses or receives already exact financial value objects;
3. validates lot/cost/date/gold shapes;
4. reconciles all lot allocations;
5. checks current semantic eligibility as advisory preflight;
6. returns normalized source facts, exact totals, fine-weight derivations,
   provenance, and warnings; and
7. performs no mutation and allocates no durable identities.

Preview success does not reserve the scope. Record repeats every check and the
database is the concurrency authority.

### Record

The recommended command shape is conceptually:

```text
RecordOpeningBalanceCommand
  HouseholdId
  PortfolioId
  AccountId
  AssetId
  AsOfDate
  Quantity
  ExternalReference?
  Note
  Lots[]

OpeningBalanceLotCommand
  Quantity
  AcquiredOn?
  CostBasis
  PhysicalGoldDetail?
```

`RecordOpeningBalanceUseCase.ExecuteAsync(idempotencyKey, command)` follows
this order:

1. validate and normalize the transport-independent command;
2. create the household-scoped receipt scope with operation code
   `RECORD_OPENING_BALANCE`;
3. look up and resolve an existing receipt before live-state validation;
4. load and validate reference data and semantic eligibility;
5. compute the canonical fingerprint;
6. create one Draft OpeningBalance, its one entry, and all required lots,
   details, and initial allocations;
7. post through Domain validation using one UTC instant;
8. atomically try to persist the graph and receipt;
9. recover and validate a concurrent same-key winner when necessary;
10. classify a semantic-guard collision without exposing SQLite text; and
11. read the committed transaction and verification from persistence.

No generated identity or server time participates in equivalence. Lot command
order is canonicalized after normalized facts are validated.

### Verification

`GetOpeningBalanceVerificationUseCase` accepts HouseholdId and TransactionId,
rejects cross-household or non-opening access, obtains authoritative transaction
readback, and calculates the current exact-scope position from effective Posted
entries. It returns no cached or mutable total.

The existing general transaction-detail query is extended so each created lot
can include optional physical-gold source facts. The Application model carries
exact numeric facts; localized formatting remains in UI.

## Required behavior

### Identify

- Opening balance is reachable from a new Record navigation destination only
  in Ready mode.
- The user selects Household, Active Portfolio, compatible active Account, and
  compatible active Asset by human name and stable code.
- The UI does not ask for a generated identity.
- A missing Currency, Institution, Account, or Asset can be created through the
  bounded create-and-use controls. The UI explains that this creates reference
  data, not a holding.
- Changing Asset clears incompatible quantity, lot, cost, and gold inputs.
- As-of date is required and its distinct meaning is explained.

### Enter source facts

- Cash/currency asks for a positive monetary amount in the asset currency and
  displays its normalized minor-unit precision. It shows cost as NotApplicable
  and offers no lot fields.
- Fund/equity asks for a positive quantity with at most eight fractional digits
  and one or more lot rows whose quantities sum exactly to it.
- Each investment lot asks for optional acquisition date and an explicit Known
  or Unknown cost selection. A blank selection is not treated as Unknown.
- Known cost asks for amount and currency. Unknown clears and forbids both.
- Gold asks for total positive gross grams and one or more lot rows. Each row
  asks for gross grams, fineness preset or precise per-mille input, positive
  piece count, optional acquisition date, Known/Unknown cost, hallmark,
  certificate reference, and note.
- The page derives fine-gold weight; it never asks for fine weight.
- Transaction note is required. External reference is optional.
- Server-rendered add/remove-lot controls work without JavaScript. Helper POSTs
  that do not mutate the database may return the form with preserved values;
  the final successful mutation always uses Post/Redirect/Get.

### Review

- Review shows current names plus stable codes for Household, Portfolio,
  Account, Asset, and Currency where applicable.
- It shows as-of date separately from every lot acquisition date and from the
  future system-generated audit time.
- It shows entered and normalized exact quantity, every lot quantity, and the
  allocation total.
- It shows Known amount/currency, Unknown, and NotApplicable as visibly distinct
  states.
- For aggregate broker cost it says that no acquisition price or date was
  inferred.
- For gold it shows gross grams, exact fineness label and ppm, piece count, and
  exact derived fine grams for each lot and total.
- It repeats the required provenance note and optional references.
- It shows no current market value, acquisition unit price, lifetime return,
  gain/loss, or allocation percentage.
- Every hidden review value is untrusted input. Final POST reconstructs,
  normalizes, and validates the full command again.

### Post, retry, and failure

- Final Post is an explicit labelled action and is the only action that creates
  ledger history.
- The server generates a logical idempotency key on initial form creation and
  carries it through review. JavaScript may disable a button as an enhancement;
  server idempotency is authoritative.
- Equivalent double-click, refresh, reconnect, and retry return one transaction
  and the same receipt.
- The same key with a different normalized command returns an idempotency
  conflict and never mutates data.
- A new key for an already effective opening returns a semantic conflict with a
  safe link to the existing opening.
- A new key when other effective history already exists returns a history
  conflict without adding an opening.
- Any validation, cancellation, database, trigger, or receipt failure leaves no
  partial transaction, lot, detail, allocation, or receipt graph.
- An uncertain client outcome directs the user to retry with the same key or
  open the persisted receipt; it never suggests creating a new key first.

### Receipt and correction

- Successful POST redirects to a stable GET receipt URL.
- Receipt displays the posted transaction ID and links to the existing complete
  transaction detail.
- Receipt reloads persisted facts and derived verification on every request.
- It states that the entry is immutable and that independent reconciliation is
  still required.
- It offers Start another opening and the bounded Reverse action.
- Reversal shows M003 eligibility before posting. A successful reversal links
  both sides and permits a new corrected opening only after semantic state is
  re-read.

## Persistence and transaction boundaries

Opening reference creation and financial posting are separate transactions:

- each reference create-and-use command atomically creates at most one master
  record or resolves an equivalent existing record;
- the opening posting transaction atomically persists the LedgerTransaction,
  its one TransactionEntry, every AssetLot, each initial
  LotEntryAllocation, every required PhysicalGoldLotDetail, the command
  receipt, and the final Posted transition;
- no ledger transaction is committed if any child, receipt, or Posted guard
  fails;
- no reference creator writes a ledger fact; and
- no UI state, preview, derived position, or fine weight is persisted as an
  accounting fact.

SQLite foreign-key enforcement stays enabled on every connection. Durable
masters and ledger history retain restrictive deletes. All enum-like values
use established stable text codes, GUIDs retain the established TEXT mapping,
business dates remain ISO `YYYY-MM-DD`, and audit timestamps remain normalized
UTC ISO-8601 values.

The expected migration is trigger/index-only unless implementation evidence
finds a genuine schema prerequisite. A schema expansion, new authoritative
state, or alteration to AssetLot custody meaning requires returning the
milestone to review.

## Idempotency and semantic-duplicate rules

Idempotency scope remains:

```text
HouseholdId + RECORD_OPENING_BALANCE + IdempotencyKey
```

The current fingerprint version contains, in a documented binary/text
canonical order:

- HouseholdId, PortfolioId, AccountId, and AssetId;
- as-of date;
- total quantity raw E8;
- normalized ExternalReference and required Note;
- for each canonically sorted lot: quantity raw E8, optional acquisition date,
  cost status, optional money minor units/currency, and optional gold fineness
  ppm, piece count, hallmark, certificate reference, and note.

Null, empty, and normalized text must retain their established distinctions.
Generated IDs, audit time, row order, fine-weight display, current position,
and current master labels are excluded.

Semantic uniqueness is instead:

```text
one effective OpeningBalance per
HouseholdId + PortfolioId + AccountId + AssetId
```

and is accompanied by the no-other-effective-history rule for the same tuple.
A different idempotency key does not bypass either rule. A Posted reversal
neutralizes the original for eligibility; Draft or Cancelled reversals do not.

## API contract

### Opening preview and posting

Add:

```text
POST /api/ledger/opening-balances/preview
POST /api/ledger/opening-balances
GET  /api/households/{householdId}/ledger/opening-balances/{transactionId}/verification
```

The final POST requires `Idempotency-Key` with the existing 1-256 character
rules. Preview does not accept or reserve a key.

Recommended request shape:

```json
{
  "householdId": "00000000-0000-0000-0000-000000000001",
  "portfolioId": "00000000-0000-0000-0000-000000000002",
  "accountId": "00000000-0000-0000-0000-000000000003",
  "assetId": "00000000-0000-0000-0000-000000000004",
  "asOfDate": "2026-01-31",
  "quantityRawE8": 12345678900,
  "externalReference": "SYNTHETIC-STATEMENT-2026-01",
  "note": "Synthetic opening balance for learning and verification.",
  "lots": [
    {
      "quantityRawE8": 12345678900,
      "acquiredOn": null,
      "costBasisStatusCode": "UNKNOWN",
      "originalCostBasisMinorUnits": null,
      "costBasisCurrencyCode": null,
      "physicalGoldDetail": null
    }
  ]
}
```

For Known cost, amount and currency are both present. For Unknown they are both
null. Cash/currency sends an empty lot collection. Physical gold supplies:

```json
{
  "finenessPartsPerMillion": 916000,
  "pieceCount": 2,
  "hallmark": "SYNTHETIC-916",
  "certificateReference": "SYNTHETIC-CERTIFICATE",
  "note": "Two synthetic matching bracelets."
}
```

Preview returns `200 OK` with normalized raw values, lot total, exact cost
states, exact derived fine-weight decimal strings, semantic-eligibility state,
and warnings. Invalid preview input uses the same stable error contract as
posting and performs no write.

First successful posting and equivalent replay return `201 Created`, the same
TransactionId, `Location` for
`/api/ledger/transactions/{transactionId}`, a verification location, and the
persisted created-lot IDs. Returned lot IDs come from readback, not the receipt's
single optional lot field.

The verification GET uses the route HouseholdId as an explicit scope, returns
only an OpeningBalance owned by that Household, and includes persisted
entry/allocation comparison plus current derived position. Unknown and
NotApplicable are codes, not numeric zero.

Extend existing transaction readback's created-lot item with nullable physical
gold detail:

```text
FinenessPartsPerMillion
PieceCount
Hallmark?
CertificateReference?
Note?
FineWeightGramsExact
```

`FineWeightGramsExact` is an invariant-culture decimal string so no client is
forced through binary floating point.

### Narrow reference creation

Add naturally idempotent, opening-scoped PUT routes:

```text
PUT /api/opening-balance-reference-data/currencies/{currencyCode}
PUT /api/opening-balance-reference-data/institutions/{institutionCode}
PUT /api/households/{householdId}/opening-balance-reference-data/accounts/{accountCode}
PUT /api/opening-balance-reference-data/assets/{assetCode}
```

The URI stable code is authoritative and is not repeated in the body. First
creation returns `201 Created`; an exact normalized existing match returns
`200 OK` with `WasCreated = false`; a conflicting or inactive same-code record
returns `409 Conflict`. Account and asset responses contain server-generated
IDs. Currency response uses its three-letter code. These routes cannot update,
rename, reactivate, or delete.

All routes exist only in Ready mode and inherit M004/M006 loopback and host
restrictions. JSON API mutation does not use UI antiforgery; Razor form POSTs
do.

## UI workflow and accessibility

Add **Record** to the primary Ready navigation only when the opening workflow
is complete. Initially it contains one real option, **Opening balance**, and no
dead links for later transaction types.

Recommended routes:

```text
GET/POST /record/opening-balance
GET      /record/opening-balance/{transactionId}
GET/POST /record/opening-balance/{transactionId}/reverse
```

The main page uses clear fieldsets for Identify, Source facts, and Review.
Server handlers may redisplay a non-mutating validation/review POST, but the
successful financial mutation always redirects to the receipt GET.

The initial GET generates the idempotency key. Review carries all normalized
facts and the key in hidden fields; every final POST treats them as untrusted
and recomputes validation and fingerprint. No browser storage, cookie, session,
TempData value, or hidden field is authoritative ledger state.

The flow preserves M006 accessibility behavior:

- one page heading and meaningful landmarks;
- programmatic labels, instructions, units, and examples;
- field-level errors linked from a validation summary whose first error gains
  focus;
- logical keyboard order, visible focus, and minimum target sizes;
- add/remove-lot controls that work without JavaScript;
- no color-only Known/Unknown/reconciliation status;
- responsive reflow at narrow and desktop viewports and a 200%-equivalent
  effective viewport;
- reduced-motion and forced-colors support;
- no modal-only final confirmation or correction action; and
- exact Turkish-first display with stable technical codes available through
  progressive disclosure.

Screen readers receive both karat label and exact fineness, explicit signs and
units, lot sequence labels, reconciliation status, and the distinction among
Unknown, NotApplicable, and zero.

## Validation and error behavior

Validation is deterministic and occurs at the earliest owning layer, then is
repeated at posting where necessary. API responses use sanitized Problem
Details with stable error codes; UI maps the same Application outcomes to
specific fields or a safe summary.

Use these HTTP categories:

- `400 Bad Request`: malformed JSON, empty/default IDs, malformed date/code,
  missing or invalid Idempotency-Key, or values that cannot be parsed into the
  transport contract;
- `404 Not Found`: referenced Household, Portfolio, Account, Asset, Currency,
  Institution, transaction, or opening receipt does not exist within the
  allowed scope;
- `409 Conflict`: idempotency fingerprint conflict, reference stable-code
  collision, effective opening already exists, other effective scope history,
  concurrent semantic winner, or reversal dependency/state conflict;
- `422 Unprocessable Entity`: inactive or household-inconsistent references,
  incompatible asset/account shape, non-positive/over-precision/overflowing
  value, future or misordered date, invalid lot/cost/gold combination, duplicate
  indistinguishable lot, or exact reconciliation failure; and
- `500 Internal Server Error`: an unexpected failure with only stable support
  reference and generic language.

At minimum, stable application/error distinctions cover:

```text
OPENING_BALANCE_ALREADY_EXISTS
OPENING_BALANCE_SCOPE_HAS_EFFECTIVE_HISTORY
OPENING_BALANCE_ASSET_NOT_SUPPORTED
OPENING_BALANCE_ACCOUNT_ASSET_MISMATCH
OPENING_BALANCE_LOTS_REQUIRED
OPENING_BALANCE_LOTS_FORBIDDEN
OPENING_BALANCE_LOT_TOTAL_MISMATCH
OPENING_BALANCE_COST_BASIS_INVALID
OPENING_BALANCE_GOLD_DETAIL_REQUIRED
OPENING_BALANCE_GOLD_DETAIL_FORBIDDEN
OPENING_BALANCE_ACQUISITION_DATE_AFTER_AS_OF
OPENING_BALANCE_AS_OF_DATE_IN_FUTURE
OPENING_BALANCE_REFERENCE_CONFLICT
IDEMPOTENCY_CONFLICT
```

User-visible errors never include SQL, trigger messages, connection strings,
file paths, stack traces, command fingerprints, request bodies, or EF entity
values. A database-trigger error is translated to an owned stable Application
outcome before reaching an adapter.

## Privacy and synthetic-data rules

- All automated tests, browser artifacts, examples, logs, and documentation use
  anonymous synthetic identities and amounts.
- The repository never receives a `.db`, SQLite sidecar, backup, exported
  statement, screenshot, trace, certificate image, or real reference.
- Browser tests create unique disposable database and backup roots outside the
  repository and verify their resolved paths before startup.
- Browser tests block external network requests and clean up their own test
  processes and uniquely generated files. The human learning lab is not deleted
  automatically.
- Routine logs omit household/person/institution/account/asset names, notes,
  references, quantities, costs, weights, fineness, certificate text, request
  bodies, rendered HTML, and idempotency keys.
- Screenshots and traces remain off by default. If failure diagnostics are
  enabled for a synthetic test, their location, contents, and cleanup are
  reviewed before verification.
- No telemetry, analytics, remote font, CDN, price provider, or AI service is
  introduced.

## Migration expectations

Implementation is expected to add `006_OpeningBalanceCutoverGuards` after
`005_WorkspaceIdentity`. The exact timestamp is generated at implementation
time. Roadmap M007 and migration 006 are independent identifiers.

The migration is expected to contain additive SQLite trigger/index protection,
not new balance or cutover-state tables. It must:

- preserve every existing M001-M006 table, constraint, trigger, and index;
- validate existing opening rows or fail before claiming compatibility;
- protect direct SQL and concurrent writers at final posting;
- keep foreign keys enabled;
- have a focused Down path that removes only M007 database objects;
- update the model snapshot only if the EF model genuinely changes; and
- pass pending-model-change and migration-up/down/up verification against a
  disposable database.

Before applying it to any non-disposable database, M004 status, exclusive
ownership, safe path, compatible current schema, and a verified workspace-bound
backup are mandatory. Normal API startup does not auto-migrate.

## Test scenarios

### Domain

- OpeningBalance accepts exactly one positive Principal entry with no unit
  price, cost component, or cash-flow detail.
- It rejects zero/negative quantity, extra entries, non-Principal roles, price,
  cash flow, and costs.
- Cash/currency opening has no lot and uses NotApplicable presentation.
- Fund/equity exact-lot and aggregate-lot creation preserves optional dates and
  Known/Unknown cost.
- Known zero remains Known zero; Unknown cannot contain an amount or currency.
- Fund/equity lot totals must reconcile exactly, including Optional mode.
- Physical gold requires detail, positive piece count, and valid fineness.
- Non-gold lots reject physical detail.
- Per-mille preset and precise conversions produce the accepted integer ppm or
  reject extra scale/range.
- Fine-gold calculation is exact at boundaries and never uses binary floating
  point.
- Quantity, money, fineness, multiplication, sum, negation, and conversion
  overflow boundaries fail deliberately.
- Acquisition date cannot follow as-of date.

### Application

- Active household/location/asset/currency validation succeeds for every
  accepted family and account combination.
- Cross-household, inactive, missing, incompatible, and misdated references are
  rejected.
- Narrow reference creation returns created/equivalent/conflicting outcomes and
  cannot mutate existing records.
- Preview normalizes and reconciles without allocating durable IDs or writing.
- Exact historical lots, aggregate supported cost with unknown dates, mixed
  Known/Unknown lots, and fully Unknown cost produce the expected aggregates.
- Cash and foreign-currency decimals respect Currency.MinorUnitDigits before E8
  conversion.
- Receipt lookup precedes semantic validation and equivalent replay returns the
  original result after restart or later state changes.
- Same-key changed payload conflicts.
- Different-key semantic duplicate and prior effective history conflict.
- Fully reversed opening permits corrected replacement; Draft/Cancelled
  reversal does not.
- Command fingerprint is versioned, stable across lot order, and sensitive to
  every normalized source fact.
- Multi-lot receipts use null `AssetLotId` and obtain all created IDs through
  readback.
- Post-write verification compares persisted entry/allocation facts and derives
  position from Posted history.
- Cancellation and every injected failure produce no claimed success.

### SQLite integration

- All accepted asset families round-trip exact values through real SQLite.
- Foreign keys and stable enum text mappings remain enabled and correct.
- Transaction, entry, all lots, gold details, allocations, receipt, and Posted
  transition commit atomically.
- Failure at each persistence stage rolls back the complete graph and receipt.
- Direct SQL cannot post an opening with invalid entry count/role/sign, price,
  cash flow, costs, incompatible asset/account, missing/partial lots, invalid
  gold detail, or misordered dates.
- Optional fund/equity openings reconcile exactly under the M007 guard while
  unrelated accepted Optional behavior remains unchanged.
- Same-key concurrent independent DbContexts converge on one receipt and graph.
- Different-key concurrent openings for one tuple create exactly one effective
  opening; the loser resolves a stable semantic conflict.
- A concurrent non-opening entry versus opening cannot bypass the no-history
  guard.
- Required-lot and gold-detail constraints protect direct writes.
- Posted opening graphs remain immutable and deletion-restricted.
- Reversal uses the same lots with inverse allocations and nets derived
  position to zero.
- Down restores the pre-M007 schema objects, and up-after-down restores the
  guards without data loss in a disposable database.
- Database restart preserves receipt, readback, semantic protection, and exact
  derived results.

### API

- Preview and post accept all four asset families with exact raw contracts.
- First post and equivalent replay return the same `201`, TransactionId,
  Location, verification location, and lot IDs.
- Missing/invalid idempotency key, conflicting replay, semantic duplicate,
  prior history, missing reference, invalid shape, overflow, and persistence
  failure map to the specified sanitized category.
- Reference PUT routes distinguish create, equivalent retry, and conflict.
- Transaction readback includes complete physical-gold detail and exact derived
  fine-weight string.
- Verification rejects cross-household and non-opening transactions.
- Ready mode exposes routes; setup and Blocked modes do not.
- Non-loopback and invalid Host requests remain rejected.
- Logs contain no request body or protected source values.

### UI

- Record navigation appears only with the complete Opening balance workflow and
  contains no dead later-workflow choices.
- Identify selections use current labels and enforce scope/compatibility.
- Bounded reference creation explains that it changes master data but not
  holdings.
- Cash TRY 12,345.67 normalizes exactly and displays NotApplicable cost.
- 123.456789 synthetic fund units split across two lots reconcile exactly.
- 17 synthetic equity shares with one Unknown-cost lot preserve Unknown.
- Two synthetic 22K/916-per-mille bracelets show combined gross weight, piece
  count two, 916,000 ppm, and exact derived fine weight without asking for it.
- Known, Unknown, NotApplicable, and zero have distinct text and status.
- Invalid decimal scale, overflow, incomplete Known cost, lot mismatch, gold
  fields, duplicate lot, and date errors focus the relevant field.
- Review contains all normalized facts and none of the forbidden valuation or
  performance values.
- Successful post uses PRG and receipt refresh creates nothing.
- Double-click and resubmission create one transaction.
- Restart and receipt URL reconstruct the same persisted result.
- Semantic conflict links safely to the existing opening.
- Opening reversal preview/post works and downstream dependency is explained.
- JavaScript-disabled, keyboard-only, narrow viewport, desktop viewport,
  reduced-motion, forced-colors, and 200%-equivalent reflow journeys remain
  usable.

### Browser

- Start a real loopback host with unique explicit synthetic data and backup
  paths outside the repository.
- Prove setup-mode routes cannot post an opening.
- Complete or reuse a synthetic Ready workspace and post one example for each
  supported family through the visible UI.
- Exercise client double-submit, browser Back, review refresh, receipt refresh,
  host restart, validation correction, and direct receipt navigation.
- Exercise exact two-lot reconciliation and one mismatch rejection.
- Exercise bounded reversal and corrected replacement.
- Block all external requests and assert no screenshot/trace by default.
- Inspect captured application logs for synthetic-value leakage.
- Stop the test-owned process and remove only the exact test-owned paths.

## Operational verification

M007 implementation verification uses a fresh disposable environment outside
the repository. Before host startup, record the resolved live database and
backup directories, prove the live database is absent or is the intended
synthetic lab, confirm ownership availability, and inspect relevant environment
variables and command-line overrides.

The operational journey is:

1. start in StorageUninitialized and verify only setup routes;
2. explicitly create and migrate the safe synthetic database;
3. restart into WorkspaceUninitialized;
4. create only anonymous synthetic core masters;
5. restart into InitialBackupRequired;
6. create and independently verify the workspace-bound initial backup;
7. restart into Ready;
8. inspect Today, empty Ledger, Settings, and data-safety information;
9. create any missing synthetic opening-scoped references through UI;
10. post the four small opening examples through review and receipts;
11. independently calculate and compare their quantities, allocations, and
    fine-gold weight;
12. restart and read every transaction and verification result again;
13. reverse one opening and post a corrected replacement;
14. create and verify a new backup generation;
15. stage and verify restore into a separate disposable target without
    replacing the active lab; and
16. inspect logs and browser artifacts for privacy.

The persistent human learning lab is never deleted automatically. Automated
tests clean only paths they created and positively identified. No command in
this verification points to an ambient or real household database.

The operational journey completed on 2026-09-11 against the retained,
synthetic-only roots below; the restore target is a third, separate retained
root rather than the active database or backup directory:

```text
%LOCALAPPDATA%\WealthLedger-LearningLabs\M007-20260908-153913
%LOCALAPPDATA%\WealthLedger-LearningBackups\M007-20260908-153913
%LOCALAPPDATA%\WealthLedger-LearningRestores\M007-20260908-153913
```

The retained run proved the four successful startup progression modes and their
fail-closed route exposure; host/browser tests additionally prove `Blocked`.
It created the six-migration database explicitly, initialized anonymous core
masters, created and independently verified the required workspace-matched
backup, and restarted into Ready. The Ready workflow posted synthetic TRY cash,
foreign currency, two fund lots with mixed Known/Unknown cost, an Unknown-cost
equity lot, and one two-piece physical-gold lot. Exact persisted readback showed
`25.25 * 0.916 = 23.129` grams of derived fine weight without storing a second
weight fact.

A second effective equity opening was rejected without a write. The accepted
M003 UI path then posted a separate reversal, derived a zero position, and
allowed a separately reviewed corrected replacement. Replaying the replacement
with the same key returned the same receipt and left the transaction count
unchanged. A second immutable backup generation was created and independently
verified, then staged without active replacement; a fresh Ready process over
the staged database read seven transactions, the corrected 18-share position,
the receipt, and the derived gold detail. No screenshot or trace was generated,
and captured Production logs contained route templates, outcome, count, and
duration rather than entered values, notes, references, or transaction IDs.

One pre-existing M006 diagnostic observation remains: the setup-state
cardinality probe uses an unordered bounded `Take(2)`, so EF emits warning
10102 at startup. Any two rows are sufficient to classify the unsupported
multiple-household state, so this does not change behavior or financial
results and disclosed no source value. It was not widened into M007.

## Real-data readiness limitations

M007 Verified does not by itself authorize WealthLedger to become the sole
record of real household assets. Before any real cutover recommendation:

- M007 must be Accepted, implemented, and Verified at the final repository
  checkpoint;
- all applicable ROADMAP real-data readiness conditions must be rechecked;
- resolved live and backup paths must be inspected by the operator;
- M006's verified rendered-page, error-page, and captured-log privacy coverage
  must remain green, and M007's new forms, errors, receipts, and logs must pass
  equivalent privacy verification;
- governed exports remain absent, while screenshots or traces deliberately
  captured by an operator remain that operator's privacy responsibility;
- a workspace-bound backup and isolated restore drill must succeed;
- every entered position must be reconciled against independent evidence;
- original statements or physical inventory records must be retained; and
- WealthLedger must not be presented as proof of market value, performance, or
  tax treatment that it does not yet calculate.

Unknown cost and missing evidence remain visible limitations, not readiness
failures that may be “fixed” with current prices.

## Implementation sequence

Implementation began only after explicit human acceptance. The accepted
sequence keeps each change buildable and reviewable:

1. record the Accepted and In Progress checkpoint and update ROADMAP and
   PROJECT_STATE without claiming implementation or verification;
2. add focused Domain opening/gold derivation invariants and tests;
3. add reference choice/create Application contracts and tests;
4. add preview, canonicalization, fingerprint, semantic eligibility, record,
   and verification Application contracts and tests;
5. extend persisted readback for physical-gold facts;
6. add the M007 migration, posting conflict classification, indexes if proven,
   and real-SQLite tests including races and direct SQL;
7. add JSON DTOs, mapping, routes, stable errors, and API tests;
8. add Record navigation and the server-rendered opening workflow with focused
   UI tests;
9. add the bounded opening reversal UI over M003;
10. add real-browser journeys and privacy inspection;
11. run focused tests, then the full repository and operational verification;
12. reconcile canonical documentation, correct the migration summary omission,
    and update verified state only after every acceptance criterion passes.

If implementation discovers a conflict with an Accepted ADR, a need to store
fine weight, a need for un-lotted investment cost, a need for a formal cutover
state, or an unavoidable broad master-data editor, stop and return M007 to
human review. Do not silently widen the contract.

## Acceptance criteria

- The complete decision set has explicit human acceptance before production
  implementation begins.
- A Ready user can record one exact opening for cash/base currency, foreign
  currency, fund units, equity shares, or physical-gold gross weight without a
  generic transaction editor.
- Missing M007 reference data can be created narrowly without exposing broad
  CRUD or creating a holding prematurely.
- Every opening creates exactly one positive Principal entry with no price,
  balancing entry, cash flow, or transaction cost.
- Cash/currency creates no lot and presents cost as NotApplicable.
- Fund/equity creates one or more fully reconciled Known/Unknown lots for both
  Optional and Required accepted modes.
- Gold creates one or more fully reconciled lots, each with required fineness
  and piece count, and displays exact derived fine weight from gross weight.
- Unknown cost and date remain absent and are never replaced with zero, as-of
  date, current price, or an inferred unit price.
- Review displays every normalized authoritative source fact and excludes
  valuation, performance, and allocation claims.
- Transaction, entry, all lots/details/allocations, receipt, and Posted state
  commit atomically or not at all.
- Equivalent retry, double-submit, refresh, reconnect, and restart resolve one
  transaction; changed replay conflicts.
- A second effective opening or an opening over effective history cannot be
  posted, including under concurrent different-key writers or direct SQL.
- Receipt and transaction detail read physical-gold facts from persistence and
  show a current derived position without storing it.
- Opening mistakes can be reversed through M003; downstream allocation rules
  remain effective; a corrected replacement is a separate transaction.
- UI remains keyboard-usable, JavaScript-optional for critical behavior,
  responsive, exact, Turkish-first, and privacy-safe.
- No database, backup, real financial value, screenshot, trace, or sensitive
  log artifact enters source control.
- M002-M006 behavior and the full repository suite do not regress.
- Formatting, EF model drift, migration round-trip, browser, operational backup,
  restore staging, and privacy checks pass.
- ROADMAP, PROJECT_STATE, DATABASE_DESIGN, DOMAIN_LEDGER, DATA_CAPTURE, UX_MVP,
  operations/security documentation, and this milestone agree with verified
  source reality before M007 is marked Verified.

## Verification commands

At implementation verification, restore once when needed and then run:

```powershell
dotnet restore WealthLedger.slnx --verbosity minimal
dotnet test WealthLedger.slnx --no-restore --verbosity minimal
dotnet format WealthLedger.slnx --verify-no-changes --no-restore --verbosity minimal
dotnet ef migrations has-pending-model-changes --project src/WealthLedger.Infrastructure/WealthLedger.Infrastructure.csproj --startup-project src/WealthLedger.Infrastructure/WealthLedger.Infrastructure.csproj --context WealthLedgerDbContext --no-build
```

Run the repository's pinned Playwright browser-install command and browser test
workflow documented by the final implementation. Run the M004 status,
backup-create, backup-verify, isolated restore-stage, and migration smoke checks
against explicit disposable paths. Do not run active replacement as part of an
ordinary M007 verification unless its exact disposable target has been
independently confirmed.

Planning-only verification before acceptance consisted of Markdown inspection,
required-section/decision checks, `git diff --check`, and a clean review of the
documentation-only worktree delta. It did not claim behavioral verification.

Final implementation verification on 2026-09-11 passed:

- Domain: 98;
- Application: 156;
- Infrastructure against real SQLite: 195;
- UI: 71;
- API/UI host against real SQLite: 130;
- Operations: 23;
- Playwright Chromium: 3;
- total: 676 passed, 0 failed;
- formatter drift: none; and
- pending EF model changes: none, with six applied migrations ending at
  `20260910101810_006_OpeningBalanceCutoverGuards`.

The three real-Chromium journeys include storage/setup/backup/Ready gating and
the opening workflow with exact cash, two-lot fund validation and correction,
equity creation, gold creation and derivation, immutable reversal and corrected
replacement, refresh/restart readback, JavaScript-disabled and keyboard-only
operation, narrow and 200%-equivalent reflow, reduced motion, forced colors,
blocked external requests, and no default screenshot or trace artifact.

## Documentation and state updates

Before this milestone was accepted:

- add only this milestone document;
- leave `PROJECT_STATE.md` at the M006 verified checkpoint;
- leave M007 Planned in ROADMAP until the human acceptance workflow updates it;
- do not create or alter an ADR merely to make the proposal appear accepted.

At the accepted implementation checkpoint:

- record the acceptance date and exact accepted/amended decisions here;
- change the appropriate ROADMAP and PROJECT_STATE delivery status without
  claiming implementation;
- add an ADR only if an accepted cross-cutting architecture decision changes an
  existing ADR boundary.

After implementation verification:

- update `PROJECT_STATE.md` with the concise verified checkpoint and actual
  test/migration evidence;
- mark M007 Verified in ROADMAP and this document;
- update `DATABASE_DESIGN.md`, including the missing 005 migration summary and
  actual M007 triggers/indexes;
- reconcile `DOMAIN_LEDGER.md`, `DATA_CAPTURE.md`, `UX_MVP.md`,
  `OPERATIONS.md`, and `SECURITY_OPERATIONS.md` with implemented behavior;
- document final API examples and the synthetic operational verification; and
- preserve all prior milestone and ADR history.

## Suggested commit boundaries after acceptance

No commit is authorized by this proposal. If the human owners later authorize
commits, keep the implementation in small coherent boundaries such as:

```text
feat(domain): define controlled opening-balance invariants
feat(application): add opening reference and preview contracts
feat(application): add retry-safe opening submission and verification
feat(persistence): enforce opening cutover guards atomically
feat(api): expose opening-balance contracts
feat(ui): add reviewed opening-balance workflow
feat(ui): expose bounded opening reversal interaction
test(browser): verify synthetic opening cutover journeys
docs(state): verify M007 opening-balance cutover
```

Every intermediate source commit must build and pass its focused tests. Do not
mix normal M008/M009 activity or M010 reconciliation into these boundaries.

## Risks, rollback, and correction behavior

The principal accounting risk is treating a snapshot as additive activity.
The single-scope command, no-effective-history rule, semantic guard, review,
and persisted verification jointly reduce that risk.

The principal evidence risk is converting Unknown cost into zero or current
value. Typed CostBasis, no entry unit price, explicit UI states, and tests keep
those meanings separate.

The principal concurrency risk is relying only on an Application precheck.
The posting-time SQLite guard is required; it must be exercised with independent
connections and different idempotency keys.

The principal physical-gold risk is ambiguous karat conversion or duplicating
fine weight. Exact labelled presets, precise per-mille entry, ppm persistence,
and non-persisted exact derivation avoid those errors.

The principal migration risk is weakening existing posted-history triggers.
M007 uses additive objects where possible, tests every inherited guard, and
requires a verified backup before any non-disposable migration. Migration Down
is a schema rollback tool, not a way to erase already Posted M007 facts.

An incorrect Posted opening is never updated or deleted. The user previews and
posts an exact M003 reversal. If downstream lot allocations exist, they must be
reversed according to their own accepted workflows before the opening becomes
eligible. A corrected opening is then a new separately reviewed command with a
new idempotency key. Original, reversal, and replacement remain auditable.

If M007 source changes must be abandoned before any real deployment, revert
only the M007 code and migrate a disposable database down using the tested Down
path. If a non-disposable database has already received Posted M007 history,
do not remove or rewrite that history; keep the compatible schema and use
reversal/correction. Restore is reserved for verified disaster recovery, not
ordinary correction.

## Accepted decision record

The human owners explicitly accepted all fifteen Recommended decisions exactly
as written on 2026-09-10 and authorized implementation directly on `main`.
There were no unresolved product decision gates at implementation start, and
the accepted contract is Verified by the source, tests, migration, browser, and
operational evidence above as of 2026-09-11. This acceptance and verification
do not authorize commits, pushes, merges, rebases, or remote state changes.
