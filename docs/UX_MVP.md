# WealthLedger MVP Interaction Model

Status: Proposed product interaction model

Last reviewed: 2026-09-08

## Scope and constraint

This document describes the broader MVP interaction model, much of which remains
proposed independently of a delivery framework. The implemented M006 subset uses
server-rendered Razor Pages in a dedicated UI assembly and the existing local API
host, as accepted by ADR-008. That decision does not accept Blazor, a SPA,
desktop, or mobile delivery for later workflows.

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

The M006 landing workspace shows only facts supported by current read contracts:

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

Record is not implemented by M006. It remains a later task-oriented entry point
with explicit choices:

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

Assets is not implemented by M006. A later accepted workflow may show positions
grouped by portfolio, account, institution, and asset family. Fund positions may
show units and lots; physical-gold inventory may show pieces, gross and fine
weight, fineness, acquisition lineage, and custody.

### Ledger

M006 implements the bounded recent Posted feed using M005's opaque cursor
unchanged. Selecting a row opens a complete read-only explanation of final M003
facts, exact effects, costs, lots, allocations, and both reversal directions.
Current master labels are visibly current context and inactive or archived
masters remain explainable.

Broad filters for date, type, asset, institution, account, portfolio, external
reference, status, and reversal relationship remain part of M010 rather than
M006.

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
- Ledger may use that same default feed and follow a transaction identity to
  the complete detail route; the broad filters described above remain M010;
- Settings and guided setup may populate household, member, institution,
  portfolio, account, currency, and asset choices from the bounded current-
  display routes, including inactive/history choices when explicitly requested;
- account choices carry nullable current Institution context, while entry
  effects carry enough stable scope identities to open the point-position route;
- a valid empty position is distinct from the sanitized unknown/cross-household
  scope error.

Master-data editing, broad local administration, selector caching, and the later
Record/Assets/Plan workflows remain responsibilities of later accepted
milestones. M006 presentation formatting is implemented in `WealthLedger.UI`.

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
cache. Opening-balance import and practice transaction entry are not M006
features and remain later work.

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

- The implemented setup and shell have semantic landmarks, one clear heading,
  a first-focusable skip link, programmatic labels/help, linked validation
  summaries, visible keyboard focus, logical navigation, and text-based status.
- Layouts reflow at narrow and desktop widths and at a 200%-equivalent effective
  viewport. CSS respects reduced-motion and forced-color preferences.
- Critical first-run and Ledger navigation pass in real Chromium with JavaScript
  disabled and with keyboard-only operation. These focused checks do not claim
  general WCAG conformance or replace assistive-technology review.
- Confirmation text and errors use plain, sanitized language.
- Screenshots and diagnostic exports default to hiding household names,
  references, notes, and exact values unless explicitly included.
- The UI must not expose connection strings, raw SQL, stack traces, or internal
  row representations during routine use.
- M006 adds no screenshot or diagnostic-export feature. Tests and any manually
  captured artifacts use synthetic isolated data only.

## MVP UX acceptance

The M006 subset is verified when a non-developer can initialize synthetic local
storage and core masters, create and verify the first backup, restart into Ready,
and browse Today, recent Ledger explanations, and read-only Settings without
issuing an HTTP request or editing SQLite directly. That outcome is implemented
and covered by real-browser journeys.

The broader MVP interaction model is not yet complete. Opening positions,
ordinary contribution/acquisition entry, position navigation, and correction
from the UI remain in M007 and later accepted milestones.
