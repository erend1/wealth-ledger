# WealthLedger Security and Operations Requirements

Status: Proposed operational baseline

Last reviewed: 2026-08-24

## Purpose

WealthLedger is intended to hold private, long-lived household financial
records. Correct Domain logic is insufficient if the live database can be
committed accidentally, exposed over a network, corrupted during backup, or
lost with one workstation.

This document defines required outcomes. It does not claim that they are all
implemented. Verified operational reality remains in `PROJECT_STATE.md`.

## Current verified gaps

As of 2026-08-24:

- the API's default SQLite connection uses a relative `wealthledger.db` path;
- the repository ignore rules do not explicitly cover SQLite database, WAL, or
  shared-memory files;
- no supported backup or restore workflow exists;
- encryption-at-rest and deployment are open decisions;
- authentication and authorization are open decisions;
- migration and setup are disabled by default, which is a useful safety
  baseline but not a complete operating model.

Synthetic development and tests may continue. Real household data must not rely
on the repository as its only operational protection.

## Initial threat model

The first operating model must consider:

- accidental Git commit of live data or backups;
- loss, theft, or compromise of the workstation;
- a damaged database or failed migration;
- an incomplete copy while SQLite WAL activity is present;
- accidental duplicate submission;
- unintended LAN or internet exposure;
- setup or administrative endpoints remaining enabled;
- sensitive data in logs, screenshots, crash reports, exports, or agent prompts;
- a malicious or mistaken direct write that bypasses Application invariants;
- loss of the only decryption key or backup credential.

The initial model does not assume hostile cloud multi-tenancy. Such a deployment
requires a separate security design and milestone.

## Operational requirements

### OPS-001: Explicit live-data directory

Normal operation must resolve the live database to an explicit per-user data
directory outside the source repository and build output. Startup must display
or diagnostically expose the resolved path without logging private contents.

Relative repository-root storage may remain available only for tests or an
explicit development profile.

### OPS-002: Source-control protection

Repository ignore rules must cover at least:

```text
*.db
*.db-wal
*.db-shm
*.sqlite
*.sqlite-wal
*.sqlite-shm
```

Backup and export extensions chosen later must also be ignored. A verification
test or documented check should prove that a representative live-data file is
not tracked.

Ignore rules reduce mistakes; they do not replace placing live data outside the
repository.

### OPS-003: Local exposure policy

The initial API/UI host must bind only to the intended local interface unless a
different deployment is explicitly accepted. Development launch settings must
not become the security boundary.

Remote access, reverse proxies, shared-machine use, and multi-user access
require an accepted authentication, authorization, and transport-security
design.

### OPS-004: Setup and migration control

One-time setup remains disabled during normal operation. Database migration is
an explicit, observable operation with a pre-migration backup and a documented
failure path. A normal application restart must not silently recreate master
data or discard a failed database.

### OPS-005: Consistent backup

A backup operation must use a SQLite-safe mechanism such as the supported
backup API or an equivalently tested process. It must not assume that copying
only the main file is consistent while WAL activity is possible.

Each backup records or exposes:

- creation timestamp;
- source database identity or schema version;
- application version when available;
- integrity-check result;
- destination;
- encryption state;
- verification status.

### OPS-006: Restore verification

Restore always targets an explicit path and never overwrites the only working
copy without a recoverable pre-restore backup. The workflow must:

1. validate the backup package;
2. restore to an isolated target;
3. open it with foreign keys enabled;
4. run SQLite integrity and application compatibility checks;
5. prove representative ledger queries;
6. require explicit confirmation before replacing the active database.

A backup is not trusted until this workflow has succeeded at least once.

### OPS-007: Multiple recoverable copies

The operating guide should recommend at least three recoverable copies across
two storage media with one copy separated from the primary workstation. The
exact destination may be encrypted removable storage, an encrypted backup
service, or another accepted medium.

Synchronization is not automatically a backup. Deletion or corruption that
replicates immediately must not erase every recoverable version.

### OPS-008: Encryption decision

Before real data is broadly used, the household must make an explicit decision
about:

- full-disk encryption reliance;
- database or backup encryption;
- key and recovery-material custody;
- consequences of forgotten credentials;
- whether any external backup provider can read plaintext.

Do not introduce custom cryptography. Use established platform or library
mechanisms only after a concrete design is accepted and tested.

### OPS-009: Secrets and configuration

Connection strings containing secrets, provider credentials, encryption keys,
and backup credentials must use supported configuration or secret storage and
must not be committed. Production-like values do not belong in screenshots,
tests, or canonical docs.

### OPS-010: Privacy-safe diagnostics

Routine logs may include correlation identity, endpoint, duration, outcome, and
stable error category. They must avoid household names, account identifiers,
notes, certificate references, external references, exact balances, request
bodies, and SQL parameter values unless an explicit diagnostic export is
requested and protected.

Problem Details returned by the API must not expose SQLite, EF Core, filesystem,
or stack-trace internals.

### OPS-011: Governed exports

An export states its schema/version, effective time, included regions, and
whether private fields are redacted. Exporting does not mutate the ledger.
Machine-readable exports use stable identifiers and exact decimal strings or
integer representations without binary floating-point loss.

### OPS-012: Direct-write prohibition

Routine tools, UI, importers, scripts, and agents write only through Application
use cases. Direct SQL is restricted to migrations, verified recovery, or
explicit diagnostics and must not silently create posted facts.

## Backup cadence recommendation

Until usage evidence supports a different policy:

- create a backup after meaningful data-entry sessions;
- create a pre-migration backup before every schema upgrade;
- retain multiple dated generations rather than one overwritten file;
- perform a scheduled restore drill at least quarterly and after changing the
  backup format or encryption method;
- record the last successful backup and restore verification in the UI.

Cadence is an operational recommendation, not an authoritative ledger fact.

## Incident response

If corruption, duplicate posting, or suspected exposure occurs:

1. Stop writes and preserve the active database, WAL, SHM, logs, and relevant
   backups without editing them.
2. Work on copies in an isolated location.
3. Identify the last verified backup and the last known valid transaction.
4. Use deterministic queries and integrity checks to measure impact.
5. Correct ledger facts through supported reversal/correction workflows when
   the database remains structurally valid.
6. Restore only after validation and preserve the superseded copy for audit.
7. Rotate exposed credentials or keys and document the incident separately from
   transaction notes.

## Real-data operations checklist

Before entering real household balances:

- [ ] Live data resolves outside the repository.
- [ ] SQLite and backup files are ignored by Git.
- [ ] Local binding and remote-access behavior are understood.
- [ ] Setup is disabled after initialization.
- [ ] A dated backup succeeds through the supported workflow.
- [ ] A restore drill succeeds to an isolated target.
- [ ] Encryption and recovery-key custody are explicitly decided.
- [ ] Logs and error responses are inspected for sensitive fields.
- [ ] Duplicate-request behavior and correction behavior are verified.
- [ ] The household understands which copy is authoritative and which copies
      are backups or exports.

## Required verification for the operations milestone

The implementation milestone must include focused tests or repeatable checks
for path resolution, source-control ignore behavior, local exposure, consistent
backup under SQLite use, integrity validation, restore into isolation, schema
compatibility, migration backup behavior, and privacy-safe failure reporting.

