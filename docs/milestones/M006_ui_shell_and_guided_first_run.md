# M006: Local UI Shell and Guided First Run

Status: Proposed

Owner: Human and agent

Last reviewed: 2026-08-31

## User outcome

The household can open WealthLedger in an ordinary browser on the local
computer, understand whether its protected data store is ready, complete the
one-time core setup without issuing raw HTTP requests or constructing GUIDs,
create and verify the first backup, and then browse recent recorded activity
and current master data through a clear Turkish-first application shell.

Recorded monetary amounts, fund units, prices, dates, states, and transaction
effects are presented in human notation without losing their exact persisted
meaning. The UI never becomes a second accounting engine and never turns a
missing or unknown value into zero.

M006 establishes the presentation and hosting boundary needed by later entry
workflows. It does not yet let the household import opening positions or post,
reverse, or correct investment activity. Those write outcomes remain in their
ordered milestones.

## Current evidence

This proposal was originally prepared on the stacked planning branch at commit
`f58ebe6` on 2026-08-31, when M003 was Accepted but absent from that branch and
M004 and M005 were planning documents only. That planning baseline had 163
passing tests and is now obsolete.

It was reconciled against the verified checkpoint on 2026-09-03 at commit
`5ef3363`, where M003, M004, and M005 are all Verified. That baseline has 417
passing tests:

- Domain: 83;
- Application: 95;
- Infrastructure: 145;
- API: 71;
- Operations: 23.

The reconciliation confirmed that M005 navigation contracts, M003 transaction
readback, and the M004 operations surface match what this proposal assumed,
with two exceptions that required decisions rather than adaptation: the
readiness gate recorded in the Decision 4 amendment, and the setup-mode
ownership boundary recorded in Decision 4's hosting rules.

The EF Core model-drift check passes. The formatting check has the pre-existing
`LedgerTransaction.cs` whitespace discrepancy already recorded by the M004
planning audit and repeated in `PROJECT_STATE.md`; M006 neither introduces nor
hides it.

Repository and SDK inspection establishes these facts:

- `WealthLedger.slnx` contains Domain, Application, Infrastructure, and Minimal
  API projects plus their tests. There is no UI project, page, component,
  static-asset pipeline, or browser test.
- `WealthLedger.Api` is the only runtime composition root. It references
  Application and Infrastructure, registers scoped use cases, maps Minimal API
  routes, and currently has no Razor Pages or MVC presentation registration.
- `ARCHITECTURE.md` already treats API and UI as separate delivery mechanisms
  that point inward toward Application, but deliberately leaves the UI
  framework unaccepted.
- The current setup path is one atomic Application use case for a base
  currency, household, optional member, institution, portfolio, account, cash
  asset, and one required-lot fund asset. Its JSON endpoint is default-off and
  there is no graphical setup state query.
- The setup use case accepts stable codes and fixed-point configuration that a
  human currently has to provide through JSON. The UI must make those fields
  understandable and must never ask the user for generated entity identities.
- `UX_MVP.md` calls for review before record, economic language, progressive
  disclosure, explicit posting, correction instead of editing, traceability,
  visible uncertainty, safe retry, keyboard operation, and privacy-safe
  failures.
- M004 proposes one safe local data path, one process owner, explicit lifecycle
  operations, loopback-only hosting, a versioned verified backup, and no HTTP
  restore/migration administration.
- M005 proposes current human-oriented master projections, a bounded recent
  Posted feed, exact raw transport values, opaque cursor pages, and valid scope
  semantics. It deliberately adds no UI and no write endpoint.
- The installed SDK is .NET SDK `10.0.400` with the .NET `10.0.11` runtime.
  Its built-in templates include Razor Pages, Razor Class Library, and Blazor
  Web App templates targeting .NET 10.

Official platform guidance also supports a bounded choice:

- [.NET support policy](https://dotnet.microsoft.com/en-us/platform/support/policy)
  identifies .NET 10 as the current LTS line.
- [Choose an ASP.NET Core UI](https://learn.microsoft.com/en-us/aspnet/core/tutorials/choose-web-ui?view=aspnetcore-10.0)
  describes Razor Pages as a page-oriented, server-rendered, organized, and
  testable model.
- [ASP.NET Core dependency injection](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/dependency-injection?view=aspnetcore-10.0)
  gives Razor Pages an ordinary per-request scoped lifetime.
- Microsoft's [Blazor dependency-injection](https://learn.microsoft.com/en-us/aspnet/core/blazor/fundamentals/dependency-injection?view=aspnetcore-10.0)
  and [Blazor with EF Core](https://learn.microsoft.com/en-us/aspnet/core/blazor/blazor-ef-core?view=aspnetcore-10.0)
  guidance explains the longer circuit lifetime and additional `DbContext`
  precautions of interactive server components.
- [Playwright for .NET](https://playwright.dev/dotnet/docs/intro) provides an
  official xUnit integration for real-browser end-to-end checks.

These sources do not choose the product architecture by themselves. They make
the lifecycle and testing trade-offs explicit for human acceptance.

## Why now

M004 protects the only local database and establishes reusable lifecycle
operations. M005 makes master identities and recent ledger facts discoverable
without reading SQLite. Once those contracts are Verified, a UI can remain a
thin delivery mechanism instead of inventing file handling, querying EF rows,
or duplicating accounting rules.

M007 needs a stable shell, exact value presentation, selection conventions,
failure behavior, and browser verification before it adds the much higher-risk
opening-balance write paths. Choosing a framework while M003-M005 are still
being implemented would create rework in hosting, dependency injection,
startup readiness, and read models. Specifying and accepting the boundary now,
then implementing only after those dependencies are Verified, keeps that work
ordered.

## Decisions and decision gates

Every decision below is Proposed. Human acceptance of all eleven decisions is
required before M006 becomes Accepted. The accepted framework and hosting
boundary must then be recorded in the next available ADR before implementation
begins.

### Decision 1: delivery sequencing

**Recommended:** review and accept M006 while M003-M005 are progressing, but do
not implement it until M003, M004, and M005 are merged and Verified. Create the
implementation branch from that checkpoint, rerun the complete suite and M004
operational smoke checks, and compare the final M003 transaction detail, M004
readiness/operations contracts, and M005 navigation results with this proposal.

If a verified dependency differs materially, return M006 to Proposed and amend
the unimplemented contract. Do not adapt by calling persistence directly or by
silently weakening the setup, backup, or navigation boundary.

### Decision 2: framework and project topology

**Recommended:** use ASP.NET Core Razor Pages on the repository's accepted
.NET 10 LTS line. Add a dedicated `WealthLedger.UI` Razor Class Library for
pages, PageModels, presentation view models, localization resources, and local
static assets. The existing `WealthLedger.Api` executable remains the single
local web host and composition root and references the UI assembly to discover
its pages and static web assets.

The accepted dependency shape becomes:

```text
WealthLedger.Api host ──> WealthLedger.UI ──> WealthLedger.Application
          │                                          │
          ├──────────────────────────────────────────┤
          └────────> WealthLedger.Infrastructure ────┘
                                                     ↓
                                             WealthLedger.Domain
```

`WealthLedger.Api` is still the JSON API delivery mechanism and runtime host;
the new reference exists only so one process can compose both delivery
mechanisms. `WealthLedger.UI` must not reference Infrastructure, EF Core,
SQLite, API request/response DTOs, or endpoint implementation types.

Do not select interactive Blazor Server for M006. Its circuit-scoped service
lifetime, reconnect state, and concurrent component activity add risk around
the repository's currently request-scoped use cases and `DbContext`. Do not
select Blazor WebAssembly, React, another SPA, or self-hosted client application
because they introduce a second client state model, HTTP mapping duplication,
and a larger build/runtime surface before a need exists. Do not select WPF,
MAUI, Avalonia, or another desktop shell because native packaging, update, and
second-host decisions are not required to deliver the local household flow.

Those alternatives are not permanently forbidden. A later demonstrated need
for rich client-side interaction, offline operation, or native integration can
supersede the ADR.

### Decision 3: server-side interaction and Application boundary

**Recommended:** pages are server-rendered and critical workflows use ordinary
HTTP GET and POST requests. Each PageModel resolves narrow Application use
cases or query services in the normal request scope. UI code maps human input
to Application commands and Application results to presentation view models.

The UI does not call the co-hosted JSON API through `HttpClient`. Self-HTTP
would add serialization, URI, retry, and error-mapping layers without creating
an isolation boundary. API and UI both consume the same accepted Application
contracts and remain separately testable delivery mechanisms.

The UI may add one narrowly shaped Application query that composes the final
M003 transaction detail with current M005 master labels when a direct
transaction URL cannot otherwise be explained without N+1 lookups. It may not
add a generic presentation repository, expose `IQueryable`, or move formatting
or HTML concerns into Application.

Every financial mutation remains an Application use case followed by Domain
validation and one Infrastructure transaction. PageModels contain no balance,
cost-basis, lot-allocation, price-times-quantity, or reversal arithmetic.

### Decision 4: fail-closed host readiness modes

**Recommended:** extend the accepted M004 startup boundary into five explicit
local presentation modes. The mode is derived from M004 status plus a narrow
core-setup-state query before normal endpoints are made available:

1. `Blocked`: an unsafe/unconfigured path, corrupt or incompatible database,
   pending migration, unavailable ownership, or another fail-closed M004 state;
2. `StorageUninitialized`: the configured safe live database does not exist;
3. `WorkspaceUninitialized`: a compatible empty database exists but the atomic
   core setup has not completed;
4. `InitialBackupRequired`: core setup is complete but the final M004 status
   contract recognizes no verified backup for the current configured live
   database;
5. `Ready`: storage, schema, core setup, and an M004 verification result tied to
   the current configured live database satisfy the accepted checks.

In `Blocked`, the host serves only a loopback read-only status page with the
sanitized M004 category and the exact supported operations command needed. It
maps no ledger API, setup mutation, generic file browser, migration, restore,
or replace action.

This deliberately refines M004's statement that an incompatible API startup
fails: the normal ledger/API service still fails closed, while the same
executable may serve a narrowly restricted presentation-only status surface.
The M006 ADR must record that refinement explicitly rather than rewriting or
pretending it was already part of the earlier operating-model decision.

The three setup modes serve only the guided setup pages and local static
assets. They do not map normal ledger JSON endpoints or ordinary transaction
writes. `StorageUninitialized` may invoke the same M004 `database initialize`
Application operation in-process after an explicit path review.
`WorkspaceUninitialized` may invoke only the atomic core setup use case.
`InitialBackupRequired` may create and verify the first immutable M004 backup.
The host is the one lifecycle owner in these modes; it does not launch the
operations executable or open a second database owner.

Migration, restore staging, active replacement, unsafe-path overrides, and
backup-file selection remain outside browser control. They continue through
the explicit M004 console workflow with the normal host stopped. This keeps
destructive lifecycle authority out of a CSRF-reachable surface.

After setup and the first backup succeed, the completion page asks for one
clean application restart. A `Ready` startup maps the normal UI and JSON API
and does not map setup pages. This static startup boundary is preferred over
dynamically enabling write endpoints in a process that began in maintenance
mode.

The mere presence of a file ending in `.wlbackup` is never a Ready signal. If
the final M004 status contract cannot distinguish a compatible verified backup
for the configured live database from an unrelated package, implementation
must stop and return this decision to review rather than adding a weaker UI
shortcut.

#### Amendment accepted 2026-09-03: durable workspace binding

Pre-implementation reconciliation established that the verified M004 status
contract could not make that distinction. It enumerated every valid package in
the configured directory, verified each in isolation, selected the newest, and
reported protection without any evidence of origin. A synthetic reproduction
using two independently initialized databases sharing one backup directory
showed a database with no backups of its own reporting `LocalProtectionReady`
against another workspace's package.

The human owner accepted the following amendment on 2026-09-03. It was
implemented and verified ahead of any UI work.

`InitialBackupRequired` and `Ready` require a `.wlbackup` package whose
**snapshot** carries the same durable workspace identity as the configured live
database, cross-checked against the value recorded in its manifest. The newest
*matching* package is selected; a newer non-matching package never substitutes
for it. A package that predates durable lineage has unknown origin and is not
protection. Recency, filename, creation timestamp, schema version, application
version, directory location, and the existence of any `.wlbackup` remain
insufficient, individually and in combination.

The identity is a random opaque value held in the database file itself, so the
relationship survives a process restart, an isolated restore, an active
replacement, and a change of live path. After an active replacement the live
database correctly rebinds to the promoted package's lineage, and packages of
the superseded lineage correctly stop counting as protection.

`LocalProtectionReady` was strengthened rather than duplicated: it now requires
proved binding in addition to its existing M004 terms. This is a deliberate,
recorded change of meaning, not a silent one. It corrects a warning that could
previously tell an operator their data was protected when no backup of their
database existed.

`Ready` requires a workspace-bound verified package. The separation and
encryption acknowledgements keep their accepted M004 meaning as operator
attestations: they are shown on the data-safety page and still gate
`LocalProtectionReady`, but they do not by themselves withhold the shell. The
UI is therefore not stricter than the operations console, which only warns.

`Backup:Directory` and the two acknowledgement settings remain host
configuration. The browser cannot set them. A host started without a configured
backup directory resolves to `Blocked` with the accepted console guidance,
because status cannot report protection evidence without one.

### Decision 5: bounded guided setup

**Recommended:** wrap the verified core setup graph rather than introduce
general master-data editing. The wizard contains these steps:

1. explain ledger immutability, correction, and local-data responsibility;
2. show and confirm the resolved live and backup paths and their M004 safety
   states without showing a connection string;
3. explicitly initialize a missing safe database when needed;
4. enter and review base currency, household, optional member, institution,
   portfolio, account, cash asset, and one initial fund asset;
5. atomically initialize the workspace;
6. create and independently verify the first `.wlbackup` generation;
7. show a completion summary and require a clean restart into `Ready`.

Routine controls use human names and economic language. Stable codes are
pre-populated with safe suggestions, explained as immutable identifiers, and
shown in an expandable advanced section and the final review. The user never
enters or copies a GUID, minor-unit integer, raw E8 value, migration identifier,
connection string, or SQL. `TRY`, Turkish lira, and two minor-unit digits may be
the Turkish-first currency defaults, but every submitted value still passes
the existing Application and Domain validation.

The current one-household, one-institution, one-account, one-cash-asset, and
one-fund-asset bootstrap shape remains explicit. Creating and managing more
masters is not smuggled into M006; the opening and later lifecycle milestones
must add the bounded master workflows their real entry paths require.

The wizard does not offer an opening balance or synthetic posting because M007
has not yet accepted those writes. Its completion/empty-state copy explains
that no holdings exist until an opening or supported transaction is recorded.

### Decision 6: first functional shell and navigation

**Recommended:** a `Ready` host initially exposes only destinations backed by
verified behavior:

- **Today**: local safety state, latest verified-backup age, an honest empty
  state, and a small recent Posted activity preview from M005;
- **Ledger**: the bounded M005 recent-recorded feed, cursor continuation, and a
  complete read-only transaction explanation using final M003 readback;
- **Settings**: read-only current household/member, institution, portfolio,
  account, currency, asset, data-path, backup, version, and schema status.

Do not display dead Record, Assets, or Plan links merely to imitate the future
navigation model. Add each destination when its first complete workflow is
Verified. Today must not fabricate portfolio totals, liquid reserve, market
value, return, goal progress, or reconciliation status before the underlying
read contracts exist.

Inactive or archived current labels remain visible when explaining historical
facts. Transaction entries use current display context while preserving stable
identities and M005's warning that current names are not historical snapshots.
The Settings pages are not generic table editors.

### Decision 7: exact value formatting and Turkish-first presentation

**Recommended:** make Turkish (`tr-TR`) the initial user-facing culture while
keeping code, contracts, tests, and canonical documentation in English. Put
user-visible strings in UI resource files from the start; M006 does not need a
runtime language switch or complete second translation.

Add small, framework-independent presentation formatters/components for:

- money from signed minor units plus Currency code and `MinorUnitDigits`;
- quantity and signed quantity delta from raw E8 plus stable unit code;
- unit price from raw E8 plus explicit currency code;
- business date and UTC timestamp with an unambiguous time-zone label;
- stable status/type/role codes with localized human text and the stable code
  available in technical details.

Formatting uses integer decomposition or checked decimal/string operations;
it never passes an authoritative value through `double` or `float`. It handles
the complete signed `long` range without `Math.Abs(long.MinValue)` overflow.
Trailing zeros that carry no persisted scale may be omitted, while enough
digits remain to reproduce the recorded E8 value exactly.

Representative presentation is:

```text
1.234,56 TRY
+12,3456789 fon birimi — artış
-0,25 gram — azalış
31.08.2026
31.08.2026 14:30:00 Europe/Istanbul (11:30:00Z)
```

Currency code is never replaced by a symbol alone. Null or absent facts render
as a localized `Unknown` or `Not applicable` according to contract semantics;
genuine zero renders as `0`. The UI never guesses which of those states a null
means. Stable IDs and raw transport diagnostics are available only in an
explicit technical-details disclosure, not as the primary value.

### Decision 8: visual system, progressive enhancement, and accessibility

**Recommended:** use semantic HTML, a small local CSS system based on custom
properties and ordinary Grid/Flexbox, and no npm build, CDN, web font,
third-party component suite, chart library, or icon package. Small local
JavaScript modules may enhance focus, disclosures, or double-submit feedback,
but every critical setup and navigation action works when JavaScript is absent.

The shell has one restrained responsive layout rather than a trading-terminal
dense dashboard. It provides a skip link, landmarks, one clear page heading,
associated labels and help text, validation summary, visible keyboard focus,
logical tab order, adequate target sizes, reduced-motion respect, and text or
icon-plus-text status cues. Color is never the only signal.

The same page must remain usable at a narrow phone-like viewport and a normal
desktop viewport, but M006 is not a native mobile application. Destructive or
irreversible actions are not placed in modal-only flows. The browser's Back,
Refresh, and resubmission behaviors remain safe.

### Decision 9: local web security and privacy

**Recommended:** inherit M004's loopback-only bind and host validation. Local
network exposure remains forbidden; loopback is a deployment boundary, not an
authentication system. Remote access, accounts, roles, TLS termination, and
authorization require a later accepted security milestone.

Every state-changing Razor form requires ASP.NET Core antiforgery validation,
uses POST, and follows Post/Redirect/Get. Cookies are limited to framework
antiforgery and essential presentation preferences, use secure same-site
settings appropriate to the actual local scheme, and never contain ledger
values or authoritative workflow state. No mutation occurs on GET.

Use a restrictive Content Security Policy compatible with local static assets,
standard output encoding, no inline untrusted HTML, no telemetry, no analytics,
and no external runtime request. Notes, names, references, paths, quantities,
and rendered bodies are absent from routine request logs. Error pages expose a
stable sanitized category and support reference, never SQL, connection strings,
stack traces, raw cursor contents, request bodies, or EF row details.

Full resolved paths may appear only on the local setup/data-safety screen where
the operator must inspect them. Ordinary Today and Ledger pages show a short
safe location label and status. M006 adds no screenshot/export feature and
does not claim that an open browser session hides values from someone already
controlling the logged-in desktop.

### Decision 10: state, retry, restart, and concurrency semantics

**Recommended:** authoritative setup progress is derived on every request from
M004 lifecycle status and persisted core-master existence. Do not use browser
local storage, server session, TempData, cookies, or a new database table as an
authoritative setup state machine. ModelState may preserve unsubmitted form
input after validation failure; it is not ledger state.

Every successful POST redirects to a GET. A lost response, double-click,
Refresh, or browser resubmission has these results:

- database initialization observes the newly initialized compatible database
  and advances; it never recreates or overwrites it;
- core setup observes an already complete atomic setup and advances only after
  re-reading a valid completed state; it never invents success for partial or
  conflicting masters;
- each successful backup retry creates and verifies a new immutable generation
  under M004 rules; it never overwrites a prior package;
- concurrent setup/lifecycle attempts produce one success or one sanitized
  busy/already-complete result and never a partially initialized graph;
- read-page refreshes create no ledger, receipt, lot, allocation, backup, or
  operational fact.

A failed form preserves human input when safe and says whether nothing was
committed, an equivalent step already succeeded, or the outcome requires a
fresh status read. Cancellation reaches Application/Infrastructure calls. A
process restart reconstructs the same readiness mode and never relies on an
in-memory wizard step.

### Decision 11: verification and dependency policy

**Recommended:** add one ordinary xUnit UI test project for PageModel,
formatter, resource, and rendered-host tests, and one small Playwright .NET
xUnit browser project for Chromium end-to-end smoke tests. Pin packages through
the repository's normal explicit package references and pin the Playwright
browser revision through its package tooling. Do not add a JavaScript test
runner.

Browser installation is a documented prerequisite and a separate repeatable
verification command. Browser tests use unique synthetic temporary data and
backup directories, start the real loopback host on an ephemeral port, block
all external network requests, and leave no process or file behind. They do not
use the household's live database.

HTTP-level host tests remain responsible for startup mode, route exposure,
antiforgery, headers, and sanitized errors. Real-browser tests cover only the
critical first-run and read-navigation journey, responsive shell, keyboard
reachability, and no-external-request contract. Do not create brittle
pixel-perfect screenshots or pretend a small automated suite proves complete
WCAG conformance; attach a concise manual accessibility/visual checklist to the
implementation review.

## In scope

- the accepted UI/hosting ADR and corresponding architecture update;
- a `WealthLedger.UI` Razor Class Library and one-host composition in
  `WealthLedger.Api`;
- fail-closed setup, blocked, and ready startup modes built on verified M004
  operations and ownership;
- a narrow Application query for core-setup readiness when needed;
- explicit in-process create-only database initialization in setup mode;
- the human guided core setup form and review;
- first backup creation and verification in setup mode;
- setup completion and one clean restart into normal mode;
- a responsive Today, Ledger, transaction-detail, and read-only Settings shell;
- current M005 labels and status context around final M003 transaction facts;
- exact money, quantity, price, date/time, and stable-code presentation;
- Turkish-first UI resources and plain-language empty/error states;
- antiforgery, Post/Redirect/Get, local-only security headers, and privacy-safe
  logging behavior;
- PageModel/formatter, host-functional, and Playwright browser verification;
- synthetic fixtures and documentation needed to run and review the UI.

## Out of scope

- opening-balance transactions, imports, or cutover logic from M007;
- contribution, purchase, sale, withdrawal, transfer, reversal, correction, or
  other ordinary transaction-entry forms;
- ongoing master-data create, edit, rename, deactivate, archive, or delete
  screens after first-run bootstrap;
- broad transaction search, filters, lot/position inventory, reconciliation,
  evidence upload, or export from M010;
- live market data, valuation, price freshness, performance, charts, goals,
  allocation, optimization, forecasts, or agent analysis;
- browser-triggered migration, restore, active replacement, backup-file upload,
  generic filesystem browsing, arbitrary path override, or SQL access;
- automatic/background backup scheduling, pruning, cloud upload, or normal-
  operation write quiescence;
- remote access, authentication, authorization, multi-user sessions, TLS
  deployment, or Internet hosting;
- Blazor interactivity, SPA/PWA/offline caching, service workers, native desktop
  packaging, auto-update, system-tray behavior, or operating-system shortcuts;
- a runtime theme builder, chart/design system, third-party component suite,
  full multilingual release, or visual-regression image baseline;
- application-managed encryption or a claim that loopback protects an unlocked
  desktop from its current user;
- schema/provider changes unrelated to a proved M006 query need.

## Required behavior

### Startup and route exposure

The host validates loopback exposure before serving any page. It resolves M004
status without creating a database as a read side effect and selects exactly
one startup mode. Normal JSON endpoints and normal UI pages exist only in
`Ready`; setup mutation pages exist only in the applicable setup mode. A
blocked host cannot be turned into a normal host by a URL parameter, cookie,
form value, forwarded header, or JavaScript state.

The blocked page distinguishes actionable stable categories without revealing
database internals. Missing safe storage offers initialization. Pending
migration, corrupt/incompatible storage, unsafe configuration, or ownership
conflict offers only the accepted operations instruction. After setup
completion, the current maintenance-mode process never begins serving ordinary
ledger writes; the user restarts into a freshly validated `Ready` process.

### Guided first run

Each step explains the effect before its POST button. Paths and stable codes
are reviewed before mutation. Validation happens at the page boundary for
required text and parsable codes, then again in Application and Domain.
Browser validation is convenience only and never the sole rule.

Core setup remains atomic. A failure cannot leave a subset that the wizard
quietly completes by guessing. An already initialized compatible workspace is
read back and advances; a non-empty incompatible/partial workspace is blocked
with recovery guidance.

The first backup is created from the newly initialized workspace only after
core setup commits. It must pass the complete M004 package verification before
the UI says first-run protection is complete. A failed verification leaves no
final-looking package and offers a safe retry. Setup creates no transaction,
opening balance, lot, allocation, or command receipt.

### Ready shell

The root page selects the implemented household without exposing a GUID as a
required input. With one bootstrapped household, it is the current household.
If a later verified schema permits more than one, M006 must not guess; an
explicit selector or later accepted policy is required.

Today states what is known. It can show storage/backup readiness and recently
recorded Posted facts. It cannot label recorded contributions as cash on hand,
sum raw quantities across unlike assets, or turn absence of valuation into zero
wealth.

Ledger uses M005's opaque cursor unchanged. A previous/next interaction does
not parse or manufacture the cursor in JavaScript. A malformed, stale, or
scope-mismatched cursor produces a plain sanitized page and a safe link to the
first page. Newly posted history appears when the first page is refreshed under
M005 semantics.

A transaction detail shows all final M003 facts in plain language: identity,
type/status, dates, reference/note where accepted, ordered effects, costs,
cash-flow classification, created lots, allocations, and reversal navigation.
Current master names are visibly current context. Unknown optional facts remain
unknown, and inactive referenced masters remain explainable.

Settings is read-only in M006. It separates household/master information from
data safety and technical details. It does not present disabled Save/Delete
buttons or imply that changing rendered text edits persisted facts.

### Formatting

All value formatting is deterministic for a supplied culture and metadata.
The same raw input produces the same visible exact number before and after
restart. Negative signs, decimal separators, unit labels, currency codes, and
time-zone labels remain unambiguous in text and assistive names.

Formatting failure is not converted to zero. Missing Currency metadata,
unsupported minor-unit digits, overflow, or an unknown stable code produces a
sanitized explicit unavailable state and diagnostic category. It never emits a
plausible but false number.

### Failure and privacy

Expected validation failures return the same page with a summary, field links,
and preserved safe input. Unexpected failures route to a private-data-safe
error page with a support reference. The response and captured logs are
inspected for connection strings, SQL, stack traces, model-binding internals,
raw bodies, private notes, exact values, and cursor payloads.

Static assets load locally and the application remains usable without an
Internet connection. A blocked external request is a test failure, not an
accepted font/icon/analytics dependency.

## Invariants

- Ledger history remains the sole authoritative source of financial effects.
- UI view models, formatted strings, cookies, and browser state are never
  authoritative balances, positions, costs, prices, or setup facts.
- PageModels call narrow Application contracts and never query or write EF,
  SQLite, files, backup archives, or API endpoint internals directly.
- UI and API are delivery mechanisms; neither contains Domain arithmetic or a
  second business-rule implementation.
- Fixed-point values remain exact and never pass through binary floating point.
- Unknown, not applicable, genuine zero, estimated value, cash consideration,
  and cost basis remain distinct concepts.
- Current labels never rewrite historical stable identities or facts.
- Posted transactions are read-only; later correction remains reversal plus a
  separately supported replacement.
- Setup is atomic, one-time, unavailable in `Ready`, and never seeds posted
  financial history.
- M004 is the only lifecycle/backup implementation. UI wraps its Application
  operations in the permitted setup mode rather than copying files or invoking
  shell commands.
- Migration, restore, active replacement, and arbitrary path selection are not
  exposed to browser requests.
- Normal ledger routes are unavailable until safe storage, compatible schema,
  core setup, and first verified backup satisfy readiness.
- Every state-changing form is POST plus antiforgery plus PRG; GET is read-only.
- Critical behavior works without JavaScript and without external network
  access.
- Normal operation remains loopback-only; UI addition does not imply remote
  access or authentication.
- Routine logs and errors omit private values, labels, references, paths,
  bodies, SQL, cursors, and stack traces.
- Tests, screenshots, and browser traces use synthetic data and isolated paths.
- No new authoritative financial table, cached total, session state, or browser
  financial cache is introduced.

## API or UI contract

Existing accepted JSON routes and payloads remain compatible. M006 adds no
generic admin JSON route and does not expose M004 lifecycle commands over JSON.
The normal setup JSON endpoint remains default-off and is not the browser
wizard's implementation path.

### Presentation routes

Representative canonical routes are:

```text
GET  /                         Today
GET  /ledger                   Recent Posted ledger page
GET  /ledger/{transactionId}   Read-only transaction explanation
GET  /settings                 Settings index
GET  /settings/master-data     Read-only current master data
GET  /settings/data-safety     Read-only storage/backup/schema status

GET  /setup                    Current first-run step
GET  /setup/storage            Storage review
POST /setup/storage            Explicit create-only initialization
GET  /setup/workspace          Core master-data form
POST /setup/workspace          Validate and atomically initialize
GET  /setup/backup             Initial backup review/status
POST /setup/backup             Create and verify one immutable generation
GET  /setup/complete           Restart instruction

GET  /blocked                  Sanitized read-only lifecycle guidance
GET  /error                    Sanitized unexpected-error page
```

Exact Razor page filenames are not a public compatibility promise, but route
paths, GET/POST safety, and startup-mode exposure are frozen by functional
tests. Unknown, empty, or malformed `transactionId` never becomes a default
identity and returns a localized non-disclosing not-found/bad-request page.

Ledger accepts only the M005 query contract:

```text
?pageSize=<bounded integer>&cursor=<opaque optional value>
```

The cursor may appear in a local navigation URL but is not logged, decoded in
the browser, or treated as authorization.

### Form contract

Every form contains an antiforgery token, one clearly named submit action, and
a review of the exact target. Server validation is authoritative. Stable field
error keys are mapped to localized labels; raw exception messages are not
rendered.

The workspace form covers the final verified fields of
`InitializeCoreLedgerCommand`. Generated GUIDs and timestamps are never form
inputs. Suggested stable codes are editable only before initialization, are
normalized through the accepted Application rules, and are shown in review.

No financial posting form is present in M006.

### Presentation value contract

Formatters receive typed presentation inputs rather than unrelated primitive
bags. Representative shapes are:

```text
MoneyDisplay(amountMinorUnits, currencyCode, minorUnitDigits)
QuantityDisplay(quantityRawE8, unitCode, signedEffect)
UnitPriceDisplay(unitPriceRawE8, currencyCode)
BusinessDateDisplay(date)
UtcTimestampDisplay(timestampUtc, displayTimeZone)
StableCodeDisplay(codeFamily, stableCode)
```

They return structured display models with visible text, assistive text, and
an explicit semantic state such as Known, Zero, Unknown, or NotApplicable.
They do not return HTML from Application and do not calculate portfolio facts.

## Persistence impact

M006 adds no financial or master schema change. It uses the verified M004
operational artifacts, current normalized master tables, M003 transaction
readback, and M005 query index/contracts.

It does add exactly one non-financial migration,
`20260903075104_005_WorkspaceIdentity`, required by the accepted Decision 4
amendment above. That migration creates a single-row `WorkspaceIdentity` table
holding a random opaque lineage value and its creation timestamp, seeded inside
SQLite so every database receives its own distinct identity through any
supported creation path.

The table is deliberately outside the EF model. Infrastructure reads it with a
bounded direct query, exactly as it already reads `__EFMigrationsHistory`, so
the value can never be joined into a ledger query, projected into a read model,
or mistaken for a financial fact. No ledger row or foreign key references it,
it holds no household, account, asset, transaction, or other private fact, and
the EF model-drift check remains clean.

This refines M004's statement that no operational history table is added to the
ledger. A single-row lineage identity is not history, but the earlier sentence
is narrowed explicitly rather than reinterpreted.

The guided setup still writes only the accepted core master graph through one
existing atomic transaction, and the first backup still writes an external
immutable `.wlbackup` package under M004.

The guided setup writes only the already accepted core master graph, using one
existing atomic transaction. The first backup writes an external immutable
`.wlbackup` package under M004; it does not add a ledger row or UI-state table.

Do not add server session persistence, wizard-progress tables, cached current
positions, rendered HTML caches, browser databases, or service-worker storage.
If implementation discovers that further readiness state is needed beyond the
accepted lineage identity, stop and return the milestone to Proposed rather
than hiding another schema change in M006.

### Compatibility and rollback of the lineage migration

The backup manifest stays at format version 1. The reader compares that version
for equality, so raising it would make every existing package unreadable by
this build and every new package unreadable by an earlier one. The lineage
value is therefore an additive optional member inside version 1, under the
compatibility rule M004 already accepted. Both directions were verified: a
build that predates the member skips it and still verifies the package, and
this build treats its absence as unknown lineage rather than as a failure.

Because the manifest sits outside the snapshot digest, the manifest value is a
convenience copy only. Verification requires it to agree with the identity read
from the snapshot itself, mirroring the existing migration-history cross-check.
A package whose manifest lineage was edited to claim another workspace, or
stripped while its snapshot still carries one, is rejected as an invalid
package. As with every other `.wlbackup` guarantee, this is corruption and
mistake evidence, not authenticity against a determined editor.

Packages created before this migration have no lineage and stop counting as
protection. **After upgrading, an operator must create one new backup before
status reports protection again.** The older packages remain valid, verifiable,
and restorable; they simply cannot prove which database they came from.

Rollback is no longer free. Once migration 005 is applied to a database, an
earlier build reports it as `Incompatible`, and recovery is a pre-migration
package restore through the accepted `OPERATIONS.md` procedure. This is the
ordinary forward-only migration cost in this repository, but it replaces this
milestone's earlier expectation that source rollback could remove M006 without
touching stored data.

One residual risk is recorded rather than designed away: two hosts that both
descend from one restored package share a lineage identity and will each accept
the other's packages. Detecting *divergence* between copies of one lineage
would require content or generation tracking and new authoritative state, which
is outside M006.

## Acceptance criteria

- The accepted ADR records Razor Pages, the dedicated UI assembly, the single
  Api host, direct Application calls, startup modes, and rejected alternatives.
- `WealthLedger.UI` has no reference to Infrastructure, EF Core, SQLite, or API
  contracts; Domain remains independent of every UI concern.
- Normal operation starts one loopback process and serves both accepted JSON
  routes and the local UI without self-HTTP.
- Wildcard/LAN/public binding remains rejected under the final M004 rules.
- Every M004 lifecycle state selects exactly one documented presentation mode.
- Blocked or setup mode exposes no normal ledger API or ordinary transaction
  writer.
- The browser can initialize only a missing configured safe database; it cannot
  migrate, restore, replace, upload a backup, browse arbitrary files, or change
  an unsafe path.
- A user can complete core setup using names and reviewed stable codes without
  supplying a GUID, raw E8/minor-unit value, SQL, connection string, or raw API
  request.
- Core setup remains all-or-nothing and an equivalent retry/restart advances
  safely without creating duplicate masters.
- First-run cannot report completion until one new backup generation passes
  full M004 verification.
- A failed or concurrent lifecycle/setup operation preserves the source and
  produces no partial authoritative-looking database or backup.
- After a clean restart, a Ready host returns 404/not mapped for setup routes
  and exposes Today, Ledger, and read-only Settings.
- Today displays only implemented safety and recent-recorded facts and does not
  fabricate total assets, reserve, value, return, or goal progress.
- Ledger paging preserves M005 cursor/scope semantics and renders final M003
  transaction facts with current M005 human context in bounded queries.
- Direct transaction URLs work after restart and never require prior in-memory
  navigation state.
- Inactive/archived masters remain explainable and current labels are not
  presented as source-time snapshots.
- Exact money, signed E8 quantity, unit price, date, timestamp, and stable-code
  formatters pass zero, null-state, boundary, negative, and `long`-extreme tests
  without binary floating point.
- Currency code and unit/effect text remain visible independently of color;
  Unknown, Not applicable, and Zero have distinct output.
- Setup and core navigation work with JavaScript disabled and an unavailable
  Internet connection.
- All state-changing forms enforce antiforgery and PRG; cross-site or missing-
  token posts fail without mutation.
- Browser Back, Refresh, double-submit, lost-response simulation, cancellation,
  and process restart produce the accepted safe outcome.
- Semantic landmarks, labels, validation links, focus order, skip navigation,
  reduced motion, and narrow/desktop layouts pass the documented review.
- No page loads a CDN, analytics, telemetry, remote font, external script, or
  external image.
- Responses, logs, browser console, traces, and error pages contain no forbidden
  SQL, connection strings, stack traces, raw bodies, or real household data.
- Focused UI/host/browser tests, the complete post-M005 suite, formatting, EF
  model drift, and M004 operational smoke checks pass.
- `PROJECT_STATE.md`, `ROADMAP.md`, architecture, UX, security/operations, and
  README claim only the behavior actually Verified.

## Test scenarios

### Domain

No Domain behavior should change. Dependency/source-review checks prove that
Razor, HTTP, culture, resources, HTML, filesystem, and readiness concerns did
not enter Domain.

### Application

- readiness composition for Blocked, StorageUninitialized,
  WorkspaceUninitialized, InitialBackupRequired, and Ready;
- cancellation and stable sanitized classification propagation;
- core setup-state query distinguishes empty, complete, and partial/conflicting
  master graphs without writing;
- UI setup mapping preserves every reviewed stable code/name/type/date field;
- equivalent already-complete setup advances only after a valid readback;
- a partial/conflicting graph blocks rather than being guessed or completed;
- transaction explanation composition preserves M003 identities, child order,
  exact values, reversal/allocation relationships, and M005 current context;
- current-label resolution is bounded and does not become a generic repository;
- PageModel tests prove that display formatting does not alter Application
  commands or results.

### Infrastructure

- real-SQLite readiness for absent, initialized-empty, complete, partial,
  pending-migration, incompatible, corrupt, and ownership-busy synthetic files;
- workspace binding, verified ahead of the UI and already passing:
  independently initialized databases receive distinct identities; an unrelated
  workspace's package is never protection; a newer unrelated package never
  displaces an older matching one; a forged or stripped manifest lineage is
  rejected; a package predating lineage stays valid but unknown; a migrated
  database does not accept its own pre-migration package until one new backup
  is taken; identity survives isolated restore and restart; active replacement
  rebinds to the promoted lineage;
- setup-mode initialize uses the same M004 operation, lifecycle guard, safe
  path, staging, and validation behavior as the console;
- atomic core setup and rollback remain unchanged under UI invocation;
- initial backup uses the same M004 online backup/package/verification code and
  produces a restart-verifiable immutable generation;
- failures injected between setup, backup snapshot, package validation, and
  publication leave no false Ready state or final-looking artifact;
- batched transaction explanation has bounded query count and no N+1 labels;
- restart preserves exact M003/M005 read facts and M004 readiness;
- no unexpected migration or EF model drift is introduced.

### API or UI

- each startup mode maps only its accepted routes and static assets;
- setup GETs are read-only; every setup POST requires a valid antiforgery token;
- direct/cross-site POST, missing/invalid token, duplicate submit, and stale form
  cannot mutate outside the accepted step;
- storage, workspace, backup, completion, restart, Today, Ledger, transaction,
  Settings, blocked, empty, validation, not-found, and unexpected-error pages;
- setup input survives ordinary validation failure without rendering secrets or
  raw exception text;
- no GUID is required, while reviewed stable codes and generated identities can
  be inspected after success;
- Ready startup makes every setup route unavailable even if a caller retains an
  old form token;
- Today and Settings use honest known/unknown/empty wording;
- recent paging preserves cursor links and handles malformed/mismatched cursor
  without leaking payload;
- transaction entries, costs, lots, allocations, and reversal links retain
  deterministic order and exact localized values;
- `long.MinValue`, `long.MaxValue`, negative, zero, eight-decimal, and currency
  minor-digit boundary rendering;
- no stable code is localized at the contract boundary even when its visible
  description is localized;
- response headers include the accepted CSP and related local security policy;
- static assets are served locally with no external requests;
- page source, errors, captured logs, browser console, and trace artifacts are
  inspected for forbidden private/diagnostic data;
- keyboard-only first-run and ledger navigation, skip link, focus on validation
  summary, reduced motion, and 390-pixel/desktop viewport smoke checks;
- JavaScript-disabled first-run and read navigation;
- process and temporary-file cleanup after every browser test.

### Manual review

- run the complete first-run journey against a disposable synthetic path;
- inspect the same pages at narrow and desktop sizes at 100% and 200% zoom;
- complete the journey using keyboard only and a screen-reader spot check;
- inspect light/high-contrast presentation and status cues without relying on
  color;
- disconnect external networking and confirm all required assets remain;
- inspect browser developer tools for failed/external requests and console
  errors;
- stop/restart at each setup boundary and confirm reconstructed state;
- perform the documented M004 backup verification and isolated restore smoke
  against the synthetic first-run backup;
- confirm no screenshot, trace, log, or repository file contains real data.

## Verification commands

Focused project names may be adjusted to the accepted implementation, but the
equivalent checks are required:

```powershell
dotnet test tests/WealthLedger.UI.Tests/WealthLedger.UI.Tests.csproj --no-restore --verbosity minimal
dotnet test tests/WealthLedger.Api.Tests/WealthLedger.Api.Tests.csproj --no-restore --filter FullyQualifiedName~UiHosting --verbosity minimal
dotnet test tests/WealthLedger.UI.BrowserTests/WealthLedger.UI.BrowserTests.csproj --no-restore --verbosity minimal
```

Install the pinned Playwright browser after building the browser-test project;
the exact generated path and package version must be documented rather than
copied from this planning example:

```powershell
dotnet build tests/WealthLedger.UI.BrowserTests/WealthLedger.UI.BrowserTests.csproj --no-restore
pwsh tests/WealthLedger.UI.BrowserTests/bin/Debug/net10.0/playwright.ps1 install chromium
```

Final repository verification:

```powershell
dotnet test WealthLedger.slnx --no-restore --verbosity minimal
dotnet format WealthLedger.slnx --verify-no-changes --no-restore --verbosity minimal
dotnet ef migrations has-pending-model-changes --project src/WealthLedger.Infrastructure/WealthLedger.Infrastructure.csproj --startup-project src/WealthLedger.Infrastructure/WealthLedger.Infrastructure.csproj --context WealthLedgerDbContext --no-build
```

Also run the final M004 synthetic status, initialization, backup creation,
backup verification, isolated restore, loopback-binding, and restart smoke
workflow. Browser verification must use that disposable database and never the
configured real household path.

## Documentation updates

After human acceptance:

- mark M006 Accepted and record the accepted date and decisions;
- create the next available ADR for the Razor Pages, single-host, direct-
  Application, readiness-mode, and no-external-runtime-resource decisions;
- index the ADR in `docs/decisions/README.md`;
- update `ARCHITECTURE.md` with the host/UI project topology without claiming
  implementation;
- keep M004/M005 histories intact and do not assign an ADR number that conflicts
  with their accepted implementation branches.

After implementation verification:

- mark M006 Verified and record the verification date;
- update `PROJECT_STATE.md` with the factual UI project, host modes, first-run,
  exact formatters, pages, and browser-test checkpoint;
- move M006 to Verified in `ROADMAP.md` and make M007 the next candidate;
- update `ARCHITECTURE.md` with the verified delivery/read/write flow;
- update `UX_MVP.md` to distinguish implemented Today/Ledger/Settings/setup
  behavior from later Record/Assets/Plan workflows;
- update `SECURITY_OPERATIONS.md` with the verified blocked/setup/ready route
  matrix, antiforgery, CSP, privacy logging, and remaining remote-access risks;
- update `README.md` with local start, first-run, restart, browser prerequisites,
  synthetic screenshot guidance, and links to M004 operations;
- add a compact canonical UI/accessibility style guide only if implementation
  needs rules beyond the accepted ADR and UX document;
- do not rewrite Verified milestone or ADR history.

## Suggested commit boundaries

```text
docs(ui): record the accepted local ui architecture
feat(ui): add exact localized value presentation
feat(hosting): select fail-closed ui startup modes
feat(ui): guide safe first-run initialization
feat(ui): require a verified initial backup
feat(ui): render the local application shell
feat(ui): explain recent ledger transactions
test(ui): prove setup retry privacy and accessibility
test(browser): cover first-run and read navigation
docs(state): record the verified ui checkpoint
```

Keep every intermediate commit buildable. Do not mix M007 opening writes, later
transaction forms, master management, market data, analytics, remote hosting,
desktop packaging, or provider integrations into M006.

## Risks and rollback

- **Planning on unverified dependencies:** M003-M005 may change hosting,
  readback, or migrations. Rebase only after all three are Verified and return
  this milestone to Proposed on a material conflict.
- **Composition-root coupling:** making the API executable host the UI can blur
  delivery responsibilities. Preserve a separate UI assembly and direct
  inward dependencies, and record the topology in the ADR.
- **Interactive-framework temptation:** Blazor or SPA features can look faster
  for a form but add circuit/client state and duplicate error behavior. Keep
  critical M006 flows request/response and reconsider only with a concrete need.
- **Maintenance-mode privilege:** a browser-accessible local page can become an
  accidental admin API. Allow only create-new storage, atomic core setup, and
  the first backup in the restricted mode; keep migrate/restore/replace in the
  explicit console.
- **CSRF against localhost:** loopback alone does not stop a hostile website
  from attempting local requests. Require antiforgery, same-site cookies, no
  mutation on GET, restrictive headers, and route-mode tests.
- **False readiness:** file existence or one backup manifest is not enough, and
  neither is a verified package of unproved origin. Consume the M004
  compatibility, verification, and workspace-binding result and do not
  reimplement a weaker UI check. The Decision 4 amendment records why: the
  earlier contract accepted any valid package in the configured directory.
- **Partial setup:** browser navigation can stop between steps. Keep storage
  initialization staged, core setup atomic, progress derived, and restart tests
  exhaustive.
- **Duplicate submit:** JavaScript button disabling is insufficient. Use
  idempotent create-only lifecycle semantics, atomic setup readback, immutable
  backup generations, PRG, and concurrency tests.
- **Formatting lies:** culture conversion, overflow, or null coercion can show a
  plausible wrong number. Test integer boundaries and explicit semantic states
  and fail unavailable rather than showing zero.
- **UI business logic:** PageModels may gradually calculate totals or validate
  ledger economics. Keep them mapping-only and reject calculations without an
  accepted Application contract.
- **N+1 transaction explanation:** resolving current labels per child can make
  detail slow and inconsistent. Use one bounded Application projection and
  assert query count.
- **Private traces:** HTML, Playwright traces, screenshots, paths, and logs can
  expose more than JSON tests. Use synthetic fixtures, block routine body/value
  logging, and inspect artifacts before commit.
- **Accessibility drift:** a visually simple page can still be unusable by
  keyboard or at zoom. Combine semantic automated checks with the documented
  human review instead of relying on color or screenshots.
- **Browser-test weight:** Playwright adds a browser download and can leave
  processes behind. Pin it, document installation, isolate contexts, and prove
  cleanup; do not make the critical journey an unrepeatable manual claim.
- **False product breadth:** a shell can imply positions, valuation, or planning
  exist. Hide destinations without verified behavior and use honest empty
  states.

Source rollback removes the UI project, host registration, and tests without
changing ledger rows. It does **not** undo migration 005: a database that has
already received its lineage identity is reported as `Incompatible` by an
earlier build, and recovery is a pre-migration package restore under
`OPERATIONS.md`. See the compatibility and rollback section above.

A database or backup successfully created through the accepted M004 operations
is preserved; rollback never deletes it. If a presentation regression reaches a
development build, the JSON API and M004 console remain the supported recovery
surfaces. All rollback checks use synthetic copies, never the household's only
database.
