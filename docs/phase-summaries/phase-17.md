# Phase 17 — Dashboards and reporting (cross-system): summary

Status: **built — awaiting owner acceptance** (ADRs 0140–0149 accepted by the owner before the build). Design note:
[`docs/design/reporting.md`](../design/reporting.md) (§16 "As built").

## What was built

| Area | Result |
|---|---|
| **Reporting read model** (`Strategy.Platform.Reporting` + new `Strategy.Platform.Reporting.Contracts`, schema `reporting`) | Every module exposes its figures through Contracts: `IReportingMetricSource` (metrics with AR / EN labels, dimensions, scope permission), `IReportingListSource` (short scoped lists), `IReportingDatasetSource` (report-builder objects), `IMyWorkCardSource`, optional `IReportingDimensionLabelSource` (labels kept in module tables). Each module computes on **its own DbContext** — no cross-schema SQL. Facts are stored per **month, owning unit, partition and dimensions** as additive parts (value / weight), so any set of units recombines exactly (ADR 0140 / 0141) |
| **Scope-correct figures** (ADR 0141) | New `IAccessService.GetScopeAsync` (effective scope from the cached access snapshot). The cache is used only where it is exact: scope All, or Unit / Unit-and-below without record-role grants; restricted partitions (`costs`, `confidential`, `office`, `sensitivity:<level>`) only for users the module clears. Own / Team scopes and record-role holders are computed **live** by the module on its scoped query (cached 60 s). A metric the viewer may not see is *not available*, never 0 |
| **Refresh** (ADR 0142) | `reporting.refresh` every minute: systems marked dirty by saved changes (an `ISaveChangesObserver` called by the audit interceptor) within ~20 s, every system every `RefreshMinutes` (10); `reporting.snapshot` (1st of the month), `reporting.rebuild` (24 months, on demand); a PostgreSQL advisory lock per system serialises writers. Month-grain periods: month / quarter / year / custom whole months, comparison with the preceding period of equal length, fiscal start month in Settings |
| **Metric catalogue** | 41 metrics, 15 lists, 24 datasets across Portfolio, KPI, Risk, Teams, Tasks, Requests, Document Registry, IP, Benchmarking, CAP, Audit Management and the Sample template (design note §2.5 and the module reports): e.g. `kpi.score` (weighted **capped** performance), `portfolio.health`, `risk.risks` / `risk.heat` / `risk.movement`, `auditmgmt.findings` (age buckets), `cap.closure`, `tasks.on-time` |
| **Dashboards** (ADR 0143) | Widget catalogue: tile (delta, RAG, sparkline), bar, line, bubble, heat-map (accessible grid), table, list, strategy map, department scorecard, gauge, AI brief. Definitions (JSON, validated against what the builder may see) with draft → publish versions, role defaults, shares, pins, saved filters. Seeded and editable: **one system dashboard per system** (Level 3) and the **executive dashboard** (Level 4); re-seeding never overwrites an edited copy |
| **Level 4 — Executive dashboard** (ADR 0146) | Strategy performance (organisation score, objective scores, strategy map, trend), portfolio (stage × health, budget vs actual, delayed projects, upcoming milestones), risk (Top-10, critical / high, outside appetite, movement, residual heat-map), governance (open findings by priority × age, overdue recommendations, CAP remarks by source), organisation health (on-time rates for KPI submissions, CAP actions and tasks; active committees; documents expiring), **department comparison scorecard** (one row per department at the configured org level; limits per column in Settings → Reporting), drill-down (figure → system dashboard filtered to the unit and period → records), **AI executive brief** |
| **Level 1 — My Work** (ADR 0149) | Cards from every system the user may open: pending approvals, my tasks, my KPI results, my risks and actions, my CAP remarks and actions, my projects and milestones, Wasel submissions to review, my teams and evaluations, my requests (documents / IP / benchmarks), audit follow-ups (Audit Office), benchmarking studies awaiting me, notifications, pinned dashboards. Reorder by drag or keyboard, hide / show, saved per user |
| **Level 2 — role dashboards** | Module dashboards accept `from` / `to` (each module's natural date) and use the shared frame, period selector and kit; each links to its system dashboard and exports through the reporting export |
| **Report builder** (ADR 0144) | Object → columns → filters (operator whitelist per type) → grouping (≤ 2) → sorting → preview (paged), Excel / PDF export (bilingual, RTL), save, share (users / roles / units), schedule. Hidden fields are dropped from columns and **refused** in filters, grouping and sorting; shared and scheduled reports run with the **runner's** rights |
| **Scheduled delivery** (ADR 0145) | Daily / weekly / monthly, period rule, PDF / Excel / link; recipients users, role holders (direct, unit, everyone) and units below, expanded at send time; content built **as each recipient** in **their language**; nothing visible → no e-mail; idempotent per (schedule, recipient, period); additive e-mail attachments in the notification pipeline (files kept 30 days, readable only by the recipient) |
| **AI executive brief** (ADR 0147) | Feature `reporting-executive-brief`: built from the viewer's own executive dashboard (current and previous period); evidence cited by opaque references mapped server-side (unknown ones dropped); shown as an AI-generated draft, editable, saved by a person |
| **Charts** (ADR 0148) | One `Chart` wrapper on Apache ECharts (lazy, only the used charts; own chunk 556 kB / gzip 187 kB instead of the whole library), RTL mirrored centrally, tokens for light / dark, `aria` description and "Show as table"; the interim CSS / SVG charts of Tasks, Teams, KPI, IP, DocReg and Risk are replaced; the risk heat-map stays an accessible button grid (`HeatGrid`); shared kit: `DashboardFrame`, `PeriodSelector`, `Tile`, `Section`, `ExportMenu`, `RagGrid`, `StrategyMap`, `CountChart`, `ProgressBar` |
| **Deployment fix (owner request)** | `--create-admin` no longer migrates on the runtime connection (it failed with "permission denied for schema core"): with `ConnectionStrings:StrategyMigrations` set to another role it migrates as the owner first, in a separate host; otherwise it requires `--migrate` first (exit 3, clear message). Compose: `migrate` `pull_policy: build`, `app` `pull_policy: never`. `deploy/README.md` first start now works as written (password on standard input — a root-only `chmod 600` file mounted from the host is unreadable by the non-root container user) |

## Acceptance checks and requested tests

| Check / test | Evidence |
|---|---|
| A Director General sees the executive dashboard and drills from a red department into its records | E2E `reporting.spec.ts` (ar + en): executive dashboard → scorecard cell → system dashboard filtered to the department → record link; integration `ExecutivePerformanceTests` (scorecard rows per department) |
| Two users with different scopes see different numbers on the same dashboard | `ReportingEngineTests.Two_users_with_different_scopes_…` (All 6, unit 3 from the cache, own 1 live), `ExecutivePerformanceTests` (Director General vs unit-scoped executive viewer: fewer active projects, one scorecard row); per-module live-vs-cache tests in every `<Module>ReportingTests` |
| Admin builds a new dashboard for a role without code | `ReportingEngineTests.The_builder_offers_and_accepts_only_metrics_the_builder_may_see_and_admins_publish_role_dashboards_without_code`; E2E dashboard builder (publish) |
| Every widget and report respects access scope | Engine tests above + restricted partitions (`costs` amounts only for editors; Portfolio costs, confidential tasks, office audit findings, document sensitivity in the module tests); a metric without access is *unavailable* |
| Report builder cannot expose fields or records the user may not see | `The_report_builder_never_exposes_fields_or_records_the_runner_may_not_see` (dropped column, 400 on filter / sort by a hidden field, shared report runs with the runner's scope) + module dataset tests |
| Scheduled e-mail in both languages with the recipient's permissions | `A_scheduled_report_is_delivered_to_each_recipient_in_their_language_with_their_permissions_at_send_time` (Arabic recipient: RTL sheet, Arabic headers, 3 rows; English recipient: LTR, all rows; idempotent re-run); E2E: Mailpit message (screenshot) |
| Executive dashboard performance on the seeded volume | `ExecutivePerformanceTests` — 500 projects, 1 000 KPI cards (+ results), 1 000 risks, 2 000 findings, 2 000 remarks, 10 000 tasks: seeding 471 ms, full refresh of all systems 994 ms, executive dashboard data (Director General, year, three timed runs after a warm-up) 77 / 151 / 72 ms — target < 3 s |
| Aggregates refresh after a relevant event | `ReportingRefreshTests` (a save marks the system dirty; the cache keeps its figure until the refresh; the refresh updates it) |
| Drill-down links land on records the user may open | `Drill_down_links_land_on_records_the_viewer_may_open_and_not_on_others` + list-link checks (200 / 404) in the module tests |
| First start of the documented Docker install (owner request) | `MigrationTests.First_start_on_a_fresh_two_role_database_…` (init role, migrate as owner, `--create-admin` in the app service and in the migrate service, refused before `--migrate`, administrator signs in); manual run of `deploy/README.md` on a fresh Docker volume (below) |

## Quality gates run

| Gate | Result |
|---|---|
| `dotnet build Strategy.slnx -c Release` | 0 warnings, 0 errors |
| `dotnet format Strategy.slnx --verify-no-changes` | clean |
| Unit + architecture tests | 1 930 / 1 930 |
| Integration tests (Testcontainers / local PostgreSQL 16) | 2 567 / 2 567 (a Document Registry test that compared against the UTC date failed between 20:00 and 24:00 UTC — fixed to use the installation's date; one CAP due-date test passed on re-run) |
| Web: `lint`, `format:check`, `typecheck`, `test`, `build` | pass; Vitest 367 / 367 |
| `npm audit --audit-level=high`, `lint:licenses` | no high / critical (8 moderate, dev tooling); 617 packages, all allowed licences |
| `tools/check-vulnerable.sh`, `tools/check-api-client.sh` | E2E_GATE_VULN |
| Playwright E2E (`ar` + `en`) | E2E_GATE_RESULT |
| Fresh Docker install per `deploy/README.md` | DOCKER_GATE |

## Defects found while verifying (fixed in this phase)
- **Scheduled jobs never ran on their schedule (since Phase 3).** `JobRunListener` read `triggeredBy` with `GetString`, which
  throws on a missing key in Quartz 3.22; only manual runs carry the key, so Quartz logged "Job will NOT be executed" for every
  cron firing — reminders, SLA escalation, KPI windows, digests, the reporting refresh and all other jobs ran only when started from
  the job dashboard. Integration tests call jobs directly, so they never saw it; the Phase 17 E2E (waiting for the refresh job) did.
  Fixed with `TryGetString`; `JobDashboardTests` now fires a run without the key. Because cron jobs now really fire, integration
  test hosts set the new `Scheduling:RunSchedules=false` (default true), which vetoes only schedule-fired runs.
- `/dashboards/<system.key>` returned a 404 problem on a deep link or refresh (a dotted last segment looks like a file) — added to
  the SPA fallback routes.
- My Work count keys from Tasks, Risk and Requests carried the `myWork.counts.` prefix the client adds itself (chips showed the
  generic error text); the contract now says count keys are bare codes.
- The approvals card and the detailed approvals list were both regions named "Pending approvals" — the card is now "My approvals".
- Document Registry expiry test compared with the UTC date (failed 20:00–24:00 UTC); Wasel E2E navigated before the password was saved.

## Decisions (ADRs, accepted by the owner)
0140 reporting read model through Contracts · 0141 scope-correct cached aggregates · 0142 periods, snapshots and refresh ·
0143 widget catalogue and dashboard builder · 0144 report builder datasets and field security · 0145 scheduled delivery as the recipient ·
0146 executive dashboard and department scorecard · 0147 AI executive brief · 0148 ECharts as the single chart library · 0149 My Work cards.

## Deviations
- **From the SPEC:** none known.
- **From the design note** (recorded in §16): refresh job every minute (dirty systems + periodic full refresh) instead of every 10 minutes
  plus a separate trigger; one dashboard export route under `/api/v1/reporting`; permission `reporting.aggregate.refresh`; delivery failures in
  the delivery log instead of a separate e-mail; Requests has no Level 2 dashboard page (its system dashboard is the builder's).
- **Shared files changed additively:** Access (`GetScopeAsync`, `EffectiveScope`, `IUserRoleQuery.RoleKeysOfAsync` / `OpenSystemsAsync`),
  Persistence (`ISaveChangesObserver` after audited saves; `ModuleMigrator.HasPendingMigrationsAsync`), Notifications (`AttachmentFileIds`,
  `EmailMessage.Attachments`, My Work card), Workflow (approvals card), AI demo provider (brief answer), `PdfDocument` internal blocks, Host
  (platform catalogue, dev seed reporting roles for Mariam, `--create-admin`), home page (cards; the three hand-made Tasks / KPI / Risk
  cards are replaced by the module card sources), router, sidebar, settings navigation, job dashboard and authorization-matrix test lists.
  Each module: `Reporting/` folder, DI registrations, dashboard `from` / `to`.

## Known gaps
- Live figures (Own / Team scope, record-role holders) cover the current month for stock metrics; past-period comparisons of stock metrics
  are cache-only (monthly snapshots), so they show "—" on the live path.
- Document Registry: users cleared for a sensitivity level only in their own unit see those documents through the module screens; the
  cache counts only levels cleared everywhere (an under-count, never a leak).
- Risk: project risks are not in `risk.*` metrics (they are Portfolio records under project scope); the Risk module dashboard still shows them.
- KPI weights: a card without a weight counts as weight 1 in `kpi.score` (facts must add up across units); identical to the scorecard
  unless weighted and unweighted cards are mixed in one set.
- Dashboards poll every 5 minutes while visible; no SignalR push of widget updates.

## How to demo
```bash
REGISTRY_PREFIX=mirror.gcr.io/ docker compose -f deploy/docker-compose.yml --profile dev up -d postgres gotenberg mailpit
dotnet run --project src/Host/Strategy.Host
cd web && npm ci && npm run dev        # http://localhost:5173
```
1. **Mariam** (`mariam@strategy.local`, MFA — demo Director General): My Work shows a card per system (*Customise* to reorder / hide).
   *Executive dashboard* (sidebar): period selector, scorecard — click a red cell → that system's dashboard for the department → a record.
   *Draft the brief* (Settings → AI with the demo provider): AI draft with evidence links; edit and save.
2. *Dashboards* → each system dashboard; *Settings → Dashboards*: new role dashboard, add widgets, choose roles, *Publish*.
3. *Reports* → *New report*: object, columns, filters, grouping, run, export Excel / PDF, save, share, *Schedule by e-mail*
   (Mailpit at http://localhost:8025 receives it in the recipient's language).
4. **Ahmed** (`ahmed@strategy.local`): the same dashboards show only his units' figures.

## Environment notes
- PostgreSQL 16 local (`STRATEGY_TEST_POSTGRES`, `max_connections = 600`); Docker for Gotenberg / Mailpit and Testcontainers images through
  `mirror.gcr.io/`. The shared disk filled once during the build (parallel worktrees); test databases and Docker build cache were pruned.
- Screenshots (`E2E_SCREENSHOTS=phase-17`) in [`docs/screenshots/phase-17/`](../screenshots/phase-17/) as `ar-*` / `en-*` pairs.
