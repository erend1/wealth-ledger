# M008: Complete Investment-Fund Lifecycle

Status: Proposed

Owner: Human and agent

Last reviewed: 2026-09-12

This proposal is ready for human review. It does not authorize implementation.
Every Recommended decision below must be accepted or amended explicitly before
the milestone may move to Accepted or In Progress.

## Objective

Complete the ordinary investment-fund workflow from funded cash through
purchase, sale, lot consumption, persisted receipt, realized-cost explanation,
and immutable correction. Expose the already implemented contribution writer
through the local UI so a household can perform the complete recurring monthly
fund path without raw API calls.

M008 extends the verified M001/M002 fund-purchase slice instead of replacing
the ledger model. It preserves exact source facts, makes every cash and cost
effect explicit, uses scope-correct deterministic FIFO for sales, and derives
realized cost only from recorded acquisition lineage.

## User outcome

After M008, a household operator in Ready mode can:

1. record an external cash contribution through a reviewed browser workflow;
2. select or narrowly create the references needed for a fund trade;
3. record a completed fund purchase using separate fund and cash accounts;
4. preserve units, executed price, actual cash consideration, dates, costs,
   reference, and note without silently reconciling one source fact into
   another;
5. create one exact acquisition lot whose Known economic cost follows the
   accepted cash-and-cost contract;
6. preview a completed fund sale and inspect the exact scope-aware FIFO lots it
   will consume;
7. post the reviewed sale once, receive cash, consume the selected lots, and
   inspect persisted verification after restart;
8. see complete, partial, or Unknown realized-cost information without treating
   missing cost as zero or converting between currencies without evidence; and
9. correct a purchase or sale through the existing immutable reversal workflow
   and a separately reviewed replacement.

M008 does not calculate current market value, realized gain, investment return,
tax liability, or religious compliance. It records and explains facts; it does
not provide tax, financial, legal, or religious advice.

## Current evidence

The verified checkpoint is M007 on `main` commit `999e216`. The complete
baseline run on 2026-09-12 passes 676 tests:

- Domain: 98;
- Application: 156;
- Infrastructure against real SQLite: 195;
- UI presentation/contract: 71;
- API/UI host against real SQLite: 130;
- Operations: 23; and
- Playwright Chromium: 3.

M001 already supplies:

- `RecordFundPurchaseUseCase`;
- `POST /api/ledger/fund-purchases`;
- one positive Fund Principal entry and one negative cash Consideration entry;
- one acquisition lot with a full positive allocation and Known cost;
- exact E8 quantities, E8 executed price, and minor-unit cash conversion; and
- a persisted point-position query.

M002 makes that purchase retry-safe through `RECORD_FUND_PURCHASE`, a versioned
fingerprint, and an atomic command receipt. M003 can reverse the purchase and
its allocation, subject to downstream dependency protection. M005/M006 expose
current reference choices, ledger readback, exact presentation, and a local
Ready shell. M007 adds narrow create-only references, complete opening lots,
persisted verification, and browser patterns that M008 can reuse.

The current purchase is intentionally incomplete for this outcome:

- one `AccountId` is used for both the Fund and cash entries;
- OrderDate and SettlementDate are not accepted;
- no TransactionCostComponent is accepted or reconciled;
- lot cost is always the entered cash consideration alone;
- no non-mutating purchase preview or Ready-mode purchase UI exists;
- no sale command, API, UI, or persisted sale receipt exists;
- the Domain FIFO service reads each lot's global CurrentQuantity rather than
  an exact Portfolio/Account custody quantity;
- no sale freshness contract protects a reviewed FIFO plan from intervening
  history; and
- no derived realized-cost method has yet been accepted.

The current Domain and schema already contain Buy and Sell transaction
semantics, TransactionCostComponent and CostTreatment vocabularies,
LotEntryAllocation, immutable reversal, order/execution/settlement dates, and a
deterministic FIFO planning seed. M008 must use those foundations rather than
introduce a disposal-only model, mutable remaining quantity, or authoritative
realized-cost table.

`ROADMAP.md` identifies M008 as the next Planned candidate and requires this
bounded contract and explicit human acceptance before implementation.

## Why now

M007 can establish the household's pre-existing Fund lots, cash, and Unknown or
Known historical cost. The next normal event is a monthly contribution followed
by a Fund purchase; eventual liquidation requires a sale that consumes those
lots. Without M008, the user must call a limited API for contributions and
purchases and cannot record a sale at all.

M008 is also a prerequisite for:

- the first complete user-operable release through M009;
- M010 inventory and reconciliation over ordinary trades;
- M011 valuation over trustworthy positions; and
- M012 performance and goal analysis using explainable realized cost.

Physical-gold trade and custody semantics remain M009. Search, evidence files,
market observations, valuation, performance, tax reporting, and agent analysis
remain M010-M013.

## Terminology

**Fund account** is the Investment or Pension Account whose Fund position
changes.

**Cash account** is the Cash, Investment, or Pension Account whose selected
Cash or Currency asset changes. It may differ from the Fund account, but M008
keeps both entries in one Household and one Portfolio.

**Executed quantity** is the exact positive number of Fund units acquired or
disposed. A Sell stores it as a negative Principal effect.

**Executed unit price** is the non-zero source price preserved on the Fund
Principal entry. It is not manufactured by dividing consideration by units.

**Price-implied gross amount** is the deterministic currency-minor-unit
comparison obtained from executed quantity times executed unit price. It is a
derived review value, not another persisted source amount.

**Cash consideration** is the positive amount entered by the user and mapped
to the trade's Consideration entry with the sign dictated by Buy or Sell. Its
precise relationship to costs is fixed by Decision 5.

**Additional cash outflow** is a cost paid as another debit rather than already
contained in consideration. It produces a negative Fee or Tax entry as well as
an explanatory TransactionCostComponent.

**Effective sale** is a Posted Fund Sell that has no Posted reversal. A reversed
sale and its reversal remain visible audit history but do not contribute to the
current effective realized-cost sequence.

**Scoped lot availability** is the quantity of one AssetLot currently derived
for the selected Household, Portfolio, Fund Account, and Fund Asset. It is not
the lot's global household quantity and is never stored as RemainingQuantity.

**FIFO preview plan** is the ordered set of existing Lot identities and exact
quantities that the deterministic current history would allocate to one sale.
It is review evidence, not user-selected accounting authority.

**Realized-cost completeness** states whether all, some, or none of the sold
quantity has supported Known acquisition cost. It is not a gain/loss result.

## Decisions and decision gates

None of the following decisions is accepted merely because it appears in this
proposal.

### Decision 1: deliver the complete recurring Fund path

**Recommended:** M008 includes three dedicated Ready-mode browser workflows:

1. Contribution, using the existing M001/M002 accounting and idempotency
   behavior;
2. Fund purchase, extending the existing writer and preserving backward API
   compatibility; and
3. Fund sale, adding FIFO allocation and realized-cost verification.

Contribution is included because a reviewed cash-boundary step is a direct
prerequisite to the normal monthly Fund purchase path and already exists below
the UI. Do not add Withdrawal, standalone Fee/Tax, transfer, dividend, income,
or generic transaction editing.

### Decision 2: record completed trades, not pending orders

**Recommended:** M008 records only executed Fund trades and posts them
immediately as immutable Posted transactions. ExecutionDate is required.
OrderDate and SettlementDate are optional source facts, but when SettlementDate
is supplied the user records the transaction only after settlement is observed.
When present, OrderDate cannot be later than ExecutionDate and ExecutionDate
cannot be later than SettlementDate, preserving the existing Domain chronology.
OrderDate, ExecutionDate, and SettlementDate cannot lie after the host's current
local operating date obtained through TimeProvider and an explicit time zone.

Do not expose Ordered, Cancelled, order amendment, pending settlement, or broker
order synchronization. The current transaction-level date model gives all
entries the ExecutionDate economic effect; SettlementDate is metadata and does
not defer only the cash leg.

### Decision 3: one Portfolio with separate Fund and cash Accounts

**Recommended:** one purchase or sale contains exactly:

- one Household;
- one active Portfolio;
- one active Fund Account;
- one active Cash Account;
- one Fund Asset;
- one Cash or Currency Asset; and
- one set of effective dates and source metadata.

The Fund Account and Cash Account may be the same or different and may refer to
different Institutions, but both must belong to the same Household. Every entry
uses the same selected Portfolio. Moving cash between Portfolio purposes is a
Transfer concern and cannot be hidden inside a Fund trade.

Purchase effects are:

    Fund Principal              positive in Fund Account
    cash Consideration          negative in Cash Account
    additional Fee/Tax entries  negative in Cash Account when present

Sale effects are:

    Fund Principal              negative in Fund Account
    cash Consideration          positive in Cash Account
    additional Fee/Tax entries  negative in Cash Account when present

### Decision 4: constrain asset, account, and currency compatibility

**Recommended:** accept only this matrix:

| Role | Required shape |
|---|---|
| Fund Asset | Active Fund, FundUnit, Optional or Required lot tracking, non-null BaseCurrency |
| Fund Account | Active Investment or Pension Account in the selected Household |
| cash Asset | Active Cash or Currency, CurrencyUnit, no lot tracking |
| Cash Account | Active Cash, Investment, or Pension Account in the selected Household |

The Fund BaseCurrency, cash-asset BaseCurrency, executed-price currency,
consideration currency, and every cost-component currency must match. Cross-
currency purchase, FX conversion, and multi-currency costs are out of scope.

Investment and Pension Accounts retain the M007 requirement for an active
Institution. If Account.OpenedOn is known, ExecutionDate cannot precede it.
Inactive current references remain readable in history but cannot receive a new
trade.

### Decision 5: make cash consideration and cost treatment unambiguous

**Recommended:** use the following source contract.

For a purchase, CashConsideration is the positive amount represented by the
trade-line debit. It may already contain costs marked IncludedInConsideration.
Costs marked AdditionalCashOutflow are not contained in it and create separate
negative cash entries.

For a sale, CashConsideration is the positive trade-line cash credit actually
received after any cost marked WithheldFromProceeds or
IncludedInConsideration. Costs marked AdditionalCashOutflow are separate debits.

Therefore the exact signed net Cash/Currency position effect is:

    purchase = -(CashConsideration + AdditionalCashOutflow total)
    sale     = +(CashConsideration - AdditionalCashOutflow total)

IncludedInConsideration, WithheldFromProceeds, and InformationalOnly components
do not create a second cash entry. This prevents double counting. Every cost
remains an explanatory TransactionCostComponent even when it also causes an
aggregated Fee or Tax entry.

### Decision 6: bound Fund cost types, treatments, and supporting entries

**Recommended:** M008 accepts only Commission, Brokerage, WithholdingTax,
OtherTax, and Other. MakingCharge and the property/insurance/notary vocabulary
are rejected for Fund trades.

All accepted components:

- have a strictly positive amount;
- use the trade currency;
- have an optional normalized note of at most 1,000 characters;
- are limited to sixteen components per transaction; and
- participate in a deterministic, order-independent canonical form.

An exact duplicate normalized component is rejected with guidance to aggregate
it or distinguish it with a meaningful note.

Purchase rejects WithheldFromProceeds because no proceeds exist. Sale accepts
all four existing treatments. Additional components create at most one
aggregated negative Fee entry and one aggregated negative Tax entry:

- Commission, Brokerage, and Other map to Fee;
- WithholdingTax and OtherTax map to Tax.

The sums of those entries must equal the matching AdditionalCashOutflow
components exactly. Zero-value placeholder components are not persisted.

### Decision 7: compare price and consideration without rewriting either

**Recommended:** preserve quantity, unit price, cash consideration, and cost
components exactly as entered after boundary normalization. Compute the
price-implied gross amount in checked C# arithmetic, never SQLite arithmetic or
binary floating point.

For non-negative raw E8 quantity `Q`, raw E8 price `P`, and currency precision
`d`, compare money using:

    exact minor units = Q * P * 10^d / 10^16

Use a sufficiently wide integer intermediate and round to integer minor units
with midpoint-to-even. The review shows the unambiguous rounded amount and the
signed residual.

Expected consideration is:

    purchase: rounded price gross + IncludedInConsideration costs
    sale:     rounded price gross
              - WithheldFromProceeds costs
              - IncludedInConsideration costs

AdditionalCashOutflow and InformationalOnly do not enter this comparison.

An absolute difference of at most one minor unit is classified as rounding-
consistent. A larger difference is never silently corrected. It requires a
non-empty transaction Note, remains visibly unresolved in review/receipt, and
posts only the entered source facts. A negative expected sale consideration or
overflow is rejected.

This is an audit consistency check, not a broker-price, tax, or NAV judgment.

### Decision 8: create one exact Known-cost lot for each purchase

**Recommended:** every successful Fund purchase creates exactly one AssetLot.
Its positive opening allocation equals the Fund Principal quantity exactly,
AcquiredOn equals ExecutionDate, and CostBasis is Known in the trade currency.

The lot's economic acquisition cost is:

    CashConsideration + AdditionalCashOutflow total

Included costs are already contained in CashConsideration and are not added
again. WithheldFromProceeds is invalid for a purchase; InformationalOnly never
changes cost. This method explains the actual acquisition cash outflow without
manufacturing a UnitPrice or overwriting the separately preserved executed
price.

Fund assets with Optional or Required stored tracking both receive complete
lineage in M008. Fund assets with None are rejected. Existing M001/M002 purchase
facts and lot costs are not rewritten.

### Decision 9: use custody-correct deterministic FIFO for sales

**Recommended:** the user does not manually select lots in M008. The sale
planner loads effective availability for the selected Household, Portfolio,
Fund Account, and Fund Asset and allocates the requested quantity oldest first.

Do not pass global AssetLot.CurrentQuantity as scoped availability. Introduce a
focused candidate contract containing the reconstituted lineage and the exact
derived quantity currently held in the selected scope. Preserve AssetLot as
acquisition lineage without AccountId or PortfolioId.

FIFO order is:

1. unknown AcquiredOn first, preserving the existing DateOnly.MinValue policy;
2. known AcquiredOn ascending;
3. AssetLot.CreatedAtUtc ascending; and
4. AssetLot.Id ascending.

Unknown-date precedence is conservative for pre-cutover holdings but is not a
claim about their actual purchase order. The review must label that uncertainty.
The resulting negative allocations reconcile exactly to the one negative Fund
Principal entry.

### Decision 10: make reviewed sale plans stale-safe and retry-safe

**Recommended:** sale preview writes nothing and returns the ordered FIFO plan
plus a versioned plan fingerprint. The post command carries the reviewed lot
identities and quantities. Application and Infrastructure recompute eligibility
and the same FIFO plan within the commit transaction.

If intervening effective history changes availability or order, posting returns
a sanitized stale-review conflict and writes no transaction, allocation, or
receipt. The user must review again with a new logical command identity.

Receipt lookup occurs before current eligibility checks. An equivalent retry
after a successful post returns the original transaction even though its own
sale has changed current quantities. Same-key/different-command reuse is a
conflict. Different-key identical trades are allowed because two genuine Fund
orders may share every visible financial fact; no heuristic semantic duplicate
guard is introduced.

Use a new `RECORD_FUND_SALE` operation code. Preserve
`RECORD_FUND_PURCHASE` and `RECORD_CONTRIBUTION`.

### Decision 11: derive partial-sale cost through Proposed ADR-009

**Recommended:** accept
`docs/decisions/ADR-009-deterministic-realized-lot-cost.md` with M008. It
defines realized cost as a rebuildable query result, never an entered or
authoritative stored amount.

For a Known-cost lot with original positive opening quantity `Q` and total
minor-unit cost `C`, consider only effective sale allocations and order them by
PostedAtUtc, TransactionId, entry Sequence, and allocation Id. For cumulative
disposed quantities `D(i-1)` and `D(i)`, assign allocation `i`:

    round_even(C * D(i) / Q)
      - round_even(C * D(i-1) / Q)

Use arbitrary-width or equivalently overflow-safe integer intermediates. Full
effective disposal assigns exactly C. Fully reversed sales are excluded from
the current effective sequence; a later correction may therefore redistribute
at most a minor-unit rounding residue among still-effective sales. Every result
states the method and effective query time.

This is deterministic household economic cost allocation, not an assertion of
the tax-lot method required by any jurisdiction.

### Decision 12: report Unknown and multi-currency realized cost honestly

**Recommended:** derive one of:

- CompleteKnown: every sold unit came from Known-cost lots;
- PartiallyKnown: both Known- and Unknown-cost quantities were consumed; or
- Unknown: no sold quantity has supported Known cost.

Return the Known realized-cost amounts grouped by their original CostBasis
currency, together with exact Known and Unknown disposed quantities. Do not add
different currencies and do not use current or historical FX rates. Do not
display a partial Known subtotal as if it were the complete realized cost.

CostBasis.NotApplicable on a Fund lot is an unsupported persisted shape and
must fail closed. Known zero remains Known zero. M008 does not calculate
realized gain, profit/loss, return, or tax liability.

### Decision 13: block impossible Fund disposal but not negative cash

**Recommended:** reject a sale whose requested quantity exceeds the effective
Fund quantity in the selected Portfolio/Account scope. Enforce non-negative
global lot balance and scope availability again at the SQLite posting boundary
so stale or direct-SQL writes cannot consume another Account's custody.

Do not reject a purchase merely because the currently derived cash position
would become negative. WealthLedger records source facts and may not yet contain
all cash history; overdraft and settlement realities also exist. Preview and
receipt show the projected cash effect and a prominent data-quality warning.
When the currently derived cash position would become negative, require a
non-empty Note explaining the known gap or funding context.

No balance, available-cash field, or overdraft flag is stored as authority.

### Decision 14: require minimum trade provenance

**Recommended:** require at least one of:

- a normalized ExternalReference of at most 256 characters; or
- a normalized transaction Note of at most 2,000 characters.

Reject control characters. Account/Institution context remains visible but is
not, by itself, evidence of a particular order. A discrepancy or negative-cash
warning requires Note specifically because an opaque reference cannot explain
it.

M008 stores references and notes only. Document upload, statement hashes,
broker-import lineage, and formal reconciliation remain M010.

### Decision 15: reuse narrow create-only reference behavior

**Recommended:** add `ListFundTradeChoicesUseCase` and reuse the M007
create-only reference persistence and validation semantics for missing
Currency, Institution, eligible Account, Cash/Currency Asset, or Fund Asset.
Household and Portfolio remain required existing selections.

The implementation may extract compatibility-preserving shared Application
logic behind M007 and M008 wrappers, but existing M007 classes, routes, and
responses must continue to work. The Fund UI exposes explicit reference
creation before financial review; it never hides master writes inside trade
posting.

Do not add editing, deletion, archival, reactivation, provider lookup, TEFAS
catalog synchronization, or broad master-data administration.

### Decision 16: preserve purchase API and receipt compatibility

**Recommended:** retain `POST /api/ledger/fund-purchases` and its existing
response fields. Extend its request additively: omitted CashAccountId defaults
to the legacy AccountId, dates/costs default absent, and legacy source fields
retain their old meaning. This is transport and receipt compatibility, not a
promise that a new under-specified write bypasses M008's accepted provenance,
date, reference, or safety validation. Existing successful receipts remain
replayable before any current validation.

New purchase submissions use fingerprint version 2 containing both Accounts,
dates, ordered-independent costs, and all normalized source facts. Existing
version-1 receipts remain replayable only when every new field has its legacy
default. A request with non-default new facts against a version-1 receipt is an
idempotency conflict; version-1 computation must never ignore meaningful new
fields.

Sale uses its own version-1 canonical fingerprint and receipt scoped by
Household plus `RECORD_FUND_SALE`. Sale receipts use TransactionId as the
canonical result and leave the legacy optional AssetLotId null because one sale
may consume several lots. Persisted readback supplies allocation identities.

### Decision 17: use dedicated review, receipt, and correction pages

**Recommended:** add dedicated routes under Record:

```text
/record/contribution
/record/fund-purchase
/record/fund-purchase/{transactionId}/receipt
/record/fund-sale
/record/fund-sale/{transactionId}/receipt
/record/fund-trade/{transactionId}/reverse
```

Contribution redirects to the existing persisted transaction detail as its
receipt; M008 does not add a second contribution-only read model. The dedicated
Fund receipts add only the trade-specific lot, cash, discrepancy, and realized-
cost explanation that generic transaction detail cannot supply.

Each write follows Identify, Source facts, Review, explicit Post, and persisted
receipt. UI PageModels call Application directly and never call the co-hosted
JSON API. Form state is not authoritative session, cookie, TempData, local-
storage, or database Draft state. Post/Redirect/Get and stable idempotency keys
make refresh and double-submit safe.

Purchase receipt shows the created lot and Known cost. Sale receipt shows
consumed FIFO lots, exact cash effects, cost components, completeness-aware
realized cost, current derived Fund position, and links to transaction detail.
Both expose M003 eligibility and exact reversal. A replacement is a separate
reviewed transaction with no fabricated durable replacement link.

### Decision 18: add only additive Fund-trade database guards

**Recommended:** add one forward migration after
`006_OpeningBalanceCutoverGuards`, expected as
`007_FundTradeLifecycleGuards`. Roadmap M008 and migration 007 remain
independent identifiers.

Use additive named SQLite triggers and only query-plan-proven indexes. Do not
edit historical migrations or weaken M003/M007 guards. Protect at the
Draft-to-Posted boundary:

- supported Fund Buy/Sell entry shape and signs;
- one Household and Portfolio with compatible active Accounts and Assets;
- full purchase and sale allocation reconciliation for Optional and Required
  Fund assets;
- purchase-lot acquisition date and Known cost equation;
- allowed cost types, treatments, currencies, and exact AdditionalCashOutflow
  Fee/Tax entry sums;
- scope-correct sale availability and non-negative lot balance; and
- stale/concurrent sale-plan rejection without partial persistence.

The migration adds no realized-cost, remaining-quantity, current-position,
profit/loss, valuation, tax, or market-data table. Down removes only M008
objects and restores the exact pre-M008 behavior. Upgrade from migration 006
must preserve all M007 opening, receipt, reversal, and backup evidence.

The migration must not reinterpret or reject already Posted M001-M007 facts
merely because they predate M008's stricter write contract. New guards apply to
new Draft-to-Posted transitions. Any pre-existing non-Posted Fund Buy or Sell
remains immutable only after posting and must satisfy the new guards before it
can post; migration never edits, deletes, or silently completes it.

## In scope

- Contribution choices, preview, Ready-mode UI, reviewed posting, and receipt
  over the existing contribution command.
- Fund-trade choices and bounded create-only reference actions.
- Purchase preview and the additive extension of the existing retry-safe Fund
  purchase Application/API contract.
- Separate Fund and cash Accounts in one Household and Portfolio.
- Optional OrderDate and SettlementDate for completed trades.
- Exact cost components with accepted treatment and cash-entry semantics.
- Price/consideration comparison, tolerance classification, and explainable
  unresolved differences.
- One Known-cost acquisition lot for each Fund purchase.
- Fund sale preview, post, receipt, and transaction readback.
- Scope-aware deterministic FIFO planning and exact negative allocations.
- Stale-preview and concurrent-disposal protection.
- Completeness-aware, currency-preserving derived realized cost.
- M003 reversal and separately reviewed replacement for purchases and sales.
- Ready-only JSON adapters and server-rendered Razor Pages.
- Additive database posting guards, migration/recovery verification, focused
  tests, full regression, and canonical documentation reconciliation.
- Proposed ADR-009, accepted only with explicit human approval.

## Out of scope

- Pending broker orders, Ordered/Cancelled lifecycle UI, partial fills, order
  amendment, or broker synchronization.
- Withdrawal, standalone Fee/Tax, dividend, income, expense, adjustment,
  corporate action, or generic transaction editing.
- Manual or alternative lot selection such as LIFO, specific identification,
  weighted average, or jurisdiction-specific tax optimization.
- Tax-return calculation, tax advice, tax-lot certification, or religious
  compliance determination.
- Cross-Portfolio trades, cash transfer, custody transfer, FX conversion, or
  multi-currency transaction costs.
- Physical-gold purchase, sale, transfer, making charge, seller, assay, or
  custody lifecycle from M009.
- Search, statement import, file/blob evidence, or formal reconciliation from
  M010.
- Market/NAV providers, current price, valuation, stale-quote logic, realized
  gain, unrealized gain, performance, allocation, or goal projection from
  M011/M012.
- Broad master-data CRUD, authentication, non-loopback hosting, cloud sync,
  agent writes, or autonomous investment action.

## Invariants

- LedgerTransaction remains the source of Buy/Sell entries, costs, dates, and
  immutable posting state.
- AssetLot remains acquisition lineage and never receives AccountId,
  PortfolioId, OriginalQuantity, RemainingQuantity, RealizedCost, or ClosedAt.
- LotEntryAllocation remains the only signed relation between a Fund lot and a
  transaction entry; LotDisposal must not return.
- Fund purchase has one positive Principal and one negative Consideration plus
  only the accepted negative Fee/Tax supporting entries.
- Fund sale has one negative Principal and one positive Consideration plus only
  the accepted negative Fee/Tax supporting entries.
- Every purchase creates one lot and every sale consumes one or more existing
  lots. Exact allocations equal the Principal magnitude.
- A sale cannot make global or scoped lot quantity negative.
- Executed quantity, price, consideration, and costs are independent source
  facts. Derived comparison never overwrites them.
- The cost equation and realized-cost method use checked integer/fixed-point
  arithmetic; `double` and `float` are forbidden.
- Realized cost is a dated/method-labelled derived result. Unknown is not zero.
- Posted facts cannot be mutated or deleted. Correction uses M003.
- Application receives TimeProvider; callers do not supply CreatedAtUtc or
  PostedAtUtc.
- Every user-visible stable failure has a sanitized code and maps back to a
  concrete field or stale-review action where applicable.

## Application commands and queries

Expected focused behavior includes contracts equivalent to:

- `ListContributionChoicesUseCase`;
- `PreviewContributionUseCase`;
- existing `RecordContributionUseCase`;
- `ListFundTradeChoicesUseCase`;
- bounded Fund-trade reference creation wrappers where needed;
- `PreviewFundPurchaseUseCase`;
- extended `RecordFundPurchaseUseCase` with v1 receipt compatibility;
- `PreviewFundSaleUseCase`;
- `RecordFundSaleUseCase`;
- `GetFundTradeVerificationUseCase`;
- a scope-aware open-lot/custody read port;
- an atomic Fund-sale posting port that rechecks the reviewed FIFO plan; and
- a deterministic realized-cost calculator/read port over persisted facts.

Names may differ when repository conventions justify it, but do not create a
generic repository, generic transaction service, CQRS framework, or child-
specific repository. Reuse current transaction readback, position queries,
command receipts, reversal services, reference stores, exact presenters, and
error handling where their contracts fit.

Preview returns no durable transaction, lot, allocation, or receipt identity.
Record returns only after persisted readback proves the accepted graph.

## Required behavior

### Contribution

- Select an active Household, Portfolio, compatible Cash Account, Cash/Currency
  Asset, category, and non-future execution date.
- Optionally select an active HouseholdMember in the same Household.
- Show the exact positive cash effect and source metadata before posting.
- Preserve existing accounting, operation code, API response, fingerprint, and
  receipt behavior.
- Post with PRG, survive refresh/restart, and link to transaction detail.

### Purchase preview and post

- Validate every reference, account opening date, date order, quantity, price,
  amount, currency, cost, text, and provenance rule.
- Show Fund increase, cash consideration decrease, additional cost decreases,
  total cash effect, price-implied amount, expected consideration, residual,
  warning state, and exact acquisition-lot cost.
- Post the reviewed source facts atomically with one lot and receipt.
- Replay equivalent legacy and v2 requests according to Decision 16.
- Persisted receipt recomputes the same explanatory values from ledger facts.

### Sale preview and post

- Load only effective Fund quantity and lot custody in the selected scope.
- Reject zero/negative quantity and insufficient scope availability.
- Display the exact FIFO order, each lot's available/consumed quantity,
  acquisition-date knowledge, cost knowledge, and total allocation.
- Display consideration/cost equations and completeness-aware realized cost.
- Carry the reviewed plan into post and fail stale without writing if current
  history differs.
- Persist one Sell, entries, costs, all negative allocations, and receipt in one
  SQLite transaction.
- Reload the transaction, allocations, realized-cost derivation, and scoped
  positions after commit.

### Retry, restart, and correction

- Equivalent same-key retry returns the original receipt before current-state
  eligibility checks.
- Same-key/different-command returns sanitized conflict and no write.
- Concurrent different-key sales cannot over-consume any lot or selected
  custody scope.
- Receipt refresh and direct navigation create nothing.
- Restart reconstructs receipts and derived results from persisted facts.
- Purchase reversal is blocked while effective downstream sale allocation
  exists.
- Sale reversal restores the exact original lot quantities and cash entries.
- Corrected replacement is a new command with a new key.

## API contract

Retain existing routes and add Ready-only adapters equivalent to:

```text
POST /api/ledger/contributions/preview
POST /api/ledger/contributions
POST /api/ledger/fund-purchases/preview
POST /api/ledger/fund-purchases
POST /api/ledger/fund-sales/preview
POST /api/ledger/fund-sales
GET  /api/households/{householdId}/ledger/fund-trades/{transactionId}/verification
```

Existing transaction/reversal and current reference-navigation routes remain.
Any new bounded reference route must preserve M007 compatibility and stable
create/equivalent/conflict behavior.

JSON uses raw E8 integers, signed/unsigned semantics stated by field, integer
minor units, ISO dates, GUIDs, and stable explicit codes. Cost arrays are
canonicalized independently of request order. The API never accepts formatted
Turkish display strings as authority.

Posting requires exactly one valid `Idempotency-Key`. Preview does not. Created
responses provide resolvable Location and verification locations without
changing existing purchase/contribution response fields incompatibly.

Expected stable error families include invalid reference/shape, invalid date,
invalid precision/overflow, invalid cost/treatment, unexplained discrepancy,
insufficient Fund quantity, stale FIFO review, idempotency conflict, persistence
conflict, and not found. Do not expose SQL, connection strings, resolved private
paths, raw request bodies, notes, references, fingerprints, or stack traces.

## UI contract

Record navigation exposes Contribution, Fund purchase, and Fund sale as
separate choices. It does not expose a generic debit/credit table.

Every flow follows the accepted four-stage pattern:

1. Identify.
2. Enter source facts.
3. Review economic effect.
4. Post and inspect persisted receipt.

Routine fields stay visible. Dates, costs, references, and notes use progressive
disclosure without hiding validation. Selectors show human labels and stable
context; users do not invent GUIDs, raw E8, minor units, fingerprint, or
idempotency values.

Review shows exact decimal values, explicit signs in language, source/destination
Accounts, total cash movement, cost treatment, lot effects, Unknown data, and
all warnings. It does not show current value, return, gain/loss, tax liability,
or investment advice.

Pages remain server-rendered, usable without JavaScript, keyboard accessible,
responsive near 390 CSS pixels and 200-percent-equivalent reflow, compatible
with reduced motion and forced colors, and protected by antiforgery. Local CSS
only; no telemetry, remote font, CDN, provider, or analytics request.

## Persistence impact

Expected migration:

```text
007_FundTradeLifecycleGuards
```

The implementation must first prove whether each proposed index is needed with
the actual SQLite query plan. A SQL-only trigger/index migration is acceptable;
do not add an artificial EF entity merely to force model change.

The transaction, entries, cost components, new purchase lot/allocation or sale
allocations, command receipt, and Posted transition share one explicit SQLite
transaction. Sale candidate loading, reviewed-plan comparison, and posting-time
scope protection must observe a coherent write transaction.

No authoritative current-state or calculated-cost table is permitted. If an
optional rebuildable read model becomes demonstrably necessary, stop and return
to human/ADR review rather than silently adding it.

Before a non-disposable migration, M004's exclusive ownership, verified current
backup, workspace lineage, and explicit Operations workflow remain mandatory.

## Test scenarios

### Domain

- Buy/Sell exact sign and supporting-entry shapes.
- Fund-only cost types and treatment rules where Domain-owned.
- Scope-availability FIFO plan rather than global-lot quantity.
- Unknown-date precedence and stable tie-breakers.
- Exact split across at least two lots and insufficient quantity.
- Known-cost cumulative rounding, full cost conservation, zero cost, large
  overflow-safe inputs, reversed-sale exclusion, and stable ordering.
- CompleteKnown, PartiallyKnown, and Unknown composition without currency
  conversion.

### Application

- Separate Fund and cash Accounts in one Household/Portfolio.
- Invalid/cross-household/cross-Portfolio/inactive/reference/account matrix.
- All date and Account.OpenedOn rules.
- Price/consideration exact, one-minor-unit, explained mismatch, unexplained
  mismatch, negative expected proceeds, and overflow.
- Every cost type/treatment/role mapping, duplicates, count limit, currency, and
  exact additional-entry sum.
- Purchase lot cost includes additional outflow once and included costs once.
- Sale FIFO uses exact scoped custody and returns completeness-aware cost.
- Stale reviewed plan; receipt-first replay after holdings change; conflicting
  replay; legitimate different-key identical trade.
- Negative projected cash warning/note without false hard rejection.
- Contribution UI application adapters preserve existing behavior.
- Reversal eligibility for purchase dependencies and corrected replacements.

### Infrastructure

- Purchase and sale round trip against real file-backed SQLite.
- Atomic rollback for entry, cost, lot/allocation, receipt, and final posting
  failures.
- Optional/Required Fund allocation equality.
- Scoped custody cannot be consumed from another Account or Portfolio.
- Different-key sale races cannot over-consume quantity.
- Direct SQL cannot bypass accepted Buy/Sell shape, cost-entry equation, lot
  equation, currency/account compatibility, or scoped availability.
- Existing v1 purchase receipt replay after migration.
- Migration 006 -> 007, down/up, model drift, integrity, query plan, backup,
  isolated restore, and fresh-process readback.
- No M001-M007 trigger, history, workspace identity, or reversal regression.

### API and UI host

- Preview routes are side-effect free.
- Raw contracts preserve all exact values and stable codes.
- Existing purchase request/response compatibility.
- Purchase-v1 receipt replay rejects meaningful v2 differences.
- Sale post/replay/conflict/stale/insufficient paths and resolvable locations.
- Cross-household verification rejection and non-Fund transaction rejection.
- Contribution, purchase, sale, receipt, and reverse routes exist only in Ready.
- Antiforgery, validation linking/focus, privacy-safe Problem Details, and logs.
- No note/reference/raw value appears in captured logs.

### Browser

- Complete synthetic first-run or prepared Ready environment, then contribution
  -> two purchases -> sale across both lots -> receipt -> restart readback.
- Separate cash and Fund Accounts and exact cash movement.
- Included, withheld, additional, and informational cost presentation.
- Price/consideration mismatch correction and explained-warning path.
- Mixed Known/Unknown opening lots and currency-bucket realized cost.
- Stale review, double-click, browser Back, review refresh, receipt refresh, and
  direct receipt navigation.
- Sale reversal, restored lot quantities, corrected replacement, and blocked
  purchase reversal while sale is effective.
- JavaScript disabled, keyboard-only, narrow/desktop viewport, reflow, reduced
  motion, forced colors, and external-request rejection.
- Process exit and disposal of synthetic database/backup roots; no screenshot or
  trace containing private data.

## Acceptance criteria

- All eighteen decisions and Proposed ADR-009 are explicitly accepted or
  amended before implementation.
- A user can complete contribution, Fund purchase, and Fund sale from the Ready
  UI without raw IDs or API calls.
- Existing contribution behavior and purchase transport/receipt compatibility
  remain intact; documented M008 validation applies to new first submissions.
- Both trade Accounts, all source facts, costs, dates, references, and exact
  values survive persisted readback and restart.
- Purchase creates exactly one fully allocated Known-cost lot using the accepted
  economic cash-outflow formula.
- Sale consumes only Fund quantity actually derived in its selected scope and
  uses the reviewed deterministic FIFO order.
- Stale and concurrent sales cannot produce duplicate or overdrawn allocation.
- Known, partial, Unknown, zero, and multi-currency realized-cost results are
  honest, deterministic, method-labelled, and derived without an authoritative
  cache.
- Cost treatments produce the accepted exact cash effects without double
  counting.
- Material price/consideration discrepancy remains visible and requires the
  accepted explanation rather than silent normalization.
- Equivalent retry, conflicting retry, refresh, Back, restart, reversal, and
  corrected replacement behave as contracted.
- No physical-gold, market-data, valuation, performance, tax-advice, broad CRUD,
  remote-hosting, or agent-write scope enters M008.
- Migration guards direct SQL and races while preserving the complete M001-M007
  history and recovery chain.
- Focused tests, full suite, formatting, EF drift, migration round trip, real
  browser, backup/restore, privacy, and artifact checks pass.
- Canonical documentation and PROJECT_STATE describe only verified behavior
  after verification.

## Verification commands

At implementation verification, restore once if required and run:

```powershell
dotnet test WealthLedger.slnx --no-restore --verbosity minimal
dotnet format WealthLedger.slnx --verify-no-changes --no-restore --verbosity minimal
dotnet ef migrations has-pending-model-changes --project src/WealthLedger.Infrastructure/WealthLedger.Infrastructure.csproj --startup-project src/WealthLedger.Infrastructure/WealthLedger.Infrastructure.csproj --context WealthLedgerDbContext --no-build
```

Run focused Domain, Application, real-SQLite, API/UI host, Operations, and
Playwright projects during implementation. Run the repository-pinned Playwright
browser installation command before browser verification when needed.

Use explicit disposable paths outside the repository for migration, backup,
restore-stage, and fresh-process smoke tests. Do not run active replacement
unless the exact synthetic target is independently confirmed. Do not use or
copy real household data.

Planning-only verification for this Proposed document consists of the verified
676-test baseline, Markdown/link/section inspection, `git diff --check`, and
review of the documentation-only branch. It does not claim M008 behavior.

## Documentation updates

During proposal review:

- link this Proposed milestone from ROADMAP and PROJECT_STATE;
- add Proposed ADR-009 to the ADR index; and
- leave the verified implementation checkpoint unchanged.

After explicit acceptance:

- record the acceptance date and exact accepted/amended decisions here;
- mark M008 Accepted/In Progress according to actual work; and
- mark ADR-009 Accepted only if its decision is approved.

After implementation verification, reconcile:

- `README.md`;
- `docs/PROJECT_STATE.md`;
- `docs/ROADMAP.md`;
- `docs/PRODUCT_REQUIREMENTS.md` only if durable intent needs clarification;
- `docs/DOMAIN_LEDGER.md`;
- `docs/DATA_CAPTURE.md`;
- `docs/DATABASE_DESIGN.md`;
- `docs/ARCHITECTURE.md`;
- `docs/UX_MVP.md`;
- `docs/OPERATIONS.md` and `docs/SECURITY_OPERATIONS.md` where the migration or
  real-data gate changes; and
- `docs/decisions/README.md` plus ADR-009.

Do not rewrite a Verified milestone to make M008 behavior appear older.

## Suggested implementation sequence and commit boundaries

Keep every intermediate commit buildable and reviewable. A likely sequence is:

```text
docs(state): accept M008 fund lifecycle contract
feat(domain): define scoped FIFO and realized-cost derivation
feat(application): add fund trade previews and exact cost semantics
feat(application): add retry-safe fund sale and persisted verification
feat(persistence): protect fund trade lifecycle atomically
feat(api): expose complete fund trade contracts
feat(ui): add reviewed contribution and fund purchase workflows
feat(ui): add reviewed FIFO fund sale and correction workflow
test(browser): verify synthetic monthly fund lifecycle
docs(state): verify M008 investment-fund lifecycle
```

Refactoring the existing purchase or M007 reference components must stay
compatibility-preserving and be committed with the behavior that proves why it
is needed. Do not mix M009 physical-gold work or later analytics.

## Risks and rollback

The principal accounting risk is double counting a cost already included in
consideration. Decision 5 fixes the cash equation, preview displays it, and
Application plus SQLite compare supporting entries with components.

The principal lot risk is consuming a globally open lot that is not held in the
selected Account/Portfolio. Decision 9 requires scope-derived availability and
Decision 18 protects it at posting.

The principal concurrency risk is showing one FIFO plan and posting another.
The reviewed-plan fingerprint, in-transaction recomputation, database
arbitration, and receipt-first replay prevent silent substitution.

The principal evidence risk is presenting a partial Known cost as complete or
adding currencies. Decision 12 preserves Unknown quantity and currency buckets.

The principal rounding risk is losing or inventing minor units across partial
sales. Proposed ADR-009 uses overflow-safe cumulative rounding and exact full-
lot conservation while disclosing method/effective-time behavior.

The principal compatibility risk is reinterpreting existing version-1 purchase
receipts. Decision 16 retains v1 and prevents it from ignoring new semantics.

The principal migration risk is weakening the established posted-history,
opening, or reversal triggers. M008 uses additive objects, full migration-chain
tests, a verified pre-migration backup, and an isolated restore drill.

Before any real M008 deployment, code can be reverted while a disposable
database is returned to migration 006 through the tested Down path. If a
non-disposable database has migration 007 but no M008 facts, schema rollback
still requires M004 ownership, a verified backup, and the documented procedure.
Once Posted M008 trades exist, do not deploy an older binary or remove their
supporting schema as an ordinary rollback. Keep the compatible schema and use
dependency-safe reversal plus replacement. Restore is reserved for verified
disaster recovery, never ordinary correction, and no rollback deletes or edits
Posted trades.
