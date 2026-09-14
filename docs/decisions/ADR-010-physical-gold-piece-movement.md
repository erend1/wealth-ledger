# ADR-010: Track physical-gold pieces beside signed lot allocations

- Status: Accepted
- Decision date: 2026-09-14
- Related milestone: M009 Complete Physical-Gold Lifecycle
- Extends: ADR-004, ADR-005, and ADR-009

## Context

PhysicalGold gross weight is the authoritative quantity stored by a
TransactionEntry and LotEntryAllocation. `PhysicalGoldLotDetail.PieceCount`
currently records the positive original number of pieces acquired in one lot.
That original fact is enough for M007 opening inventory, but it cannot explain
what remains after a partial sale or where pieces are held after a custody
transfer.

Mutating the original PieceCount would destroy acquisition evidence. Deriving
pieces from the ratio of current gross weight to original gross weight would
invent equal per-piece weights that may never have been observed. Requiring one
piece per lot would contradict M007's accepted homogeneous evidence groups and
would strand existing multi-piece opening lots.

ADR-004 already represents every lot quantity movement with a signed
LotEntryAllocation. ADR-005 derives custody by joining those allocations to
transaction entries. Physical-gold pieces need an asset-specific signed fact
that follows the same allocation identity without adding piece fields to the
generic relation.

Physical-gold partial sales also need a deterministic Known-cost method.
ADR-009 already apportions original lot cost cumulatively by effective quantity,
but its implemented M008 scope is Fund. M009 needs to state explicitly whether
pieces or gross weight drive physical-gold cost apportionment.

## Decision

Add an asset-specific one-to-one detail for every PhysicalGold lot allocation,
provisionally named `PhysicalGoldLotAllocationDetail`.

It is keyed by LotEntryAllocationId and contains one non-zero signed integer
PieceDelta. It has no independent economic identity.

The following rules apply:

- every allocation of a PhysicalGold lot has exactly one piece detail;
- an allocation for any other asset type has no piece detail;
- PieceDelta and allocation QuantityDelta have the same sign;
- the positive acquisition PieceDelta equals the original
  `PhysicalGoldLotDetail.PieceCount`;
- a sale or transfer records the exact moved gross weight and exact moved whole
  pieces independently; neither is inferred from the other;
- a transfer has equal-and-opposite piece details beside its equal-and-opposite
  quantity allocations on the same acquisition lot;
- a reversal creates the exact opposite piece movement beside the exact
  opposite lot allocation;
- global and custody-scoped piece sums cannot become negative; and
- CurrentPieceCount and RemainingPieceCount are derived sums and are never
  stored as mutable authority.

Existing M007 physical-gold opening allocations are backfilled from the
original lot PieceCount. An exact full reversal is backfilled with its negative.
A migration fails closed when any other existing physical-gold allocation
history cannot prove its piece movement; it never derives pieces from an average
weight.

PhysicalGold realized cost extends ADR-009 rather than replacing it. Explicit
physical selection determines which lot was sold. Within each selected lot,
ADR-009 cumulative apportionment uses gross raw-E8 allocation quantity as Q and
q(i). PieceDelta does not apportion cost. Unknown cost, currency grouping,
rounding, effective-sale ordering, reversal residue, and failure behavior remain
exactly as ADR-009 defines them.

This ADR does not make fine-gold weight authoritative. Fine weight remains a
checked derived result of each effective gross allocation and its immutable lot
fineness.

## Consequences

Positive:

- original piece evidence remains immutable;
- partial sale and custody transfer can explain both grams and whole pieces;
- current pieces can be rebuilt globally and per custody scope;
- transfers preserve acquisition lineage and do not clone lots;
- average per-piece weight is never fabricated; and
- physical-gold realized cost reuses one deterministic, conservation-preserving
  method.

Costs:

- persistence gains one asset-specific child row for every physical-gold lot
  allocation;
- the AssetLot aggregate and reversal persistence must carry quantity and piece
  movements together;
- posting guards must enforce child presence, sign, reconciliation, and
  non-negative global and scoped counts;
- migration needs a fail-closed historical backfill; and
- custody queries must derive two dimensions instead of one.

## Rejected alternatives

### Mutate PhysicalGoldLotDetail.PieceCount

Rejected because it would overwrite acquisition evidence and create a mutable
remaining-balance authority.

### Infer remaining pieces from gross-weight proportion

Rejected because grouped physical pieces need not have equal weight and M007
explicitly forbids inferred per-piece weights.

### Require exactly one piece per lot

Rejected because valid M007 homogeneous groups may contain several pieces and
existing opening data must remain usable.

### Add nullable PieceDelta to generic LotEntryAllocation

Rejected because piece count is an asset-specific physical fact and generic lot
allocation must remain usable without gold-specific nullable columns.

### Allocate cost by piece count

Rejected because supported cost belongs to the lot's aggregate acquisition and
gross weight is its authoritative quantity. Pieces select and describe physical
movement; they are not assumed to have equal cost.

## Verification obligations

- Domain tests cover sign, zero, acquisition, partial movement, transfer, and
  reversal rules.
- Real-SQLite tests prove every PhysicalGold allocation has exactly one detail,
  other assets cannot acquire one, and global/scope piece counts cannot become
  negative under direct SQL or races.
- Migration tests backfill active and exactly reversed M007 multi-piece lots and
  reject unprovable history before mutation.
- Restart and restore tests reconstruct exact gross weight, pieces, fine weight,
  custody, and correction history.
- Realized-cost tests apply ADR-009 to repeated, closing, reversed, Known,
  Unknown, multi-currency, midpoint, residue, and overflow gold sales.
- Fund allocation, FIFO, realized-cost, and verification behavior remains
  unchanged.
