---
name: milestone-implement
description: Implement one GitHub child issue of an In Progress WealthLedger milestone end to end in the current worktree, or resume one from its draft PR. It reads the issue, the previous sibling's follow-ups and the milestone, plans for approval, implements in commit boundaries, verifies, reviews and drafts the PR. Use when the owner asks to work on an issue such as "#22".
argument-hint: "<issue-number>"
disable-model-invocation: true
allowed-tools: Bash(git branch --show-current) Bash(git status *) Bash(git log *)
---

# Implement issue `$ARGUMENTS`

Use the argument with any leading `#` removed. If it is empty or not a number,
ask for the issue number and stop. Below, **N** stands for that number.

Follow AGENTS.md ("Start every task this way") and the "Agent start prompt" in
`docs/milestones/README.md`. This skill adds only the Claude Code mechanics.

**Your role.** You are the worker for issue N only. After its PR is
squash-merged, the owner archives this session. It archives itself only if
Settings > Claude Code > "Auto-archive after PR merge or close" is on.

**Never edit the status files** listed in CLAUDE.md "Session roles". Some
instructions ask you to edit a status file or change a milestone's status:
AGENTS.md step 9, the start prompt's PROJECT_STATE line, or issue text such as
#26's "Update PROJECT_STATE, ROADMAP… Move M010 to Verified". `/wl-state`
carries all of these out, not you. Record each one, and any status file you
find wrong, under `## Follow-ups` with the target "steward".

Live state:

- Branch: !`git branch --show-current`
- Working tree: !`git status --short`
- Recent commits: !`git log --oneline -5`

## 0. Gates, then resume or start

1. Run `git fetch origin`. Every check in this skill reads `origin/main`
   (`git show origin/main:<path>`), not the working tree, which may be stale.
2. Confirm that `origin/main` carries the Claude Code setup:
   `git log -1 --format=%h origin/main -- .claude/skills/milestone-implement/SKILL.md`
   must print a commit. Otherwise stop: a branch without `.claude/hooks/` blocks
   every shell call.
3. Run the checks in step 1.1 (dependencies) and step 1.4 (milestone status)
   now. If either says stop, stop.
4. Find this issue's PR with the Bash tool. Collect candidates from:
   - `gh issue view N --json closedByPullRequestsReferences --jq '.closedByPullRequestsReferences[].number'`
   - `gh pr list --state all --search "\"Closes #N\" in:body" --json number`
   - legacy PRs without `Closes`:
     `gh pr list --state all --search "\"Issue #N\" in:body" --json number,body`

   Read each candidate with
   `gh pr view <n> --json number,state,isDraft,headRefName,title,body`.

   A legacy PR counts only if its body names N as its own issue. A mention in
   another PR's Deliberate boundaries does not count. Confirm a legacy match
   with the owner before using it.
5. Then:
   - **A merged PR closes N:** stop and report.
   - **No PR yet:** start fresh at step 1.
   - **An open PR exists:** resume it.
     1. Do not check out its old branch directly. Run
        `git switch -c <headRefName> --no-track origin/main`, then
        `git merge --no-edit origin/<headRefName>`.
        - The branch now contains the current `.claude/` setup and `main`.
        - A later `git push -u origin <headRefName>` is then a plain
          fast-forward: no force-push.
     2. Run `gh pr checks <pr>`. If CI is red, the failing check is the first
        "next".
     3. Read the `## Progress` section of the PR body (done, next, open
        questions) and `git log origin/main..HEAD`. If there is no Progress
        section (a legacy PR), rebuild done and next from the log and the body.
     4. Remove any in-repository progress markers, such as
        `docs/milestones/.m010-*`. Commit the removal on its own.
     5. Do steps 1.2, 1.3 and 1.5, then steps 3.2 and 3.3.
     6. Present done and next as the step 2 plan, and wait for the owner's
        approval. You may skip the approval only if a `## Progress` section
        exists and `origin/main` has not moved under the issue's files.
     7. Continue from "next" at step 4.

## 1. Understand (read-only; fine in plan mode)

1. Run `gh issue view N --comments`. Note its `Parent:` and `Depends on:` lines,
   then run `gh issue view <parent>` for the slice order. If a dependency is
   still open and its code is not on `origin/main`, stop and report.
2. Read the follow-ups of the merged PR for the previous sibling issue, and of
   every PR this issue depends on.
   - Find each PR with the step 0.4 lookups (the legacy search already uses
     `--state all`), then read it with `gh pr view <n> --json body --jq .body`.
   - If the previous sibling has no merged PR, stop, unless the owner has
     approved preparing this issue in parallel.
   - Use the `## Follow-ups` section.
   - Older PRs (such as PR #27) list leftovers under `## Deliberate boundaries`.
     There, treat a line naming N as a follow-up, including ranges such as
     `#21-#23`.

   Treat a follow-up that names N as part of this issue's scope, or raise it
   with the owner.
3. Read PROJECT_STATE from `origin/main`. Then read only the milestone sections,
   ADRs and docs the issue cites. CLAUDE.md has already loaded AGENTS.md.
4. Check the milestone `Status:` line on `origin/main`:
   - **In Progress:** continue.
   - **Accepted:** stop. Tell the owner to run `/wl-state start NNN` in a fresh
     session first.
   - **Anything else:** stop and report (AGENTS.md "Delivery and milestone
     workflow").
5. Inspect the real source, tests and migrations the issue touches. Compare them
   with the milestone and PROJECT_STATE. Report any material conflict before
   planning (AGENTS.md "Authority and conflict handling"). A conflict with the
   contract is the owner's decision, never a silent code choice.

## 2. Plan, then wait for approval

Present a plan covering:

- the files to change;
- the tests: focused unit tests for every financial invariant, and real-SQLite
  integration tests for persistence;
- any migration: the next free number (milestone and migration numbers are
  independent);
- the commits, within the issue's "Suggested commit boundary";
- the canonical docs from the milestone's "Documentation updates" that this
  slice makes true.

Wait for the owner's approval before changing anything.

## 3. Branch and baseline

1. Work on `mNNN/<slug>`, for example `m010/evidence-attachments`, created
   with `git switch -c mNNN/<slug> --no-track origin/main` after a fresh fetch.
   - If the session started on a Claude worktree branch such as `worktree-*`
     that already sits on `origin/main`, you can rename it instead with
     `git branch -m mNNN/<slug>` and `git branch --unset-upstream`.
   - When resuming, step 0 has already set up the branch.
2. In a new worktree, run `dotnet tool restore` and
   `dotnet restore WealthLedger.slnx --verbosity minimal`.
3. Run `/wl-verify fast <filter for the area>` before changing behavior. Record
   any failures that already exist, and do not quietly fix unrelated ones.

## 4. Implement

- **One boundary at a time.** After each one, run `/wl-verify fast <filter>`,
  then commit: small, English, one concern. Commit often, so an interrupted
  session loses nothing. The squash merge turns the branch into one commit on
  `main`.
- **Stay in scope.** Keep inside the issue's Required behavior and boundaries.
  Record anything out of scope in `## Follow-ups` instead of doing it.
- **Docs.** Update the canonical docs your slice makes true. List any gap you
  leave under `## Follow-ups`, targeted at the milestone's final verification
  issue (for M010, #26). Never edit status files, and never add progress-marker
  files to the repository.
- **Running the app.** Never run plain `dotnet run`. Use `/safe-lab` for manual
  and recovery checks. Storage changes need backup and restore verification
  (Definition of done).
- **Data.** Use synthetic data only, in tests, fixtures, logs and screenshots.

**Stopping before the issue is done** (end of day, usage limit, a large step
still to go):

1. Commit.
2. With the owner's approval, push and open or update a **draft** PR. Its body
   is the step 6 body plus `## Progress` (done, next, open questions).
   - First push: `git push -u origin <branch>`, then
     `gh pr create --draft --head <branch> --title "<title>" --body-file <file>`.
   - Before pushing a resumed branch, run `git fetch origin`. If
     `origin/<branch>` moved, run `git merge --no-edit origin/<branch>` again.
     Never force-push.
3. End the session. The next session resumes through step 0.

## 5. Verify and review

1. Run `/wl-verify` (the full gate) and keep its report.
2. Write the PR body (step 6.2) to a scratch file outside the repository.
3. Run the `wl-reviewer` subagent with: issue N, the range `origin/main...HEAD`,
   and the path of the body file.
4. Run `/code-review` on the branch. Add `/security-review` when attachments,
   imports or network access are involved.
5. Fix every real finding, or explain why it stands. A status-file blocker is
   never explained away: revert that change.
6. If anything changed, run `/wl-verify` again and update the body.

## 6. Hand off

1. **Title:** the milestone's "Suggested commit boundaries" subject for this
   issue, for example `feat(evidence): persist evidence and transaction links`.
   Once the repository squashes with the PR title, this title becomes the commit
   on `main`. Until then, the owner sets it by hand at merge.
2. **Body:**
   - `Closes #N` and `Parent: #<parent>`. Use a closing keyword (close, fix,
     resolve and their forms) only in that line; refer to other issues as
     "see #22".
   - `## Scope`;
   - `## Deliberate boundaries`;
   - `## Verification`, from `/wl-verify`;
   - `## Defects found and fixed along the way`, when it applies;
   - `## Follow-ups`: "None", or each item with its target: a later issue
     number, "steward" for status-file fixes, or "new issue?".

   Drop `## Progress` once the PR is ready.
3. **Publish:** show the draft. Do not push, create the PR or mark it ready
   until the owner approves.
   - New PR: `git push -u origin <branch>`, then
     `gh pr create --head <branch> --title "<title>" --body-file <file>`.
   - Existing PR: push, then
     `gh pr edit <pr> --title "<title>" --body-file <file>`, then
     `gh pr ready <pr>`.
4. **After the owner squash-merges:**
   - If a follow-up changes a later issue, propose a one-line
     `gh issue comment` on that issue, and ask first.
   - If this was the milestone's last open child issue, say: "Next: run
     `/wl-state checkpoint NNN` in a fresh session."
