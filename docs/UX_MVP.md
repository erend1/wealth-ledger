# WealthLedger MVP Interaction Model

Status: Proposed product interaction model

Last reviewed: 2026-10-29

## Scope and constraint

This document describes the broader MVP interaction model, much of which remains
proposed independently of a delivery framework. The implemented M006 shell and
M007 opening-balance subset use server-rendered Razor Pages in a dedicated UI
assembly and the existing local API host, as accepted by ADR-008. The PageModels
invoke Application use cases directly; they do not call the co-hosted JSON API.
That decision does not accept Blazor, a SPA, desktop, or mobile delivery for
later workflows.

The MVP is a private household tool used periodically, especially before and
after a monthly allocation decision. It should optimize for correctness,
clarity, and low entry friction rather than trading-terminal density.

## Interaction principles

1. **Review before record.** The default landing view explains current state,
   stale data, and unresolved work before offering a new transaction.
2. **Economic language first.** Forms say amount, units, grams, price, cost,
   account, and date rather than exposing minor units or E8 integers.
3. **Progressive disclosure.** Routine fields are visible; settlement, source,
   certificate, cost treatment, and diagnostic fields expand when needed.
4. **Explicit posting.** A review screen shows the resulting asset and cash
   effects before a transaction becomes Posted.
5. **Correction, not editing.** Posted transaction pages offer Reverse or
   Correct, never Edit or Delete.
6. **Traceability everywhere.** A position opens its transactions and lots; an
   analytical result opens its inputs and method.
7. **Uncertainty remains visible.** Unknown cost, stale price, missing evidence,
   and unreconciled quantity are badges, not silently filled values.
8. **Safe retry.** A double-click, refresh, or reconnect does not duplicate a
   command.

## Primary navigation

### Today

The M006/M007 landing workspace shows only facts supported by current read
contracts:

- current local data-safety state;
- age and workspace binding of the latest applicable verified backup;
- an honest empty state when no Posted activity exists;
- a small recent-Posted preview with links to complete explanations.

It does not call a recorded contribution cash on hand, sum unlike quantities,
or fabricate balances, market values, returns, reserve, goal progress, price
freshness, or reconciliation status. Later accepted analytical milestones may
extend Today to answer:

- total recorded assets by major asset family;
- available liquid reserve;
- latest known valuation date and stale-price warnings;
- recent contributions, purchases, sales, and corrections;
- unresolved reconciliation or data-quality items;
- progress toward the active household goal;
- the next planned monthly review date.

It does not present a single return percentage without its time range, method,
cash-flow treatment, and source data.

### Record

Through M009, Record provides dedicated workflows rather than a generic
transaction editor:

- Contribution
- Fund purchase
- Fund sale
- Physical-gold purchase
- Physical-gold sale
- Physical-gold custody transfer
- Opening balance

The following choices remain later workflows:

- Withdrawal
- generic transfer for other asset families
- Adjustment

Each implemented receipt exposes only its bounded reversal/correction path and
links to a separately reviewed replacement where allowed. No page edits a
posted transaction.

### Assets

Assets was not implemented by M006. M010 adds it as a derived inventory
surface, in progress and not yet verified; see "Implemented M010 workflows (in
progress)" below. It shows positions grouped by portfolio and account, and
lot-tracked positions open their acquisition lots. Physical-gold lots show
pieces, gross and fine weight, fineness, acquisition lineage, and custody.

### Ledger

M006 implements the bounded recent Posted feed using M005's opaque cursor
unchanged. Selecting a row opens a complete read-only explanation of final M003
facts, exact effects, costs, lots, allocations, and both reversal directions.
Current master labels are visibly current context and inactive or archived
masters remain explainable.

M007 extends persisted transaction readback and Ledger detail with physical-gold
fineness, piece count, optional identifiers/notes, and exact derived fine weight.
Opening receipts are a focused verification view over the same persisted
history, not client-side success state.

M010 (in progress, not yet verified) replaces the unfiltered recent feed on the
Ledger page with bounded search over effective Posted history: date, type,
asset, institution, account, portfolio, external reference, and reversal
relationship filters, ordered by financial date. Status is fixed to Posted.
Today keeps the M005 recent-Posted preview.

### Reconcile

M010 (in progress, not yet verified) adds Reconcile for retained evidence and
dated reconciliation. It is described in "Implemented M010 workflows (in
progress)" below.

### Plan

Plan is not implemented by M006. A later accepted workflow may show reserve
policy, goals, target allocation ranges, deviation, planned contributions, and
documented decisions, with suggestions clearly separated from recorded facts.

### Settings

M006 implements a read-only Settings index, current household/master-data review,
and data-safety review. Data safety shows resolved paths, schema and integrity
state, encryption mode, workspace identity prefix, latest verified backup and
its workspace binding, unrelated verified-package count, and the two M004
operator attestations. It offers no disabled Save/Delete controls and does not
imply that rendered facts are editable.

The browser deliberately does not expose migration, restore, active database
replacement, backup-file selection, path override, filesystem browsing, or SQL.
Those M004 lifecycle operations remain in the explicit Operations CLI. Any
future browser administration requires a separately accepted security and
operations decision; it must not manipulate SQLite, archives, locks, or paths
directly.

M005 provides the verified read-only query foundation that M006 now consumes
directly through Application use cases:

- Today may consume the bounded recent Posted feed for its recent-activity
  region without treating execution date as posting recency;
- Ledger used that same default feed through M009 and follows a transaction
  identity to the complete detail route; M010 replaces the feed on the Ledger
  page with the broad search described above;
- Settings and guided setup may populate household, member, institution,
  portfolio, account, currency, and asset choices from the bounded current-
  display routes, including inactive/history choices when explicitly requested;
- account choices carry nullable current Institution context, while entry
  effects carry enough stable scope identities to open the point-position route;
- a valid empty position is distinct from the sanitized unknown/cross-household
  scope error.

Broad master-data editing, local administration, selector caching, and the later
Record/Assets/Plan workflows remain responsibilities of later accepted
milestones. M007 permits only opening-scoped create-and-use actions for a missing
Currency, Institution, Account, or Asset. Household and Portfolio remain
existing selections; there is no edit, delete, archive, or reactivation UI.

## First-run experience

M006 implements this restart-delimited first-run flow:

1. Explain that the application is a ledger and that posted history is not
   edited.
2. Review the already resolved, server-validated local data location without
   offering arbitrary path selection or filesystem browsing.
3. Create only a missing configured-safe database through the existing M004
   ownership-protected operation; never migrate or replace one.
4. Create base currency, household, portfolio, institution, account, cash
   asset, and one fund asset atomically through human-readable fields and
   reviewed stable codes.
5. Review the configured backup destination and create and verify one new,
   immutable, workspace-matched backup generation.
6. Explain that the process remains in its startup mode and require one clean
   restart before entering Ready.
7. Make every setup route unavailable in Ready.

The user never constructs GUIDs, E8 or minor-unit integers, connection strings,
SQL, or migration identities. Readiness is reconstructed from Application/M004
state on every request, not session, cookies, TempData, local storage, or a UI
cache. M007 begins only after Ready and therefore cannot bypass any of these
storage, workspace, backup, or restart gates.

## Monthly review flow

The intended recurring workflow is:

1. Open Today and check backup, price freshness, and reconciliation warnings.
2. Confirm the cash contribution available for the period.
3. Review current allocation and goal progress.
4. Record the contribution.
5. Record the selected fund and/or physical-gold purchase.
6. Inspect the posted transaction and resulting position.
7. Record the reasoning or next review note when useful.

The application records what was actually done. A proposed allocation remains
a proposal until corresponding transactions are explicitly posted.

## Transaction-entry pattern

Every posting workflow uses four stages:

### 1. Identify

Choose transaction type, date, portfolio, account, institution where relevant,
and asset.

### 2. Enter source facts

Enter the quantities, consideration, execution price, costs, physical details,
reference, and evidence defined by `DATA_CAPTURE.md`.

### 3. Review economic effect

Show, in formatted units:

- assets increasing and decreasing;
- account and portfolio affected;
- lots created or consumed;
- total cash movement;
- fees, taxes, and included costs;
- unknown or missing information;
- generated idempotency/retry identity as a diagnostic detail only.

### 4. Post and inspect

Require an explicit confirmation, post once, then navigate to the resolvable
transaction detail. The result screen links to position and lot impact.

## Opening-balance cutover

M007 implements this four-stage pattern for exactly one household, portfolio,
account, asset, and as-of date per command:

1. **Identify:** select existing Household/Portfolio context, a compatible
   Account and supported Asset, and the opening as-of date. Missing Currency,
   Institution, Account, or Asset may be created through a separate narrow
   create-and-use form that does not create a holding.
2. **Enter source facts:** enter a positive exact quantity, required source note,
   optional external reference, and the complete lot breakdown for Fund,
   Equity, or PhysicalGold. Cash/Currency shows cost as Not applicable and has
   no lot. Lot cost is Known only with a supported amount/currency or explicitly
   Unknown with neither.
3. **Review:** show normalized exact quantity, account/portfolio/asset/date,
   allocation total, cost state, acquisition dates, provenance, and physical-
   gold derived fine weight. Lot mismatch, invalid scale, incompatible choices,
   and semantic duplicate errors are linked to real fields. Review shows no
   current value, return, or profit/loss.
4. **Post and receipt:** require a final action, submit through the established
   idempotency contract, redirect to a receipt reconstructed from persisted
   ledger history, compare the submitted quantity with the derived scoped
   position, and link to full transaction detail.

Cash and foreign Currency accept CurrencyUnit/NONE in Cash, Investment, or
Pension accounts; Cash must use the household base currency and Currency must
not. Fund and Equity accept FundUnit/Share with Optional or Required lot
tracking in Investment or Pension accounts, but M007 requires complete opening
lots in both modes. PhysicalGold requires GrossGram/REQUIRED in a PhysicalVault.

The fineness control offers explicit 24K/999.9, 22K/916, 18K/750, 14K/585, and
8K/333 per-mille mappings plus a precise 0.001-to-1000 per-mille option with at
most three decimals. It displays the resulting ppm and derives exact fine grams
from authoritative gross weight. Pieces may be grouped only when their product,
fineness, acquisition, cost, identifiers, and provenance form one evidence
group.

A second effective opening in the same exact scope links to the existing
receipt and does not write. An opening also cannot be added over other effective
history. Correction uses `/record/opening-balance/{transactionId}/reverse`:
the user reviews exact inverse entries/allocations, supplies a reason, posts a
separate M003 reversal, and then records a separately reviewed replacement.

## Fund purchase form

Routine fields:

- fund;
- portfolio and investment account;
- cash account;
- execution date;
- acquired units;
- total cash paid;
- executed unit price and price currency.

Advanced fields:

- order and settlement dates;
- commission, withholding tax, other tax, or brokerage costs;
- source institution and external reference;
- note and evidence reference.

Before posting, the UI must highlight a material mismatch between units times
price and entered consideration. It must not silently rewrite either source
fact. The accepted tolerance and rounding policy belong to the use-case
milestone.

## Physical-gold purchase form

M009 implements `/record/physical-gold-purchase`. Routine fields are:

- gold asset or product;
- physical-vault account;
- purchase date;
- gross weight;
- fineness;
- piece count;
- total cash paid;
- optional seller Institution, shown separately from custody.

Advanced fields:

- form or product label;
- hallmark or certificate reference;
- making charge and its treatment;
- other fees or taxes;
- cash account;
- note and evidence reference.

Fine weight is derived and displayed. It is not entered as a second
authoritative quantity. Review shows exact gross raw-E8 presentation, immutable
fineness, derived fine weight, original pieces, cash consideration, every cost
treatment, Known acquisition cost, and any preserved price discrepancy. One
post creates one homogeneous acquisition lot.

## Physical-gold sale and transfer forms

`/record/physical-gold-sale` requires the operator to select the acquisition
lot or lots actually disposed from one custody scope and to enter exact moved
gross weight and whole pieces for each. Review shows those selections,
completeness-aware ADR-009 realized cost by currency, and a versioned plan
fingerprint. It never substitutes Fund FIFO, infers a per-piece weight, or
shows gain/loss.

`/record/physical-gold-transfer` selects distinct source and destination
PhysicalVault scopes and exact movements against the same acquisition lots.
Review shows equal-and-opposite gross and piece effects, unchanged household
totals and lineage, and any separately treated transfer cost. The transfer
creates no acquisition lot or consideration.

Both posts revalidate the reviewed source-custody plan inside the write
transaction. A changed plan returns a sanitized conflict and writes nothing.
Equivalent retry returns the original persisted receipt; a reused key with
different facts is rejected.

## Transaction detail and correction

A transaction detail must show:

- stable transaction identity, type, status, dates, and references;
- entries in plain language and exact units;
- costs and their treatments;
- cash-flow classification when present;
- lots created or consumed;
- original/reversal/corrected relationship;
- creation and posting timestamps;
- links to affected positions and evidence metadata.

Reverse begins with a dependency check and an explanation. If a reversal is
allowed, the UI previews the exact inverse effect. If blocked, it identifies the
later dependent activity without exposing storage internals.

## Value display rules

- Show currency codes with formatted amounts; do not rely on a symbol alone.
- Show fund units with enough precision to reproduce the recorded quantity.
- Show physical-gold weight in grams and preserve exact stored precision.
- Show a rounded display value without discarding access to the exact source
  value.
- Label estimated market value separately from cash consideration and cost
  basis.
- Display `Unknown`, `Not applicable`, and `Zero` distinctly.
- Every percentage states its denominator and effective date in context.

## Empty, loading, and failure states

An empty screen explains the next valid action. A loading state does not invite
duplicate submission. A failed submission preserves the user's input and makes
clear whether nothing was posted, the original submission succeeded, or the
result must be retrieved by its retry identity.

## Accessibility and privacy

- The implemented setup and shell have semantic landmarks, one clear heading,
  a first-focusable skip link, programmatic labels/help, linked validation
  summaries, visible keyboard focus, logical navigation, and text-based status.
- Layouts reflow at narrow and desktop widths and at a 200%-equivalent effective
  viewport. CSS respects reduced-motion and forced-color preferences.
- Critical first-run, Ledger navigation, opening, Fund, and physical-gold
  review/post/receipt/reversal paths pass in real Chromium with JavaScript
  disabled and with keyboard-only operation. They include linked financial
  validation, double-submit/retry, restart readback, narrow and
  200%-equivalent reflow, reduced motion, and forced colors. These focused
  checks do not claim general WCAG conformance or replace assistive-technology
  review.
- Confirmation text and errors use plain, sanitized language.
- Screenshots and diagnostic exports default to hiding household names,
  references, notes, and exact values unless explicitly included.
- The UI must not expose connection strings, raw SQL, stack traces, or internal
  row representations during routine use.
- M006-M009 add no screenshot or diagnostic-export feature. Tests and any
  manually captured artifacts use synthetic isolated data only.

## MVP UX acceptance

The M006 subset is verified when a non-developer can initialize synthetic local
storage and core masters, create and verify the first backup, restart into Ready,
and browse Today, recent Ledger explanations, and read-only Settings without
issuing an HTTP request or editing SQLite directly. That outcome is implemented
and covered by real-browser journeys.

The M007 subset is verified when that Ready user can create only the missing
opening-scoped references, review and post cash/currency, fund, equity, and
physical-gold openings, inspect persisted receipts and derived positions, and
reverse and replace an incorrect opening without editing history. That outcome
is implemented and covered by real-browser and retained synthetic recovery
journeys.

The M008/M009 subset is verified when that Ready user can record a contribution,
review and post Fund or physical-gold purchases and sales, transfer selected
gold custody, inspect persisted receipts, and correct supported activity through
reversal plus a separate reviewed replacement. That outcome is implemented and
covered by real-browser and disposable recovery journeys.

The first M002-M009 entry slice is complete. Searchable inventory and
reconciliation, evidence objects, valuation, market observations, and planning
remain in later milestones.

## Implemented recording workflows (M008)

Record is a hub that names each workflow rather than offering one generic
debit and credit table:

```text
/record
/record/contribution
/record/fund-purchase
/record/fund-sale
/record/fund-trade/{transactionId}/receipt
/record/fund-trade/{transactionId}/reverse
/record/opening-balance
```

Each flow runs the accepted four stages: identify, enter source facts, review
the exact economic effect, then post and inspect the persisted receipt.
Preview writes nothing. Post/Redirect/Get means refreshing a receipt records
nothing a second time, and the receipt rebuilds from persisted facts so it
survives a restart and direct navigation.

Review states exact decimal values, both accounts, the total cash movement,
each cost treatment, the lots affected, and every warning. A sale additionally
shows the ordered FIFO plan with each lot's acquisition-date and cost
knowledge, and realized cost with its completeness, currency buckets and
method. It never shows current value, gain, return or tax.

Unknown, known-zero and not-applicable are rendered as distinct states and
never collapse into each other. A partially known realized cost is labelled as
covering only the known part.

Correction runs from the receipt. The reversal page states current eligibility
in the user's terms, shows the exact opposite effects and the lot quantities
that would be restored, and requires a reason. Posting it leaves the original
Posted and writes a separate reversal; a corrected trade is then a new,
separately reviewed submission with its own command identity. A purchase whose
units have since been sold explains the dependency and offers no reversal form
at all.

## Implemented physical-gold workflows (M009)

The Record hub adds these Ready-only routes:

```text
/record/physical-gold-purchase
/record/physical-gold-sale
/record/physical-gold-transfer
/record/physical-gold/{transactionId}/receipt
/record/physical-gold/{transactionId}/reverse
```

Purchase, sale, and transfer use server-rendered Turkish-first forms and call
Application directly from their Razor PageModels. They require antiforgery,
work without JavaScript, use Post/Redirect/Get, and reconstruct receipts from
persisted verification rather than browser state. The common receipt route
fails closed for Fund or unsupported transaction families.

Purchase records exact gross weight, fineness, derived fine weight, original
pieces, optional seller and identifiers, cash, costs, and one Known-cost lot.
Sale displays only explicitly selected source-custody lots, exact negative
gross/piece effects, proceeds, and completeness-aware realized cost. Transfer
displays the same acquisition lots leaving one scope and entering another with
equal-and-opposite gross and piece facts.

Correction starts from the persisted receipt. Reversal mirrors entries,
allocations, and piece movements exactly and never copies original cost or
counterparty rows. After reversal, the UI offers a new normal workflow for a
separately reviewed corrected replacement; it does not claim a mutable or
durable replacement link.

## Implemented M010 workflows (in progress)

M010 is In Progress. The pages below are implemented with focused host and
presentation tests. Their real-browser journey, recovery drill and
verification are still outstanding, so nothing here is Verified yet. The
primary navigation now reads Today, Ledger, Assets, Reconcile, Record and
Settings:

```text
/ledger
/assets
/assets/lots
/reconcile
/reconcile/evidence/new
/reconcile/evidence/{evidenceId}
/reconcile/evidence/{evidenceId}/observe
/reconcile/runs/{runId}
```

All of them are Ready-only Razor Pages that call Application use cases
directly. They use the Turkish-first presentation of exact values, work
without JavaScript, require antiforgery on every POST, and show no market
value, price, return or performance. Stable identifiers, raw E8 values and
digests appear only under technical details. Failures show sanitized messages
keyed by stable error codes and never echo input, cursors, SQL or paths.

**Ledger** is a GET search form. Results order by financial date, then
posting time and identity, and each opens the existing transaction
explanation. Both reversal directions are marked and linked. The next-page
link carries the same filters, so the opaque cursor stays bound to them; a
cursor reused with other filters is refused. A malformed filter is a field
message. An identity that does not exist in the household is a sanitized
"scope not found", never an empty result.

**Assets** shows the current position, or the position as of a financial
date. Every quantity is summed by the inventory use case from effective
Posted entries; none is stored. Exact zero is hidden unless "show zero" is
chosen. A valid zero is then labelled as such, and an unknown scope is never
shown as zero.
- Each position links to the Ledger search over its exact scope, with the
  as-of date as the upper bound.
- A lot-tracked position also links to its lots.
- The lot page pages through acquisition lots. Each shows its original and
  current quantity, its Known, Unknown or not-applicable cost, and its
  custody by scope. A gold lot adds immutable fineness, whole pieces and
  derived fine weight.
- Allocations link to their transactions. Exhausted lots appear only when
  asked.

**Reconcile** lists evidence records and reconciliation runs. A run in the
list shows its stored counts; only its own page re-derives staleness.

- *Evidence capture* is one explicit multipart step. It takes a source kind,
  an effective date (not in the future), a label, optional institution or
  account context, a reference and a note. Retaining one file is optional:
  at most 10 MiB, as PDF, PNG, JPEG, UTF-8 text or UTF-8 CSV. A page-issued
  retry key makes a double submit return the first record. After a
  validation error the file must be chosen again.
- *The evidence page* shows the record's metadata. A retained file is
  offered only as a download: the response forces an attachment and sends
  `nosniff`, `no-store` and a sandbox content-security policy. It is never
  previewed.
- *Links.* The reviewer selects Posted transactions to link, from the
  evidence's account (or else its institution) dated on or before the
  effective date. Linking changes no ledger fact.
- *Corrections.* A record is immutable. A mistake is corrected by recording
  a superseding record. The old record stays readable and accepts no new
  links, observations or runs.
- *Observation entry* takes one exact portfolio, account and asset scope. The
  quantity is entered in the asset's unit with at most eight decimal places
  and no thousands separator; it is never rounded. The tolerance is entered
  the same way and defaults to zero. A piece count is accepted for physical
  gold only. A review step shows the exact parsed value before anything is
  recorded. A scope the evidence does not mention is "not observed", not
  zero.
- *A run* compares every observation with the ledger as of the evidence
  date, using a page-issued retry key. Its page shows observed, ledger,
  delta and tolerance for each item. Matched, OutOfTolerance, Open,
  CorrectedByTransaction, DocumentedException and Stale each have their own
  text and badge style, so none relies on colour.
- *Staleness.* When later history changes the ledger as of the evidence
  date, the run is marked stale. It shows the freshly derived quantity and
  offers a rerun, which creates a new run instead of rewriting the old one.
- *Resolution.* An open out-of-tolerance item is resolved in one of two
  ways. The first is a documented exception with a required explanation; it
  stays visibly out of tolerance. The second links a Posted corrective
  transaction recorded through the existing Record workflows. Reconcile
  itself never posts or edits a transaction.
