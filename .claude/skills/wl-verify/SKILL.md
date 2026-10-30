---
name: wl-verify
description: Run WealthLedger verification from the repo or worktree root. With no argument it runs the full gate, the same steps as .github/workflows/verify.yml (tool restore, restore, build, Playwright Chromium, full test suite, format check, EF pending-model check), and reports per-project test counts in the PR-body Verification format. With "fast" it runs the seconds-long inner loop. Use after changes and before every push or PR.
argument-hint: "[fast [test-filter]]"
---

# WealthLedger verification

Arguments: `$ARGUMENTS`

Rules for every step:

- Run each command with the PowerShell tool from the repository or worktree root.
- Start every PowerShell call with `$env:DOTNET_CLI_UI_LANGUAGE = 'en'`. This
  machine's tr-TR culture otherwise localizes the output: `Başarılı` means
  passed and `Başarısız` means failed. `.claude/settings.json` sets the same
  variable; the prefix covers sessions where project settings are not applied.
- Never skip a failing step to reach the next one.
- Take every number from the actual output. Never estimate counts or copy them
  from docs.

Each test project prints one line like this, or `Failed!` when any test fails:

`Passed!  - Failed:     0, Passed:   193, Skipped:     0, Total:   193, Duration: 243 ms - WealthLedger.Domain.Tests.dll (net10.0)`

## Fast loop (`fast`, optional test filter)

1. Build: `dotnet build WealthLedger.slnx --no-restore --verbosity minimal`
   (`TreatWarningsAsErrors` is on). In a new worktree or after a package change,
   first run `dotnet tool restore` and `dotnet restore WealthLedger.slnx --verbosity minimal`.
2. Run the fast projects in one PowerShell call:
   ```powershell
   $env:DOTNET_CLI_UI_LANGUAGE = 'en'
   dotnet test tests/WealthLedger.Domain.Tests/WealthLedger.Domain.Tests.csproj --no-build --verbosity minimal
   dotnet test tests/WealthLedger.Application.Tests/WealthLedger.Application.Tests.csproj --no-build --verbosity minimal
   dotnet test tests/WealthLedger.UI.Tests/WealthLedger.UI.Tests.csproj --no-build --verbosity minimal
   ```
3. If a filter follows `fast`, also run the matching real-SQLite tests. AGENTS.md
   requires them for persistence work.
   ```powershell
   dotnet test tests/WealthLedger.Infrastructure.Tests/WealthLedger.Infrastructure.Tests.csproj --no-build --verbosity minimal --filter "FullyQualifiedName~<filter>"
   ```
   If the output says `No test matches the given testcase filter`, the exit code
   is still 0 and no `Passed!` line appears. Report
   `Infrastructure (filter <filter>): 0 matched` and ask whether the filter is
   right. Never count it as a pass.
4. Report the build result and each project's line. A fast loop never replaces
   the full gate before a push.

## Full gate (no argument)

The steps follow the order of `.github/workflows/verify.yml`. Stop at the first
failure, show the failing output, then fix it or report it.

1. `dotnet tool restore`
2. `dotnet restore WealthLedger.slnx --verbosity minimal`
3. `dotnet build WealthLedger.slnx --no-restore --configuration Debug --verbosity minimal`
4. `pwsh tests/WealthLedger.UI.BrowserTests/bin/Debug/net10.0/playwright.ps1 install chromium`.
   This is quick when the pinned Chromium is already installed.
5. `dotnet test WealthLedger.slnx --no-restore --no-build --configuration Debug --verbosity minimal`
   - This takes three to four minutes; use a 10-minute timeout. The slow
     projects are Infrastructure, Api and Browser (about 2.5 minutes each, in
     parallel) and Operations (about 1 minute).
   - The solution run prints one line per test project and no grand total.
   - Check that exactly seven lines appear: Domain, Application,
     Infrastructure, Operations, UI, Api and UI.BrowserTests.
   - Compute the total by adding them up.
6. `dotnet format WealthLedger.slnx --verify-no-changes --no-restore --verbosity minimal`
   This takes about a minute.
   - If it fails, run `dotnet format WealthLedger.slnx --no-restore --verbosity minimal`,
     review the diff, and rerun the check.
   - If it reports only line-ending changes in a fresh worktree, check
     `.gitattributes` and `core.autocrlf` before touching any code.
7. `dotnet ef migrations has-pending-model-changes --project src/WealthLedger.Infrastructure/WealthLedger.Infrastructure.csproj --startup-project src/WealthLedger.Infrastructure/WealthLedger.Infrastructure.csproj --context WealthLedgerDbContext --no-build`
   It should print `No changes have been made to the model since the last migration.`

**Known flake, until it is fixed.** `CriticalJourneysTests` in the browser
project can fail with `Assert.Empty() Failure` at the
`Assert.Empty(consoleErrors)` line in `RunJourneyAsync`, with entries like
`Failed to load resource: net::ERR_CONNECTION_REFUSED`.

- These console errors are captured while the test restarts the host on purpose.
- Line numbers move, so open the line in the stack trace and check that it is
  this assertion. Any other line, including `Assert.Empty(requestFailures)`, is
  not this flake.
- That assertion also catches other console errors and uncaught page exceptions.
  It is the flake only if every visible entry starts with
  `Failed to load resource: net::ERR_CONNECTION`. Any other entry is a real
  failure, even at this line.
- xUnit truncates long entries and lists. If the list is cut off, say so in the
  report: the hidden entries are unproven.
- Every failed test in the project must match. If it is the flake, rerun the
  browser project once:
  `dotnet test tests/WealthLedger.UI.BrowserTests/WealthLedger.UI.BrowserTests.csproj --no-build --verbosity minimal`
- The rerun must pass completely; a second failure is a stop.
- Report both the flake and the rerun. Never present it as a clean pass.

## Report

Produce this block, ready to paste into a PR body:

```markdown
## Verification

Local run on Windows/.NET 10 (`/wl-verify`):

- Domain: N passed
- Application: N passed
- Infrastructure / real SQLite: N passed
- Operations: N passed
- UI: N passed
- API: N passed
- Playwright Chromium: N passed
- total: N passed, N failed, N skipped
- `dotnet format --verify-no-changes`: passed
- EF Core pending-model check: passed (`No changes have been made to the model since the last migration.`)
```
