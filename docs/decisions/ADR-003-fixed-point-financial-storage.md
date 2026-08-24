# ADR-003: Use integer and fixed-point financial storage

- Status: Accepted
- Decision date: 2026-08-24 context-pack baseline

## Context

WealthLedger must reproduce financial quantities and values exactly across C#, SQLite, imports, API calls, and tests. Binary floating-point representations introduce rounding drift and are unsuitable for authoritative money, quantity, price, weight, or percentage records.

SQLite uses signed 64-bit INTEGER and does not supply a native fixed-scale decimal type with the semantics required by the Domain.

## Decision

Use explicit integer representations:

- Money: signed 64-bit minor units plus CurrencyCode.
- Quantity and QuantityDelta: signed 64-bit raw E8, where one unit is 100,000,000.
- UnitPrice: non-negative signed 64-bit raw E8 plus price CurrencyCode.
- Allocation weights: integer basis points, where 10,000 is 100 percent.
- Fineness: integer parts per million.

Use checked C# arithmetic. Use decimal or suitably wide intermediate logic for scale-changing calculations and apply an explicit, tested rounding policy at the boundary.

Store currency metadata such as MinorUnitDigits in reference data; Money itself remains exact minor units.

Persist enum-like financial metadata through explicit stable text mappings, not numeric enum ordinals.

## Consequences

Positive:

- exact storage and deterministic round trips;
- no binary floating-point drift;
- clear units and scales;
- cross-platform/API/database representations are auditable.

Costs:

- conversions and rounding policies must be explicit;
- multiplication can overflow even when inputs are valid and must be checked;
- display formatting requires currency/reference metadata;
- schema and code must never lose the scale meaning.

## Superseded ideas

- double or float for authoritative financial values.
- unscaled decimal storage with provider-dependent behavior.
- performing unchecked price-times-quantity arithmetic in SQLite.
- encoding enum semantics as CLR ordinal integers.
