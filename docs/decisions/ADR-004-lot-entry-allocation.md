# ADR-004: Use LotEntryAllocation instead of disposal-only modeling

- Status: Accepted
- Decision date: 2026-08-24 context-pack baseline

## Context

A disposal-only LotDisposal relation can explain a sale, but it cannot naturally explain:

- an acquisition opening one or several lots;
- transfer of an existing lot between accounts;
- a reversal restoring or removing quantity;
- one disposal consuming several lots;
- one lot participating in many later entries.

Special cases for each movement would fragment the ledger and make quantity reconstruction unreliable.

## Decision

Use LotEntryAllocation as the generic signed many-to-many relation between AssetLot and TransactionEntry.

Examples:

    acquisition allocation   positive
    sale allocation          negative
    transfer source          negative
    transfer destination     positive
    reversal                 opposite of the original allocation effect

The pair AssetLotId plus TransactionEntryId is unique.

Rules:

- lot and entry assets match;
- allocation sign matches entry sign;
- allocation is non-zero;
- a lot's allocation to an entry does not exceed that entry's magnitude;
- total allocations across lots reconcile exactly to a Required lot-tracked entry;
- the sum of allocations for a lot never becomes negative.

Lot current quantity is the sum of its allocations.

## Consequences

Positive:

- one relation models acquisition, disposal, transfer, and reversal;
- FIFO or another accepted lot policy can split one entry across lots;
- transfers preserve cost basis and lineage;
- remaining quantity is derived from immutable movement facts.

Costs:

- exact reconciliation spans multiple aggregates/rows and needs Application validation;
- negative-balance protection needs history-aware validation and integration testing;
- queries must join allocations to entries to derive location.

## Superseded ideas

LotDisposal is superseded and must not be reintroduced.

Stored RemainingQuantity and ClosedAt are also superseded as authoritative lot state. IsClosed is derived when allocation sum equals zero.
