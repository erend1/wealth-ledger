# ADR-001: Use ledger history as the source of truth

- Status: Accepted
- Decision date: 2026-08-24 context-pack baseline

## Context

WealthLedger must track many asset families over years and answer historical as well as current questions. Values such as current position, remaining quantity, average cost, profit, and allocation change after every economic event and market observation.

Storing those values as independently mutable master data would duplicate the economic facts, create synchronization paths, and make audit and correction unreliable.

WealthLedger is not a classical same-unit accounting general ledger. It is a multi-asset transaction/inventory ledger whose entries carry signed quantities in their own asset units and obey transaction-specific invariants.

## Decision

Posted LedgerTransaction history, TransactionEntry facts, AssetLot acquisition facts, and LotEntryAllocation history are authoritative.

Current state is derived:

- position from effective posted entry deltas;
- lot quantity from signed allocations;
- cost and gain from lots and allocations;
- value from position plus market/reference observations;
- allocation from derived values and policy;
- transaction effect from original entries plus any reversal entries.

Read models and caches may be introduced only as rebuildable projections. They cannot become a second source of truth.

## Consequences

Positive:

- every reported value is traceable to durable events;
- historical and current queries use the same facts;
- new asset families can reuse the core;
- corrections preserve audit evidence;
- projections can be rebuilt after bugs or schema changes.

Costs:

- queries and projections are more deliberate than reading a mutable balance column;
- integration tests must prove derived-query semantics;
- performance work may later require indexed views/queries or rebuildable read models.

## Superseded ideas

The following are rejected as authoritative mutable storage:

- CurrentPosition;
- CurrentBalance;
- RemainingLotQuantity;
- CurrentPortfolioValue;
- AveragePurchasePrice;
- CurrentProfit;
- CurrentAllocationPercentage.

They may appear only as clearly non-authoritative views or rebuildable projections.
