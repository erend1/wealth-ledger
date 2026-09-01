# ADR-007: Use a fail-closed local data operating model

- Status: Accepted
- Decision date: 2026-09-01

## Context

WealthLedger stores private, long-lived financial history in SQLite. The
verified M003 checkpoint still resolves a relative database path, permits an
opt-in migration during API startup, relies on development launch settings for
binding, and has no supported backup or restore workflow. Those gaps make it
too easy to operate on the wrong file, expose the host beyond loopback, copy an
inconsistent SQLite main file, or replace the only working database without a
recoverable predecessor.

M004 defines ten bounded operational decisions. The human owner accepted all
ten Recommended gates exactly as written in
`docs/milestones/M004_safe_local_data_operations.md` on 2026-09-01.

## Decision

Use the accepted M004 local data operating model:

- normal operation resolves one absolute database path below the current OS
  user's local application-data directory, with only an absolute safety-checked
  override and explicit isolated test paths;
- a focused `WealthLedger.Operations` console exposes status, explicit
  initialization and migration, backup creation and verification, isolated
  restore staging, and confirmed active replacement;
- the API binds only to loopback and one process owns the authoritative database;
- lifecycle operations and backup creation require exclusive cross-process
  ownership;
- `.wlbackup` packages contain exactly one SQLite online-backup snapshot and one
  versioned privacy-safe manifest and are published only after independent
  integrity, compatibility, migration-history, foreign-key, digest, and
  representative-query verification;
- backup generations are immutable, use an explicit destination distinct from
  live data, and are not deleted automatically;
- restore verifies and stages away from the active path, while active
  replacement additionally requires literal confirmation, a verified
  pre-restore backup, a same-filesystem swap, preservation of the superseded
  database, promotion verification, and rollback on failure;
- API startup never migrates. Explicit migration requires exclusive ownership
  and a verified pre-migration backup before the first schema change;
- M004 adds no SQLCipher, custom cryptography, or application-managed key. The
  active database relies on OS access controls and verified device encryption,
  and `.wlbackup` is plaintext at the application-package layer;
- operational results may expose resolved paths, versions, migration state,
  UTC times, integrity state, and a digest prefix, but never connection strings,
  SQL, stack traces, ledger values, notes, or private financial identifiers.

Infrastructure owns SQLite, archive, filesystem, locking, and migration
mechanics. Application exposes narrow operation-oriented ports and use cases.
Domain remains independent of every operational concern. No operation writes
ledger facts or accepts caller-supplied SQL.

The milestone document is authoritative for the exact accepted command,
failure, validation, recovery, and test contract. A deviation requires a new
explicit human decision rather than reinterpretation in implementation.

## Consequences

Positive:

- the authoritative file is independent of the process working directory and
  protected from repository/build paths;
- backup and recovery confidence comes from verified SQLite snapshots rather
  than main-file copying or hashes alone;
- migration and replacement cannot race the normal writer and cannot begin
  without recoverable protection;
- startup and operational diagnostics fail closed without disclosing ledger
  contents;
- later UI work can wrap stable Application operations instead of handling
  SQLite or files directly.

Costs:

- normal startup requires an already initialized, compatible database and
  explicit lifecycle commands for initialization or migration;
- backup creation initially requires the API to be stopped;
- operators remain responsible for off-device generations, externally
  encrypted destinations, recovery material, restore drills, and retention;
- unsigned plaintext packages detect accidental corruption but do not claim
  authenticity against a hostile editor.

## Rejected alternatives

- Relative live paths or repository-local live data.
- Automatic API startup migration or implicit database recreation.
- Copying only the SQLite main file.
- Live replacement while another process owns the database.
- HTTP administration endpoints or a general SQL console.
- Automatic backup deletion, cloud upload, or custom encryption in M004.
- Treating host-header filtering, file synchronization, Git, or one same-disk
  copy as sufficient protection.
