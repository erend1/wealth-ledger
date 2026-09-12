# ADR-009: Derive Realized Lot Cost with Deterministic Cumulative Apportionment

- Status: Proposed
- Decision date: Pending M008 human acceptance
- Related milestone: M008 Complete Investment-Fund Lifecycle

## Context

AssetLot stores acquisition lineage and one Known, Unknown, or NotApplicable
CostBasis. A Known basis is a non-negative Money total for the lot rather than a
mutable per-unit average. LotEntryAllocation records exact signed quantity
effects and remaining quantity is derived from those allocations.

M008 introduces partial Fund sales. One sale may consume several lots, and one
Known-cost lot may be consumed by several sales. Money is stored only in integer
minor units, so a proportional share such as one third of one minor unit cannot
be represented directly. Independently rounding every sale can lose or invent
minor units and can fail to assign the original total cost when the lot closes.

Opening balances may also contain Unknown-cost lots or Known costs in different
currencies. Treating Unknown as zero, adding currencies, or storing an entered
realized-cost number would break the ledger's evidence and derivation rules.

Corrections add another constraint. A Posted sale is never edited or deleted;
it may receive a separate Posted reversal. Current effective realized cost must
follow effective history without making the reversal or a cached cost row a new
source of truth.

This decision is cross-cutting because the same lot-cost arithmetic may later
apply to physical gold or another lot-tracked asset. M008 implements it only for
Fund sales.

## Decision

Realized lot cost is a deterministic, rebuildable query result derived from:

- the lot's positive opening allocation;
- the lot's CostBasis;
- effective Posted sale allocations; and
- explicit reversal relationships.

It is not accepted from user input and is not stored as an authoritative amount,
per-unit average, remaining cost, or disposal row.

### Original quantity and cost

For one lot:

- `Q` is the positive raw-E8 allocation whose TransactionEntryId equals the
  lot's OpeningTransactionEntryId;
- `C` is the Known CostBasis amount in integer minor units; and
- `q(i)` is the positive magnitude of effective sale allocation `i` in raw E8.

Q must be greater than zero. C may be zero. A Fund lot whose CostBasis is
NotApplicable is an unsupported persisted shape and fails closed.

### Effective disposal sequence

Only allocations belonging to a Posted Fund Sell with no Posted reversal enter
the current effective-disposal sequence. A reversed sale and the reversal
remain audit history but are excluded from current effective realized-cost
apportionment.

For each lot, effective sale allocations are ordered by:

1. LedgerTransaction.PostedAtUtc ascending;
2. LedgerTransaction.Id ascending;
3. TransactionEntry.Sequence ascending; and
4. LotEntryAllocation.Id ascending.

Posting order is used so a later-entered backdated transaction does not silently
claim it was processed before an already Posted sale. This order allocates
rounding residue; it does not select the lot. FIFO lot selection remains based
on acquisition lineage and the accepted M008 custody-aware policy.

### Cumulative apportionment

Let:

    D(i) = q(1) + ... + q(i)
    D(0) = 0

For a Known-cost lot, define:

    R(i) = round_half_to_even(C * D(i) / Q)
    r(i) = R(i) - R(i - 1)

`r(i)` is the Known minor-unit cost assigned to allocation `i` in the current
effective sequence.

The multiplication and division use arbitrary-width integer arithmetic, or an
equivalent implementation proven not to overflow. Rounding operates on the
integer quotient and remainder and is tested explicitly; binary floating point
and ad hoc SQLite multiplication are forbidden.

The method guarantees:

- non-negative assigned cost for a valid monotonically increasing sequence;
- deterministic results for the same effective history;
- cumulative assigned cost equal to the correctly rounded proportional cost;
- exactly C minor units when effective disposed quantity reaches Q; and
- no independently rounded per-sale drift.

An effective disposed quantity greater than Q is invalid persisted history and
fails closed rather than being clamped.

### Reversal and correction consequence

When a sale is reversed, it leaves the effective sequence. The cumulative
apportionment is rebuilt for the remaining effective sales. Because Money uses
integer minor units, removing an earlier sale may redistribute a rounding
residue among later effective sales. The change is bounded by the allocation of
minor-unit residues and does not change the aggregate correctly rounded cost of
the currently effective disposed quantity.

Therefore every realized-cost result states:

- that it is derived;
- the method code/version;
- the effective query timestamp or as-of context; and
- whether the source sale is currently effective or reversed.

The application does not describe an old per-transaction derived amount as an
immutable source fact. Transaction entries, allocations, CostBasis, and
reversal links remain the immutable facts.

### Unknown and mixed cost

For one sale, the derived result reports:

- exact disposed quantity allocated from Known-cost lots;
- exact disposed quantity allocated from Unknown-cost lots;
- Known realized-cost amounts grouped by their original CurrencyCode; and
- a completeness state:
  - CompleteKnown when all disposed quantity has Known cost;
  - PartiallyKnown when both Known and Unknown quantities occur; or
  - Unknown when no disposed quantity has Known cost.

Known zero remains Known. Unknown is never converted to zero. Different
currencies are never added or converted without a separately accepted market/
reference observation policy.

If the result is PartiallyKnown, the UI and API must not label a Known subtotal
as total realized cost. If it is Unknown, no synthetic Money total is returned.

### Scope and advice boundary

Realized cost follows the actual LotEntryAllocation chosen by M008 FIFO. It does
not independently choose lots and does not mutate custody.

This is a household economic-accounting method. It is not represented as a
jurisdiction's legal tax-lot method, a tax-return calculation, financial advice,
or a religious ruling. Alternative lot-selection or tax-accounting policies
require a later explicit milestone and, when cross-cutting, a superseding ADR.

## Consequences

Positive:

- full lot disposal conserves the original Known minor-unit cost exactly;
- partial sales are reproducible without stored RemainingCost or AverageCost;
- corrections follow current effective immutable history;
- Unknown and multi-currency evidence remain honest;
- future read models can rebuild the result from the ledger; and
- the method can be reused for later lot-tracked assets after explicit scope
  acceptance.

Costs:

- realized-cost queries need ordered effective allocation history for each
  consumed lot;
- arbitrary-width or carefully proven overflow-safe arithmetic is required;
- a reversal can redistribute a minor-unit residue among remaining effective
  sales;
- API/UI contracts must expose completeness, currency buckets, method, and
  effective context rather than one convenient scalar; and
- performance work must not assume every realized-cost result is complete.

## Rejected alternatives

### Store realized cost entered by the user

Rejected because realized cost must follow acquisition lineage and allocation,
not an unverified sale field.

### Store mutable remaining cost or average cost on AssetLot

Rejected because it creates a second authority that can diverge from immutable
allocations and violates ADR-001, ADR-004, and ADR-005.

### Round each allocation independently

Rejected because repeated rounding may fail to conserve C when the lot closes.

### Treat Unknown cost as zero

Rejected because absence of evidence is not a zero-cost acquisition.

### Convert all cost to the sale currency automatically

Rejected because no accepted FX observation, date, source, or conversion policy
exists before M011.

### Persist an authoritative realized-cost table

Rejected because the result is derivable. A future rebuildable performance read
model is possible only under a separately accepted contract that cannot be
mistaken for ledger authority.

### Claim the method is legally required FIFO tax accounting

Rejected because the product does not provide jurisdiction-specific tax advice
or certification.

## Compatibility and verification

Existing LedgerTransaction, AssetLot, CostBasis, and LotEntryAllocation rows do
not change. No historical migration is rewritten.

M008 tests must cover:

- exact full disposal;
- multiple partial disposals;
- one-minor-unit and tie-to-even boundaries;
- Known zero;
- very large values without overflow;
- mixed Known and Unknown lots;
- multiple Known cost currencies;
- reversed-sale exclusion and residue redistribution;
- corrupt over-disposal and NotApplicable Fund cost failing closed; and
- deterministic results after restart and independent query reconstruction.

If human review chooses a different rounding, reversal, or completeness policy,
this ADR remains Proposed and M008 must be amended before implementation.
