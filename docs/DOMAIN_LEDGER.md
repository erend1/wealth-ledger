# WealthLedger Domain and Ledger

Status: Canonical domain model

Last distilled: 2026-08-24

## Numerical language

### CurrencyCode

A normalized three-letter uppercase ASCII currency code. It is not an unrestricted string in Domain code.

### Money

Money contains:

- signed 64-bit MinorUnits;
- a CurrencyCode.

Money does not embed the number of minor digits for every currency. That belongs to Currency reference data. Arithmetic between different currencies is invalid unless an explicit conversion operation exists outside the value object.

### Quantity and QuantityDelta

Quantities use a signed 64-bit raw E8 scale:

    1 unit = 100,000,000 raw units

Quantity represents a non-negative amount. QuantityDelta represents a signed movement and may be positive, negative, or zero as a value, although transaction entries and allocations forbid zero movements.

### UnitPrice

UnitPrice uses a non-negative 64-bit raw E8 value and a price currency. Authoritative quantity-times-price calculations use checked arithmetic and decimal/intermediate logic in C# with an explicit rounding rule. Do not assume SQLite integer multiplication is safe or correctly rounded.

### BasisPoints and Fineness

Allocation weights use integer basis points:

    10,000 = 100 percent

Gold fineness uses integer parts per million:

    916,000 = 91.6 percent

Fine-gold quantity is derived from gross quantity and fineness.

## Asset model

Asset defines:

- Guid identity;
- stable code and display name;
- AssetType;
- AssetUnit;
- optional base currency;
- LotTrackingMode;
- active/inactive state.

Current AssetType vocabulary:

- Cash
- Currency
- Fund
- Equity
- PhysicalGold
- RealEstate
- Land
- Vehicle
- Other

Current AssetUnit vocabulary:

- CurrencyUnit
- FundUnit
- Share
- GrossGram
- Piece
- Property
- LandParcel
- Vehicle
- Other

LotTrackingMode is None, Optional, or Required. Persist these as stable text codes rather than enum ordinals.

## Master data distinctions

Household is the ownership boundary.

Portfolio describes purpose or goal attribution.

Account describes custody/location.

Institution describes the external custodian/provider.

These concepts are not interchangeable. Every transaction entry identifies both a Portfolio and Account so a query can answer why and where an asset is held.

Current vocabulary:

- InstitutionType: Bank, Broker, AssetManager, Jeweler, Pension, Other.
- PortfolioStatus: Active, Closed, Archived.
- AccountType: Cash, Investment, PhysicalVault, Pension, PropertyRegistry, Other.

## LedgerTransaction aggregate

LedgerTransaction contains:

- Id and HouseholdId;
- TransactionType and TransactionStatus;
- OrderDate, ExecutionDate, and SettlementDate;
- optional ExternalReference and Note;
- optional ReversalOfTransactionId;
- CreatedAtUtc and optional PostedAtUtc;
- ordered TransactionEntry children;
- TransactionCostComponent children;
- optional CashFlowDetail.

Business dates use DateOnly semantics. Audit timestamps use UTC DateTimeOffset semantics.

### Transaction types

Current vocabulary:

- Contribution
- Withdrawal
- Buy
- Sell
- Transfer
- Dividend
- Income
- Expense
- Fee
- Tax
- CorporateAction
- OpeningBalance
- Adjustment
- Reversal

Expense and CorporateAction exist in the vocabulary but their posting semantics were reported as intentionally not implemented in Domain v1. Do not treat an enum member as proof that a complete use case exists.

### Lifecycle

Statuses are:

- Draft
- Ordered
- Posted
- Cancelled

Only Buy and Sell may enter Ordered, and an ordered transaction requires an OrderDate.

Posting requires:

- Draft or Ordered status;
- at least one entry;
- an ExecutionDate;
- valid date ordering;
- PostedAtUtc not earlier than CreatedAtUtc;
- transaction-type-specific semantic validation.

Draft and Ordered transactions may be cancelled. Posted transactions cannot be cancelled, mutated, or deleted.

There is deliberately no Reversed status. Reversal is a transaction type and relationship.

### Date ordering

Where both values exist:

    OrderDate <= ExecutionDate <= SettlementDate

Do not invent times for business dates.

## TransactionEntry

Each entry records:

- Id and zero-based Sequence within the transaction;
- PortfolioId;
- AccountId;
- AssetId;
- non-zero QuantityDelta;
- EntryRole;
- optional UnitPrice.

Entry roles:

- Principal
- Consideration
- Transfer
- Income
- Fee
- Tax
- Adjustment

Entries are created and owned by LedgerTransaction. They are not independent aggregates and do not have their own repository.

UnitPrice is a preserved source fact when the executed price is known. Cash consideration remains a separate asset entry; do not add a second generic Amount field that duplicates it.

## Transaction semantics

### Contribution

External capital enters the investment system as one or more positive entries. A contribution is not a purchase.

CashFlowDetail is currently supported specifically for Contribution and classifies the source using:

- Salary
- Bonus
- AcademicIncome
- Gift
- ExternalSale
- Other

HouseholdMemberId is optional attribution. Withdrawal-purpose classification is not currently forced into this incoming-capital vocabulary.

### Withdrawal

Capital leaves the investment system using negative boundary-flow entries. It is not a Sell: a Sell exchanges one internal asset for another internal asset.

### Buy

Expected core signs:

    Principal > 0
    Consideration < 0

The principal entry represents the acquired asset. The consideration entry represents the asset given up, often cash. Executed unit price may be retained on the principal entry.

### Sell

Expected core signs:

    Principal < 0
    Consideration > 0

Net cash and explanatory cost components must be mutually consistent at the use-case level. Do not double count a fee that is already reflected in the consideration entry.

### Transfer

An internal transfer changes custody and possibly portfolio attribution without creating capital, return, or a new acquisition lineage.

For every transferred asset:

    sum of QuantityDelta across transfer entries = 0

For a lot-tracked asset, the same lot may receive a negative allocation on the source entry and a positive allocation on the destination entry. Global lot quantity is unchanged.

### Dividend and Income

These create positive income entries. Their exact supporting detail models may evolve through explicit use cases.

### Fee and Tax

Standalone Fee and Tax transactions use appropriately negative effective entries. TransactionCostComponent provides breakdown semantics for charges attached to another transaction.

### OpeningBalance

OpeningBalance imports real quantity that predates the ledger.

- entries and opening lot allocations are positive;
- known historical cost may be recorded when supported;
- unknown historical cost uses CostBasis.Unknown with no amount;
- do not fabricate a price from current market value;
- performance since ledger inception may be computed from an inception valuation, but lifetime return remains unknown when original cost is unknown.

### Adjustment

Adjustment supports an explicit, auditable exceptional change with either sign. It is not a shortcut for editing a posted transaction.

### Reversal

A reversal:

- is created only from a posted non-reversal transaction;
- references the original transaction;
- preserves the original effective business dates;
- contains the original effective entries in sequence with negated quantity deltas;
- is posted as a separate immutable transaction;
- leaves the original transaction in Posted state.

An original transaction may be reversed at most once.

A reversal transaction is not directly reversed. Correct the chain through an explicitly designed compensating operation rather than creating ambiguous toggle behavior.

Full reversal of an acquisition is allowed only when lots created by it have no later effective allocations. This dependency check requires stored history and belongs in Application orchestration backed by a query.

## TransactionCostComponent

A component contains:

- CostType;
- CostTreatment;
- non-negative Money amount;
- optional note.

CostType vocabulary:

- Commission
- WithholdingTax
- OtherTax
- MakingCharge
- Brokerage
- TitleDeed
- Expertise
- Notary
- Insurance
- Other

CostTreatment vocabulary:

- AdditionalCashOutflow
- WithheldFromProceeds
- IncludedInConsideration
- InformationalOnly

Treatment prevents double counting. For example, a making charge included in purchase consideration is an analytical breakdown, not a second cash outflow.

Spread is not a CostType. Bid and ask are market/reference observations; spread is derived from them.

## CostBasis

CostBasis is a value object with one of:

- Known plus a non-negative Money amount;
- Unknown plus no amount;
- NotApplicable plus no amount.

Invalid combinations such as Unknown plus an amount are rejected.

Cost basis belongs to acquisition lineage. Realized cost basis for a partial disposal is derived from allocations and the accepted lot-selection/accounting policy. Arithmetic and rounding must be deterministic and tested.

## AssetLot aggregate

AssetLot contains:

- Id;
- AssetId;
- OpeningTransactionEntryId;
- optional AcquiredOn;
- CostBasis;
- optional PhysicalGoldLotDetail;
- CreatedAtUtc;
- signed LotEntryAllocation children.

It deliberately does not contain:

- AccountId;
- PortfolioId;
- OriginalQuantity;
- RemainingQuantity;
- ClosedAt.

An acquisition entry may create more than one lot, for example multiple physical pieces acquired by one transaction. OpeningTransactionEntryId therefore is not unique.

### Creation invariants

- the asset uses Optional or Required lot tracking;
- opening entry asset matches the lot asset;
- opening entry quantity delta is positive;
- opening quantity is positive;
- opening quantity does not exceed the opening entry quantity;
- physical-gold details may be attached only to a PhysicalGold asset;
- creation adds the initial positive allocation.

### LotEntryAllocation

Each allocation contains:

- Id;
- AssetLotId;
- TransactionEntryId;
- non-zero signed QuantityDelta;
- creation timestamp in persistence.

The pair AssetLotId plus TransactionEntryId is unique.

Allocation invariants:

- allocated entry asset matches lot asset;
- allocation sign matches entry sign;
- one lot's allocation to an entry cannot exceed that entry's magnitude;
- an allocation must not make the lot's total quantity negative;
- total allocations across all lots for a Required lot-tracked entry equal the entry quantity exactly.

The last rule spans multiple lots and is enforced by the Application/domain service plus persistence tests.

Current quantity:

    sum of all LotEntryAllocation.QuantityDelta for the lot

IsClosed is derived from current quantity being zero.

### Custody derivation

Lot custody is derived by joining allocations to transaction entries and grouping by Account and Portfolio. A transfer supplies offsetting negative and positive allocations for the same lot, so acquisition date and cost basis remain unchanged.

### FIFO allocation

The reported Domain v1 includes a deterministic FIFO allocation service and plan items.

Expected behavior:

- consider open lots for the same asset;
- order by acquisition date and a deterministic tie-breaker;
- consume the oldest available quantity first;
- split one disposal entry across as many lots as needed;
- fail when available quantity is insufficient;
- never mutate by persisting a RemainingQuantity field.

Verify the exact tie-breaker and rounding behavior in source/tests before extending the service.

## PhysicalGoldLotDetail

Contains:

- Fineness;
- positive PieceCount;
- optional Hallmark;
- optional CertificateReference;
- optional Note.

Gross weight is the lot quantity. Fine-gold weight is derived:

    gross quantity × fineness

Neither value is duplicated in the detail record.

## Derived results

All of the following originate from posted effective entries, allocations, lots, and market/reference data:

- position by household, portfolio, account, and asset;
- remaining lot quantity and open/closed state;
- realized and unrealized cost basis;
- portfolio value;
- profit/loss;
- allocation percentage;
- contribution and withdrawal totals;
- performance since ledger inception.

Cancelled and unposted drafts do not affect effective positions. Posted originals and their posted reversals both participate, naturally netting to zero.
