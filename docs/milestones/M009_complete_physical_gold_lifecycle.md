# M009: Complete Physical-Gold Lifecycle

Status: In Progress

Owner: Human and agent

Last reviewed: 2026-09-15

Accepted: 2026-09-14

Implementation started: 2026-09-15

## Acceptance record and implementation gate

The human owners reviewed the complete Draft and explicitly approved it as
suitable on 2026-09-14. All twenty Recommended decisions were accepted without
amendment. ADR-010 records the accepted allocation-level piece-movement model
and the PhysicalGold extension of ADR-009 cumulative realized-cost
apportionment.

Acceptance authorizes this bounded M009 contract. It was drafted and accepted
against committed M008 implementation commit `b2f316d` in an isolated planning
worktree, then reconciled onto verified M008 merge commit `d1d9ece` on
2026-09-15.

The predecessor gate closed when PR #12 verified and merged all material M008
review findings:

- sale-preview realized cost must agree with ADR-009 after an earlier partial
  disposal;
- Fund purchase and sale correction must have a working bounded UI path;
- the Fund posting guard must enforce the accepted cash scope, currency, and
  account/institution rules;
- a real concurrent sale race must be covered and provider exceptions must stay
  behind the persistence boundary;
- Fund verification must reject non-Fund Buy and Sell transactions; and
- the committed M008 checkpoint must agree with `PROJECT_STATE.md`.

These fixes remain M008 behavior, not M009 scope. M009 must consume their
corrected contracts and keep focused regression tests around the
Fund-versus-PhysicalGold boundary.

## Objective

Deliver one complete, auditable physical-gold path for the household's routine
use: record a cash purchase into physical custody, inspect gross weight,
fineness, fine weight, pieces, making charge, cost basis, and custody; move
selected physical pieces between custody scopes without inventing a new
acquisition; sell selected pieces with exact proceeds and derived realized
cost; and correct each supported activity through immutable reversal and a
separate replacement.

M009 completes the first user-operable product slice. It records physical facts
and cash effects. It does not introduce live gold prices, valuation,
performance, spread analysis, document storage, tax advice, or trade execution.

## User outcome

After M009, a household operator in Ready mode can:

1. select or narrowly create the Currency, Institution, PhysicalVault Account,
   cash Account, cash Asset, and PhysicalGold Asset needed by the workflow;
2. record a completed purchase of one homogeneous gold inventory group, such as
   one bracelet or several genuinely identical pieces from one receipt;
3. preserve exact gross grams, actual fineness, piece count, product identity,
   hallmark, certificate reference, seller, cash paid, making-charge treatment,
   other supported costs, dates, reference, and note;
4. review exact gross and derived fine weight, the complete cash effect, and the
   acquisition cost before explicitly posting;
5. inspect the persisted transaction, acquisition lot, current pieces, exact
   custody, cost basis, and current gross/fine weight after restart;
6. select the physical lot or lots actually sold rather than pretending that
   physically distinguishable jewelry was disposed by an invisible FIFO rule;
7. record exact sold gross weight and whole pieces, receive cash, and see
   completeness-aware realized cost derived from acquisition lineage;
8. transfer selected lots or pieces between physical-vault Accounts or
   Portfolio purposes while preserving total household quantity, piece count,
   acquisition date, fineness, and cost basis;
9. receive the original result on an equivalent retry and a sanitized conflict
   when a key is reused for different facts;
10. be protected when another write changes the selected custody after review;
11. reverse an eligible purchase, sale, or transfer through exact inverse
    entries, lot allocations, and piece movements; and
12. record a corrected replacement as a new reviewed command without editing or
    deleting posted history.

## Current evidence

Repository inspection at the drafting baseline establishes these facts:

- `AssetType.PhysicalGold`, `AssetUnit.GrossGram`, Required lot tracking, and
  `AccountType.PhysicalVault` already exist.
- `PhysicalGoldLotDetail` stores integer-ppm fineness, one positive original
  `PieceCount`, optional Hallmark, optional CertificateReference, and optional
  Note. It derives fine weight from exact gross E8 quantity and fineness.
- `AssetLot` is acquisition lineage without AccountId or PortfolioId. Its
  quantity and custody are derived through signed `LotEntryAllocation` rows.
- `LotEntryAllocation` stores signed gross quantity only. No current model
  records signed piece movement, so the original `PieceCount` cannot explain a
  partial sale or transfer.
- M007 records one or more PhysicalGold opening lots in a PhysicalVault and
  requires exact gross-weight allocation, gold detail, positive piece count,
  Known or Unknown cost, and evidence-honest grouping.
- M007 allows identical pieces to share a lot when the user asserts that all
  relevant product, fineness, acquisition, cost, identifier, and provenance
  facts form one evidence group. It never infers per-piece weight.
- the generic Domain already supports Buy, Sell, and Transfer transaction
  shapes. A Transfer can contain offsetting Transfer entries and optional
  negative Fee/Tax entries, but no physical-gold Application workflow currently
  uses that shape.
- ADR-004 models acquisition, disposal, transfer, and reversal with signed lot
  allocations. ADR-005 keeps custody out of AssetLot. ADR-009 defines exact
  cumulative realized-cost apportionment over effective disposals.
- M008 adds reviewed Fund purchase and sale commands, costs, separate cash and
  asset Accounts, scoped lot availability, plan fingerprints, command receipts,
  persistence readback, and UI patterns whose hardening gate is now verified.
- migration `007_FundTradeLifecycleGuards` currently treats every newly Posted
  Buy or Sell as a Fund trade. M009 cannot post a PhysicalGold Buy or Sell until
  a forward migration dispatches the guard by principal-asset family without
  weakening the Fund guarantees.
- the corrected M008 verification read store fails closed unless the principal
  asset is a Fund. M009 must preserve that Fund-only boundary and expose a
  separate physical-gold verification contract.
- there is no physical-gold purchase, sale, transfer, custody-inventory,
  transaction-specific verification, API, receipt, or routine Ready-mode form.
- market/reference observations remain M011. Search, evidence upload, formal
  reconciliation, and inventory-count comparison remain M010.

## Why now

M007 can establish the household's initial physical-gold inventory, but no
ordinary later event can explain a monthly bracelet purchase, an actual sale,
or movement between home and bank custody. Recording those events as repeated
opening balances would corrupt their economic meaning and collide with M007's
cutover rules.

M009 is the next roadmap outcome and completes the first usable product slice.
It also resolves the last asset-specific movement gap before M010 builds search
and reconciliation over stable transaction and custody facts.

## Terminology

**Gross weight** is the authoritative E8 quantity of a PhysicalGold lot and its
movement entries.

**Fineness** is the immutable actual purity of one acquisition lot, stored in
integer parts per million.

**Fine-gold weight** is a derived result of gross weight multiplied by fineness.
It is neither entered as an independent fact nor persisted as a balance.

**Original piece count** is the positive count in
`PhysicalGoldLotDetail` at acquisition.

**Piece movement** is a signed whole-number change associated with one signed
lot allocation. Current and scoped piece counts are sums of effective movement
facts, not mutations of the original count.

**Homogeneous inventory group** is one lot whose pieces satisfy M007 Decision 6
and may honestly share product, fineness, acquisition, cost, identifier, and
provenance facts.

**Custody scope** is Household, Portfolio, PhysicalVault Account, and
PhysicalGold Asset. A lot can have positive custody in several scopes while
remaining one acquisition lineage.

**Selected-item plan** is the reviewed list of exact lot identities, gross
quantities, and whole-piece counts that a sale or transfer will move. It records
what is physically selected; it is not an accounting-policy override.

**Cash consideration** is the actual trade-line debit for purchase or credit for
sale, using the same net/source-fact semantics accepted by M008.

**Making charge** is an explicit transaction-cost component. Its treatment says
whether it is already included in consideration, paid as an additional cash
outflow, or informational only. It is never inferred from a marketing label.

**Counterparty** is an optional existing Institution identifying the seller on
a purchase or buyer on a sale. It is distinct from the institution, if any,
that owns the PhysicalVault or cash Account.

## Decisions and decision gates

Every decision below was accepted as recommended by the human owners on
2026-09-14. A later change to a material decision requires an explicit milestone
amendment and, where cross-cutting, a superseding ADR.

### Decision 1: deliver purchase, sale, and custody transfer together

**Recommended:** M009 delivers the complete physical-gold lifecycle in one
bounded milestone:

- reviewed purchase and persisted acquisition;
- selected-lot sale and derived realized cost;
- selected-lot custody or Portfolio transfer;
- persisted verification and current inventory readback; and
- exact reversal plus separate corrected replacement.

Do not split off valuation, market quotes, reconciliation, or general inventory
administration. A lifecycle without sale or transfer would still invite manual
database changes for ordinary physical events.

### Decision 2: one PhysicalGold Asset and one homogeneous acquisition lot per purchase

**Recommended:** one purchase command records one Household, one Portfolio, one
PhysicalVault Account, one cash Account, one PhysicalGold Asset, one cash Asset,
and exactly one newly created acquisition lot.

The lot may contain one piece or several pieces only when they meet M007's
accepted homogeneous-group rule. Different products, fineness values,
individual supported costs, acquisition dates, hallmarks, certificates, or
provenance require separate purchase commands. Several transactions may share
the same receipt reference.

This keeps the exact acquisition-cost equation provable. It avoids inventing a
rule for splitting one cash total across heterogeneous pieces.

### Decision 3: preserve gross grams, fineness, and pieces as different facts

**Recommended:** use:

- positive gross E8 grams for purchase quantity;
- negative gross E8 grams for sale/source transfer quantity;
- positive gross E8 grams for destination transfer quantity;
- one immutable Fineness value on the acquisition lot; and
- positive or negative integer piece movements for every PhysicalGold lot
  allocation.

Fine weight is derived separately for each lot allocation or custody result
from its gross quantity and the lot's fineness, then added with checked decimal
arithmetic. Do not average fineness across lots and do not persist an aggregate
fine-weight balance.

### Decision 4: add allocation-level physical-piece movement

**Recommended:** introduce an asset-specific one-to-one child table and Domain
fact, provisionally named `PhysicalGoldLotAllocationDetail`, keyed by
`LotEntryAllocationId` and containing a non-zero signed integer `PieceDelta`.

Rules:

- every allocation of a PhysicalGold lot has exactly one piece detail;
- no non-PhysicalGold allocation has one;
- PieceDelta sign equals QuantityDelta sign;
- original acquisition allocation PieceDelta equals the lot detail's positive
  PieceCount;
- global and scope-derived piece sums never become negative;
- reversal copies the exact opposite PieceDelta; and
- no CurrentPieceCount or RemainingPieceCount is stored.

Migration must backfill existing M007 physical-gold opening allocations from the
lot's original PieceCount and backfill exact full opening reversals with the
negative count. It must fail closed on any existing allocation history whose
piece movement cannot be proved rather than guessing from average weight.

Rejected default: require every future lot to contain exactly one piece. That
would avoid a table but contradict M007's accepted evidence grouping and would
leave existing multi-piece opening lots unable to support honest partial
movement.

### Decision 5: require exact physical and cash account compatibility

**Recommended:** a purchase or sale uses:

| Role | Accepted shape |
|---|---|
| Gold Asset | Active PhysicalGold, GrossGram, Required lot tracking, non-null BaseCurrency |
| Gold Account | Active PhysicalVault in the Household |
| Cash Asset | Active Cash or Currency, CurrencyUnit, no lot tracking |
| Cash Account | Active Cash, Investment, or Pension Account in the Household |

Investment and Pension cash Accounts retain the active-Institution requirement.
A PhysicalVault may have an active Institution but does not require one. A bank
metal account that does not represent possessed allocated physical gold remains
outside this shape.

Purchase and sale keep all entries in one selected Portfolio. A custody transfer
may cross Portfolio purposes deliberately and records both exact scopes.

The gold Asset base currency, cash Asset base currency, price currency when a
price is supplied, consideration currency, and every cost currency match.
Cross-currency settlement and hidden FX conversion remain out of scope.

### Decision 6: persist seller or buyer separately from custody

**Recommended:** add an optional PhysicalGold-trade child, provisionally named
`PhysicalGoldTradeDetail`, keyed by LedgerTransactionId with nullable
`CounterpartyInstitutionId`.

It exists only for supported PhysicalGold Buy and Sell transactions. When
present, the Institution is an active existing global master at posting and may
be Jeweler, Bank, or Other. Institution currently has no Household ownership,
so the contract must not invent one. The counterparty is not inferred from the
PhysicalVault or cash Account institution. A private or unavailable
counterparty remains absent and is explained by provenance text rather than a
fabricated master record.

Do not add a generic editable counterparty model or free-text duplicate of an
Institution in M009.

### Decision 7: record completed physical events, with honest dates

**Recommended:** M009 posts completed Buy, Sell, and Transfer transactions. It
does not implement pending orders, reservations, delivery promises, or partial
settlement state.

Purchase and sale require ExecutionDate and allow optional OrderDate and
SettlementDate with:

    OrderDate <= ExecutionDate <= SettlementDate

Transfer uses ExecutionDate only; OrderDate and SettlementDate are null.
ExecutionDate cannot precede any selected Account.OpenedOn and cannot be later
than the host's local operating date obtained through TimeProvider. PostedAtUtc
and CreatedAtUtc come from one application clock instant, never from the caller.

### Decision 8: make observed gross-gram price optional and explicit

**Recommended:** CashConsideration is always required for purchase and sale.
ExecutedUnitPrice is optional because a receipt may provide only a total and
jewelry may not be quoted per gross gram.

When supplied, UnitPrice means the seller's or buyer's explicit gold value per
one gross gram of the selected PhysicalGold Asset, before separately identified
cost treatments. It is positive E8 in the trade currency and is stored on the
Principal entry. A per-piece, fine-gram, estimated market, or retrospectively
divided all-in value must not be placed in this field.

When no supported executed price exists, preserve null. The UI may display a
clearly labelled derived all-in cash-per-gross-gram observation for convenience,
but it is not persisted or described as an executed price.

### Decision 9: retain exact price/consideration discrepancy evidence

**Recommended:** when ExecutedUnitPrice exists, compute price-implied gross with
checked `Int128` multiplication and midpoint-to-even conversion to integer minor
units, using the verified M008 arithmetic primitive.

Expected consideration is:

    purchase: rounded gross-gold value + IncludedInConsideration costs
    sale:     rounded gross-gold value
              - WithheldFromProceeds costs
              - IncludedInConsideration costs

AdditionalCashOutflow and InformationalOnly do not enter that comparison. An
absolute difference of at most one minor unit is rounding-consistent. A larger
difference requires a non-empty Note and remains visible; neither price nor cash
is overwritten. A negative expected sale consideration or overflow is rejected.
No discrepancy check is invented when executed price is absent.

### Decision 10: bound physical-gold costs and treatments

**Recommended:** accept these existing stable cost codes:

| Activity | Cost types | Treatments |
|---|---|---|
| Purchase | MakingCharge, Commission, OtherTax, Other | AdditionalCashOutflow, IncludedInConsideration, InformationalOnly |
| Sale | Commission, OtherTax, Other | AdditionalCashOutflow, WithheldFromProceeds, IncludedInConsideration, InformationalOnly |
| Transfer | Commission, Insurance, OtherTax, Other | AdditionalCashOutflow, InformationalOnly |

WithholdingTax, Brokerage, TitleDeed, Expertise, and Notary are not accepted by
default. `Other` requires a normalized component Note. An assay deduction uses
`Other` plus an explanatory Note until a later accepted vocabulary change; do
not add a new stable code casually.

Each component is positive Money. Duplicate canonical components are rejected,
the count is bounded, and additional-outflow fee/tax components create exact
aggregated negative cash entries in the selected cash scope. Included,
Withheld, and Informational components do not create a second cash entry.
Spread is never a cost component.

### Decision 11: use one unambiguous cash contract

**Recommended:** follow the accepted M008 source-fact contract:

    purchase cash effect = -(CashConsideration + AdditionalCashOutflow total)
    sale cash effect     = +(CashConsideration - AdditionalCashOutflow total)

CashConsideration on purchase is the actual trade-line debit and may already
contain IncludedInConsideration costs. CashConsideration on sale is the actual
trade-line credit received after WithheldFromProceeds or
IncludedInConsideration costs.

A transfer has no Consideration entry. If it has accepted additional cash
costs, it carries one selected cash scope and exact negative Fee/Tax entries.
Informational transfer costs have no cash entry.

### Decision 12: create one Known-cost lot from an ordinary purchase

**Recommended:** every successful purchase creates exactly one PhysicalGold
AssetLot whose:

- opening allocation equals the positive Principal gross weight;
- allocation piece detail equals the positive entered piece count;
- AcquiredOn equals ExecutionDate;
- PhysicalGoldLotDetail preserves fineness and optional identifiers;
- CostBasis is Known in the trade currency; and
- Known cost equals CashConsideration plus AdditionalCashOutflow total.

Included costs are already inside CashConsideration and are not added twice.
Informational costs do not change cost. An ordinary purchase with actual cash
paid cannot create Unknown or NotApplicable cost.

### Decision 13: use explicit physical selection for sale

**Recommended:** a gold sale does not use automatic FIFO. The user reviews and
submits one or more selected existing lot identities with exact positive gross
weight and positive whole-piece count to dispose.

Every selected lot belongs to the selected custody scope and PhysicalGold Asset.
The command creates one negative Principal entry for the aggregate gross weight,
one negative lot allocation per selected lot, and one matching negative piece
detail per allocation.

The selection order is canonicalized by immutable lot identity for
fingerprinting only. It does not claim that one physical item was sold before
another.

### Decision 14: permit partial grouped-lot movement only from exact facts

**Recommended:** a sale or transfer may move fewer pieces than a grouped lot
contains only when the operator supplies both the exact moved whole-piece count
and exact moved gross weight from source evidence or measurement.

Do not infer moved gross weight as:

    lot gross weight / original pieces * moved pieces

Do not infer moved pieces from a gross-weight ratio. Both global and selected-
scope gross and piece availability must remain non-negative. A full-lot action
uses the exact full currently available quantity and count.

### Decision 15: derive realized cost by gross allocation under ADR-009

**Recommended:** extend ADR-009 to PhysicalGold sale allocations, using gross
raw-E8 quantity as Q and q(i). Explicit physical selection chooses the lot;
ADR-009 only apportions that selected lot's Known original cost across its
effective partial sales.

The result reports complete, partial, or Unknown cost; separate currency
buckets; Known and Unknown disposed gross quantity; the method/version; and
whether the source sale remains effective. It never treats Unknown as zero and
does not compute gain/loss without an accepted valuation/FX context.

Acceptance of this decision extends ADR-009's scope through ADR-010. ADR-010 is
additive and leaves the accepted historical ADR unchanged.

### Decision 16: make reviewed sale and transfer plans stale-safe

**Recommended:** sale and transfer preview return a deterministic plan
fingerprint over:

- operation and version;
- Household and source/destination scopes;
- PhysicalGold Asset;
- each selected AssetLotId;
- exact gross quantity and PieceCount per line; and
- total requested gross quantity and pieces.

Post carries the reviewed plan. Inside the same database transaction that writes
the ledger graph and command receipt, Infrastructure re-derives current scoped
gross and piece availability and compares the reviewed facts before writing.
Changed or insufficient custody returns a stable sanitized conflict and leaves
no partial graph.

Equivalent same-key retry resolves its persisted receipt before current-state
eligibility. Same-key/different-command is an idempotency conflict. Different
keys may represent two genuinely separate identical physical events.

### Decision 17: preserve lineage through exact custody transfer

**Recommended:** one transfer command selects one source scope, one distinct
destination scope, one PhysicalGold Asset, and one or more exact lot movement
lines.

It creates exactly two Transfer entries for the gold Asset:

    source       negative gross weight in source Account/Portfolio
    destination  positive equal gross weight in destination Account/Portfolio

Each selected lot receives equal-and-opposite quantity allocations and equal-
and-opposite piece details on those entries. No new AssetLot is created; cost,
acquisition date, fineness, hallmark, certificate, and original PieceCount stay
unchanged. Total household gross weight, fine weight, and pieces net to zero.

Optional accepted transfer costs use separate cash Fee/Tax entries and never
mutate or capitalize the lot's original CostBasis.

### Decision 18: reuse immutable reversal with gold-specific eligibility

**Recommended:** expose a dedicated physical-gold correction route backed by
the generic M003 reversal engine plus gold-specific preview and persistence
checks.

- purchase reversal is blocked while any effective later allocation uses its
  lot;
- sale reversal restores the exact source-scope gross quantities and pieces and
  reverses every cash entry;
- transfer reversal moves the exact quantities and pieces from the original
  destination back to the original source and is blocked if current destination
  custody cannot support that inverse movement;
- reversal mirrors the exact entries, lot allocations, and piece movements;
  original costs and `PhysicalGoldTradeDetail` remain source facts on the
  original and are not copied to the reversal; and
- correction is a separately reviewed replacement with a new idempotency key.

The original remains Posted and readable. A reversal is never edited, deleted,
or reversed again.

### Decision 19: reuse narrow reference creation visibly

**Recommended:** add `ListPhysicalGoldChoicesUseCase` and focused wrappers over
M007's create-only Currency, Institution, Account, and Asset behavior.

The purchase, sale, and transfer UI visibly exposes creation of a missing
PhysicalVault, eligible cash Account, Cash/Currency Asset, PhysicalGold Asset,
or counterparty Institution before financial review. It never creates master
data as a hidden side effect of posting. Household and Portfolio remain existing
selections.

Do not add editing, deletion, archival, provider lookup, jewelry catalogs, or
broad master-data administration.

### Decision 20: isolate Fund and PhysicalGold persistence contracts

**Recommended:** add one forward migration after M008 migration 007,
provisionally named `008_PhysicalGoldLifecycle`. It must replace or supersede
the all-Buy/Sell Fund trigger only through an explicit asset-family dispatch:

- Fund Buy/Sell continues to satisfy every corrected M008 guard;
- PhysicalGold Buy/Sell satisfies the M009 gold shape;
- unsupported Buy/Sell principal asset families continue to fail closed;
- Fund verification cannot read a gold trade and gold verification cannot read
  a Fund trade; and
- PhysicalGold Transfer receives its own exact movement and scope guards.

Do not weaken a historical migration in place. Migration upgrade, Down/Up,
backup, restore, and both old Fund and opening-gold data must be tested.

## In scope

- PhysicalGold purchase preview, post, replay, receipt, restart readback, and
  correction.
- PhysicalGold selected-lot sale preview, post, realized-cost query, replay,
  receipt, restart readback, and correction.
- PhysicalGold selected-lot custody/Portfolio transfer preview, post, replay,
  receipt, restart readback, and correction.
- Exact gross E8, integer ppm fineness, derived fine weight, original pieces,
  signed piece movements, and current scoped inventory.
- One purchase lot representing one M007-compatible homogeneous inventory group.
- Separate physical-vault and cash Accounts.
- Optional counterparty Institution distinct from custody.
- Making charge and the bounded cost/treatment vocabulary in Decision 10.
- Optional explicit gross-gram execution price and exact discrepancy review.
- ADR-009 realized-cost extension to physical-gold gross allocations.
- Narrow create-only references required by the workflows.
- Application, API, Razor Pages, persistence, migration, recovery, privacy,
  accessibility, and real-browser verification.
- Focused Fund regressions at every Buy/Sell shared boundary.

## Out of scope

- bank or unallocated digital gold accounts;
- bullion-backed funds, certificates, futures, options, margin, lending, or
  collateral;
- pending orders, layaway, delivery promises, reservations, or execution;
- foreign-exchange conversion or multi-currency settlement inside one command;
- market bid/ask observations, spot prices, valuation, spread, or freshness;
- realized or unrealized gain, return, allocation, or goal progress;
- tax-lot advice, tax liability calculation, or religious/legal judgment;
- inferred fineness, inferred per-piece weight, or destructive assay modeling;
- melting, resizing, repair, loss, theft, gift, inheritance, exchange/barter,
  scrap conversion, or product transformation;
- document/image upload, receipt OCR, hashes, formal reconciliation, or
  inventory-count cases;
- generic transfer UI for Funds, Equity, Currency, or other assets;
- broad master-data editing, deactivation, deletion, or catalog integration;
- stored current weight, current pieces, remaining cost, average price, market
  value, or profit/loss; and
- M010 or later search, observations, analytics, allocation, and agent features.

## Invariants

- Posted history is immutable. Corrections use a separate Posted reversal and
  optional new replacement.
- The original transaction remains Posted; no Reversed status is introduced.
- Money uses signed integer minor units. Gross quantity and optional unit price
  use signed 64-bit E8. Fineness uses integer ppm. Piece movement uses signed
  checked integers.
- Binary floating point is forbidden for authoritative financial or weight
  arithmetic.
- PhysicalGold uses GrossGram, Required lot tracking, and PhysicalVault custody.
- Gross weight is authoritative; fine weight is derived per lot fineness.
- Unknown acquisition cost remains Unknown and is never treated as zero.
- AssetLot remains acquisition lineage and gains no AccountId, PortfolioId,
  current custody, remaining quantity, or remaining pieces.
- Current gross quantity is the sum of effective signed lot allocations.
- Current piece count is the sum of effective signed physical-gold allocation
  details.
- Every PhysicalGold allocation has exactly one matching piece movement and no
  other asset allocation has one.
- Gross and piece balances remain non-negative globally and within every custody
  scope.
- Required lot allocations reconcile exactly to each PhysicalGold entry.
- Purchase creates a new lot; sale and transfer do not.
- Transfer nets household gold gross quantity, fine weight, and pieces to zero
  and preserves acquisition lineage and cost.
- Sale uses user-selected physical lots and never silently substitutes FIFO.
- ADR-009 arithmetic derives realized cost from immutable effective history.
- Cost treatment never counts one cash amount twice. Spread is not a cost.
- All selected references are active, compatible, and household-consistent at
  first submission; historical readback survives later deactivation.
- Idempotency replay is receipt-first. A review or GET never posts data.
- A stale or racing custody plan cannot partially persist.
- Domain remains independent of EF Core, SQLite, HTTP, Razor, providers, and
  AI/LLM code.
- UI and agents never become authority for allocation, cost, piece, or
  fine-weight arithmetic.

## Application commands and queries

Recommended focused types, with final names settled during acceptance:

```text
ListPhysicalGoldChoicesUseCase
PreviewPhysicalGoldPurchaseUseCase
RecordPhysicalGoldPurchaseUseCase
PreviewPhysicalGoldSaleUseCase
RecordPhysicalGoldSaleUseCase
PreviewPhysicalGoldTransferUseCase
RecordPhysicalGoldTransferUseCase
GetPhysicalGoldActivityVerificationUseCase
GetPhysicalGoldCustodyInventoryUseCase
```

Recommended operation codes:

```text
RECORD_PHYSICAL_GOLD_PURCHASE
RECORD_PHYSICAL_GOLD_SALE
RECORD_PHYSICAL_GOLD_TRANSFER
```

Each command has its own versioned canonical fingerprint. Cost collections and
selected-lot lines are canonicalized independently of request order. Sale and
transfer additionally carry a separate reviewed-plan fingerprint so current
custody can be rechecked without confusing review freshness with command
idempotency.

Application owns:

- current reference and account/asset compatibility;
- text normalization and provenance requirements;
- exact date, currency, cost, and optional-price evaluation;
- purchase lot construction;
- selected-lot gross/piece availability planning;
- ADR-009 realized-cost composition;
- transfer and reversal eligibility;
- persisted result reconstruction; and
- stable sanitized error categories and codes.

Infrastructure owns exact persisted reads, atomic graph/receipt commits,
within-transaction stale-plan rechecks, migration guards, and recovery evidence.

## Required behavior

### Identify and prepare references

The Ready workflow identifies one Household and existing active Portfolio,
loads only compatible current choices, and offers explicit M007-compatible
creation when an eligible reference is missing. Reference creation has its own
reviewable request and conflict semantics and does not post a gold activity.

### Purchase review and post

The operator enters the physical and cash scopes, PhysicalGold product,
counterparty when known, dates, exact gross weight, fineness, pieces, cash paid,
optional explicit gross-gram price, making charge and other costs, hallmark,
certificate, reference, and note.

Preview is side-effect free. It shows normalized source facts, derived fine
weight, complete signed cash effects, price/consideration comparison when
available, cost treatment, one prospective lot, and all warnings. Post carries
the reviewed source facts and stable idempotency key. Success redirects to a
persisted receipt and creates one acquisition lot atomically.

### Sale review and post

The operator selects the exact source custody and PhysicalGold Asset, then
selects one or more currently available physical lots. Each line states exact
gross weight and pieces. Preview shows original and scoped availability,
fineness, gross/fine movement, cost knowledge, proceeds, costs, projected
realized cost, and the plan fingerprint.

Post recomputes availability and the exact plan in the write transaction. A
changed plan, insufficient gross weight, insufficient pieces, wrong scope, or
wrong asset writes nothing. Success consumes only the reviewed lots and returns
persisted verification.

### Transfer review and post

The operator chooses distinct source and destination PhysicalVault scopes and
selects exact lot gross/piece movement. Preview shows equal-and-opposite custody
effects, unchanged household totals, preserved lineage/cost/fineness, any
explicit cash cost, and the reviewed plan.

Post rechecks source custody and writes both transfer entries, both sides of
each lot allocation, all piece details, optional costs, and the command receipt
atomically. Destination receipt and inventory reconstruct correctly after
restart.

### Receipt, retry, and failure

Receipt reads only persisted facts. Direct navigation and refresh create
nothing. Equivalent same-key retry returns the original transaction before
current eligibility. Conflicting retry returns a stable conflict. Validation,
staleness, database-guard, and persistence failures expose no SQL, path,
connection string, stack trace, note, reference, hallmark, certificate, or raw
fingerprint.

### Reversal and replacement

Correction starts from persisted verification, shows exact inverse cash,
quantity, pieces, and custody effects, and requires a normalized reason. Post
uses the generic reversal command through gold-specific eligibility. Success
returns to the original physical-gold receipt showing both directions. A
replacement is a separate new purchase, sale, or transfer.

## API contract

Retain existing routes and add Ready-only adapters equivalent to:

```text
POST /api/ledger/physical-gold-purchases/preview
POST /api/ledger/physical-gold-purchases
POST /api/ledger/physical-gold-sales/preview
POST /api/ledger/physical-gold-sales
POST /api/ledger/physical-gold-transfers/preview
POST /api/ledger/physical-gold-transfers
GET  /api/households/{householdId}/ledger/physical-gold-activities/{transactionId}/verification
GET  /api/households/{householdId}/physical-gold/custody
```

Existing generic transaction and reversal routes remain. Any narrow reference
route preserves M007 create/equivalent/conflict behavior.

Transport uses raw signed/unsigned E8 integers as stated by field, integer minor
units, integer ppm, integer piece counts, ISO dates, GUIDs, and stable explicit
codes. Human-formatted Turkish values are never accepted as authority by JSON.

Posting requires exactly one valid `Idempotency-Key`; preview does not. Created
responses provide resolvable activity-verification and generic transaction
locations. Preview responses include exact normalized facts, warnings, cost
equations, selected plans, and plan fingerprints but no writable server session
or Draft identity.

Expected stable error families include invalid reference/shape, inactive
reference, invalid date, invalid quantity/pieces/fineness, invalid cost,
currency mismatch, discrepancy note required, unsupported price basis,
insufficient or changed custody, invalid selected lot, duplicate selection,
idempotency conflict, reversal blocked, and sanitized persistence conflict.

## UI contract

Add dedicated Ready routes:

```text
/record/physical-gold-purchase
/record/physical-gold-sale
/record/physical-gold-transfer
/record/physical-gold/{transactionId}/receipt
/record/physical-gold/{transactionId}/reverse
```

One common receipt route is intentional in M009 and must render the correct
Purchase, Sale, or Transfer vocabulary from verified asset-specific facts. It
must reject Fund and other transaction families.

Each form follows Identify, Source facts, Review, explicit Post, and persisted
receipt. Routine fields appear before advanced price, cost, identifiers, and
provenance. The UI works without JavaScript; enhancement may improve selection
but cannot own financial state or arithmetic.

Purchase review and receipt show gross grams, fineness in the accepted human
label plus ppm, exact fine grams, pieces, total cash, making charge treatment,
other cash effects, counterparty, and created lot.

Sale review and receipt show each selected lot, exact moved/remaining scoped
gross and pieces, fineness and fine weight, proceeds, cost components, and
completeness-aware realized cost. Transfer shows source and destination custody,
equal-and-opposite movements, preserved lineage, and unchanged household total.

Validation summary links to fields and receives focus. Control targets, keyboard
operation, narrow/zoomed reflow, reduced motion, forced colors, and sanitized
failure pages preserve the M006-M008 patterns. No page relies on color alone or
on hidden client storage as authority.

## Persistence impact

Expected forward migration after M008 migration 007:

```text
008_PhysicalGoldLifecycle
```

Accepted additive schema design, to be implemented by M009:

```text
PhysicalGoldLotAllocationDetail
    LotEntryAllocationId  PK/FK
    PieceDelta            signed INTEGER, non-zero

PhysicalGoldTradeDetail
    LedgerTransactionId       PK/FK
    CounterpartyInstitutionId nullable FK
```

The migration must:

- preflight existing PhysicalGold allocation history;
- backfill provable M007 opening and exact reversal piece movements;
- reject unprovable legacy gold movement rather than infer pieces;
- protect physical-gold piece-detail shape and posted immutability;
- protect global and scope-derived non-negative gross and piece availability;
- enforce purchase, sale, transfer, allocation, account, currency, cost, and
  counterparty compatibility at Draft-to-Posted;
- enforce exact purchase lot gross/piece/cost equations;
- enforce exact selected sale gross/piece reconciliation;
- enforce exact equal-and-opposite transfer gross/piece reconciliation;
- replace the broad M008 Buy/Sell guard with asset-family-specific enforcement
  while preserving every corrected Fund clause;
- reject unsupported Buy/Sell principal asset families;
- add an index only when the exact production query plan proves a need; and
- supply a Down path that removes only M009 objects and restores the exact
  corrected M008 guard behavior.

All activity graph rows, allocations, piece details, cost rows, asset-specific
trade detail, command receipt, and final Posted transition share one database
transaction. No current inventory, remaining weight, remaining pieces, average
cost, current value, or realized-cost amount is stored as authority.

Migration verification includes an M007 database with active and reversed
multi-piece gold openings, a corrected M008 database with Fund trades, a fresh
database, Down/Up, model drift, foreign-key/integrity checks, query plans where
applicable, verified pre-migration backup, isolated restore, and fresh-process
readback.

## Acceptance criteria

- The M008 hardening dependency gate is closed and its committed state agrees
  with source and tests.
- A Ready user can record one synthetic single-piece and one synthetic
  homogeneous multi-piece PhysicalGold purchase without raw API calls.
- Purchase persists exact gross quantity, fineness, original pieces, optional
  identifiers/counterparty, cash consideration, costs, Known cost basis, and
  one complete acquisition allocation.
- Fine weight is exact, derived, restart-stable, and never stored as an
  authoritative quantity.
- Current scoped pieces derive from signed allocation details and cannot become
  negative globally or in one custody scope.
- A user can partially sell an aggregate lot only by supplying exact gross and
  whole pieces; no per-piece weight is inferred.
- Sale consumes only explicitly selected lots in the chosen custody and writes
  exact negative gross and piece movements.
- Sale realized cost follows the accepted ADR-009 extension and reports
  Unknown/multi-currency completeness honestly.
- A user can transfer selected gold between two distinct custody scopes with
  exact equal-and-opposite gross and piece facts and unchanged household totals.
- Purchase, sale, and transfer preview are side-effect free; post is explicit,
  retry-safe, stale-safe, atomic, and reconstructable after restart.
- Equivalent retry returns the original receipt; conflicting retry and racing
  custody changes write nothing and expose stable sanitized errors.
- Purchase reversal respects downstream dependencies. Sale and transfer
  reversal restore the exact cash, gross, piece, and custody effects when
  eligible.
- Fund verification rejects PhysicalGold activity and physical-gold
  verification rejects Fund activity.
- Existing M001-M008 contribution, opening, Fund, reversal, navigation,
  startup, backup, and restore behavior remains green.
- Direct SQL cannot bypass the accepted gold Buy/Sell/Transfer shape, piece
  reconciliation, cost/cash equation, currency/account compatibility, or scoped
  availability.
- No live data, provider call, market price, valuation, document upload, or
  agent write is introduced.
- Full tests, formatting verification subject only to the documented baseline
  line-ending caveat, EF model drift, and a disposable recovery drill pass.
- `PROJECT_STATE.md` and `ROADMAP.md` are updated only after implementation is
  actually verified.

## Test scenarios

### Domain

- PhysicalGold allocation detail requires non-zero whole pieces and matching
  quantity sign.
- Acquisition gross/piece detail matches lot opening facts.
- Gross and fine-weight arithmetic covers zero rejection, exact ppm boundaries,
  E8 extremes, checked overflow, and no binary floating point.
- PhysicalGold cost rules cover every accepted/rejected type and treatment.
- Explicit selected-lot planning rejects duplicates, wrong asset, wrong scope,
  excessive gross, excessive pieces, and inferred partial-piece shapes.
- Transfer produces equal-and-opposite quantity and piece facts.
- ADR-009 physical-gold partial sales cover first, repeated, closing, reversed,
  Known, Unknown, mixed-currency, midpoint, residue, and overflow cases.
- Fund cost and FIFO behavior remains unchanged.

### Application

- Choice filtering and narrow reference creation use only accepted active shapes.
- Purchase validation covers Account.OpenedOn, dates, provenance, optional
  counterparty, fineness, pieces, optional executed price, cash, every cost
  treatment, discrepancy, and overflow.
- Purchase creates exactly one complete Known-cost gold lot.
- Sale and transfer preview are side-effect free and expose exact selected plans.
- Post rechecks both gross and piece availability and rejects stale review.
- Receipt-first replay succeeds after references or custody later change.
- Same-key/different-command conflicts; different-key identical events remain
  distinct.
- Partial grouped-lot movement requires separately entered exact gross and
  pieces.
- Transfer preserves all original lineage/detail/cost facts.
- Purchase dependency, sale restoration, transfer-destination eligibility, and
  corrected replacement are covered.
- PhysicalGold verification rejects non-gold Buy/Sell/Transfer and Fund
  verification rejects gold.

### Infrastructure

- Purchase, selected sale, transfer, and all three reversal paths round trip
  against real file-backed SQLite.
- Atomic rollback covers transaction, entry, cost, lot, allocation, piece
  detail, trade detail, receipt, and final posting failures.
- Real parallel independent-context sale and transfer races cannot over-consume
  source gross or pieces and return bounded persistence results.
- A transfer cannot draw from another Account/Portfolio scope even when global
  lot quantity is sufficient.
- Direct SQL cannot omit or forge gold detail, piece detail, cash scope, cost
  currency/treatment, selected allocation, counterparty relationship, or
  transfer equality.
- Global and scoped gross/piece non-negative triggers survive reversal races.
- Existing multi-piece opening and reversal data backfills exactly; unprovable
  history fails migration before mutation.
- Corrected Fund Buy/Sell guards survive M009 dispatch unchanged.
- Migration 007 -> 008, Down/Up, fresh database, pending-model check, integrity,
  backup, isolated restore, and fresh-process readback pass.

### API and UI host

- Every preview route is side-effect free and every post requires one valid
  Idempotency-Key and antiforgery token as appropriate.
- Raw contracts preserve E8, minor units, ppm, pieces, stable codes, dates,
  selected lot identities, and plan fingerprints exactly.
- Routes exist only in Ready and reject cross-household or wrong-family reads.
- Reference creation is explicit and never hidden inside trade posting.
- Validation links and focuses correctly; inputs survive failed post.
- Receipt refresh and direct navigation create nothing.
- Purchase, sale, transfer, receipt, and reverse pages contain no external
  resource dependency and work without JavaScript.
- Captured logs and errors omit note/reference/hallmark/certificate values,
  fingerprints, raw forms, SQL, resolved paths, and connection strings.

### Browser

- Starting from a prepared synthetic Ready database, create/select missing gold
  references, record two purchases, partially sell one selected lot, transfer
  another between vaults, inspect receipts, restart, and recover exact custody.
- Journey includes one 22K/916 bracelet, a homogeneous two-piece group, included
  making charge, additional cost, exact fine weight, and partial realized cost.
- Sale and transfer show exact selected lots and never silently use another
  custody scope.
- Reverse one sale or transfer, post a separate corrected replacement, and
  confirm gross/piece/cash reconstruction after restart.
- Double-submit/retry, JavaScript-disabled operation, keyboard-only validation,
  narrow and desktop viewports, 200%-equivalent reflow, reduced motion, forced
  colors, loopback-only access, host cleanup, and no retained artifacts pass.

## Verification commands

```powershell
dotnet test WealthLedger.slnx --no-restore --verbosity minimal
dotnet format WealthLedger.slnx --verify-no-changes --no-restore --verbosity minimal
dotnet ef migrations has-pending-model-changes --project src/WealthLedger.Infrastructure/WealthLedger.Infrastructure.csproj --startup-project src/WealthLedger.Infrastructure/WealthLedger.Infrastructure.csproj --context WealthLedgerDbContext --no-build
git diff --check
git status --short
```

Focused test filters and the exact disposable M004 backup/restore commands must
be recorded in the implementation verification section after they are known.
The existing Windows line-ending formatter caveat must be reported honestly and
must not conceal new formatting drift.

## Documentation updates

Acceptance documentation completed on 2026-09-14:

- this milestone records acceptance of all twenty decisions without amendment;
- ADR-010 records the piece-movement model and physical-gold extension of
  ADR-009 without rewriting accepted history;
- `ROADMAP.md`, `PROJECT_STATE.md`, and the ADR index distinguish the accepted
  plan from implemented or verified behavior; and
- canonical Domain/database/data-capture/architecture/UX prose remains
  unchanged until implementation establishes the actual behavior.

After implementation verification:

- set this milestone and ROADMAP M009 to Verified;
- update `PROJECT_STATE.md` concisely with actual source, migration, test, and
  recovery evidence;
- update `DOMAIN_LEDGER.md`, `DATABASE_DESIGN.md`, `DATA_CAPTURE.md`,
  `ARCHITECTURE.md`, and `UX_MVP.md` where actual behavior changed;
- record final API/UI routes and migration names; and
- identify M010 as the next Planned candidate without starting it.

## Suggested implementation sequence and commit boundaries

```text
docs(m009): accept physical-gold lifecycle contract
feat(domain): model signed physical-gold piece movement
feat(application): add physical-gold purchase evaluation and posting
feat(application): add selected gold sale and realized-cost derivation
feat(application): add custody transfer and correction eligibility
feat(persistence): persist gold movement details and guard activity atomically
test(persistence): verify gold lifecycle races and migration recovery
feat(api): expose exact physical-gold activity contracts
feat(ui): add reviewed gold purchase sale transfer and correction workflows
test(browser): verify synthetic physical-gold lifecycle
docs(state): record verified M009 checkpoint
```

The M008 prerequisite fixes were verified and merged through PR #12 before this
planning branch was rebased. Do not duplicate or reinterpret them inside M009
implementation commits.

## Risks and rollback

The principal modeling risk is treating original PieceCount as current
inventory. Decision 4 prevents that by recording signed piece movement beside
every physical-gold allocation and deriving current counts.

The principal evidence risk is inferring the weight of one item from an
aggregate lot. Decision 14 requires exact moved gross weight and pieces and
rejects average-based inference.

The principal accounting risk is double counting making charge or another cost.
Decisions 9-12 fix the cash and cost-basis equations and require matching
supporting entries.

The principal custody risk is allowing a globally positive lot to be sold or
transferred from a scope that does not hold it. Selected-scope planning,
within-transaction recheck, and SQLite non-negative scope guards all remain
required.

The principal compatibility risk is that M008's current posting trigger applies
Fund rules to every Buy/Sell. M009 must replace that behavior through a tested
asset-family dispatch, not weaken or edit migration 007.

The principal migration risk is unprovable historical piece movement. Preflight
and backup occur before schema mutation; migration fails closed rather than
guessing. A failed application rolls back. Recovery uses the independently
verified pre-migration package and the existing M004 staged-restore workflow.

The principal privacy risk is free text and physical identifiers appearing in
logs, screenshots, or fixtures. Tests use anonymous synthetic products and
forbid note, reference, hallmark, certificate, counterparty, path, and raw-form
payloads in captured logs. No real household database, receipt image, or
inventory detail is needed for development or verification.
