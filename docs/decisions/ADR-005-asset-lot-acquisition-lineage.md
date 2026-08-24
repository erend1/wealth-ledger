# ADR-005: Model AssetLot as acquisition lineage, not custody

- Status: Accepted
- Decision date: 2026-08-24 context-pack baseline

## Context

An acquired asset can move between accounts or portfolio attributions without becoming a new economic acquisition. If AssetLot owns AccountId or PortfolioId, a transfer would require mutating the lot, cloning it, or breaking its cost-basis history.

An acquisition entry may also create several lots, such as multiple physical pieces bought in one transaction.

## Decision

AssetLot represents acquisition lineage and original cost-basis knowledge.

It contains:

- AssetId;
- OpeningTransactionEntryId;
- optional acquisition date;
- CostBasis;
- optional asset-specific lot detail;
- creation timestamp;
- signed allocation history.

It does not contain AccountId or PortfolioId.

Custody and purpose are derived by joining LotEntryAllocation to TransactionEntry and grouping the effective signed quantities by Account and Portfolio.

OpeningTransactionEntryId is not unique because one acquisition entry may create multiple lots.

OriginalQuantity, RemainingQuantity, and ClosedAt are not stored; they are derived from allocations.

## Consequences

Positive:

- transfers do not break or rewrite acquisition lineage;
- cost basis remains attached to the economic acquisition;
- the same lot can be distributed across locations;
- physical pieces can remain separate lots under one purchase entry.

Costs:

- location queries require allocation/entry joins;
- a lot may have positive quantity across more than one account;
- UI terminology must distinguish acquisition lot from custody location.

## Superseded ideas

- AssetLot.AccountId.
- AssetLot.PortfolioId.
- Treating a transfer-in as a new acquisition lot.
- Mutating a lot's location.
- Requiring OpeningTransactionEntryId to be unique.
