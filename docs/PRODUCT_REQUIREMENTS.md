# WealthLedger Product Requirements

Status: Canonical product requirements

Last reviewed: 2026-08-24

## Purpose

WealthLedger is a private household investment record and decision-support
system. It must preserve durable financial facts, make current state
explainable from those facts, and help a household review its position before
making the next contribution, purchase, sale, transfer, or correction.

The product is a record system first and an analysis tool second. It is not an
order-execution platform, a return-guarantee engine, or a replacement for
professional financial, tax, legal, or religious advice.

This document defines product outcomes and user-facing capabilities. It does
not override the accounting rules in `DOMAIN_LEDGER.md`, the architecture in
`ARCHITECTURE.md`, the persistence model in `DATABASE_DESIGN.md`, or accepted
ADRs. Implemented reality remains recorded in `PROJECT_STATE.md`.

## Primary users

### Household operator

Records contributions, acquisitions, disposals, transfers, costs, physical
inventory, and corrections. The operator needs a short, safe path from source
document to posted transaction without learning raw API or fixed-point storage
formats.

### Household reviewer

Reviews positions, custody locations, acquisition history, progress toward a
goal, and data-quality warnings. The reviewer may approve a purchase plan or
identify an incorrect record without editing posted history.

### Analyst or approved agent

Consumes deterministic, read-only query models to explain current state,
compare alternatives, and propose a next action. It must never become the
authority for ledger arithmetic or post a transaction silently.

One person may perform all three roles. The role descriptions define product
responsibilities rather than an initial authorization model.

## Core user outcomes

The product must eventually allow a household to:

1. Know what it owns, where it is held, and why it is held.
2. Trace every position back to contributions, trades, transfers, opening
   balances, lots, and corrections.
3. Record investment-fund and physical-gold activity without losing fees,
   taxes, making charges, fineness, weight, or custody facts.
4. Review the ledger before allocating a new monthly contribution.
5. Distinguish contributed capital, internal trades, investment return, market
   movement, and costs.
6. Measure progress toward a home-purchase goal without treating an uncertain
   market value as guaranteed cash.
7. Reconstruct history after an application defect, machine failure, or model
   change.
8. Export governed facts for independent analysis without exposing the
   database to direct agent writes.

## Functional requirements

### PR-001: First-run setup and master data

The user can initialize and later manage the household, optional members,
institutions, portfolios, accounts, currencies, and assets needed by the
ledger.

Master data must use human-readable names while preserving stable identifiers.
Deactivation or archival must not delete referenced financial history.

### PR-002: Opening position cutover

The user can bring pre-existing cash, fund units, equities, and physical-gold
lots into the ledger through explicit opening-balance transactions.

Known historical cost is recorded only when supported. Unknown cost remains
explicitly unknown. The product must not infer lifetime return from a fabricated
acquisition price.

### PR-003: Cash-boundary activity

The user can record contributions and withdrawals separately from trades and
transfers. A contribution identifies the destination portfolio, account,
currency asset, business date, amount, optional household-member attribution,
source reference, and note.

### PR-004: Investment-fund lifecycle

The user can record fund purchases and sales with exact units, cash
consideration, execution date, settlement date when applicable, executed unit
price, currency, fees, taxes, account, portfolio, and source reference.

Sales must use deterministic lot allocation and report realized cost only from
recorded acquisition lineage.

### PR-005: Physical-gold lifecycle

The user can record physical-gold purchases, sales, transfers, and opening
balances. Relevant source facts include gross weight, fineness, piece count,
form or product identity, custodian, seller, hallmark or certificate when
available, cash consideration, and making-charge treatment.

The system must distinguish an included making charge from an additional cash
outflow and must not record bid/ask spread as a transaction fee.

### PR-006: Transfers and custody

The user can move an asset between accounts or portfolio purposes without
creating a new acquisition history or changing total household quantity.

### PR-007: Immutable corrections

The user can inspect a posted transaction, understand its downstream
dependencies, and correct it through a posted reversal followed by a new
transaction when required. Posted facts are never edited or deleted.

### PR-008: Ledger exploration

The user can list, filter, and inspect transactions by date, type, asset,
account, portfolio, institution, reference, and correction relationship.

Every displayed position, lot quantity, cost, and gain must provide a path to
the facts used to derive it.

### PR-009: Positions and inventory

The user can view signed cash and asset positions by household, portfolio,
account, institution, and asset. Physical inventory views expose pieces,
weights, fineness, and custody without duplicating authoritative quantities.

### PR-010: Market and reference observations

The user can enter or import dated price, foreign-exchange, and physical-gold
bid/ask observations with source and freshness metadata. A market observation
is not a ledger transaction and never rewrites historical acquisition facts.

### PR-011: Valuation and performance

The product can derive portfolio value, allocation, realized and unrealized
gain, cash flows, and performance from ledger facts plus dated reference data.

Calculations must state their valuation date, data freshness, base currency,
method, assumptions, and limitations. Money-weighted and time-weighted return
must not be presented as interchangeable measures.

### PR-012: Goal and allocation policy

The user can define a goal amount, target horizon or flexible timing, liquidity
needs, minimum reserve, target allocation ranges, and risk-reduction rules.

The product compares current derived state with policy. It may propose a plan
but does not guarantee a return or automatically trade.

### PR-013: Reconciliation and data quality

The user can compare recorded positions with a statement or physical count,
record the evidence date and source, and resolve differences through explicit
transactions or documented exceptions.

Missing prices, unknown cost, stale observations, inconsistent references, and
unreconciled positions must remain visible.

### PR-014: Decision journal

The user can record a dated decision or proposed action, its assumptions,
alternatives, expected role, review date, and later outcome. A decision note is
analysis history, not a posted financial fact.

### PR-015: Backup, restore, and export

The user can create and verify a recoverable backup, restore into an explicit
target, and export human-readable and machine-readable records. Backup status
and last successful verification must be visible before real household data is
trusted to the application.

### PR-016: Governed agent access

Approved agents can read stable query contracts for transactions, positions,
lots, valuations, allocation, reconciliation, and decision history. An agent
write must be represented as a reviewable application command and requires
explicit human confirmation.

## Usability requirements

- Human-facing forms use formatted currencies, units, grams, percentages, and
  dates. Raw minor-unit and E8 values remain transport or diagnostic details.
- Every irreversible posting action has a review step that shows the complete
  economic effect in plain language.
- Retry, double-click, refresh, and network interruption must not silently
  duplicate a financial transaction.
- Errors explain which input or invariant failed without exposing database
  internals.
- The default experience favors a small number of explicit workflows over a
  generic database editor.
- Advanced fields remain available without overwhelming routine monthly entry.
- Empty, unknown, zero, and not applicable remain distinct states.

## Quality attributes

### Correctness and auditability

Deterministic arithmetic, immutable posted history, exact storage, tested
invariants, and reproducible queries take priority over visual convenience.

### Privacy

The initial product is private and local-first. Source control, tests, logs,
documentation, screenshots, and agent prompts use synthetic data. Real database
files and recoverable backups are never committed.

### Recoverability

The loss of one workstation or one database file must not destroy the only
copy of household history. Restore must be tested, not merely assumed.

### Explainability

Every analytical number must expose its source period, inputs, and method.
Agent-generated prose must be distinguishable from deterministic results.

### Extensibility

New asset families and analytics extend the ledger without introducing a
second authoritative balance system or contaminating generic transaction
facts with asset-specific fields.

## Initial product boundaries

The initial product does not:

- execute brokerage, bank, jeweler, or exchange orders;
- scrape credentials or depend on screen automation for authoritative data;
- promise maximum return, predict a purchase date, or recommend a guaranteed
  security;
- calculate authoritative tax or religious rulings;
- require cloud hosting, microservices, queues, or real-time market feeds;
- permit an LLM to edit SQLite or perform authoritative lot allocation;
- store unnecessary personal-profile data.

## Product success criteria

The first genuinely usable release is successful when a household can start
from a protected local database, import opening positions, record routine fund
and physical-gold activity through a clear interface, correct mistakes without
history loss, review positions and transactions, and restore a verified backup
without raw API calls or direct database manipulation.

Later analytical success requires that a monthly review can explain changes in
wealth from contributions, withdrawals, trades, costs, and market movement and
can compare the result with an explicit goal and allocation policy.

