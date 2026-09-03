# ADR-008: Deliver the local UI as server-rendered Razor Pages in the existing loopback host

- Status: Accepted
- Decision date: 2026-09-03

## Context

The verified M003-M005 checkpoint exposes retry-safe writes, posted reversal,
transaction readback, bounded master and ledger navigation, and a fail-closed
local data operating model. Every one of those capabilities is reachable only
through JSON requests or the operations console. A household cannot complete
first-run setup, confirm that its data is protected, or read its own recorded
history without constructing HTTP calls and GUIDs by hand.

`ARCHITECTURE.md` has always listed `WealthLedger.UI` as a delivery mechanism
pointing inward at Application, but deliberately left the framework unaccepted
so the core would not be shaped by a premature client choice.

The household's stated operating intent is a private tool used from a desktop
today and from a phone browser later, eventually hosted on a home server that
only the household can reach. The long-term product goal is agent-assisted
allocation analysis over trustworthy recorded history. Both depend on a stable
presentation and hosting boundary that does not duplicate financial rules.

M006 specifies that boundary. The human owner accepted its eleven decisions on
2026-09-03, with Decision 4 amended during pre-implementation reconciliation.

## Decision

Use the accepted M006 architecture.

**Framework and topology.** Presentation is server-rendered ASP.NET Core Razor
Pages in a dedicated `WealthLedger.UI` assembly containing pages, PageModels,
presentation view models, localization resources, and local static assets. The
existing `WealthLedger.Api` executable remains the single local web host and
composition root and references the UI assembly to discover its pages and
static web assets. `WealthLedger.UI` must not reference Infrastructure, EF
Core, SQLite, API request/response contracts, or endpoint implementation types.

**Application boundary.** PageModels resolve narrow Application use cases in
the ordinary request scope and map human input to commands and results to
display models. The UI does not call the co-hosted JSON API over `HttpClient`;
self-HTTP would add serialization, retry, and error-mapping layers without
creating an isolation boundary. UI and API are peer delivery mechanisms over
the same accepted Application contracts. PageModels contain no balance,
cost-basis, allocation, price-times-quantity, or reversal arithmetic.

**Readiness modes.** The host derives exactly one of five modes before mapping
endpoints: `Blocked`, `StorageUninitialized`, `WorkspaceUninitialized`,
`InitialBackupRequired`, and `Ready`. Normal ledger routes and normal UI pages
exist only in `Ready`. A blocked or setup-mode host cannot be promoted by a URL
parameter, cookie, form value, forwarded header, or client state, and after
first run the operator restarts into a freshly validated `Ready` process.

`InitialBackupRequired` and `Ready` require a verified `.wlbackup` package
proved to belong to the configured live database by durable workspace lineage,
per the amended Decision 4 and the implementation recorded in migration
`20260903075104_005_WorkspaceIdentity`. `Ready` does not additionally require
the separation and encryption acknowledgements; those keep their accepted M004
meaning as operator attestations and remain visible on the data-safety page.

**Maintenance authority.** Setup modes may initialize a missing safe database,
run the atomic core setup, and create the first verified backup, each through
the existing M004 Application operations. Migration, restore staging, active
replacement, backup-file selection, arbitrary path override, filesystem
browsing, and SQL remain outside browser reach and continue through the
explicit console with the normal host stopped.

**Ownership.** The process-lifetime database lease belongs to `Ready` alone.
In `Blocked` and the three setup modes the host holds no long-lived lease and
each lifecycle operation acquires exclusive ownership for its own duration, as
the console already does, so concurrent attempts resolve to one success and one
sanitized busy result.

**Presentation.** Turkish is the initial user-facing culture while code,
contracts, tests, and canonical documentation remain English. Money, quantity,
unit price, dates, timestamps, and stable codes are formatted by small
framework-independent components using integer or checked decimal operations,
never binary floating point. Unknown, Not applicable, and genuine Zero remain
distinct, and a formatting failure renders an explicit unavailable state rather
than a plausible wrong number.

**Presentation surface.** A `Ready` host exposes only destinations backed by
verified behavior: Today, Ledger with transaction detail, and read-only
Settings. Destinations without verified behavior are absent rather than shown
disabled, and no portfolio total, market value, return, or goal progress is
displayed before its read contract exists.

**Client and accessibility.** Semantic HTML with a small local CSS system, no
npm build, CDN, web font, third-party component suite, chart library, or icon
package. Local JavaScript may enhance but every critical setup and navigation
action works without it. One restrained responsive layout serves a narrow
phone-sized viewport and a desktop viewport.

**Security.** Normal operation stays loopback-only. Every state-changing form
uses POST with antiforgery validation and Post/Redirect/Get; GET never mutates.
Cookies carry only framework antiforgery and essential presentation
preferences, never ledger values or authoritative workflow state. A restrictive
Content Security Policy applies, no external runtime request is made, and
routine logs and error pages omit SQL, connection strings, stack traces, raw
bodies, cursor payloads, and private values.

**State.** Readiness and setup progress are derived from M004 lifecycle status
and persisted core-master existence on every request. Browser storage, server
session, TempData, cookies, and new tables are never an authoritative setup
state machine.

**Verification.** One xUnit UI test project for PageModel, formatter, resource,
and rendered-host tests, plus one pinned Playwright Chromium project for the
critical first-run and read-navigation journey using synthetic data on isolated
paths.

## Refinements to ADR-007

ADR-007 remains Accepted. Two of its statements are narrowed explicitly rather
than reinterpreted:

1. ADR-007 states that incompatible API startup fails closed. That remains true
   of the ledger service. This ADR additionally permits the same executable to
   serve a narrowly restricted, read-only, loopback presentation surface that
   explains the sanitized failure category and the supported console command.
   No ledger route, mutation, or lifecycle action is mapped in that state.
2. M004 recorded that no operational history table is added to the ledger. A
   single-row, non-financial workspace lineage identity now lives in the
   database file. It is not history, it is outside the EF model, no ledger row
   or foreign key references it, and it is read with a bounded direct query.

## Consequences

Positive:

- one local process serves both accepted JSON routes and the household UI
  without self-HTTP or a second client state model;
- a phone browser and a desktop browser reach the same server-rendered pages,
  which suits the household's stated device intent without a native client;
- readiness is derived from verified lifecycle facts, so a restart reconstructs
  the same mode and no in-memory wizard state exists to lose;
- destructive lifecycle authority stays in the explicit console, away from a
  CSRF-reachable surface;
- exact fixed-point presentation keeps recorded values reproducible and keeps
  unknown values from silently becoming zero;
- later entry, valuation, and agent-analysis milestones inherit a stable shell,
  formatter set, and hosting model instead of inventing their own.

Costs:

- the API executable now composes two delivery mechanisms, which blurs its name
  until a future split is justified;
- server-rendered interaction is less immediate than a client-side application
  for future dense screens;
- Playwright adds a pinned browser download and a documented installation step;
- Turkish-first resources mean user-visible text and code vocabulary diverge and
  must be kept deliberately separate.

## Rejected alternatives

- Interactive Blazor Server, whose circuit-scoped lifetime, reconnect state,
  and concurrent component activity add risk around request-scoped use cases
  and `DbContext`.
- Blazor WebAssembly, React, or another SPA, which introduce a second client
  state model and duplicate HTTP mapping before a need exists.
- WPF, MAUI, Avalonia, or another desktop shell, whose packaging, update, and
  second-host decisions are not required for a local household flow.
- The UI calling the co-hosted JSON API over HTTP.
- Enabling write endpoints dynamically inside a process that started in
  maintenance mode.
- Browser-reachable migration, restore, active replacement, backup upload, path
  override, or SQL.
- Treating any verified `.wlbackup` in the configured directory as protection.
- Remote or LAN exposure, which requires a separate accepted authentication,
  authorization, and transport-security milestone.

## Not decided here

Remote access, authentication, authorization, and TLS deployment for the
household's intended home-server hosting remain undecided and require their own
milestone and ADR. Market and reference data, valuation, allocation policy, and
the agent read-contract remain in their ordered milestones.
