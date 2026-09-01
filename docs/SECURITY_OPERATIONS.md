# WealthLedger Security and Operations Requirements

Status: Accepted operational requirements

Last reviewed: 2026-09-01

## Purpose

WealthLedger is intended to hold private, long-lived household financial
records. Correct Domain logic is insufficient if the live database can be
committed accidentally, exposed over a network, corrupted during backup, or
lost with one workstation.

This document defines required outcomes. M004 implements the bounded local-data
baseline identified below; later requirements remain explicit rather than being
implied by that milestone. Verified repository reality remains in
`PROJECT_STATE.md`, and the operator procedure is in `OPERATIONS.md`.

## Implemented M004 baseline

As verified for M004 on 2026-09-01:

- the authoritative SQLite path resolves absolutely below the per-user local
  application-data directory unless an equally validated absolute override is
  supplied;
- live databases, SQLite companions, operation locks, restore stages, and
  `.wlbackup` packages are ignored by source control;
- the API binds only to accepted loopback addresses, owns the database for its
  lifetime, validates compatibility, and never migrates at startup;
- the explicit operations CLI initializes, reports status, creates and verifies
  SQLite-consistent backups, stages restores, replaces the active database with
  rollback protection, and migrates only after a verified pre-migration backup;
- path ownership, package integrity, schema compatibility, representative
  ledger reads, and privacy-safe failures are enforced with fail-closed tests;
- application backup packages are explicitly plaintext. Operators must confirm
  external destination separation and encryption rather than treating a digest
  as confidentiality or authenticity.

## Remaining operational boundaries

M004 does not implement application-managed encryption, authentication,
authorization, remote access, a remote/off-site provider, automatic scheduling
or retention deletion, governed exports, or a UI. Full-disk/destination
encryption, recovery-key custody, physical separation, cadence, and restore
drills remain operator responsibilities. Real household data must not rely on
the repository, a single workstation, or one untested package as its only
protection.

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

M004 satisfies this requirement for normal API and operations composition.
Tests use explicit, unique synthetic roots and never relax normal path policy.

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

M004 also ignores rollback journals, `.sqlite3` companions, `.wlbackup`,
`.wlrestore`, and `.wloperation.lock` artifacts and verifies the patterns with
repository-protection checks.

### OPS-003: Local exposure policy

The initial API/UI host must bind only to the intended local interface unless a
different deployment is explicitly accepted. Development launch settings must
not become the security boundary.

Remote access, reverse proxies, shared-machine use, and multi-user access
require an accepted authentication, authorization, and transport-security
design.

M004 implements loopback-only validation for tracked settings, environment
overrides, and process startup. It deliberately does not authorize remote use.

### OPS-004: Setup and migration control

One-time setup remains disabled during normal operation. Database migration is
an explicit, observable operation with a pre-migration backup and a documented
failure path. A normal application restart must not silently recreate master
data or discard a failed database.

M004 implements explicit initialization and migration commands. Normal API
startup acquires ownership and validates readiness but neither creates nor
migrates the database.

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

M004 uses SQLite's online backup API, verifies a standalone snapshot before and
after packaging, and publishes a new immutable generation atomically. The
versioned manifest records operational metadata only.

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

M004 implements both isolated staging and confirmed active replacement. Active
replacement first creates a verified pre-restore generation, stages on the same
filesystem, preserves the superseded database, and rolls back a failed
promotion check.

### OPS-007: Multiple recoverable copies

The operating guide should recommend at least three recoverable copies across
two storage media with one copy separated from the primary workstation. The
exact destination may be encrypted removable storage, an encrypted backup
service, or another accepted medium.

Synchronization is not automatically a backup. Deletion or corruption that
replicates immediately must not erase every recoverable version.

`OPERATIONS.md` carries this recommendation and requires operators to confirm
destination separation. M004 does not provision, upload, schedule, or prune
copies.

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

The accepted M004 decision is `PLAINTEXT` at the application-package layer,
with explicit operator confirmation of established external encryption and
recovery-key custody. Application-managed database or package encryption is
deferred; a SHA-256 digest provides corruption detection, not authenticity or
confidentiality.

### OPS-009: Secrets and configuration

Connection strings containing secrets, provider credentials, encryption keys,
and backup credentials must use supported configuration or secret storage and
must not be committed. Production-like values do not belong in screenshots,
tests, or canonical docs.

M004 adds no secrets. Its paths and protection acknowledgements use ordinary
configuration overrides and all test values are synthetic.

### OPS-010: Privacy-safe diagnostics

Routine logs may include correlation identity, endpoint, duration, outcome, and
stable error category. They must avoid household names, account identifiers,
notes, certificate references, external references, exact balances, request
bodies, and SQL parameter values unless an explicit diagnostic export is
requested and protected.

Problem Details returned by the API must not expose SQLite, EF Core, filesystem,
or stack-trace internals.

M004 operations and API-startup failures map implementation details to bounded,
stable categories. Process tests inspect standard output and error for private
values, SQL, connection strings, paths supplied as private markers, and stack
traces.

### OPS-011: Governed exports

An export states its schema/version, effective time, included regions, and
whether private fields are redacted. Exporting does not mutate the ledger.
Machine-readable exports use stable identifiers and exact decimal strings or
integer representations without binary floating-point loss.

Governed exports remain outside M004.

### OPS-012: Direct-write prohibition

Routine tools, UI, importers, scripts, and agents write only through Application
use cases. Direct SQL is restricted to migrations, verified recovery, or
explicit diagnostics and must not silently create posted facts.

M004's seven-command surface accepts no SQL and changes no posted ledger fact.
Application owns the operation-oriented use cases while Infrastructure confines
the necessary SQLite, EF migration, archive, lock, and filesystem mechanics.

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
