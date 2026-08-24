# WealthLedger Agent Instructions

## Mission

WealthLedger is a long-lived household multi-asset investment ledger. It records economic events and acquisition lineage accurately enough to support audit, positions, cost basis, performance, allocation, reconciliation, and later decision-support features.

It is not merely a portfolio dashboard. Durable historical facts take priority over convenient cached totals.

All code, identifiers, database names, API routes, tests, comments, commit messages, and canonical documentation are written in English.

## Start every task this way

1. Read this file and docs/PROJECT_STATE.md.
2. Read only the architecture, domain, database, and ADR material relevant to the task.
3. Inspect the actual solution, source, tests, migrations, package references, and git status.
4. Run the existing tests before changing behavior.
5. Compare repository reality with docs/PROJECT_STATE.md. Do not assume a conversationally reported file exists.
6. Keep the change inside the requested milestone.
7. Run focused tests and then the appropriate full suite.
8. Update docs/PROJECT_STATE.md when the verified project checkpoint changes. Add or supersede an ADR only when an architectural decision changes.

Do not modify historical transcripts to make them look current. Historical material may contain superseded designs.

## Authority and conflict handling

For actual implemented behavior, current source code and passing tests are the evidence. For intended architecture, this file and accepted ADRs are authoritative.

When sources conflict:

1. Follow the current user request and active repository instructions.
2. Inspect source code and tests to establish implementation reality.
3. Apply the non-negotiable rules in this file and accepted ADRs.
4. Use PROJECT_STATE for milestone status.
5. Use the architecture, domain, and database documents for canonical detail.
6. Treat conversation transcripts and history as reference only.

Do not silently reshape code to match prose or rewrite prose to excuse a design violation. Report a material conflict, explain the impact, and resolve it explicitly with code, tests, docs, and an ADR update where appropriate.

## Technology and project boundaries

- Runtime: .NET 10.
- API: ASP.NET Core Minimal API.
- Persistence target: EF Core with SQLite.
- Tests: xUnit and the repository's established assertion library.
- Architecture: Clean Architecture with inward dependencies.

Expected projects:

    WealthLedger.Domain
    WealthLedger.Application
    WealthLedger.Infrastructure
    WealthLedger.Api
    WealthLedger.UI

Domain must not reference EF Core, SQLite, HTTP, UI frameworks, market-data providers, or AI/LLM libraries.

Application defines and orchestrates use cases and persistence/external-service ports.

Infrastructure implements persistence and external integrations.

API maps transport contracts to Application use cases. API request/response models do not become Domain entities.

UI technology is not yet an accepted decision. Do not couple the core to a UI framework.

## Non-negotiable domain rules

- The ledger is the source of truth.
- Current position, remaining lot quantity, portfolio value, average cost, profit/loss, and allocation percentage are derived, not authoritative mutable state.
- Posted transactions and their effective facts are immutable and cannot be deleted.
- Corrections use a separate posted reversal and, when necessary, a new corrected transaction.
- The original transaction remains Posted. There is no Reversed lifecycle status.
- A reversal mirrors the original effective entries with opposite quantity deltas.
- Money is stored as signed integer minor units.
- Quantities, quantity deltas, and unit prices use signed 64-bit fixed-point E8 representation where applicable.
- Fineness uses integer parts per million; allocation weights use integer basis points.
- Never use binary floating point for authoritative financial values.
- Use checked arithmetic and deliberate decimal conversion/rounding at boundaries.
- Persist enum-like values as stable explicit text codes, never CLR enum ordinals.
- Unknown is not zero. Unknown historical cost must remain explicitly Unknown with no fabricated amount.
- LotEntryAllocation is the signed relation between AssetLot and TransactionEntry.
- AssetLot represents acquisition lineage, not custody or portfolio ownership. It must not acquire AccountId or PortfolioId.
- Lot current quantity is the sum of signed allocations and may never become negative.
- For a lot-tracked entry, allocations across lots must reconcile exactly to the entry quantity.
- Contribution is external capital entering the ledger; Buy is an internal exchange of cash for an asset. Do not conflate them.
- Spread is a market quote relationship, not a transaction cost.
- Asset-specific attributes belong in detail types/tables and must not contaminate the generic ledger.

See docs/DOMAIN_LEDGER.md and the accepted ADRs for the full model.

## Aggregate boundaries

LedgerTransaction is an aggregate root. Its children include:

- TransactionEntry
- TransactionCostComponent
- CashFlowDetail

AssetLot is an aggregate root. Its children include:

- LotEntryAllocation
- PhysicalGoldLotDetail

Do not add child-specific repositories such as ITransactionEntryRepository or ILotEntryAllocationRepository. Persist and mutate children through their aggregate boundary.

Repository interfaces should be driven by Application use cases. Do not introduce a generic repository or generic service abstraction.

Cross-aggregate rules that require stored history belong in an Application service backed by explicit queries and protected by database constraints where practical.

## Persistence rules

- Use explicit EF Core configurations; do not rely on accidental conventions for financial mappings.
- Store Guid values in the established SQLite representation, expected initially as TEXT.
- Store business dates as ISO YYYY-MM-DD values and audit timestamps as UTC ISO-8601 values.
- Enable and test SQLite foreign-key enforcement.
- Use restrictive deletes for durable master and ledger history. Cascades are permitted only for owned draft aggregate children and must not bypass posted-history protection.
- Add database protection for posted transaction graphs, not only application-level guards.
- Name the first persistence milestone/migration 001_CoreLedger unless repository history already establishes another accepted name.
- Do not create authoritative CurrentPosition, RemainingLotQuantity, CurrentPortfolioValue, AveragePurchasePrice, CurrentProfit, or CurrentAllocationPercentage tables.
- Performance projections may later use views, queries, or rebuildable read models. They must remain derivable from ledger facts and clearly non-authoritative.

## AI and analysis boundary

An agent or LLM may read approved query models and propose actions. It must not:

- write directly to SQLite;
- bypass Application use cases or Domain validation;
- perform authoritative lot allocation or financial arithmetic;
- silently post transactions;
- treat a generated analysis as ledger fact.

Deterministic code performs valuation, allocation, cost-basis, and reconciliation mathematics. Any future write action requires explicit validation and an auditable application command.

## Avoid unless a concrete need is accepted

- MediatR
- AutoMapper
- GenericRepository of T
- generic service layers
- event bus
- microservices
- Redis
- a CQRS framework
- domain events added only for ceremony
- premature market-data or AI integration

Prefer direct Application use-case classes, explicit mappings, focused repositories, and ordinary dependency injection.

## Test expectations

Every financial invariant changed or added must have focused unit tests. Persistence work must add integration tests against real SQLite behavior, including:

- fixed-point round trips and overflow boundaries;
- stable enum-code mappings;
- foreign keys and uniqueness;
- posted aggregate immutability;
- reversal uniqueness and inverse entries;
- household consistency;
- lot/entry asset matching;
- allocation sign, exact reconciliation, and non-negative lot balance;
- known versus unknown cost basis;
- transaction date ordering.

Test derived queries from transaction history rather than seeding authoritative balance fields.

## Documentation discipline

PROJECT_STATE is a concise, factual handoff and should be updated after a milestone. Do not turn it into a changelog.

Create an ADR when accepting, superseding, or materially changing a cross-cutting decision. Never edit an old accepted ADR to hide a later change; add a new ADR and mark the old one Superseded.

Keep examples anonymous and synthetic. Do not place household names, addresses, account numbers, balances, income, or other personal financial details in code, tests, fixtures, logs, screenshots, or docs unless strictly required and explicitly authorized.
