# WealthLedger Architecture Decision Records

These ADRs preserve the accepted rationale behind the ledger core.

| ADR | Status | Decision |
|---|---|---|
| ADR-001 | Accepted | Ledger history is the source of truth |
| ADR-002 | Accepted | Posted transactions are immutable and corrected by reversal |
| ADR-003 | Accepted | Financial values use integer/fixed-point storage |
| ADR-004 | Accepted | LotEntryAllocation replaces disposal-only modeling |
| ADR-005 | Accepted | AssetLot represents acquisition lineage, not custody |
| ADR-006 | Accepted | Command idempotency is separate from external financial references |
| ADR-007 | Accepted | Local data operations are explicit, exclusive, verified, and fail closed |
| ADR-008 | Accepted | The local UI is server-rendered Razor Pages in the existing loopback host |

If a future decision changes one of these, add a new ADR and mark the earlier one Superseded by the new ADR. Do not rewrite accepted history.
