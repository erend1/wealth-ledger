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

- Branches:
  - planning and steward events: `docs/mNNN-<slug>`;
  - state repairs: `docs/state-repair-<slug>`;
  - implementation: `mNNN/<slug>`;
  - other work: `fix/`, `ci/` or `chore/`.

  One branch and one PR per child issue. A milestone boundary that is split
  becomes a separate issue and PR.
- Merge policy: squash-merge only, with the PR title as the commit subject on
  `main`.
  - The repository settings enforce this: squash is the only merge method, the
    squash title is the PR title, the squash message is blank, and merged
    branches are deleted.
  - A child issue's PR title is its milestone's conventional "Suggested commit
    boundaries" subject, for example
    `feat(evidence): persist evidence and transaction links`.
  - Other PRs use a conventional subject, such as `fix(browser): ...`,
    `chore(claude): ...` or `docs(state): ...`.
  - A milestone opens with a `docs(mNNN): start ...` PR and closes with
    `docs(state): record verified MNNN checkpoint`.
- Branch commits stay small, English and conventional, as
  `docs/milestones/README.md` asks. Commit at every boundary, so an interrupted
  session loses nothing.
- Branches cut from `origin/main` use `git switch -c <branch> --no-track origin/main`.
  - First push: `git push -u origin <branch>`.
  - Create PRs with `gh pr create --head <branch>`.
- Use a closing keyword (close, fix, resolve and their forms) only in the PR's
  `Closes #N` line. Refer to other issues as "see #22".
- PR body:
  - `Closes #N` and `Parent: #P`;
  - `## Scope`, `## Deliberate boundaries` and `## Verification` (per-project
    test counts, format result, EF pending-model result);
  - `## Follow-ups`: "None", or each item with its target (a later issue, the
    steward, or a proposed new issue);
  - `## Progress` (done, next, open questions) while the PR is a draft.
- Commit freely on a feature branch. Ask before `git push`, creating or merging
  a PR, and creating, editing or commenting on issues. Never push to `main` and
  never force-push.
- One agent per worktree. Do not share a working tree with another agent,
  Codex included.

## Session roles

Sessions are disposable; GitHub and the repository are the only memory. A brief
is an issue number, a `/wl-state` mode, or the planning-only prompt, never a
pasted role prompt. Project status never goes into auto memory.

**Status files.** This is the one canonical list; skills and agents refer to it.

- `docs/PROJECT_STATE.md`
- in `docs/ROADMAP.md`: the status table, "Current verified milestone and next
  candidate", the "Real-data readiness gate" verification clauses, and the
  "Decision gates" rows
- milestone `Status:` and `Last reviewed:` lines, and their acceptance,
  implementation-gate and verification records
- the README sentence "The verified MNNN checkpoint contains N passing tests",
  until a repair replaces it with a pointer to PROJECT_STATE
- `CHANGELOG.md`
- the Verified-milestone and active-milestone `Edit(...)` ask entries in
  `.claude/settings.json`

**Roles.**

- **Owner (human):**
  - picks the next issue;
  - approves plans, pushes, PRs and merges;
  - accepts milestones;
  - runs everything that touches real data.
- **Worker:** one child issue = one live session at a time = one worktree =
  one PR, through `/milestone-implement`.
  - It never edits status files. Any instruction to do so, in AGENTS.md, the
    start prompt or the issue text, is carried out by `/wl-state`.
  - It updates the canonical docs its slice makes true, from the milestone's
    "Documentation updates" list. Any gap it leaves goes under `## Follow-ups`,
    targeted at the milestone's final verification issue (for M010, #26),
    which reconciles them.
  - After the squash merge, archive the session. It archives itself only if
    Settings > Claude Code > "Auto-archive after PR merge or close" is on.
- **Steward:** one short, fresh session per delivery event, through
  `/wl-state audit | start NNN | checkpoint NNN | repair <slug>`. Run it in
  default (Manual) mode, so that ask-gated status edits prompt instead of being
  denied. It is the only writer of status files, with one exception: the
  planner's acceptance writes.
- **Planner:** one session per new milestone contract.
  - It writes the contract from `docs/milestones/TEMPLATE.md` and "Required
    milestone structure", then reviews it with the planning-only prompt in
    `docs/milestones/README.md`.
  - In owner-approved `docs(mNNN): propose | accept | amend` PRs it may set
    `Status: Proposed` or `Accepted` and `Last reviewed:`, and write the
    acceptance record.
  - In the same PRs it may add or update that milestone's ROADMAP row, its
    paragraph in "Current verified milestone and next candidate", its
    readiness-gate clause and its "Decision gates" row.
- **At most two lanes at once:** one code lane (a worker) and one docs lane (a
  steward or a planner). Never two workers. A parent issue's "may be prepared"
  allowance needs the owner's approval.
- Run `git fetch` before starting a worktree session; a new worktree can
  otherwise start from a stale `origin/main`.
- Unfinished work: commit, then, with the owner's approval, push a draft PR
  whose body carries `## Progress`. Resume from it in a fresh session; do not
  keep a long thread open.

## Claude Code tools in this repository

- `/milestone-implement <issue>`: one child issue of an In Progress milestone,
  end to end, or resumed from its draft PR.
- `/wl-state audit | start NNN | checkpoint NNN | repair <slug>`: the steward's
  drift audit, and the sanctioned way to change status files (besides the
  planner's acceptance writes).
- `/wl-verify [fast [filter]]`: the fast loop or the full gate.
- `/safe-lab`: the only sanctioned way to run the host or the Operations CLI.
- `wl-reviewer` subagent: a fresh-context review of a branch diff against
  AGENTS.md before every PR.
  - Its file tools are removed, but it keeps Bash and PowerShell for `git` and
    `gh`, so it is read-only by instruction, not by enforcement.
  - Pair it with `/code-review`.
  - Add `/security-review` for attachments, imports and network providers.

## Session habits

- One issue, one state event or one planning task per session. Start in plan
  mode for anything bigger than a small fix.
- The conversation may be in Turkish. Everything committed is English.
- `docs/history/` is gitignored reference material. Never edit it.
