---
name: safe-lab
description: Run the WealthLedger host or Operations CLI against an isolated synthetic lab, never against real household data. Use for manual UI checks, the recovery smoke test and learning sessions, and whenever a task needs the app running. This is the only sanctioned way to start either program; never use plain dotnet run.
argument-hint: "host | smoke | ops <command...> [--lab <existing lab folder>]"
---

# Safe synthetic lab

Request: `$ARGUMENTS`

## Rules

- Synthetic data only. Never touch:
  - the default database under `%LOCALAPPDATA%\WealthLedger`;
  - the owner's real backup folder;
  - `launchSettings.json`.
- Use only the PowerShell tool, in the background for the host. Do not use
  terminal tabs, preview servers, `Monitor` or a script file.
- Every launch passes `--no-launch-profile` and explicit `--Storage:DatabasePath=`
  and `--Backup:Directory=` values inside the lab folder.
  - The guard hook blocks a `dotnet run` or a direct Api or Operations binary
    launch whose text lacks these.
  - Each `dotnet run` also asks the owner for permission.
  - The hook is a textual check, not a guarantee, so follow the steps exactly.
- PowerShell state does not persist between tool calls. Define the lab
  variables in the same PowerShell call as the command that uses them.
- Labs live outside the repository and its build output, under
  `$env:USERPROFILE\WealthLedgerLabs`. Never delete a lab without asking.

## 1. Build once

`dotnet build WealthLedger.slnx --no-restore --verbosity minimal`. Every launch
below uses `--no-build`.

## 2. Choose the lab

**With `--lab <folder>`:** reuse it. First check that it sits under
`$env:USERPROFILE\WealthLedgerLabs` and contains a `data` folder.

**Otherwise:** create a new lab and report its path:

```powershell
$wlLab = Join-Path $env:USERPROFILE ('WealthLedgerLabs\lab-{0}' -f (Get-Date -Format 'yyyyMMdd-HHmmss'))
New-Item -ItemType Directory -Path "$wlLab\data", "$wlLab\backups", "$wlLab\restores" | Out-Null
$wlLab
```

## 3. Lab arguments

Start every launch call with this block:

```powershell
$wlLab = '<lab folder>'
$wlLabArgs = @(
  "--Storage:DatabasePath=$wlLab\data\wealthledger.db"
  "--Backup:Directory=$wlLab\backups"
  '--Backup:DestinationSeparationConfirmed=false'
  '--Backup:DestinationEncryptionConfirmed=false'
)
```

- Both confirmations stay `false`, because a lab is neither separated nor
  encrypted. They only change the protection status and its warning; they do
  not block initialize, backup or restore.
- The Operations CLI accepts only these four overrides and `--Environment=`.
  Each may appear only once.
- Any other `--Key=value`, including `--Setup:Enabled` and `--Urls`, exits 2
  with `INVALID_INPUT_OR_CONFIGURATION`.

## 4a. `host`: the local UI

Run this as a background PowerShell command:

```powershell
# lab arguments block here
$wlHostArgs = $wlLabArgs + @('--Setup:Enabled=false', '--Urls=http://127.0.0.1:54900')
dotnet run --project src/WealthLedger.Api/WealthLedger.Api.csproj --no-launch-profile --no-build -- @wlHostArgs
```

1. Wait for `Local startup mode: <Mode>`, then `Now listening on: http://127.0.0.1:54900`.
   Port 54900 keeps the lab apart from the owner's own profile.
2. Open `http://127.0.0.1:54900/` in the browser pane, or `/setup` for a new lab.
3. If stderr shows a `STARTUP <CATEGORY>:` line (for example
   `STARTUP INVALIDINPUTORCONFIGURATION:`), the host refused its configuration
   and exited.
4. If it logs `Local startup is blocked: <Category>`, open `/blocked`.
5. The host picks one startup mode per process and never promotes itself.
   Restart it after each guided first-run step, as README "Local UI and guided
   first run" describes.
6. Stop the background process when you are done.

## 4b. `ops <command...>`: one Operations CLI command

Stop any running lab host first. In `Ready` it holds the database lease, so the
CLI exits 4 with `OWNERSHIP_BUSY`.

```powershell
# lab arguments block here
dotnet run --project src/WealthLedger.Operations/WealthLedger.Operations.csproj --no-launch-profile --no-build -- <command...> @wlLabArgs
```

**Commands** (see `docs/OPERATIONS.md`):

- `status`
- `database initialize`
- `database migrate`
- `backup create`
- `backup verify --file <absolute .wlbackup>`
- `restore stage --file <absolute .wlbackup> --target <absolute new .db>`
- `restore replace --file <absolute .wlbackup> --confirm-replace-active`

Pass each path as the next token; `--file=<path>` is rejected. `restore replace`
swaps the lab's active database, so ask before running it, even in a lab.

**Exit codes:** 0 is success. Any other code is a category from
`docs/OPERATIONS.md` "Stable exit categories":

| Code | Category |
|---|---|
| 2 | `INVALID_INPUT_OR_CONFIGURATION` |
| 3 | `UNSAFE_PATH` |
| 4 | `OWNERSHIP_BUSY` |
| 5 | `NOT_FOUND` |
| 6 | `ALREADY_EXISTS` |

## 4c. `smoke`: disposable recovery smoke (new lab only)

Run each step as its own `ops` call. Stop at the first result that differs from
what is expected.

1. `status`: expect exit 5 with
   `FAILURE NOT_FOUND: The database has not been initialized. Run database initialize.`
2. `database initialize`: expect `SUCCESS DATABASE_INITIALIZE`.
3. `status`: expect exit 0, with `LatestBackupFile: NONE`, then
   `WARNING LOCAL_PROTECTION_NOT_READY: ...`, then `SUCCESS STATUS`. The warning
   is expected, because both confirmations are false.
4. `backup create`: expect `SUCCESS BACKUP_CREATE`. Copy the exact path from its
   `BackupFile: ` line.
5. `backup verify --file <BackupFile>`: expect `SUCCESS BACKUP_VERIFY`.
6. `restore stage --file <BackupFile> --target "$wlLab\restores\restore-$([DateTime]::UtcNow.ToString('yyyyMMddTHHmmssfffZ')).db"`:
   - expect `SUCCESS RESTORE_STAGE`;
   - copy the path from its `TargetDatabasePath: ` line;
   - the target must be new, or the command exits 6 with `ALREADY_EXISTS`;
   - the target must be outside `Backup:Directory`, or it exits 3 with
     `UNSAFE_PATH`.
7. In a fresh process, run `status` against the staged file. Use its own array,
   because a duplicate `--Storage:DatabasePath` is rejected:
   ```powershell
   $wlLab = '<lab folder>'
   $wlRestoreArgs = @(
     "--Storage:DatabasePath=<TargetDatabasePath>"
     "--Backup:Directory=$wlLab\backups"
     '--Backup:DestinationSeparationConfirmed=false'
     '--Backup:DestinationEncryptionConfirmed=false'
   )
   dotnet run --project src/WealthLedger.Operations/WealthLedger.Operations.csproj --no-launch-profile --no-build -- status @wlRestoreArgs
   ```
   Expect exit 0, `LatestBackupFile: <BackupFile>` and
   `LatestBackupWorkspaceBinding: Matched`.

Report each step's exit code and its key output lines.

## 5. Finish

- Stop any background host.
- Report the lab path.
- Leave the lab in place. Delete it only when the owner asks, and only inside
  `$env:USERPROFILE\WealthLedgerLabs`.
