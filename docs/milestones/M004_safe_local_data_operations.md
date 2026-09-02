# M004: Safe Local Data Operations

Status: Verified

Owner: Human and agent

Last reviewed: 2026-09-02

Accepted: 2026-09-01

Verified: 2026-09-02

## User outcome

The household can identify exactly which local SQLite database is authoritative,
keep it outside the repository, create a consistent dated backup, prove that a
backup is restorable, and perform an explicit database migration or restore
without silently destroying the only working copy.

Normal application startup is local-only and fail-closed. It neither creates or
migrates a database implicitly nor exposes the API to another machine. Routine
operational output explains what happened without disclosing ledger contents.

This milestone makes synthetic development and the later UI safer. It does not
by itself declare the application ready to be the household's sole record of
real assets; the real-data readiness gate in `ROADMAP.md` still applies.

## Pre-implementation baseline

The dedicated implementation branch was created from `origin/main` at
`965b6beb1237bb1907621b772c7a2d5358aec0d9`, the verified post-M003 checkpoint.
Before code changes, restore succeeded, all 243 tests passed, and EF reported no
model drift.

The Windows .NET 10.0.400 formatting check reproduced only the recorded
LF/CRLF diagnostic in the untouched `LedgerTransaction.cs` at lines 469, 471,
and 472. No repository-wide line-ending policy was changed for M004.

Inspection at that base confirmed the relative working-directory database,
unprotected artifact extensions, optional unprotected startup migration,
absence of supported backup/restore/status commands, and lack of a tracked
loopback hosting boundary described by the proposal. Those gaps define the
before-state; the verified implementation evidence below supersedes them as
current behavior.

## Why now

M003 corrects immutable ledger history and may add a database migration. M004 is
the next operational safety boundary: real history should not be entered before
the live file, migration, backup, and recovery paths are explicit and tested.
M006 first-run UI work also depends on these operations having stable
Application contracts rather than inventing file handling in the UI.

Planning M004 may proceed while M003 is being implemented because this document
changes no runtime or schema behavior. M004 may be reviewed, revised, and even
Accepted while M003 is active. It must not become In Progress until M003 is no
longer In Progress, and its implementation branch must first be rebased onto the
verified M003 checkpoint. This preserves the one-In-Progress rule and lets the
pre-migration backup tests exercise the actual post-M003 migration chain.

## Accepted decisions and decision gates

The human owner accepted all ten Recommended decisions below exactly as written
on 2026-09-01. An implementation must not silently choose a different operating
model.

### Decision 1: delivery sequencing

**Recommended:** prepare and review M004 now, but begin implementation only
after M003 is merged, verified, and no longer In Progress. Rebase M004's
implementation branch on that checkpoint and rerun the complete baseline before
editing code.

This avoids migration-chain drift and parallel edits to API startup,
Infrastructure registration, solution files, and project-state documentation.

### Decision 2: authoritative live-data location

**Recommended:** normal operation derives one absolute path below the current
OS user's local application-data directory:

```text
<LocalApplicationData>/WealthLedger/data/wealthledger.db
```

On Windows this normally resolves below `%LOCALAPPDATA%`. The application must
use the platform API rather than expanding an environment-variable string. A
single explicit `Storage:DatabasePath` override may be provided for an advanced
local deployment, but it must be absolute and pass the same safety checks.

Normal operation rejects a live path that is relative, empty, a directory,
inside the source repository, inside application build output, or the same as a
backup/restore staging path. Tests and the explicit `Testing` environment may
inject unique temporary paths. Design-time tooling uses an explicitly synthetic
path and must not fall back to a repository-root file.

The resolved authoritative path is shown by the local status command and in one
startup diagnostic. The diagnostic never prints a connection string or ledger
contents.

### Decision 3: supported operations surface

**Recommended:** add a small local .NET console composition root named
`WealthLedger.Operations`. It invokes focused Application use cases implemented
by Infrastructure and supports these initial commands:

```text
status
database initialize
database migrate
backup create
backup verify --file <absolute-path>
restore stage --file <absolute-path> --target <absolute-path>
restore replace --file <absolute-path> --confirm-replace-active
```

The exact executable invocation may vary by packaging, but these command
semantics and stable exit categories are part of M004. The console is not a
general SQL shell. It accepts no transaction-writing SQL and exposes no
unauthenticated HTTP operations endpoint.

`status`, backup verification, and isolated restore validation are read-only.
Initialization, migration, and active replacement are explicit lifecycle
operations. Every failure returns non-zero, leaves a sanitized human-readable
reason, and changes no ledger facts except a successfully applied EF migration.

### Decision 4: local exposure and process ownership

**Recommended:** the initial API is explicitly configured and validated to bind
only to IPv4 or IPv6 loopback. `localhost`, `127.0.0.1`, and `[::1]` are allowed;
wildcard, LAN, public, container-wide, and Unix-socket exposure are rejected in
normal operation. Host-header filtering is tightened consistently but is not
treated as a substitute for the bind check.

M004 does not add a remote-access opt-in. Remote access needs a later accepted
authentication, authorization, TLS, and deployment decision.

Only one process may own the authoritative database for normal writes.
Lifecycle operations that initialize, migrate, or replace it require exclusive
ownership through a tested cross-process guard; an instruction saying "please
stop the API" is not sufficient by itself. For the first local operating model,
`backup create` also requires the API process to be stopped and exclusive
ownership to be acquired. The SQLite online backup API is still required so the
snapshot remains correct in the presence of database journal state and future
connection-mode changes. A later in-process UI backup can reuse the same
Application operation without opening a second owner.

### Decision 5: backup package and consistency

**Recommended:** use the versioned extension `.wlbackup`. A package contains
exactly one standalone SQLite snapshot and one UTF-8 JSON manifest. The
snapshot is created into a unique temporary file with
`SqliteConnection.BackupDatabase`, not by copying only the main database file.

The manifest contains at least:

- backup format version;
- creation time in UTC;
- WealthLedger application version when available;
- ordered applied EF migration identifiers and latest schema version;
- SHA-256 of the standalone SQLite snapshot;
- SQLite integrity-check outcome;
- application compatibility-check outcome;
- verification time and verification status;
- encryption mode.

The command reports the resolved destination separately so a movable package
does not embed a stale absolute path. No household, account, asset, transaction,
note, balance, or source-reference value appears in the manifest or filename.

Backup creation opens the snapshot independently with foreign keys enabled,
runs full `PRAGMA integrity_check`, validates the expected migration history and
`PRAGMA foreign_key_check`, executes bounded read-only queries across ledger,
entry, lot, allocation, and receipt storage, and, when a posted transaction is
present, reconstructs one through the existing transaction read adapter without
printing its values. It then computes the digest, writes the package to a
temporary sibling, and publishes it with a same-filesystem atomic rename. A
failure removes temporary artifacts and never leaves a final-looking package.
Existing backups are never overwritten; filenames use UTC plus a collision-safe
suffix.

The package reader enforces size and entry-count limits, rejects duplicate or
unknown required entries, rejects path traversal and links, and never extracts
to a caller-controlled relative path.

### Decision 6: backup destination and retention responsibility

**Recommended:** `Backup:Directory` is an explicit absolute directory distinct
from the live-data directory. The operation warns and fails the real-data
readiness check when the only backup destination is on the same physical
workstation or when destination encryption cannot be established by the
operator.

M004 creates dated generations and never automatically deletes them. The
operations guide recommends three recoverable copies on two media with one copy
separated from the workstation, a backup after meaningful entry sessions and
before migration, and a restore drill at least quarterly. Cloud sync is not
called a backup unless version recovery has been independently verified.

Automated scheduling, remote-provider upload, credential storage, and retention
deletion are outside this milestone.

### Decision 7: restore and active replacement

**Recommended:** verification and staging never write to the authoritative live
path. `restore stage` creates a new isolated target, validates the package before
and after extraction, opens the restored database with foreign keys enabled,
runs full SQLite integrity, supported-schema, applied-migration, and
representative ledger-query checks, and reports the result. An existing target
is rejected rather than overwritten.

`restore replace` is deliberately separate and requires:

1. the explicit confirmation token shown in the command contract;
2. exclusive lifecycle ownership with the API stopped;
3. a fresh, verified pre-restore backup of the current live database;
4. a verified staged restore on the same filesystem as the live database;
5. a same-filesystem replace/swap that preserves the superseded database;
6. reopening and re-verifying the promoted database before success is reported;
7. automatic rollback to the preserved working copy if promotion verification
   fails.

WAL, SHM, and journal companions are handled explicitly; stale companions may
not be paired with a restored main file. The command never claims atomic replace
on a filesystem that cannot provide the required same-volume semantics. It
fails closed instead.

### Decision 8: explicit migration instead of startup migration

**Recommended:** remove normal API support for
`Database:ApplyMigrationsOnStartup`. API startup resolves the authoritative path
and checks that the database exists, is initialized, is structurally valid, and
has no pending migrations. It fails with an actionable sanitized message when
an explicit lifecycle command is needed.

`database initialize` creates and migrates a new database only when no file
exists. It refuses a non-empty or partially initialized destination.

`database migrate` first acquires exclusive ownership and inspects pending
migrations. If none exist it is a no-op success. If the database exists and any
migration is pending, it must create and verify a pre-migration `.wlbackup`
before calling EF migration APIs. Backup failure prevents migration. After
migration it runs integrity, compatibility, foreign-key, and representative
query checks. A migration failure preserves the original pre-migration backup
and the failed database for diagnosis; it does not silently recreate or
overwrite either one.

Tests may initialize isolated temporary databases directly through an explicit
test fixture. They must not require production startup migration behavior.

### Decision 9: encryption-at-rest boundary

**Recommended initial baseline:** do not add SQLCipher, custom encryption, or an
application-managed encryption key in M004. The active database relies on the
current OS user's access controls and verified full-disk/device encryption. A
`.wlbackup` is plaintext at the application-package layer and its manifest says
so, even when the destination device or service encrypts it externally.

Before real household data is treated as authoritative, the operators must
verify full-disk encryption, place every portable/off-device backup in an
established encrypted destination, and document who can recover that
destination. A lost recovery key is itself a data-loss scenario.

Application-level database or package encryption requires a separate accepted
ADR covering a maintained cryptographic implementation, threat model, key
creation, rotation, recovery, loss, and backup interoperability. A home-grown
cipher, password-derived ad-hoc format, or misleading "encrypted" flag is
forbidden.

### Decision 10: privacy-safe operational evidence

**Recommended:** local status and operation results may show the resolved data
and backup paths, application version, schema/migration state, UTC timestamps,
integrity status, backup digest prefix, and stable error category. They do not
show connection strings, SQL, stack traces, request bodies, household/member
names, financial identifiers, notes, balances, or transaction values.

No operational history table is added to the ledger. Backup manifests are the
evidence for individual packages. `status` may inspect configured package
manifests but must treat them as external operational evidence, not ledger fact.

## In scope

- a canonical per-user live-database path resolver and safety validator;
- explicit, test-only temporary-path injection;
- source-control exclusions for SQLite and WealthLedger backup/staging files;
- an explicit loopback-only API binding policy and startup validation;
- a focused local operations console and Application contracts;
- database status, initialization, explicit migration, consistent backup,
  backup verification, isolated restore, and confirmed active replacement;
- a versioned `.wlbackup` manifest and standalone SQLite snapshot;
- integrity, digest, migration compatibility, and representative query checks;
- exclusive lifecycle/process ownership for destructive or schema-changing
  operations;
- pre-migration and pre-restore verified backups;
- atomic publication/swap behavior and failure cleanup;
- privacy-safe diagnostics and stable command exit categories;
- an operator guide for backup cadence, restore drills, encryption reliance,
  incident preservation, and real-data readiness;
- a new Accepted ADR after the human approves these cross-cutting decisions.

## Out of scope

- changes to ledger Domain rules, transaction shapes, or financial arithmetic;
- M003 reversal implementation or any later transaction writer;
- a graphical settings, first-run, backup, or restore UI;
- HTTP backup, restore, migration, file-browser, or administrative endpoints;
- remote API access, multi-user hosting, authentication, authorization, or TLS;
- SQLCipher, custom cryptography, passwords, encryption-key storage, or key
  recovery implementation;
- automatic schedules, background backups, backup pruning, or cloud/removable-
  media provider integrations;
- data export/import, CSV/JSON portfolio export, or agent-readable data feeds;
- generic SQL execution, direct ledger repair, or direct posting outside
  Application use cases;
- database schema changes solely to record operations history;
- treating file synchronization, Git, or one same-disk copy as sufficient
  disaster recovery;
- automatically trusting or migrating an unrecognized future backup format or
  future schema;
- live replacement while another process owns the database.

## Required behavior

### Path resolution and startup

One service resolves one normalized absolute live path for API and operations.
The directory is created only by an explicit initialization or accepted normal
startup step; merely parsing status must not fabricate a database. Path
comparison follows the host filesystem's case and separator behavior and checks
protected roots after normalization. Symbolic-link/reparse-point escape is
rejected where the platform exposes it; inability to prove a candidate safe
fails closed for a normal live path.

The API acquires normal process ownership, validates loopback binding and
database compatibility, then maps endpoints. Missing, uninitialized,
incompatible, corrupt, or migration-pending state prevents write service from
starting and returns no private persistence detail.

Setup remains default-off and is mapped only by explicit configuration after a
database was initialized. M004 does not redesign setup or silently seed master
data.

### Status and initialization

`status` reports whether the configured database and backup directory exist,
whether the live path is safe, whether lifecycle ownership is available,
applied/pending migration state, SQLite integrity/foreign-key compatibility,
and the latest discoverable verified backup metadata. Missing configuration is
reported as an actionable non-zero state without creating a file.

`database initialize` refuses a relative, unsafe, existing, or non-empty target.
It stages database creation, applies the complete accepted migration chain,
validates the result, and only then publishes the authoritative file. Failure
leaves no authoritative-looking partial database.

### Backup creation and retry

An operator can retry `backup create` safely. Each successful retry creates a
new immutable generation; it does not overwrite or mutate an earlier package.
Concurrent lifecycle commands are rejected with a stable busy/ownership result.

A source write, busy condition, disk-full event, destination permission error,
cancelled operation, integrity failure, packaging error, or publish collision
leaves the source unchanged and no final-looking unverified package. Temporary
files are created only under controlled directories and cleaned best-effort;
cleanup failure is reported without hiding the primary failure.

### Backup verification

Verification never trusts the manifest before validating package structure. It
checks the snapshot digest, SQLite integrity, foreign keys, migration history,
schema compatibility, and representative read-only ledger queries from the
restored standalone snapshot. It distinguishes corrupt package, digest
mismatch, unsupported format, unsupported future schema, incomplete schema,
and I/O failure with stable sanitized categories.

A successful verification creates no ledger transaction and does not change
the source package. An old but explicitly supported schema may be reported as
`migration required`; verification itself does not mutate it.

### Restore and replacement

Staging uses a caller-selected new absolute target outside repository/build
roots. It never extracts archive paths verbatim and never follows links from a
package. Failure deletes or clearly quarantines the incomplete stage and leaves
all existing databases unchanged.

Active replacement does not begin until the package and stage are verified and
a pre-restore backup succeeds. Cancellation before the swap leaves the live
database untouched. A failure during swap or post-promotion verification
restores the preserved database and retains evidence needed to diagnose the
failed candidate. Success preserves, rather than deletes, the superseded copy
until the operator deliberately handles retention outside M004.

### Migration

Migration works only under exclusive ownership. The pre-migration package is
verified before the first schema-changing command. The operation records the
starting and ending migration identifiers in its result. Post-migration failure
is never disguised as success. Recovery instructions point to the verified
pre-migration package and require the ordinary staged-restore workflow.

### Restart and compatibility

A backup created before process exit verifies after restart. A staged or
promoted database produces the same representative transaction/position query
facts after restart as its snapshot. The checks use exact persisted integer and
text representations; they do not convert authoritative values through binary
floating point.

## Invariants

- The ledger remains the sole authoritative source of financial history.
- Backup metadata, lifecycle locks, manifests, and status results are
  operational facts, never balances or transactions.
- Backup, verification, restore, and migration do not edit posted ledger facts.
- Routine operations do not execute caller-supplied SQL.
- The Domain project remains independent of EF Core, SQLite, HTTP, filesystem,
  archive, and console concerns.
- Application exposes narrow use-case-driven operations ports; Infrastructure
  owns SQLite, filesystem, archive, and EF migration mechanics.
- The API and operations console are composition roots, not alternate business-
  rule paths.
- One normalized absolute path identifies the live database; relative working-
  directory behavior is not authoritative.
- A backup is published only after independent validation. A restore is
  promoted only after pre-replacement protection and validation.
- The only working database is never overwritten in place.
- SQLite foreign keys are enabled for every validation and restored connection.
- Journal/WAL state is never reconstructed by pairing unrelated companion
  files with a backup snapshot.
- Applied EF migration identifiers and application compatibility are checked;
  file integrity alone is insufficient.
- Digest validation detects accidental corruption, not malicious authenticity.
  M004 does not claim that an unsigned plaintext package resists a hostile
  editor.
- All paths, errors, and operation results are bounded and sanitized. Ledger
  values and private identifiers are absent from routine diagnostics.
- Real databases, journals, backups, restore stages, and operation-lock files
  are ignored by Git even though normal storage also lives outside the repo.
- No implementation or test requires real household data.

## API or UI contract

No ledger HTTP request or response contract changes. No UI is added.

The API's hosting/startup contract changes deliberately:

- normal hosting is loopback-only;
- automatic startup migration is no longer a supported normal switch;
- missing, corrupt, incompatible, or pending-migration databases fail startup
  with a sanitized instruction to use the local operations command;
- setup remains explicit and default-off.

The local operations CLI is the user-visible M004 contract. Commands return
exit code `0` only for a completed success/no-op. Non-zero categories must at
least distinguish invalid input/configuration, unsafe path, ownership/busy,
not found/already exists, invalid or incompatible backup, integrity failure,
I/O failure, and migration/restore failure. Exact numeric values and output
wording are frozen by command-contract tests during implementation.

Destructive replacement requires the literal explicit confirmation option
`--confirm-replace-active`; an interactive yes/no prompt alone is insufficient
for repeatable operation and tests. This flag authorizes only the already named
file and configured live target; it is not a general overwrite switch.

## Persistence impact

No new ledger table, column, trigger, index, or EF migration is expected. M004
must consume the migration chain present after verified M003 without editing a
historical migration.

The filesystem gains non-authoritative operational artifacts:

- the live SQLite database under the per-user data directory;
- an ownership/operation lock adjacent to operational state;
- immutable `.wlbackup` packages in the configured backup directory;
- unique temporary snapshot/package/stage files during an operation;
- preserved pre-migration, pre-restore, and superseded database generations.

Exact internal temporary extensions are implementation details, but every
chosen extension is added to `.gitignore`. At minimum repository rules cover:

```text
*.db
*.db-journal
*.db-wal
*.db-shm
*.sqlite
*.sqlite-journal
*.sqlite-wal
*.sqlite-shm
*.sqlite3
*.sqlite3-journal
*.sqlite3-wal
*.sqlite3-shm
*.wlbackup
*.wlrestore
*.wloperation.lock
```

The backup manifest is versioned independently from the EF schema. Readers
reject unknown future major versions. Additive fields in a known compatible
version are ignored only under an explicit compatibility rule; required fields
may not be inferred.

## Verification evidence

Verification on 2026-09-02 used only synthetic databases in unique temporary
directories and fresh operations processes:

- the full solution passed 388 tests: Domain 83, Application 79,
  Infrastructure 137, API 66, and Operations 23;
- named focused suites passed: local-data Application 9, backup-related
  Infrastructure 42, restore-related Infrastructure 20, local-hosting API 28,
  operations CLI 23, and the Domain dependency boundary 1;
- the documented end-to-end process workflow passed initialize, status,
  consistent backup creation, independent verification, isolated restore,
  restored status, and restart/readback;
- separate process workflows passed mandatory verified pre-migration backup
  from migration 002 to 003 and confirmed active replacement with preserved
  recovery evidence;
- the M001 regression classified the valid older schema as
  `MigrationRequired`, created and independently verified its pre-migration
  package, applied M002 and M003 in order, and preserved synthetic data in both
  the upgraded database and an isolated restore of the M001 package;
- WAL, rollback-journal, hostile/corrupt archive, path/reparse, ownership,
  cancellation, injected I/O, rollback, privacy, and restart cases passed in
  the focused and full suites;
- EF reported no pending model changes, confirming that no M004 ledger migration
  was introduced;
- all 15 representative artifact paths matched `.gitignore`, `git ls-files`
  found no database/backup/stage/lock artifact, and a full ignored-file scan
  found none in the implementation worktree;
- `git diff --check` passed. `dotnet format --verify-no-changes` reproduced only
  the three pre-existing Windows SDK line-ending diagnostics recorded in the
  baseline above and produced no committable difference.

The smoke review found and fixed abandoned initialization-stage WAL/SHM names;
the regression now checkpoints the stage, proves companion cleanup, and always
cleans the unique pre-publish basename. The focused initializer tests and the
complete recovery workflow passed after that fix.

## Acceptance criteria

- Normal API and operations resolution selects the documented absolute per-user
  path, independent of current working directory.
- Relative and repository/build-root live paths fail closed; isolated test
  paths remain explicitly supported.
- Representative SQLite, journal, backup, stage, and lock artifacts are ignored
  by Git, while no real database or backup is committed.
- Tracked configuration and startup validation prove that normal API binding is
  loopback-only and reject a wildcard/non-loopback override.
- Only one normal writer/lifecycle owner can use the authoritative database;
  conflicting commands fail without modifying it.
- A new database is initialized only by the explicit command and is usable after
  restart; an existing target is never silently recreated.
- API startup does not run migrations. Missing or pending-migration state fails
  with a stable sanitized instruction.
- Migration of an existing database cannot begin until a dated pre-migration
  backup has been created and independently verified.
- A consistent backup succeeds from a source whose SQLite journal mode and open
  transaction history would make a main-file copy unsafe.
- Every successful package has one snapshot, a versioned privacy-safe manifest,
  correct migration metadata and digest, and a successful independent integrity
  and application compatibility result.
- Disk, permission, cancellation, busy, integrity, and packaging failures leave
  the live database unchanged and no final-looking partial backup.
- Tampered, truncated, traversal-bearing, duplicate-entry, oversized,
  unsupported-format, and unsupported-schema packages are rejected without
  extracting over an existing file.
- A verified package restores to a new isolated path and produces equivalent
  representative ledger read results after restart.
- Active replacement refuses a missing confirmation, active owner, failed
  pre-restore backup, existing unsafe stage, or failed validation.
- Successful active replacement preserves a verified pre-restore backup and a
  superseded copy; failed promotion restores the previous working database.
- Status and all failures disclose no connection string, SQL, stack trace,
  request body, ledger value, note, or private financial identifier.
- The operations guide enables a human to locate the authoritative file, create
  and verify a backup, perform an isolated restore drill, migrate, recover from
  failure, and assess the real-data readiness checklist.
- Existing post-M003 tests continue to pass; focused M004 tests, formatting, and
  EF migration-model drift checks pass.
- An accepted ADR records the approved data-location, operations surface,
  backup/restore, exposure, migration, and encryption boundaries.
- `PROJECT_STATE.md`, `ROADMAP.md`, `SECURITY_OPERATIONS.md`, and this milestone
  describe verified behavior consistently only after implementation passes.

## Test scenarios

### Domain

No Domain behavior changes are expected. Add a dependency or Domain test only
to prove that filesystem/SQLite/operations concerns did not enter Domain.

### Application

- status, initialize, migrate, backup, verify, stage, and replace use cases call
  only their narrow ports and return stable result categories;
- invalid/relative/unsafe paths are rejected before an Infrastructure mutation;
- no-pending-migration is an idempotent no-op;
- pending migration requires a successful verified backup before migration;
- failed backup prevents migration; failed post-migration verification retains
  recovery evidence;
- replacement requires confirmation and successful staged/pre-restore checks;
- cancellation and concurrent-operation results do not report success;
- routine result models contain operational metadata but no ledger values.

### Infrastructure

- default and override path resolution across current-working-directory changes;
- case/separator normalization and protected-root, symlink, or reparse escape
  where supported by the host platform;
- exclusive cross-process ownership and release after success, failure,
  cancellation, and process restart where testable;
- backup through `BackupDatabase` with WAL or rollback-journal state and an open
  source connection, followed by standalone reopen;
- full integrity, foreign-key, migration-history, schema-compatibility, digest,
  and representative-query verification;
- atomic backup publication and cleanup under injected I/O, collision,
  cancellation, and disk/permission failures;
- manifest round trip, required/additive fields, UTC handling, deterministic
  migration ordering, and privacy-safe source identity;
- archive traversal, absolute entry, link, duplicate entry, entry-count/size,
  truncated database, digest mismatch, unknown format, and future-schema
  rejection;
- isolated restore refuses an existing target and never writes outside its
  controlled staging directory;
- active replacement creates a verified pre-restore backup, handles journal
  companions, preserves the old copy, reopens the promoted copy, and rolls back
  an injected failed promotion check;
- migration backup ordering is observable and a failed migration never deletes
  the pre-migration generation;
- restart from initialized, migrated, staged, and promoted files preserves exact
  representative query results;
- no M004 EF model drift or ledger schema migration is introduced.

### API or UI

- default tracked hosting starts only on loopback in a process-level test;
- wildcard, `0.0.0.0`, `::`, LAN-address, and multi-address non-loopback
  configurations fail before endpoint service;
- loopback IPv4/IPv6 variants are accepted according to the frozen contract;
- missing database, pending migration, corruption, and unsafe-path startup
  failures are actionable and sanitized;
- setup remains absent by default and present only with its explicit flag;
- the retired startup-migration switch cannot silently migrate a database;
- operations CLI command parsing, required confirmation, exit categories, and
  privacy-safe stdout/stderr are covered end to end;
- no backup, restore, migration, file-browser, or generic SQL HTTP route exists.

### Repository and operational checks

- `git check-ignore --no-index` matches every representative protected
  extension and `git ls-files` finds no database/backup artifact;
- a scripted restore drill from a generated synthetic database succeeds to a
  new directory and never references household data;
- the documented pre-migration and failed-restore procedures are executable by
  a person from a clean checkout.

## Verification commands

Focused filters and any operations smoke command must be named during
implementation. At minimum:

```powershell
dotnet test tests/WealthLedger.Application.Tests/WealthLedger.Application.Tests.csproj --no-restore --filter FullyQualifiedName~LocalDataOperation --verbosity minimal
dotnet test tests/WealthLedger.Infrastructure.Tests/WealthLedger.Infrastructure.Tests.csproj --no-restore --filter FullyQualifiedName~Backup --verbosity minimal
dotnet test tests/WealthLedger.Infrastructure.Tests/WealthLedger.Infrastructure.Tests.csproj --no-restore --filter FullyQualifiedName~Restore --verbosity minimal
dotnet test tests/WealthLedger.Api.Tests/WealthLedger.Api.Tests.csproj --no-restore --filter FullyQualifiedName~LocalHosting --verbosity minimal
```

Repository-protection checks:

```powershell
git check-ignore --no-index sample.db sample.db-journal sample.db-wal sample.db-shm sample.sqlite sample.wlbackup sample.wlrestore sample.wloperation.lock
git ls-files "*.db" "*.db-journal" "*.db-wal" "*.db-shm" "*.sqlite" "*.sqlite-journal" "*.sqlite-wal" "*.sqlite-shm" "*.sqlite3" "*.wlbackup" "*.wlrestore" "*.wloperation.lock"
```

Final verification:

```powershell
dotnet test WealthLedger.slnx --no-restore --verbosity minimal
dotnet format WealthLedger.slnx --verify-no-changes --no-restore --verbosity minimal
dotnet ef migrations has-pending-model-changes --project src/WealthLedger.Infrastructure/WealthLedger.Infrastructure.csproj --startup-project src/WealthLedger.Infrastructure/WealthLedger.Infrastructure.csproj --context WealthLedgerDbContext --no-build
```

The implementation review must also run the documented synthetic backup,
verification, isolated restore, and pre-migration smoke workflow. A passing unit
suite alone cannot prove recoverability.

## Documentation updates

After human acceptance, add an ADR for the accepted local-data operating model
and index it in `docs/decisions/README.md`. Do not mark proposed encryption
assumptions as accepted before that review.

After implementation verification:

- mark this milestone Verified and record acceptance/verification dates;
- update `PROJECT_STATE.md` with the factual data-location, backup, restore,
  migration, and exposure checkpoint;
- move M004 to Verified in `ROADMAP.md` and make M005 the next candidate;
- update `SECURITY_OPERATIONS.md` so implemented requirements are distinguished
  from remaining encryption, remote access, export, and scheduling work;
- replace README's startup-migration initialization example with operations
  commands and link the operator guide;
- add a canonical operations guide, including backup/restore drills, recovery,
  cadence, encryption reliance, and real-data readiness;
- update `ARCHITECTURE.md` for the operations composition root and narrow
  Application/Infrastructure boundary;
- update `UX_MVP.md` only to identify which later UI actions wrap the verified
  operations contracts;
- do not rewrite M001-M003 or accepted ADR history.

## Suggested commit boundaries

```text
chore(repo): ignore local database and backup artifacts
feat(operations): resolve and validate the local data location
feat(hosting): enforce local-only API ownership and compatibility
feat(operations): create and verify consistent sqlite backups
feat(operations): stage and safely replace restored databases
feat(operations): require verified backups before migration
test(operations): prove failure recovery privacy and restart behavior
docs(operations): publish the accepted local recovery guide
docs(state): record verified safe local data operations
```

Keep commits buildable where practical. Do not combine M003 ledger behavior,
M005 queries, M006 UI, provider integrations, export, or analytics with M004.

## Risks and rollback

- **False backup confidence:** a copied main file can omit WAL state, and a hash
  alone cannot prove SQLite or application compatibility. Use the SQLite backup
  API and independently open/query every published snapshot.
- **Wrong-file operation:** relative or ambiguous paths can back up or replace a
  development database instead of the live one. Normalize, display, validate,
  and explicitly confirm the authoritative absolute path.
- **Concurrent owner:** replacing or migrating while the API is open can split
  processes across different files or corrupt the operating assumption. Require
  a tested cross-process ownership guard and fail closed.
- **Partial publish/swap:** process loss or disk exhaustion can leave a package
  or database half-written. Stage on the same filesystem, publish only after
  verification, preserve the old file, and test injected failure boundaries.
- **Malicious package:** archive paths or oversized entries could escape staging
  or exhaust resources. Bound and validate structure before extraction; do not
  claim cryptographic authenticity from an unsigned SHA-256 digest.
- **Future-schema mismatch:** a structurally valid future database may be unsafe
  for an older application. Reject unsupported format/schema versions and never
  downgrade automatically.
- **Plaintext portability:** OS disk encryption stops protecting a backup after
  it is copied to an unencrypted device. Manifest and operator guidance must say
  that M004 packages are plaintext at the application layer.
- **Lost encryption recovery:** an encrypted off-device destination is useless
  if its recovery material is lost. The household must test recovery separately
  from WealthLedger.
- **Privacy leakage:** filenames, manifests, errors, or test fixtures can reveal
  financial facts. Use opaque synthetic identities and inspect all command
  output in tests.
- **Migration failure:** do not edit historical migrations or auto-recreate the
  database. Preserve the failed file and verified pre-migration package, then
  recover through ordinary staged restore.
- **Implementation rollback:** before real data use, code can be reverted to the
  pre-M004 checkpoint because no ledger schema change is expected. Any database
  that M004 initialized or migrated remains preserved; never delete it during
  code rollback. Reopen it only with a compatible application after verification.

The safest rollback is always performed against synthetic copies first. No
rollback procedure may use the live database as its only test subject.
