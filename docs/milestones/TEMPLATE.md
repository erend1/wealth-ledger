# MNNN: Outcome Name

Status: Draft

Owner: Human and agent

Last reviewed: YYYY-MM-DD

## User outcome

Describe what becomes possible for the user after this milestone. State an
observable outcome, not a framework or layer.

## Current evidence

Reference the current source, tests, migration, API behavior, and
`PROJECT_STATE.md` facts that make this change necessary.

## Why now

Explain why this is the next bounded change and which later work depends on it.

## Decisions and decision gates

List every unresolved choice that could materially change implementation. Give
a recommended default and require explicit acceptance before changing status
to Accepted.

## In scope

- List concrete behavior included in this milestone.

## Out of scope

- List adjacent behavior that must not be implemented in this milestone.

## Required behavior

Describe normal, invalid, retry, failure, restart, and concurrency behavior as
applicable.

## Invariants

- List Domain, Application, persistence, privacy, and operational rules that
  the implementation must preserve.

## API or UI contract

Define the externally visible contract or explicitly state that none changes.
Use human-formatted values at UI boundaries and stable explicit transport
representations at API boundaries.

## Persistence impact

Describe expected schema, migration, transaction, index, backup, and
compatibility consequences. State `None` when the milestone is persistence
neutral.

## Acceptance criteria

- Write independently verifiable outcomes.

## Test scenarios

### Domain

- Add only when Domain behavior changes.

### Application

- Cover orchestration and cross-aggregate rules.

### Infrastructure

- Use real SQLite for mappings, transactions, constraints, and restart behavior.

### API or UI

- Cover transport, interaction, errors, and user-visible behavior.

## Verification commands

```powershell
dotnet test WealthLedger.slnx --no-restore --verbosity minimal
dotnet format WealthLedger.slnx --verify-no-changes --no-restore --verbosity minimal
dotnet ef migrations has-pending-model-changes --project src/WealthLedger.Infrastructure/WealthLedger.Infrastructure.csproj --startup-project src/WealthLedger.Infrastructure/WealthLedger.Infrastructure.csproj --context WealthLedgerDbContext --no-build
```

Remove a command only when it is genuinely irrelevant and explain why.

## Documentation updates

- Name every canonical document, milestone status, ADR index, API example, or
  operations guide that must change after verification.

## Suggested commit boundaries

```text
feat(scope): describe one coherent behavior
test(scope): prove the behavior or regression
docs(state): record the verified checkpoint
```

## Risks and rollback

Describe data-loss, migration, compatibility, privacy, and behavioral risks and
the recoverable rollback path.

