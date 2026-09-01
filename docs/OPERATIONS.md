# WealthLedger Local Data Operations

Status: Canonical M004 operator guide

Last verified: 2026-09-01

## Safety boundary

`WealthLedger.Operations` is the only supported M004 lifecycle command surface.
It can report status, initialize a new database, migrate an existing database,
create and verify backups, stage a restore, and deliberately replace the active
database. It is not a SQL console and cannot post or edit ledger transactions.

Normal storage resolves to:

```text
<LocalApplicationData>/WealthLedger/data/wealthledger.db
```

On Windows this is normally below `%LOCALAPPDATA%`. Use `status` to obtain the
exact resolved path; do not infer it from the current directory. An advanced
`Storage:DatabasePath` override must be absolute, outside the repository and
build output, and pass the same root and reparse-point checks.

All examples below use PowerShell. Stop the API before initialization, backup
creation, migration, or active replacement. The commands enforce this with an
exclusive adjacent operation lock; stopping the API is not a substitute for
that check.

## Configure a backup destination

`Backup:Directory` is required for status protection evidence, backup creation,
pending migration, and active replacement. It must be an absolute directory
separate from the live-data directory. Choose an established destination before
entering real data.

```powershell
$wlBackupDirectory = 'E:\Encrypted WealthLedger Backups'
$wlProtectionArgs = @(
  "--Backup:Directory=$wlBackupDirectory"
  '--Backup:DestinationSeparationConfirmed=true'
  '--Backup:DestinationEncryptionConfirmed=true'
)
```

Set the two confirmations to `true` only after verifying that the destination
is separated from the primary workstation and encrypted by an established
device, volume, or service control. The application does not detect or create
that external protection. A `.wlbackup` is plaintext at the application-package
layer even when its destination protects it externally.

For an advanced live-path override, place this additional argument in a
separate array and use the same array for every API and operations invocation:

```powershell
$wlDatabasePath = 'D:\WealthLedger Data\wealthledger.db'
$wlStorageArgs = @("--Storage:DatabasePath=$wlDatabasePath")
```

The remaining examples use the default live path and therefore omit
`$wlStorageArgs`.

## Locate and initialize the database

Status does not create a database. Before initialization it prints the resolved
path and exits with the stable `NOT_FOUND` category.

```powershell
dotnet run --project src/WealthLedger.Operations/WealthLedger.Operations.csproj -- status @wlProtectionArgs

dotnet run --project src/WealthLedger.Operations/WealthLedger.Operations.csproj -- database initialize @wlProtectionArgs

dotnet run --project src/WealthLedger.Operations/WealthLedger.Operations.csproj -- status @wlProtectionArgs
```

Initialization refuses an existing main file or SQLite journal companion. It
creates a unique stage, applies the accepted migration chain, validates it, and
only then publishes the authoritative file. It never recreates an existing
database.

One-time master-data setup remains a separate, default-off API action. After
database initialization, start the loopback API with `Setup:Enabled=true`, call
the documented setup endpoint once, stop it, and restart without that flag.
Automatic startup migration is not supported.

## Create and verify a backup

Stop the API, then create a new immutable generation:

```powershell
dotnet run --project src/WealthLedger.Operations/WealthLedger.Operations.csproj -- backup create @wlProtectionArgs
```

Copy the exact `BackupFile` path printed by the command. Do not choose a package
only because its filename looks recent.

```powershell
$wlBackupFile = 'E:\Encrypted WealthLedger Backups\wealthledger-manual-20260901T120000000Z-example.wlbackup'

dotnet run --project src/WealthLedger.Operations/WealthLedger.Operations.csproj -- backup verify --file $wlBackupFile @wlProtectionArgs
```

Every successful retry creates a different UTC-and-collision-safe filename;
existing packages are never overwritten or pruned. Each package contains
exactly `database.sqlite` and `manifest.json`. Creation uses SQLite's online
backup API, independently reopens the snapshot, runs integrity, foreign-key,
migration, bounded storage, transaction-readback, and position checks, computes
SHA-256, verifies the completed package again, and atomically publishes it.

The manifest and filename contain operational metadata only: version, UTC
times, application and schema versions, digest, verification outcomes, and the
literal encryption mode `PLAINTEXT`. They contain no household, account, asset,
transaction, note, balance, or source-reference values.

Status inspects at most 256 packages in the configured active backup directory.
Move older generations to another retained encrypted archive before that bound
is reached; M004 never deletes them automatically.

## Perform an isolated restore drill

Choose a new absolute target outside the live and backup directories. The target
and all SQLite companions must not exist.

```powershell
$wlRestoreRoot = Join-Path `
  ([Environment]::GetFolderPath([Environment+SpecialFolder]::LocalApplicationData)) `
  'WealthLedger\restore-drills'
$wlRestoreTarget = Join-Path `
  $wlRestoreRoot `
  ("restore-{0}.db" -f [DateTime]::UtcNow.ToString('yyyyMMddTHHmmssfffZ'))

dotnet run --project src/WealthLedger.Operations/WealthLedger.Operations.csproj -- restore stage --file $wlBackupFile --target $wlRestoreTarget @wlProtectionArgs
```

Validate the restored file after a fresh process start by temporarily treating
it as the status target:

```powershell
$wlRestoreStatusArgs = @(
  "--Storage:DatabasePath=$wlRestoreTarget"
  "--Backup:Directory=$wlBackupDirectory"
  '--Backup:DestinationSeparationConfirmed=true'
  '--Backup:DestinationEncryptionConfirmed=true'
)

dotnet run --project src/WealthLedger.Operations/WealthLedger.Operations.csproj -- status @wlRestoreStatusArgs
```

Staging validates the archive before extraction, copies only the named snapshot
to a controlled sibling stage, normalizes journal state, validates digest and
representative query equivalence, and then publishes the new target. It never
writes to or overwrites the authoritative live path.

## Apply an explicit migration

Stop the API and run:

```powershell
dotnet run --project src/WealthLedger.Operations/WealthLedger.Operations.csproj -- database migrate @wlProtectionArgs
```

With no pending migration this is an idempotent success and creates no backup.
With a pending migration, the command holds exclusive ownership, creates and
independently verifies a dated `pre-migration` package, repeats the live-state
check, and only then invokes EF Core. It reports starting and ending migration
identifiers and the recovery-package path. A backup or verification failure
prevents the first schema-changing command.

If migration reports failure:

1. Stop all writers and do not initialize, delete, overwrite, or edit either
   generation.
2. Preserve the reported `PreMigrationBackup` and the failed live database.
3. Verify the package with `backup verify`.
4. Stage it to a new unique target with `restore stage`.
5. Validate that target after restart before deciding on active replacement.

Do not edit a historical migration or directly modify `__EFMigrationsHistory`.

## Deliberately replace the active database

Use ordinary isolated staging for drills. Active replacement is reserved for a
reviewed recovery decision. Stop the API, verify the exact package, and run:

```powershell
dotnet run --project src/WealthLedger.Operations/WealthLedger.Operations.csproj -- restore replace --file $wlBackupFile --confirm-replace-active @wlProtectionArgs
```

The literal `--confirm-replace-active` token is mandatory and authorizes only
the named package and configured live target. The command:

1. acquires exclusive lifecycle ownership;
2. verifies a same-filesystem candidate;
3. creates and verifies a fresh `pre-restore` backup of the current live file;
4. checkpoints and separates current WAL/journal companions;
5. moves the current main file to a unique `superseded` sibling;
6. promotes and reopens the candidate;
7. restores the previous generation automatically if promotion validation
   fails.

Success preserves both the verified pre-restore package and superseded database.
Failure after swap preserves the failed candidate and pre-restore package while
restoring the previous working database. M004 does not automatically delete any
of that evidence and does not claim the multi-step swap is atomic across a
process or machine crash.

If a crash interrupts replacement, do not run `database initialize`. Preserve
the live directory, adjacent `.superseded.db`/`.wlrestore` generations, lock
marker, and pre-restore packages. An unlocked stale lock marker does not block a
restart. Recover through verification and a new isolated stage from the verified
pre-restore package; never pair a main file with unrelated `-wal`, `-shm`, or
`-journal` files.

## Stable exit categories

The command exits `0` only for success or a migration no-op.

| Code | Category |
|---:|---|
| 2 | `INVALID_INPUT_OR_CONFIGURATION` |
| 3 | `UNSAFE_PATH` |
| 4 | `OWNERSHIP_BUSY` |
| 5 | `NOT_FOUND` |
| 6 | `ALREADY_EXISTS` |
| 7 | `INVALID_BACKUP` |
| 8 | `INCOMPATIBLE_BACKUP` |
| 9 | `INTEGRITY_FAILURE` |
| 10 | `IO_FAILURE` |
| 11 | `MIGRATION_FAILURE` |
| 12 | `RESTORE_FAILURE` |
| 13 | `CANCELLED` |
| 14 | `DATABASE_NOT_READY` |

Output may include resolved paths, application/schema versions, UTC times,
integrity and compatibility status, and a digest prefix. It never intentionally
prints a connection string, SQL, stack trace, request body, or ledger value.

## Backup cadence and real-data readiness

- Keep at least three recoverable copies across two media, with one copy
  separated from the workstation.
- Back up after meaningful entry sessions and before every pending migration.
- Perform and record an isolated restore drill at least quarterly and after a
  backup-format or external-encryption change.
- Do not call synchronization a backup unless version recovery has been tested.
- Verify full-disk encryption for the live device and established encryption
  plus recovery-key custody for every portable or off-device package.
- Keep setup default-off, API binding loopback-only, and all normal writes behind
  Application use cases.

M004 supplies tested local recovery mechanics. It does not make one workstation
or one plaintext package an adequate sole record of real household assets.

## Synthetic verification workflow

The process tests execute the same commands against unique temporary paths and
ephemeral state. From a clean checkout:

```powershell
dotnet restore WealthLedger.slnx

dotnet test tests/WealthLedger.Operations.Tests/WealthLedger.Operations.Tests.csproj --no-restore --filter FullyQualifiedName~OperationsCli_InitializeStatusBackupVerifyStageAndRestart --verbosity minimal

dotnet test tests/WealthLedger.Operations.Tests/WealthLedger.Operations.Tests.csproj --no-restore --filter FullyQualifiedName~OperationsCli_MigrationCreatesOneVerifiedPreMigrationGeneration --verbosity minimal

dotnet test tests/WealthLedger.Operations.Tests/WealthLedger.Operations.Tests.csproj --no-restore --filter FullyQualifiedName~OperationsCli_ReplacementRequiresConfirmationAndPreservesEvidence --verbosity minimal
```

These checks cover initialize, status, consistent package creation, independent
verification, isolated staging, restore validation, verified pre-migration and
pre-restore paths, confirmed replacement, restart/readback, and preservation of
recovery evidence without using real household data.
