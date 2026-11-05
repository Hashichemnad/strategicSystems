# Design note — Phase 17: Dashboards and reporting (`Strategy.Platform.Reporting`)

SPEC §0.4 (all four levels, dashboard builder, report builder, period selectors, performance), §0.2 (access, data scope, audit),
§0.5 (AI principles), the Screens sections §1.8 / §1.12, §2.5, §3.6, §4.6, §6.4, §7.4, §8.5, §9.6, §10.3 C, §11.7, and Appendix A
(Q-P8: "as specified in §0.4 and each module's Screens section"). Written **before the code**; open choices are ADRs **0140–0149**
(status *Proposed* until the owner accepts the plan).

**Goal.** One reporting layer for the whole suite: every number on every dashboard, report and scheduled email comes from the module
that owns the data, through its Contracts, filtered by the viewer's access (system access → system-role permission within data scope →
record roles). Cached aggregates make the executive dashboard load in < 3 s on the seeded volume. Nothing organisation-specific in code:
dashboards, role defaults, thresholds, department level and schedules are configuration with seeded defaults.

## 1. Projects and boundaries

| Project | Content |
|---|---|
| `Strategy.Platform.Reporting.Contracts` (**new**) | What modules implement and consume: `IReportingMetricSource`, `MetricDefinition`, `MetricFact`, `IReportingDatasetSource`, `DatasetDefinition` / `DatasetField`, `ReportQuery` / `ReportResult`, `IMyWorkCardSource` / `MyWorkCard`, `ReportingPeriod`, `DrillLink`. No EF Core |
| `Strategy.Platform.Reporting` (existing library, **extended**) | Keeps `ExcelExport` / `PdfRendering`. Adds `ReportingDbContext` (schema **`reporting`**), the fact store and refresh engine, the scope resolver, dashboards / widgets / builder, report builder execution, schedules, executive dashboard and department scorecard, AI executive brief, My Work cards aggregator, jobs, endpoints `/api/v1/reporting/*`, access catalogue (system `reporting`), notification templates, `DatasetQuery<T>` helper (field whitelist → `IQueryable` filters / sort / group) used by modules |
| Each module (`Strategy.Modules.<M>`) | New folder `Reporting/`: one `IReportingMetricSource` (facts for its metrics, computed in **its own DbContext**), one `IReportingDatasetSource` (report-builder objects; query runs in its own DbContext with its own scope), one `IMyWorkCardSource`. Registered with `services.AddReportingSource<T>()` / `AddMyWorkCard<T>()` |

Rules (architecture tests extended): Reporting never references a module or reads another schema; modules reference only
`Platform.Reporting.Contracts` (+ the `DatasetQuery<T>` helper in `Platform.Reporting`, a Platform project). **No cross-schema SQL**: the
fact table is written by the reporting engine from values the modules return.

## 2. Reporting read model (ADR 0140, 0141, 0142)

### 2.1 Metrics and facts
A module declares metrics (`MetricDefinition`):

| Field | Meaning |
|---|---|
| `Code` | `<system>.<metric>`, e.g. `portfolio.projects`, `kpi.score`, `risk.open`, `auditmgmt.findings-open`, `tasks.overdue` |
| `LabelAr/En`, `UnitKind` | count · ratio · money (AED + currency) · score · hours |
| `Kind` | **stock** (state at period end: open findings, projects by RAG) or **flow** (events in the period: results approved, actions completed) |
| `Dimensions` | allowed breakdowns, e.g. `stage`, `rag`, `band`, `priority`, `source`, `status`, `objective`, `perspective`, `type`, `age-bucket` (codes; labels from lookups) |
| `Permission` | the module permission that must hold **for the record** (normally `<system>.dashboard.read` or the record read permission) — the resource type used for scope |
| `Aggregation` | sum · ratio-of-sums (numerator / denominator components) · weighted mean (Σw·x / Σw) — **always additive components** so they recombine over any set of units (ADR 0141) |
| `Drill` | link template to the system dashboard and to the record list with the same filters (`/kpi/cards?unit=…&objective=…`) |

`IReportingMetricSource.ComputeAsync(metricCodes, periodMonths, ct)` returns `MetricFact` rows:
`(metric, period_month, unit_id, partition, dims jsonb, n, num, den, w)` — one row per owning unit (the record's scope unit: project owning
unit, KPI card department, risk register unit, finding department, remark department, task assignee unit, team unit, document / IP / study
unit) and **partition** (see 2.2). Facts are stored in `reporting.fact` (unique `(metric, period_month, unit_id, partition, dims_hash)`;
indexes on `(metric, period_month)`), written in one transaction per metric (delete + insert of the refreshed months). The fact table is
**derived data**: not audited per row (CLAUDE.md §8 applies to business data; refresh runs are logged in `scheduling.job_run` and
`reporting.refresh_log`), runtime role SELECT / INSERT / DELETE on it only.

### 2.2 Scope-correct reading (ADR 0141)
For a metric the engine asks `IAccessService` for the viewer's **effective scope** on the metric's permission (new additive method
`GetScopeAsync(principal, permission) → {scope, unitIds (expanded), ownEmployeeId, recordGrants for the resource type}` built from the
existing cached access snapshot):
- **All** → every fact. **Unit / UnitAndBelow** → facts whose `unit_id` is in the expanded unit set.
- **Partitions** handle record-level restrictions that are not unit-based: `public` (default), `confidential` (Tasks confidential tasks,
  visible only to participants), `sensitivity:<code>` (Document Registry clearance), `costs` (Portfolio cost figures hidden per ADR 0098).
  The module source states which partitions the viewer may read (`VisiblePartitionsAsync(user)`); restricted partitions not in the list
  are excluded.
- **Live fallback** — the cache is used only when it is provably exact. The figure is computed **live** by the module for this viewer
  (`ComputeForUserAsync`, same code path as the facts but on the module's scoped query) when: the scope is Own or Team; the viewer holds
  active record-role grants on the metric's resource type outside the scope units (e.g. a project manager of another department's
  project); or the viewer reads a restricted partition record-by-record (confidential task participant). Live results are cached per
  user for 60 s (memory). Leadership (All) and unit-scoped managers — the dashboard audience — hit the cache.
- No system access, system disabled, or permission missing → the metric is **absent** (widget shows "not available to you"), never 0.

Consequence: two users with different scopes see different numbers on the same widget, and a user never sees a number that includes a
record they may not open (integration-tested per metric, see §11).

### 2.3 Periods and comparison (ADR 0142)
Facts are kept at **month grain** (`period_month` = first day). Period selector on every dashboard: **month · quarter · year · custom**
(custom = whole months from–to; the UI says so). Stock metrics read the **last month** of the period (state at period end, current
month = now); flow metrics **sum** the months. Comparison = the preceding period of the same length (quarter vs previous quarter,
custom range vs the same number of months before); the delta and arrow use the metric's "higher is better" flag. Past months are frozen at
month end (monthly snapshot job) — that is what gives "risk movement since last quarter" and KPI trend without history tables in modules.
Year boundaries follow the calendar year; KPI periods (monthly / quarterly / half-yearly / yearly cards) map to the months they cover.
The fiscal-year start month is a Settings → Reporting value (default January).

### 2.4 Refresh (ADR 0142)
- Job **`reporting.refresh`** (Quartz, every 10 min, configurable; `[DisallowConcurrentExecution]`): recomputes the **current month** of
  dirty metrics; job **`reporting.snapshot`** (01:30 on the 1st): freezes the previous month and recomputes it fully; job
  **`reporting.rebuild`** (manual from the job dashboard / Settings → Reporting): every metric, last 24 months.
- **Dirty marking on relevant changes:** (a) an additive `IAuditObserver` hook in Platform.Audit receives the system codes touched by each
  committed `SaveChanges` (the interceptor already knows them) and marks that system's metrics dirty in `reporting.dirty` (upsert, cheap);
  (b) integration events that change other systems' figures (`KpiResultApproved`, `ProjectClosureApproved`, `CapActionCompleted`,
  `AuditFindingClosed`, `RiskKriBreached`, `DocumentPublished`, …) are subscribed through the outbox and mark the metrics they affect;
  (c) a dirty mark schedules a one-off refresh within **60 s** (debounced trigger), so figures follow a change within about a minute.
  Every fact row carries `computed_at`; widgets show "as of hh:mm" and a **Refresh** button for users with `reporting.refresh` (queues the
  refresh, never runs it in the request).
- Idempotent: recomputing replaces the same rows. A failing module source is logged, its previous facts stay with their timestamp.

## 3. Widget catalogue and dashboards (ADR 0143)

### 3.1 Widget types (one React component each in `web/src/ui/dashboard/`, all ECharts except tiles / tables / lists / heat-grid)

| Type | Data | Notes |
|---|---|---|
| `tile` | one metric (+ comparison) | value, delta vs previous period, RAG colour from thresholds, sparkline (12 months) |
| `bar` (vertical / horizontal / stacked) | metric × dimension (+ series dimension) | top-N + "others" |
| `line` | metric over months (+ series) | trend, previous period dashed |
| `bubble` | three metrics per item (Portfolio value / complexity / budget) | item-level source |
| `heatmap` | metric × two dimensions (risk likelihood × impact, department × system) | **accessible button grid** (keyboard, `aria-label` per cell, the Phase 13 component moved to `ui`) — clickable cells drill down |
| `table` | metric × dimension(s) | sortable, export |
| `list` | item list from a module list source (Top-10 risks, delayed projects, upcoming milestones, late findings) | links to records; live (scoped) |
| `strategy-map` | perspectives → objectives with KPI score | SVG layout (perspective bands, objective nodes, cause-effect arrows from KPI strategy settings), coloured by RAG of capped score, RTL mirrored |
| `rag-grid` | rows (departments / projects) × columns (metrics) with RAG cells | used by the department scorecard |
| `gauge` / `progress` | one ratio | on-time rates |

Every chart: an **"Show as table"** toggle (accessible equivalent, also what the PDF uses), `aria-label` summary, RTL axis / legend
mirroring, light / dark tokens, click → drill link, "as of" time, export (PNG via `getDataURL`, Excel / PDF via the server).

### 3.2 Dashboard definitions (schema `reporting`)

| Table | Main columns |
|---|---|
| `dashboard` | code, title_ar/en, description, **level** (`system` · `executive` · `role` · `custom`), system_code (level system), owner_user_id (custom), status (`draft` · `published` · `archived`), version, layout jsonb (12-column grid: widget id, x, y, w, h), default_period, default_filters jsonb |
| `widget` | dashboard_id, type, title_ar/en, config jsonb (metric codes, dimension, series, filters, top-N, thresholds override, list source), sort |
| `dashboard_role` | dashboard_id, role_id, sort, is_default — the **role default dashboards** (one default per role; a user with several roles gets the defaults of all their roles as tabs) |
| `dashboard_share` | dashboard_id, principal (user / role / unit), can_edit |
| `user_pin` | user_id, kind (`dashboard` · `widget` · `report`), ref_id, sort — favourites (pinned to "My dashboards" and optionally to My Work) |
| `saved_filter` | user_id, dashboard_id, name, period, filters jsonb, is_default |
| `my_work_layout` | user_id, cards jsonb (ordered card codes + hidden flags) |

Seeded definitions (code, editable by admins, re-seed never overwrites an edited copy): **one system dashboard per system** (Level 3:
`portfolio.overview`, `kpi.scorecard`, `risk.profile`, `teams.overview`, `tasks.performance`, `docreg.library`, `ip.register`,
`bench.plan`, `cap.closure`, `auditmgmt.closure`, `requests.overview`), the **executive** dashboard (Level 4), and role defaults
(Executive viewer → executive; Strategy Office → kpi.scorecard; Risk Office → risk.profile; …). Publishing a dashboard version is audited;
editing a published dashboard creates a draft that replaces it on publish.

**Builder security (ADR 0143):** the builder offers only metrics / datasets whose permission the **builder** holds; a published dashboard
assigned to a role shows each viewer only the widgets whose metric permission **the viewer** holds, with the viewer's own scope (§2.2).
A dashboard definition never carries data, so sharing it can't leak numbers. Widget config is validated server-side against the
catalogue (unknown metric, dimension, filter field → 400).

### 3.3 Levels
- **Level 1 — My Work** (§6).
- **Level 2 — role / operational dashboards** stay module pages (PM project dashboard, PMO portfolio dashboard, KPI owner pending
  results, Risk Office heat-maps, Audit Office follow-up calendar, HR evaluation progress, CAP validation queue). Phase 17 makes them
  consistent: shared `DashboardFrame` (title, **period selector**, unit filter, system-specific filters, "as of", export menu PDF / Excel /
  image, saved filters), shared tiles / sections / charts, drill-down to records on every figure, `period` query parameters
  (`from`, `to` months) added to the existing dashboard endpoints where a date applies (each module documents its date field: Portfolio
  plan dates, KPI result period, Risk register year → review date, CAP assigned on, Bench target year, Audit finding date, Tasks due /
  closed, DocReg published / expiry, IP filing / expiry, Teams formed on / evaluation cycle). One export route shape for all:
  `GET /api/v1/<system>/dashboard/export?format=pdf|xlsx&…same filters` (old routes kept as aliases).
- **Level 3 — system dashboards** (one per system) are seeded builder definitions rendered by the generic dashboard page
  `/dashboards/<code>`, reachable from each system's sub-navigation and from the executive drill-down. Contents per SPEC Screens sections
  (table in §5).
- **Level 4 — executive dashboard** (§4).

## 4. Executive dashboard and department scorecard (ADR 0146)

Route `/dashboards/executive`, permission `reporting.executive.read` (role *Executive viewer*, seeded for the Director General /
leadership; not everyone). Sections render only for systems the viewer can access; every figure is a drill link
(executive → system dashboard with the same period / unit filter → record list with the same filters → record; every link target
re-checks access, 404 outside scope).

| Section | Widgets (metric codes) |
|---|---|
| Strategy performance | Organisation score tile (`kpi.score`, weighted **capped** performance, RAG) with trend vs previous period; objective scores (bar) and perspective scores; **strategy map** coloured by objective RAG; KPI trend line (actual, 12 months) |
| Portfolio | Projects by stage × RAG (stacked bar); budget vs actual by department (bar, cost partition); delayed projects (list); upcoming milestones (list, next 30 days); health heat-map (department × RAG) |
| Risk | Top-10 institutional risks (list); critical risks (tile); outside appetite (tile + list); movement since last quarter (improved / unchanged / worsened, from monthly snapshots); residual heat-map |
| Governance & compliance | Open audit findings by priority × age bucket (heat-map); overdue recommendations (tile + list); CAP remarks open / overdue by source type (stacked bar) |
| Organisation health | On-time rates by department: KPI submissions (approved results submitted by the period deadline), CAP actions (completed by due date), tasks (completed on time) — gauge + table; active committees (tile); documents expiring in 90 days (tile + list) |
| Department comparison | **Scorecard** `rag-grid`: one row per department (org level from Settings → Reporting `departmentLevelCode`, default `department`; facts of lower units roll up to their department): KPI score (capped), project health (% green of active projects, red count), open high / critical risks, outside appetite, open / late audit findings, CAP closure rate, overdue tasks. Cell RAG from thresholds in **Settings → Reporting → Scorecard thresholds** (per column: green ≥ / amber ≥, direction; seeded). Row click → department view (the same dashboard filtered to that unit); cell click → that system's dashboard filtered to the unit, then records |
| AI executive brief | §8 |

Performance budget (< 3 s on 500 projects, 1 000 KPIs, 1 000 risks, 2 000 findings / remarks, 10 000 tasks): the page makes **one**
request `GET /api/v1/reporting/executive?period…` that reads facts only (≈ 30 metrics × ≤ 2 periods × units; indexed), resolves the
viewer scope once, and returns all sections; lists (Top-10, delayed, upcoming, late) come from module list sources that read ≤ 10 rows
with indexed queries in parallel. Measured by an integration test with the seeded volume (server time < 1.5 s, end-to-end page < 3 s in
E2E on the same seed).

## 5. Level 3 system dashboards (seeded definitions)

| Dashboard | Widgets (SPEC Screens) |
|---|---|
| Portfolio health (§1.8, §1.12) | projects by stage, RAG heat-map, bubble (value × complexity × budget), budget vs actual by department, delayed projects, upcoming milestones, pipeline funnel (requests → approved → active → completed), Wasel submissions pending |
| KPI scorecard (§2.5) | totals by type / department / objective, achievement distribution (on target / below / not reported), pending updates, approval backlog, top / bottom performers, department scorecard, strategy map |
| Risk profile (§3.6) | totals by category / type / unit / rating, inherent vs residual heat-maps, outside appetite, Top-10, overdue actions / reviews, movement, KRI breaches, include project risks |
| Teams (§4.6) | by type / status, active committees, workload, evaluation progress, score / grade distribution, rewards by year / unit |
| Tasks (§11.7) | open / overdue by department, source, status, priority; average time to accept / complete; on-time rate; average feedback; workload |
| Document library (§6.4) | by level / type / unit, expiring / expired, pending approvals, recently published |
| IP register (§7.4) | by type / unit / year, registered vs pending, upcoming expiries |
| Benchmarking plan (§8.5) | studies by stage / quarter / unit, actions completion, overdue actions, completed studies, practices adopted |
| CAP closure (§9.6) | remarks by source / department / status / priority, actions by status, overdue and ageing, closure rate per unit, budget-dependent actions, effectiveness |
| Audit closure (§10.3 C) | findings by status / year / department / priority / report type, open vs closed trend, overdue and ageing, closure rate by department, repeat, risk-accepted |
| Requests (§5) | requests by type / stage / unit, average cycle time, pending reviews |

## 6. My Work — Level 1 (ADR 0149)

New contract `IMyWorkCardSource { SystemCode; GetCardsAsync(user) → MyWorkCard[] }`; `MyWorkCard(code, titleKey, counts[{key, value,
tone}], items[≤ 5 MyWorkItem], link)`. The aggregator (`GET /api/v1/reporting/my-work/cards`) calls only sources whose system is enabled
**and** open to the user (`ISystemState` + system access), in parallel, each with a timeout; a failing source yields an "unavailable" card,
never a broken page. Existing `GET /workflow/my-work` stays (approvals, action items, submissions, counters).

| Card | Source | Shown to |
|---|---|---|
| Pending approvals (all systems, delegated, overdue first) | Workflow | everyone with tasks |
| My tasks (assigned / created / follow-up, overdue) | Tasks | Tasks access |
| My KPI results to submit (open, due, returned) | KPI | KPI owners |
| My risks and treatment actions (reviews due, actions overdue, KRI breaches) | Risk | Risk access |
| My CAP remarks and actions (to respond, actions due, late) | CAP | CAP access |
| My projects and milestones due (PM / owner; monthly update due) | Portfolio | Portfolio access |
| Wasel submissions to review (PM) | Portfolio (Wasel) | PMs of projects with vendors |
| My teams and pending evaluations | Teams | Teams access |
| My requests — documents / IP / benchmarks (status) | Requests (+ DocReg / IP / Bench) | Requests access |
| Audit follow-ups due (auditors) | Audit Management | Audit Office |
| Notifications (latest, unread count) | Notifications | everyone |
| Pinned dashboards / widgets | Reporting | users with pins |

User layout: drag to reorder (dnd-kit, keyboard sortable), hide / show, reset — saved per user in `reporting.my_work_layout`; cards for
inaccessible systems never appear, whatever the layout says.

## 7. Report builder (ADR 0144)

- **Objects** = datasets declared by modules (`DatasetDefinition`: code, labels, resource type, read permission, fields). Seeded set:
  projects, milestones, program requests, KPI cards, KPI results, risks, treatment actions, teams, team members / evaluations, tasks,
  documents, IP records, benchmark studies, CAP remarks, CAP actions, audit findings, recommendations, requests.
- **Field** (`DatasetField`): code, labels, type (text, number, money, ratio, date, lookup, user, unit, bool), filterable / groupable /
  sortable, **field permission** (optional: e.g. budget fields need `portfolio.cost.read`; Emirates ID / masked fields never exposed),
  lookup type for labels.
- **Definition** `reporting.report`: dataset, columns, filters (field, operator from a whitelist per type, value; relative dates
  "this quarter"), grouping (≤ 2 levels with count / sum / average), sorting, owner, shares (users / roles / units), status.
- **Execution**: the module runs it in its own DbContext through `DatasetQuery<T>` on **its scoped query** (`ApplyScopeAsync` with the
  dataset's read permission, partitions as §2.2). At **every run** (UI, export, schedule) fields the runner may not see are removed from
  columns **and refused in filters / grouping / sort** (400 `reporting.errors.fieldNotAllowed` — a filter on a hidden field would leak it
  by inference); a shared report runs with the **runner's** rights, not the author's. Paging (≤ 200 per page), export Excel (ClosedXML,
  RTL sheet for Arabic) / PDF (Gotenberg, bilingual template) up to a configurable row limit (default 50 000), exports audited
  (`reporting.report.export`).

## 8. AI executive brief (ADR 0147)

AI feature `reporting-executive-brief` (permission `reporting.brief.generate`, Executive viewer). Inputs (only what the viewer may see):
the executive payload for the selected period and the previous one (scores, deltas, counts by section, per-department scorecard rows) and
the evidence candidates — the top lists with **reference ids** (`ref-1` … mapped server-side to `{type, id, number, title}`). Settings → AI
data controls apply (masking of names). Output schema (validated): `{ improved[], attention[], decisions[] }`, each `{ textAr, textEn,
evidence[refId] }`. Unknown ref ids are dropped (no invented links); the brief is shown as an **AI-generated draft**, bilingual side by
side or in the viewer's language, with links to the evidence (which re-check access on click). The user may edit and **save** it as the
period's brief (`reporting.brief`: period, scope description, text, saved_by) or copy it into a scheduled email. Logged in
`ai.usage_log`, audited (channel `ai`). Off / no provider → the section is hidden; the dashboard never waits on AI.

## 9. Scheduled delivery (ADR 0145)

`reporting.schedule`: target (dashboard code or report id), frequency (daily at hh:mm · weekly on day · monthly on day), period rule
(previous / current month · quarter · year), format (PDF · Excel · link only), recipients (users, roles, units — expanded at send time),
active, owner, last / next run. Permission `reporting.schedule.manage` (for one's own dashboards / reports; admins for any).
Job **`reporting.deliver`** (every 5 min): for each due schedule and **each recipient**: build the content **as the recipient**
(a principal built from their user — their roles, scope, system access, at send time), in **their language** (profile), with their
installation time zone; a recipient who may see nothing gets no email (logged). PDF: bilingual HTML template (tiles, tables, server-side
SVG bar / line for charts — no client JavaScript in Gotenberg, ADR 0096), Excel: one sheet per widget / the report. Email through the
notification pipeline (template `reporting.scheduled`, AR / EN, editable) with an **attachment** — additive change to Notifications:
`NotificationRequest.Attachments` (file ids in `IFileStore`, purpose `reporting.delivery`, kept 30 days, size cap 10 MB, otherwise the
email carries a link to the authorised download endpoint). Idempotent per (schedule, recipient, period) — `reporting.delivery` row.
Setting `reporting.deliveryMode = attachment | link` for installations that forbid attachments.

## 10. Permissions (system `reporting`, `SystemKind.Platform` — always on, no system grant needed)

| Permission | Default roles |
|---|---|
| `reporting.dashboard.read` (generic dashboards, My Work cards, pins, saved filters) | everyone |
| `reporting.executive.read` | Executive viewer (new role, MFA) |
| `reporting.dashboard.build` (custom dashboards for oneself, share) | Report author (new role), administrators |
| `reporting.dashboard.manage` (system / role defaults, publish, role assignments) | Reporting administrator (new role, MFA), platform administrator |
| `reporting.report.run` / `reporting.report.build` / `reporting.report.share` / `reporting.report.export` | run + export everyone; build + share Report author |
| `reporting.schedule.manage` | Report author (own items), Reporting administrator (all) |
| `reporting.refresh` (request refresh, rebuild) | Reporting administrator |
| `reporting.brief.generate` | Executive viewer |
| `reporting.settings.manage` (department level, thresholds, refresh interval, delivery mode, limits) | Reporting administrator |

And for every metric / dataset the **module** permission applies on top (§2.2, §7). All endpoints declare permissions; definitions
outside the user's ownership / share answer 404.

## 11. Front end

- **ECharts the single chart library (ADR 0148):** `web/src/ui/charts/` — `Chart` (lazy `import('echarts/core')` with only the used
  charts / components registered, `manualChunks: echarts`, theme from design tokens, RTL: `inverse` x axis and legend / visualMap on the
  start side, `aria` option + `role="img"` + summary + table toggle), typed builders `barOption`, `lineOption`, `bubbleOption`,
  `gaugeOption`. **Replaced:** CSS `Bars` in Tasks, Teams, KPI, IP, DocReg dashboards, KPI `ScoreBar` trend / yearly performance chart,
  Risk movement / assessment history SVGs, CAP / Audit `PctBar` where it is a chart. **Kept:** the risk heat-map button grid (moved to
  `ui/dashboard/HeatGrid`, keyboard and screen-reader accessible — better than canvas), progress bars inside tables, the in-house Gantt.
  Existing ECharts users (Portfolio dashboard, agile metrics, Bench, CAP, Audit) move to the shared wrapper.
- **Shared dashboard kit** `web/src/ui/dashboard/`: `DashboardFrame`, `PeriodSelector` (month / quarter / year / custom + compare
  toggle, URL-synced), `Tile`, `Section`, `ExportMenu` (PDF / Excel / PNG), `AsOf`, `DrillLink`, `WidgetRenderer` (type → component),
  `SavedFilters`, `PinButton`. Duplicates in `cap`, `auditmgmt`, `risk`, `bench` components removed.
- Pages: `/` My Work (cards, reorder / hide), `/dashboards` (my dashboards, pinned, role defaults), `/dashboards/executive`,
  `/dashboards/:code`, `/reports` (my / shared reports), `/reports/new|:id` (builder: object → columns → filters → grouping → sort →
  preview → save / share / schedule), **Settings → Dashboards** (builder: grid with drag / resize via dnd-kit, widget catalogue panel,
  preview as a role, publish), **Settings → Reporting** (department level, thresholds, refresh, delivery), **Settings → Schedules**
  (all schedules, run history). Namespace `reporting` in `ar` / `en`.

## 12. Notifications, jobs, audit

Templates (AR / EN, editable): `reporting.scheduled` (dashboard / report delivery), `reporting.shared` (a report or dashboard shared with
you), `reporting.deliveryFailed` (to the schedule owner). Jobs: `reporting.refresh`, `reporting.snapshot`, `reporting.rebuild`
(manual), `reporting.deliver` → the job dashboard test grows from 23 to 27. Audited: dashboard / report / schedule / settings changes
(entity audit), publish, exports, deliveries (recipient, format, rows), AI brief (channel `ai`), refresh requests.

## 13. Deployment fix

`deploy/docker-compose.yml`: `migrate` gets `pull_policy: build` and `app` gets **`pull_policy: never`** (it reuses the image `migrate`
builds), so `docker compose --profile app up -d --build` never tries to pull `strategy-app:dev`; `deploy/README.md` explains it (and that
`APP_VERSION` tags a locally built image, not a registry image).

## 14. Tests (CLAUDE.md §16)

- **Unit:** period arithmetic (month / quarter / year / custom, previous period, fiscal start), stock vs flow reading, additive
  recombination (ratio-of-sums, weighted mean) against brute force, scorecard RAG thresholds, widget config validation, report filter
  operators per type, schedule next-run calculation (time zone), AI brief evidence ref filtering, metric source contracts per module
  (table-driven facts on small fixtures).
- **Integration (Testcontainers):** each module's metric source vs its own live dashboard numbers (same data → same figures);
  **two users with different scopes see different numbers** on the executive and a system dashboard (All vs UnitAndBelow vs Own with record
  role → live path); restricted partitions (confidential tasks, document sensitivity, costs) never counted for non-cleared users;
  **report builder** refuses hidden fields in columns / filters / grouping, and returns only scoped records (shared report run by a
  narrower user); **scheduled email** delivered through Mailpit in **Arabic and English** with each recipient's permissions (two recipients
  → two different attachments); **aggregates refresh after a relevant event** (approve a KPI result → `kpi.score` fact changes within the
  debounced refresh, triggered synchronously in the test); **drill-down links** land on records the user may open (every list link 200
  for the viewer; a narrower user gets 404 on the other's links); **executive dashboard performance** on the seeded volume (bulk seeding
  of 500 projects, 1 000 KPIs, 1 000 risks, 2 000 findings / remarks, 10 000 tasks; request < 1.5 s server time after refresh);
  authorization matrix extended with every new endpoint; job dashboard (27 jobs); audit entries for publish / export / delivery.
- **Architecture:** Reporting references no module; modules reference `Platform.Reporting.Contracts`; no new forbidden packages.
- **Vitest:** `Chart` RTL options (axis inversion, legend side), `PeriodSelector`, widget renderer per type with table toggle,
  My Work layout reorder / hide (keyboard), report builder field gating, message keys AR / EN.
- **E2E (ar + en, screenshots in `docs/screenshots/phase-17/`):** My Work with all cards (reorder), executive dashboard, department
  scorecard → drill to a system dashboard → records, each system dashboard, dashboard builder (admin builds and publishes a role
  dashboard; a user of that role sees it), report builder (build, export, save, schedule), scheduled report email (Mailpit view), AI
  executive brief (demo AI provider).

## 15. Out of scope / later
Employee performance dashboard (SPEC §0.1, "to be specified"); real-time SignalR push of widget updates (dashboards poll "as of" every
5 min when visible); Hijri period selectors (display only, Settings toggle); geographic maps.
