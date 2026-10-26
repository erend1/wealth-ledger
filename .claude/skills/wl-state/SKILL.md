---
name: wl-state
description: Steward for WealthLedger delivery state, run in a short fresh session per event. "audit" compares every status claim with git, GitHub and the code, and prints a discrepancy table without editing anything. "start NNN" writes a milestone's start PR. "checkpoint NNN" runs the full gate and writes the verified checkpoint. "repair <slug>" applies owner-approved audit fixes. This is the sanctioned way to change status files (besides the planner's acceptance writes).
argument-hint: "audit | start <NNN> | checkpoint <NNN> | repair <slug>"
disable-model-invocation: true
effort: high
allowed-tools: Bash(git branch --show-current) Bash(git status *) Bash(git log *)
---

# WealthLedger state steward: `$ARGUMENTS`

The first word of the arguments is the mode: `audit`, `start`, `checkpoint` or
`repair`.

- `start` and `checkpoint` need a three-digit milestone number.
- `repair` needs a short slug.
- For anything else, print
  `usage: /wl-state audit | start <NNN> | checkpoint <NNN> | repair <slug>` and
  stop.

Live state:

- Branch: !`git branch --show-current`
- Working tree: !`git status --short`
- Last fetched `origin/main`: !`git log --oneline -1 origin/main || echo "origin/main not present"`

## Ground rules

**What this skill writes.** It writes only the status files listed in
CLAUDE.md "Session roles", and only on its own branch, in one of three commits:

- start: `docs/mNNN-start`
- checkpoint: `docs/mNNN-checkpoint`
- an owner-approved repair: `docs/state-repair-<slug>`

**Preconditions for every writing mode.** Refuse unless both hold:

- `origin/main` already carries the Claude Code setup:
  `git log -1 --format=%h origin/main -- .claude/skills/wl-state/SKILL.md`
  prints a commit. A branch cut without `.claude/hooks/` blocks every shell call.
- The working tree is clean. If it is not, run in a fresh worktree.

**It never writes:**

- `src/`, `tests/` or migrations;
- the canonical docs (UX_MVP, DATA_CAPTURE, DATABASE_DESIGN,
  SECURITY_OPERATIONS, OPERATIONS, ARCHITECTURE, DOMAIN_LEDGER). Workers own
  them: each slice updates what it makes true, or lists the gap under
  `## Follow-ups` for the milestone's final verification issue (for M010,
  #26). If they disagree with the code, propose a worker issue.
- another milestone's body, a Verified milestone's body, or an accepted ADR;
- `docs/history/`;
- real data.

**Contract changes are not state fixes.** A change to a milestone's scope or
decisions goes through an owner-approved `docs(mNNN): amend decision N` PR.

**Never fix anything on a steward branch.** Any gate failure, including
formatting, is a stop.

**When delivery facts disagree, trust them in this order:**

1. code and tests;
2. GitHub (issues, merged PRs, CI runs);
3. git history;
4. the docs.

This is consistent with AGENTS.md "Authority and conflict handling" item 2.
Never trust a status file or auto memory over evidence.

**Mode and approvals.** Run in default (Manual) mode, so that ask-gated status
edits prompt you. Ask before every push, PR, issue write or comment.

**Commands.** Run `gh` searches with the Bash tool. The quoted search strings
below use Bash quoting.

## 1. Gather facts (every mode; read-only)

1. Run `git fetch origin` and read everything from `origin/main`
   (`git show origin/main:<path>`), not from the local checkout.
2. Collect:
   1. The `Status:` and `Last reviewed:` lines of every `docs/milestones/M*.md`.
   2. From ROADMAP:
      - every row of the status table;
      - "Current verified milestone and next candidate";
      - the "Real-data readiness gate" clauses;
      - the "Decision gates" rows.
   3. From PROJECT_STATE:
      - `As of`;
      - `Verified against: <sha>` (it may not exist yet; report that);
      - "## Current checkpoint";
      - the milestone paragraph in "## Delivery planning";
      - "## Next delivery candidate" and "## Open decisions";
      - the migration list: under "### Core persistence", or under
        "## Migrations" after the first repair;
      - the per-project test counts;
      - the line count.
   4. The README sentence "The verified MNNN checkpoint contains N passing
      tests: …", including its milestone, total and per-project counts.
   5. The migration files in
      `src/WealthLedger.Infrastructure/Persistence/Migrations/` (ignore
      `*.Designer.cs`, `*.Sql.cs` and `*ModelSnapshot.cs`), against
      PROJECT_STATE's list.
   6. The ADR files in `docs/decisions/`, against the index in
      `docs/decisions/README.md`.
   7. For the active milestone:
      - **Parent issue:** the `[MNNN]` issue with no `Parent:` line, found with
        `gh issue list --state all --search "\"[MNNN]\" in:title" --json number,title,body --jq '[.[] | select(.body | test("(?m)^Parent:") | not) | .number]'`.
        For M010 this prints `[19]`. Record the parent in the start commit.
      - **Child issues:**
        `gh issue list --state all --search "\"Parent: #<parent>\" in:body" --json number,state,title`.
      - **The PR for each child.** Collect candidates from three lookups:
        - `gh issue view <child> --json closedByPullRequestsReferences --jq '.closedByPullRequestsReferences[].number'`;
        - `gh pr list --state all --search "\"Closes #<child>\" in:body" --json number`;
        - for legacy PRs without `Closes` (draft PR #28 is one),
          `gh pr list --state all --search "\"Issue #<child>\" in:body" --json number,body`.

        Then read each candidate's state with
        `gh pr view <n> --json number,state,isDraft,mergedAt,headRefName,title`.
        A legacy PR counts only if its body names the child as its own issue,
        as in "Implements GitHub Issue #21". A mention under Deliberate
        boundaries does not count. Report legacy matches as needing
        confirmation.
   8. Each merged child PR's `## Follow-ups`. PRs that predate the section
      (such as PR #27) list leftovers under `## Deliberate boundaries`; treat
      lines that name an issue, including ranges such as `#21-#23`, as
      follow-ups.
   9. The `Edit(/docs/milestones/...)` ask entries in `.claude/settings.json`,
      against the Verified milestones and the In Progress milestone.
   10. Facts hard-coded in skills, and whether they still hold:
       - the "Known flake" block in `wl-verify` (check
         `gh run list --workflow Verify --limit 10` and the failing runs);
       - the Playwright pin.
   11. The notes in this project's auto-memory directory (the path is in your
       context), checked for any project status they record.

## 2. The invariants

Print one row per check: `check | file:line | claim | evidence | single home | fix`.
Mark each row `OK` or `DRIFT`.

- **One active milestone.** At most one milestone is In Progress.
- **Status agrees everywhere.** For every milestone, the `Status:` line, its
  ROADMAP row and prose, and PROJECT_STATE's wording all agree.
- **No missing start commit.** A milestone with merged implementation PRs is In
  Progress or Verified, never still Accepted.
- **Checkpoint anchor is current.** PROJECT_STATE's `Verified against` SHA is an
  ancestor of `origin/main`. Check with
  `git merge-base --is-ancestor <sha> origin/main`.
- **Issues and PRs match.** Merged child PRs and closed child issues match
  exactly, and no open child issue has a merged PR.
- **README count.** The README checkpoint sentence names the same milestone and
  counts as PROJECT_STATE's checkpoint, or it has been replaced by a pointer.
- **Migrations and ADRs.** The migration list matches the folder, and the ADR
  index matches the files.
- **Ask entries.** Every Verified milestone and the In Progress milestone have an
  `Edit(...)` ask entry.
- **Size budget.** PROJECT_STATE is at most 200 lines. This skill sets that
  budget to keep it a concise handoff, as AGENTS.md "Documentation discipline"
  asks.
- **Follow-ups.** No follow-up from a merged PR is left unassigned.
- **Memory.** No auto-memory note records project status.

## 3. `audit`

Stop after the table. Then:

- recommend the next event: `start`, `checkpoint`, `repair <slug>`, or nothing;
- list which fixes need an owner decision.

Edit nothing. If the owner approves fixes in this same session, continue as
`repair` (section 6).

## 4. `start NNN`

**Refuse unless all of these hold:**

- Milestone NNN is Accepted and has an acceptance record.
- No other milestone is In Progress.
- The previous milestone in ROADMAP order is Verified, or the owner explicitly
  authorizes the overlap.

**Then:**

1. Create the branch:
   `git switch -c docs/mNNN-start --no-track origin/main`.
2. Make one commit. Its subject is the milestone's first "Suggested commit
   boundaries" entry, for example
   `docs(m010): start accepted searchable inventory and reconciliation work`.
   It changes:
   - **The milestone file:**
     - set `Status:` to In Progress and update `Last reviewed:`;
     - add `Implementation started: YYYY-MM-DD` below `Accepted:`;
     - add an implementation-gate note naming the parent issue and any child
       already merged, as a fact with its PR number ("#20 merged via PR #27").
   - **ROADMAP:**
     - the row becomes In Progress, and its gate column records the start date
       and any children already merged;
     - the "Current verified milestone and next candidate" prose, any
       "Real-data readiness gate" clause and the "Decision gates" rows that
       name this milestone's status change to match.
   - **PROJECT_STATE:**
     - the milestone paragraph in "## Delivery planning" says the milestone is
       In Progress (add the paragraph if none exists);
     - so does "## Next delivery candidate";
     - remove any stale "Planned" or "must be accepted" wording.
   - **`.claude/settings.json`:** add `Edit(/docs/milestones/MNNN_*.md)` to the
     ask list if it is missing, so workers cannot quietly edit the active
     contract.

   The precedent is `7dd2133 docs(m009): start physical-gold lifecycle implementation`.
3. Draft the PR:
   - title: the commit subject;
   - body: the audit table plus `## Follow-ups`.
4. Ask before pushing and before creating the PR. Then push with
   `git push -u origin docs/mNNN-start`, and create the PR with
   `gh pr create --head docs/mNNN-start --title "<subject>" --body-file <file>`.

## 5. `checkpoint NNN`

**Refuse unless:**

- milestone NNN is In Progress;
- every child issue of its parent is closed by a merged PR;
- PROJECT_STATE is within its 200-line budget. If it is not, recommend the
  condensing repair (section 6) first.

**Then:**

1. Create `docs/mNNN-checkpoint` with
   `git switch -c docs/mNNN-checkpoint --no-track origin/main`, then run the
   full `/wl-verify` gate.
   - Every count comes from this run, never from a PR body or a document.
   - Record the `origin/main` SHA it ran on.
   - The one allowed exception is `wl-verify`'s known-flake rerun; record both
     runs. Any other failure is a stop; report it.
2. Map every item under the milestone's `## Acceptance criteria` to evidence: a
   test name, a PR, a doc section, or a recovery step done with `/safe-lab`.
   - Also check every Definition-of-done item in `docs/milestones/README.md`,
     and that the canonical docs agree with the code.
   - If anything is unproven or disagrees, stop. Report it, and propose a
     worker issue where code or canonical docs must change. Do not mark the
     milestone Verified.
3. Make one commit, `docs(state): record verified MNNN checkpoint`. It changes:
   - **PROJECT_STATE:**
     - `As of` set to today;
     - `Verified against: <sha>`;
     - "## Current checkpoint" with the per-project counts;
     - the migration list (under "## Migrations" after the first repair);
     - "## Delivery planning", "## Next delivery candidate" and
       "## Open decisions".

     Keep it within 200 lines; move detail that is now history to `CHANGELOG.md`.
   - **ROADMAP:** the row becomes Verified (with the date). The prose
     sections, the readiness-gate clauses and the "Decision gates" rows change
     to match.
   - **The milestone file:** `Status: Verified`, plus a verification record with
     the date, SHA and counts.
   - **README:** the checkpoint sentence, if it has not yet been replaced by a
     pointer.
   - **`CHANGELOG.md`:** add one section. If the file is empty, start it with
     `# Changelog`. Each section is
     `## MNNN: <title>, verified YYYY-MM-DD at <sha>`, followed by a short
     paragraph.
   - **`.claude/settings.json`:** confirm the milestone's
     `Edit(/docs/milestones/MNNN_*.md)` ask entry is present.
4. Sweep the follow-ups of every PR in the milestone. Each one becomes one of:
   - a proposed new issue;
   - a proposed "Current evidence" line, handed to the planner of the next
     milestone;
   - dropped, with a reason.

   Show the list. Create issues only after the owner approves.
5. Propose which auto-memory notes to delete.
6. Ask before pushing and before creating the PR. The PR title is the commit
   subject. Push with `git push -u origin docs/mNNN-checkpoint`, and create
   the PR with `gh pr create --head docs/mNNN-checkpoint`.

## 6. `repair <slug>`

1. Re-run section 1. Apply only the fixes the owner approved from an audit.
2. Make one `docs(state): repair <what>` commit, on a branch created with
   `git switch -c docs/state-repair-<slug> --no-track origin/main`.
3. In the PR, map each change to its evidence.
4. Ask before pushing and before creating the PR (`gh pr create --head <branch>`).

The first repair is likely to condense PROJECT_STATE into a concise handoff:

- **Keep:** the current checkpoint, delivery planning, next candidate and open
  decisions. Move the migration list under its own "## Migrations" heading.
- **Move:** the "Verified implementation" and "Verification" history into
  `CHANGELOG.md`.
  - Copy it verbatim, under `## MNNN: <title>` headings, one per milestone.
  - Add "verified YYYY-MM-DD at <sha>" only where the SHA is known.
  - New checkpoint sections (section 5) stay short paragraphs.
- **Add:** a `Verified against` line.
- **README:** replace the checkpoint sentence with a pointer to PROJECT_STATE.

## Output

Every mode ends with the table, then one line naming the next recommended
event.
