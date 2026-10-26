@AGENTS.md

# Claude Code addendum

AGENTS.md above is the shared contract for every coding agent; Codex reads it
directly. This section adds only what is specific to Claude Code. If anything
here conflicts with AGENTS.md, AGENTS.md wins: report the conflict.

## Real household data: hard rules

- Never start the host or the Operations CLI with plain `dotnet run`. The
  gitignored `src/WealthLedger.Api/Properties/launchSettings.json` targets the
  owner's real backup destination, and without `Storage:DatabasePath` both
  programs resolve the real default database under `%LOCALAPPDATA%\WealthLedger`.
- Run either program only through `/safe-lab`: the PowerShell tool (in the
  background for the host), `--no-launch-profile`, and explicit synthetic
  `--Storage:DatabasePath=` and `--Backup:Directory=` inside a lab folder.
- Never start them through terminal tabs, preview servers, `Monitor`, or a
  script file you wrote.
- Real operations on household data (initialize, backup, migrate, restore,
  replace) are the owner's to run by hand. Do not run them.
- Search source with the Grep tool, `rg` or `git grep`. Do not use `grep -r`,
  `Select-String -Recurse` or `Get-ChildItem -Recurse | Get-Content` over `src`:
  they read the gitignored launch profile, which holds a private path.
- This repository is public. Fixtures, logs, screenshots, issue and PR text
  stay synthetic (AGENTS.md "Documentation discipline").

## The guard hook

`.claude/hooks/guard-real-data.ps1` checks the text of every shell, Monitor and
terminal call, statement by statement. It blocks:

- `dotnet run` without the safe flags, and `dotnet watch`;
- direct launches of the Api or Operations binaries;
- the real data folder and `launchSettings.json`;
- paths in the gitignored private list;
- force-pushes and pushes to `main`.

The check is textual: it is a guardrail, not a security boundary. Work with it,
not around it:

- To mention `dotnet run` in a commit message, write the message to a file and
  use `git commit -F`.
- Pass PR bodies with `gh pr create --body-file`.

Prerequisites: Git for Windows (Git Bash) and PowerShell 7 (`pwsh`) on `PATH`.
Without them the hook fails closed and blocks every shell call. If Claude Code
cannot find Git Bash, set `CLAUDE_CODE_GIT_BASH_PATH`.

## Commands (PowerShell, from the repo or worktree root)

- `DOTNET_CLI_UI_LANGUAGE=en` is set in `.claude/settings.json`, so `dotnet`
  prints English summaries on this tr-TR machine.
- New worktree: `dotnet tool restore`, then
  `dotnet restore WealthLedger.slnx --verbosity minimal`.
- Fast loop after each change: `/wl-verify fast`. For persistence work, add a
  test filter, for example `/wl-verify fast LedgerSearch`. That also runs the
  matching real-SQLite Infrastructure tests.
- Full gate before any push or PR: `/wl-verify`. It runs the same steps as
  `.github/workflows/verify.yml`.
- Focused tests: `--filter "FullyQualifiedName~<Namespace.Class>"`. The suite
  uses no `[Trait]`.
- Build locally before pushing. CI is the final gate, not the compiler.

## Git and GitHub

- Branches: planning `docs/mNNN-<slug>`, implementation `mNNN/<slug>`, other
  work `fix/`, `ci/` or `chore/`. One branch and one PR per child issue.
- Conventional Commits `type(scope): subject`, following the milestone's
  "Suggested commit boundaries". A milestone's first commit is
  `docs(mNNN): start ...`. Its last is `docs(state): record verified MNNN checkpoint`.
- PR title: `MNNN: <outcome>`, for example
  `M010: persist evidence and transaction links`.
- PR body: `Closes #N` / `Parent: #P`, then Scope, Deliberate boundaries and
  Verification (per-project test counts, format result, EF pending-model result).
- PRs merge with a merge commit, which keeps the branch history. Every commit
  must therefore be a clean, conventional, single-concern commit.
- Commit freely on a feature branch. Ask before `git push`, creating or merging
  a PR, and creating, editing or commenting on issues. Never push to `main` and
  never force-push.
- One agent per worktree. Do not share a working tree with another agent,
  Codex included.
- When a milestone becomes Verified, add its file to the `Edit(...)` ask list
  in `.claude/settings.json`, next to M002–M009.

## Claude Code tools in this repository

- `/milestone-implement <issue>`: one child issue of an Accepted milestone, end
  to end.
- `/wl-verify [fast [filter]]`: the fast loop or the full gate.
- `/safe-lab`: the only sanctioned way to run the host or the Operations CLI.
- `wl-reviewer` subagent: a fresh-context review of the branch diff against
  AGENTS.md before every PR. Pair it with `/code-review`. Add
  `/security-review` for attachments, imports and network providers.

## Session habits

- One issue or one planning task per session. Start in plan mode for anything
  bigger than a small fix.
- The conversation may be in Turkish. Everything committed is English.
- `docs/history/` is gitignored reference material. Never edit it.
