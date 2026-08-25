# WealthLedger MVP Interaction Model

Status: Proposed product interaction model

Last reviewed: 2026-08-24

## Scope and constraint

This document describes user interaction independently of a UI framework. It
does not accept Blazor, desktop, SPA, mobile, or another delivery technology.
That choice belongs to the UI milestone and an ADR.

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

The landing workspace answers:

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

A task-oriented entry point with explicit choices:

- Contribution
- Withdrawal
- Fund purchase
- Fund sale
- Physical-gold purchase
- Physical-gold sale
- Transfer
- Opening balance
- Adjustment
- Reverse or correct an existing transaction

Each choice opens a dedicated workflow rather than a generic transaction table
editor.

### Assets

Shows positions grouped by portfolio, account, institution, and asset family.
Fund positions show units and lots. Physical-gold inventory shows pieces, gross
and fine weight, fineness, acquisition lineage, and custody.

### Ledger

Lists transactions with filters for date, type, asset, institution, account,
portfolio, external reference, status, and reversal relationship. Selecting a
row opens a complete read-only transaction explanation.

### Plan

Shows reserve policy, home-purchase goal, target allocation ranges, current
deviation, planned monthly contribution, and documented decisions. Suggestions
are clearly separated from recorded facts.

### Settings

Manages household master data, assets, institutions, accounts, portfolios,
backup and restore, exports, data-source settings, privacy, and diagnostics.

## First-run experience

The first-run flow should:

1. Explain that the application is a ledger and that posted history is not
   edited.
2. Ask the user to choose or confirm the local data location.
3. Confirm that the location is not inside the source repository.
4. Create base currency, household, portfolio, institution, account, cash
   asset, and initial investment assets through human-readable fields.
5. Configure and verify a backup destination.
6. Offer an opening-balance import or a synthetic practice transaction.
7. Disable the setup path after successful initialization.

The implementation may split this flow across milestones. The user must never
need to construct GUIDs or raw transport values manually.

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

Routine fields:

- gold asset or product;
- physical-vault account;
- purchase date;
- gross weight;
- fineness;
- piece count;
- total cash paid;
- seller or institution.

Advanced fields:

- form or product label;
- hallmark or certificate reference;
- making charge and its treatment;
- other fees or taxes;
- cash account;
- note and evidence reference.

Fine weight is derived and displayed. It is not entered as a second
authoritative quantity.

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

- All workflows must be keyboard-operable and must not encode status by color
  alone.
- Confirmation text and errors use plain language.
- Screenshots and diagnostic exports default to hiding household names,
  references, notes, and exact values unless explicitly included.
- The UI must not expose connection strings, raw SQL, stack traces, or internal
  row representations during routine use.

## MVP UX acceptance

The interaction model is usable when a non-developer can initialize synthetic
data, create an opening position, record a contribution and acquisition, find
the resulting transaction and position, correct an error, and verify a backup
without issuing an HTTP request or editing SQLite directly.

