---
name: milestone-implement
description: Implement one GitHub child issue of an Accepted WealthLedger milestone end to end in the current branch or worktree. It reads the issue and milestone, plans for approval, implements in the milestone's commit boundaries, verifies, reviews and drafts the PR. Use when the owner asks to work on an issue such as "#22".
argument-hint: "<issue-number>"
disable-model-invocation: true
allowed-tools: Bash(git branch --show-current) Bash(git status *) Bash(git log *)
---

# Implement issue #$ARGUMENTS

Follow AGENTS.md ("Start every task this way") and the "Agent start prompt" in
`docs/milestones/README.md`. This skill adds only the Claude Code mechanics.

Live state:

- Branch: !`git branch --show-current`
- Working tree: !`git status --short`
- Recent commits: !`git log --oneline -5`

## 1. Understand (read-only; fine in plan mode)

1. Run `gh issue view $ARGUMENTS --comments`. Note its `Parent:` and
   `Depends on:` lines. Then run `gh issue view <parent>` for the slice order.
   If a dependency is still open and its code is not on `main`, stop and report.
2. Read `docs/PROJECT_STATE.md`. Then read only the milestone sections, ADRs and
   docs the issue cites. CLAUDE.md has already loaded AGENTS.md.
3. Check the milestone status (AGENTS.md "Delivery and milestone workflow"):
   - **Proposed, Draft or Verified:** stop.
   - **Accepted, with no `docs(mNNN): start ...` commit on `main`:** report it.
     With the owner's approval, the first commit on the branch becomes that
     start commit. It sets the milestone and its ROADMAP row to In Progress and
     updates PROJECT_STATE's active-delivery status. The precedent is
     `7dd2133 docs(m009): start physical-gold lifecycle implementation`.
   - **Otherwise:** PROJECT_STATE changes only in the verified-checkpoint
     commit.
4. Inspect the real source, tests and migrations the issue touches. Compare them
   with the milestone and PROJECT_STATE. Report any material conflict before
   planning (AGENTS.md "Authority and conflict handling").

## 2. Plan, then wait for approval

Present a plan that covers:

- the files to change;
- the tests: focused unit tests for every financial invariant, and real-SQLite
  integration tests for persistence;
- any migration: the next free number (milestone and migration numbers are
  independent);
- the commits: take them from the milestone's "Suggested commit boundaries",
  not from a fixed layer order.

Wait for the owner's approval before changing anything.

## 3. Branch and baseline

1. Work on `mNNN/<slug>`, cut from an up-to-date `origin/main`, for example
   `m010/evidence-attachments`. If the session started on a Claude worktree
   branch such as `worktree-*`, rename it with `git branch -m mNNN/<slug>`.
2. In a new worktree, run `dotnet tool restore` and
   `dotnet restore WealthLedger.slnx --verbosity minimal`.
3. Run `/wl-verify fast <filter for the area>` before changing behavior. Record
   any failures that already exist; do not quietly fix unrelated ones.

## 4. Implement

- Work one commit boundary at a time. After each one, run
  `/wl-verify fast <filter>`, then commit as `type(scope): subject`: English,
  small, one concern. PRs merge with a merge commit, so every commit stays in
  `main`'s history.
- Stay inside the issue's Required behavior and boundaries. Note anything out of
  scope for a later issue instead of doing it.
- Never run plain `dotnet run`. Use `/safe-lab` for manual and recovery checks.
  Storage changes need backup and restore verification (Definition of done).
- Use synthetic data only, in tests, fixtures, logs and screenshots.

## 5. Verify and review

1. Run `/wl-verify` (the full gate) and keep its report.
2. Run the `wl-reviewer` subagent on the branch diff against `origin/main`.
3. Run `/code-review` on the branch. Add `/security-review` when attachments,
   imports or network access are involved.
4. Fix every real finding, or explain why it stands.
5. If anything changed, run `/wl-verify` again.

## 6. Hand off

1. Write the PR body to a scratch file outside the repository. It starts with
   `Closes #$ARGUMENTS` and `Parent: #<parent>`, then:
   - `## Scope`
   - `## Deliberate boundaries`
   - `## Verification`, from `/wl-verify`
   - `## Defects found and fixed along the way`, when it applies
2. The PR title is `MNNN: <outcome>`, for example
   `M010: persist evidence and transaction links`.
3. Show the draft. Do not push or create the PR until the owner approves; both
   commands ask for permission. Then run
   `gh pr create --title "<title>" --body-file <file>`.
4. If this was the milestone's last issue, propose the checkpoint commit
   `docs(state): record verified MNNN checkpoint` on its own branch once the
   owner has merged.
