# WealthLedger Project Context

Status: Canonical context

Last distilled: 2026-08-24

## What WealthLedger is

WealthLedger is a private, long-lived household investment record system. Its core is a normalized multi-asset transaction and inventory ledger that can continue working as the asset universe expands from cash, currencies, funds, equities, and physical gold to property, land, vehicles, pensions, and other assets.

The product must answer, from durable facts:

- What assets are held now, where, and for which portfolio purpose?
- When and at what quantity and cost were they acquired?
- Did money enter from outside the investment system, leave it, or merely move/trade inside it?
- What acquisition lots remain and what cost basis is realized by a disposal?
- What is the current value when market/reference data is applied?
- How does the current allocation compare with a goal or policy?
- What changed over time, and can every result be traced back to recorded events?

The ledger is designed to support future deterministic analysis and agent-assisted decision support. The agent is a consumer of governed data and use cases, never the ledger's authority.

## Product principles

1. Preserve historical truth. A posted fact is not edited away.
2. Derive changing state. Positions, values, gains, and allocation are calculations.
3. Model economic meaning, not screens. Account, portfolio purpose, asset, transaction, and lot are separate concepts.
4. Admit uncertainty. Unknown acquisition cost is stored as Unknown, not zero and not a guessed price.
5. Keep the generic ledger asset-neutral. Specialized facts live in detail models.
6. Make arithmetic exact and repeatable. Financial storage is integer/fixed-point.
7. Keep the core usable without Infrastructure, API, UI, market-data, or AI components.

## Bounded concepts

Household is the ownership boundary and supplies a base currency.

HouseholdMember is optional attribution for facts such as the source of a contribution. It must not become a store for unnecessary personal data.

Portfolio is purposeful ownership or a goal-oriented bucket: why an asset is held.

Account is custody/location: where an asset is held.

Institution identifies the bank, broker, asset manager, jeweler, pension provider, or other custodian associated with an account.

Asset defines the instrument or property and its base unit, optional base currency, and lot-tracking policy.

LedgerTransaction groups one economic event.

TransactionEntry records a signed quantity change for one asset in one account and portfolio.

AssetLot records acquisition lineage and original cost-basis knowledge.

LotEntryAllocation links a lot to entries using signed quantities. It explains acquisition, disposal, transfer, and reversal without relocating the lot object itself.

Market/reference data, goals, policies, reconciliation, and analysis history are separate regions. They are not part of the first persistence milestone.

## Multi-asset ledger, not classical general ledger

WealthLedger is not a classical double-entry accounting general ledger in which all debits and credits share a single unit. Adding 100 fund units to a cash amount is meaningless.

Instead, it is a multi-asset transaction/inventory ledger with transaction-specific invariants:

- a Buy acquires positive principal and gives negative consideration;
- a Sell disposes negative principal and receives positive consideration;
- an internal Transfer nets to zero per transferred asset;
- a Contribution crosses the system boundary into the investment ledger;
- a Withdrawal crosses the boundary out;
- a Reversal supplies the exact inverse effective entries.

## Supported and future asset families

The v1 vocabulary anticipates:

- cash and currencies;
- investment funds;
- equities;
- physical gold;
- real estate;
- land;
- vehicles;
- pensions and other assets where later modeling requires them.

Lot tracking may be None, Optional, or Required per asset. Physical gold adds lot-specific fineness, piece count, hallmark, certificate reference, and note while its weight remains part of generic quantity history.

## Technical baseline

- Repository and solution name: WealthLedger.
- Language: English throughout technical artifacts.
- Runtime: .NET 10.
- Architecture: Domain to Application to Infrastructure/API/UI with inward dependencies.
- API: ASP.NET Core Minimal API.
- Persistence: EF Core and SQLite.
- Test stack: xUnit plus the assertion library already present in the repository.
- Initial persistence milestone: 001_CoreLedger.
- First intended vertical slice: contribution, purchase of a synthetic fund asset, lot creation, SQLite persistence, and current-position query.

UI technology remains intentionally undecided.

## Data ethics and privacy

The application is meant for private financial records, but source control and agent context should use anonymous synthetic examples. Avoid personal names, exact balances, income, addresses, account identifiers, certificates, and holdings in docs, fixtures, logs, or prompts.

Use configuration/secrets facilities for connection or integration secrets. A local SQLite file containing real records must not be committed.

## Current versus superseded decisions

| Topic | Current decision | Superseded idea |
|---|---|---|
| Product model | Multi-asset investment/inventory ledger | Portfolio dashboard with stored totals |
| Authoritative state | Immutable posted history and derived positions | Mutable current-balance records |
| Correction | Separate posted reversal plus corrected transaction | Update/delete original; Reversed status |
| Financial storage | Minor units, E8, ppm, and basis points | double/float or loosely scaled decimal storage |
| Lot movement | Signed LotEntryAllocation | Disposal-only LotDisposal |
| Lot meaning | Acquisition lineage | Lot owns AccountId/PortfolioId or represents custody |
| Lot quantities | Sum allocations | Stored OriginalQuantity/RemainingQuantity/ClosedAt |
| Unknown historical cost | Explicit Unknown with null amount | Zero or fabricated acquisition value |
| Transaction charges | Typed cost component plus treatment | Undifferentiated fee; spread modeled as fee |
| Framework style | Direct use cases and explicit mappings | Mandatory MediatR, AutoMapper, generic repositories/services |
| AI role | Governed read/analysis and proposed actions | Direct database writer or source of financial math |

The accepted ADRs in docs/DECISIONS are the durable rationale for the five most important changes.
