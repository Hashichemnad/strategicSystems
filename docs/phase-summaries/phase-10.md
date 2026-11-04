# Phase 10 — Portfolio Management C: Agile / Hybrid, PM standards, Gantt, portfolio views: summary

**Status:** built and verified · ADRs 0090–0099 accepted by the owner · rebased on Phase 13 (`main` after PR #13)
**Branch:** `claude/happy-ramanujan-mujm5q`
**Planning:** the plan is the Phase 10 part of [`docs/design/portfolio.md`](../design/portfolio.md) (§29–§45), committed before the code. As in earlier
phases the session ran without an interactive approval step; open choices were recorded in ADRs 0090–0099, since accepted by the owner.

## What was built

| Area | Result |
|---|---|
| **Methodology** (ADR 0090) | `waterfall` / `agile` / `hybrid` on the project. The default comes from Settings `methodologySuggestions` (program type → methodology). The PM chooses it while drafting the charter, or before the first planning submission when the template has no charter. After that it changes only through an approved change request (area `methodology`) |
| **Agile model** | Releases, epics (weights = 100 %; in hybrid, 100 % of their sprint-delivered milestone), user stories (as-a / I-want / so-that, points, MoSCoW, assignee; tasks through Task Management, `portfolio.story` task-able), sprints. Sprint lifecycle planned → active → closed → in-approval → approved, one table, unit-tested. Review and retrospective at close (retrospective items become lessons learned). Impediments become RAID issues (challenges when RAID is off). The story history (`story_event`) feeds the metrics |
| **Board** | Columns, labels, categories and WIP limits configured per project; swimlanes none / epic / assignee. Drag-and-drop with dnd-kit, plus a keyboard "Move to…" menu. The server refuses a move into a full column and moving an approved story out of Done |
| **Agile progress and approval** (ADR 0091) | Progress = Σ epic weight × approved-done points ÷ epic points; the not-yet-approved figure is shown as "pending". Approval mode: **monthly roll-up** (default) or **per sprint** (process `portfolio.sprint-review`, Director → PM Office), overridable per project; hybrid is always monthly and its sprint-delivered milestones get their achievement from their epics. Agile and waterfall projects share the same progress %, which is what every portfolio view uses |
| **Metrics** | Velocity, sprint burndown (with ideal line), release burnup, cumulative flow, cycle time (Apache ECharts, with data tables) |
| **Feature switches** (ADR 0092) | Settings → Portfolio `features`: portfolios, prioritisation, health, baselines, earnedValue (**off** by default), raid, riskMatrix, raci, communicationPlan, benefits, quality, procurement, statusReport, gateChecklists, templates. When a switch is off, the UI hides the feature and its endpoints answer 404 `portfolio.errors.featureOff`; its gates and job do nothing |
| **RAG health** (ADR 0093) | Schedule / cost / scope / risk / overall, calculated on read from facts loaded in bulk; thresholds in settings. Each dimension can be overridden with a justification (required); clearing the override returns to the calculated value |
| **Baselines, variance, EVM** (ADR 0094) | v1 is taken **automatically** in the planning-approval hook (when the baselines switch is on and the project has none yet; asserted by the agile and waterfall integration tests). A new version is taken **only** by *Apply changes* of an approved change request with *Re-baseline* ticked; there is no other endpoint. A change request with area `plan` re-opens planning until it is applied. The variance view compares baseline, current and actual (days, weight, cost, added / removed). EVM: PV, EV, AC, SV, CV, SPI, CPI, EAC, ETC, VAC, TCPI |
| **Other standards** (ADR 0095) | Portfolios → programmes → projects; RAID log (team members' entries are *proposed* until the PM accepts them; milestone dependencies draw Gantt arrows); 5×5 risk matrix; RACI (one A per area); communication plan generated from stakeholders, with a daily reminder job `portfolio.communication-reminders`; benefits defined on the request, carried to the project and measured at Impact (impact gate); deliverable quality sign-off by the owner or PM Office (closure gate); contract payment milestones and supplier rating; configurable gate checklists (seeded items are optional); project templates (apply, save as) |
| **Status report** (ADR 0097) | `GET /projects/{id}/status-report?period&language`: a bilingual PDF (Gotenberg) built from the approved monthly update's snapshot. It shows the narrative AR/EN (typed or accepted from the AI writer), milestones, sprints, top risks, open issues, and the budget only for callers who may see costs |
| **Gantt** (ADR 0096) | In-house SVG (`web/src/ui/gantt`): phases collapsible, quarter bands over month ticks, planned / actual / baseline bars, achievement %, delayed rows (marked without relying on colour), milestone diamonds, dependency arrows, today line, mirrored in RTL, keyboard tooltips, PNG and PDF export (the PDF is written in the browser, with no library). Shown per project (with sprints and releases for agile) and portfolio-wide |
| **Portfolio dashboard** | Health heat-map by department (with R/A/G counts), value × complexity × budget bubbles, budget vs actual by department, projects by stage, pipeline funnel, delayed projects, upcoming milestones (60 days), projects table with methodology and the shared progress %, AI insights. Pages: Portfolio dashboard, Portfolio Gantt, Portfolios & programmes |
| **Cost visibility** (ADR 0098, closes the Phase 9 gap) | New permission `portfolio.project.costs` (roles and the owner, PM, requester, approver and procurement record roles), plus the team-member record role only when Settings `teamMembersSeeCosts` is on (default **off**). The single `CostVisibility` service is used by the project DTO and list, milestones, change requests (including the applied log), baselines, EVM (403), dashboard, status report, the new Ask / approver-brief project record source and the MCP project tool |
| **AI assists** (ADR 0099) | Charter assistant (KPIs, risks, stakeholders, planning items, plus lessons found by the server), plan generator (milestones with phases, or epics with stories; weights normalised to exactly 100 %), update assistant (at-risk milestones computed by the server), status report writer (AR + EN), change impact (dependent projects from the RAID log), portfolio insights; lessons search without a model. All results are drafts and nothing is saved until the user accepts. The demo provider answers each one deterministically |
| **MCP** | `portfolio_project_backlog`, `portfolio_sprint_summary`, `portfolio_summary` (read, on); `portfolio_create_story_draft` (write, **off**) |
| **Web** (AR + EN) | Project tabs Backlog, Board, Sprints (agile / hybrid only), Gantt, Health & baseline, RAID, RACI, Communication, Benefits (each only when its switch is on). On the Overview: methodology card, portfolio / programme card, gate checklist, and the AI charter-assistant and plan-generator buttons. Further UI: quality sign-off, Delivered in sprints and template buttons on Milestones; narrative, AI writer and PDF downloads on Monthly; re-plan, methodology, re-baseline and AI impact on Change requests; payments and rating on Contracts; Benefits tab on the request page. In Settings → Portfolio: switches, agile settings, cost visibility, health thresholds, gate checklists, templates editor |

## Acceptance checks (BUILD_PLAN Phase 10)

| Check | Evidence |
|---|---|
| An agile project runs two sprints with board, burndown and a monthly roll-up approval; it appears beside waterfall projects on the portfolio dashboard | Integration `An_agile_project_runs_two_sprints_…`: WIP refusal, burndown, close with retrospective → lessons, impediment → RAID issue, roll-up of both sprints in one `portfolio.monthly-update` approval, progress 0.9 (= 0.6 × 10/10 + 0.4 × 3/4), story locked after approval, dashboard lists the agile and a waterfall project, project and portfolio Gantt. E2E `portfolio-phase10.spec.ts` in **ar** and **en** with screenshots |
| Gantt renders correctly in RTL and LTR for a 30-milestone project in under 5 s | Vitest `Gantt.test.tsx` (30 milestones in 5 phases: 134 ms LTR / 71 ms RTL in jsdom; budget 1 s). E2E: a 30-milestone initiative's Gantt tab measured < 5 s in both languages; `ganttLayout.test.ts` checks RTL mirroring |
| Monthly status report PDF generated in both languages | Integration (`The_plan_is_re_baselined_…`): PDF from the approved update in ar and en, text checked with PdfPig. E2E renders page 1 to `ar-` / `en-status-report-pdf.png` |

Also asked for:
- agile progress formula (unit `AgileProgressCalculatorTests`);
- sprint and monthly roll-up modes (integration, both modes; in sprint mode the monthly job opens no window);
- baseline variance and re-baseline only after an approved change request (integration: refused before approval, plan frozen until the area-`plan` CR is approved, v2 after apply, variance shows +20 days; unit `BaselineVarianceTests`);
- EVM on the worked example (unit; integration on real figures);
- RAG calculation and override (unit at every boundary; integration: justification required, override wins, a viewer gets 403);
- each switch hides / shows its UI and APIs (integration over all 15 switches: 404 `featureOff`, then 200; UI hides tabs and sections from `/features`);
- a team member without cost permission sees no costs anywhere (integration covering project, list, milestones, change requests, baselines, EVM, dashboard and the Ask record source, then visible after the setting is turned on).

## Quality gates run (after the rebase on Phase 13)

| Gate | Result |
|---|---|
| `dotnet build -c Release` · `dotnet format --verify-no-changes` | 0 warnings, 0 errors · clean |
| `dotnet test --solution Strategy.slnx` | **3 559 / 3 559.** The first post-rebase run is void: PostgreSQL and Docker stopped during it (the session paused). After a restart, 3 541 passed and the 18 container-fixture tests were re-run green; the PDF layout snapshot passed on 3 more runs after one failure on a cold Gotenberg. Includes the Phase 10 unit tests (Portfolio unit namespace 196) and 5 Phase 10 integration tests |
| `tools/check-api-client.sh` · `tools/check-vulnerable.sh` | OpenAPI document and TS client regenerated after the rebase · no High / Critical |
| `npm run lint` · `format:check` · `typecheck` · `npm test` | clean · **288 / 288** Vitest (with Phase 13) |
| `npm run build` · `npm audit --audit-level=high` · `npm run lint:licenses` | OK (ECharts adds a size warning for the main chunk) · no High / Critical · 617 packages allowed |
| `npx playwright test` (whole suite, fresh database) | **92 / 92** in Arabic and English. The first run had 2 failures: the job dashboard expected 18 rows and there are now 19 (`portfolio.communication-reminders`); after updating the count, `platform.spec.ts` passes 7 / 7. Phase 10 screenshots refreshed from this run |

Existing tests updated for Phase 10 facts: the Portfolio MCP tool list; the job dashboard list (integration and E2E, now 19 jobs with Phase 13); the Phase 9 lifecycle test and E2E sign off the deliverable before closure (quality gate). Phase 9 helpers in `ProjectLifecycleTests` are now `internal` so the Phase 10 tests reuse them.

**Dependencies added:** `echarts` 5.6.0 (Apache-2.0; with zrender BSD-3-Clause, tslib 0BSD), recorded in `THIRD_PARTY_LICENSES.md`.

## Decisions (ADRs — accepted by the owner)
- [0090](../decisions/0090-methodologies-and-agile-model.md) Methodology choice and change; agile model; board rules; who may create / move stories.
- [0091](../decisions/0091-agile-progress-and-approval-modes.md) Progress counts approved work only; **monthly roll-up is the default**; hybrid always monthly.
- [0092](../decisions/0092-standards-feature-switches.md) Switches, defaults (EVM off), 404 when off; capacity view and quarterly portfolio review not built (not in the Phase 10 build plan).
- [0093](../decisions/0093-rag-health.md) RAG thresholds and override.
- [0094](../decisions/0094-baselines-and-earned-value.md) Baselines only via an applied change request; area `plan`; EVM inputs (AC = actual yearly costs).
- [0095](../decisions/0095-standards-data-model.md) RAID, RACI, communication, benefits, quality, payments, checklists, templates, portfolios.
- [0096](../decisions/0096-gantt-and-echarts.md) In-house SVG Gantt, browser-side export, ECharts.
- [0097](../decisions/0097-monthly-status-report-pdf.md) Status report generated on demand from the approved snapshot.
- [0098](../decisions/0098-cost-visibility.md) Cost visibility for team members (default off).
- [0099](../decisions/0099-phase-10-ai-and-mcp.md) AI assists and MCP tools.

## Deviations and behaviour changes
- **Quality gate:** with the `quality` switch on (default), closure is refused until every deliverable milestone is signed off by the owner or PM Office. Running Phase 9 flows must add the sign-off; the Phase 9 test and E2E now do.
- **Gate checklists:** seeded items are optional, so Phase 9 gates are unchanged until an organisation marks items required.
- **Cost visibility:** team members (record role `team-member`) no longer see costs by default (the safer value of the SPEC's "configurable").
- **Prioritisation switch off** means the four default criteria and bands are used; stored values are kept and come back when it is switched on.
- **Hybrid milestones delivered in sprints** are not validated for evidence or causality in the monthly update (the sprint reviews are their evidence); the PM cannot type their achievement.
- **Overdue linked tasks** (SPEC §1.16) count towards the schedule colour only for project-level tasks, not for tasks under a milestone.
- Project commands remain services called by the endpoints (as in Phase 9), not dispatcher handlers.

## Rebase on Phase 13
- Template risks are scored through the shared risk framework (`IProjectRiskScoring.ScoreAsync`, ADR 0101). The RAG risk dimension reads the residual band codes, which are the same in the framework's defaults (`low` / `medium` / `high` / `critical`); if an organisation renames its bands, the risk dimension does not recognise the new codes.
- Two OpenAPI schema names clashed with Risk types: they are now `PortfolioHeatCellDto` and `CharterRiskSuggestionDto`.
- Conflicts resolved: ADR index (both sets kept), job dashboard test (both jobs), generated client (regenerated). No chart-library conflict: Phase 13 draws its charts with CSS / SVG (ADR 0105).

## Platform / other modules (additive)
- `Strategy.Platform.AI` demo adapter: one branch for the six portfolio assists (development / E2E only).
- New scheduled job `portfolio.communication-reminders` and notification templates `portfolio.communicationDue` and `portfolio.sprintReviewed` (the second is registered but not yet sent).
- OpenAPI schema names: `ProjectTemplateContent` and `GateChecklistDto` / `GateChecklistItemDto`, to avoid clashes with the Teams / Tasks types.

## Known gaps
- §1.12 *capacity view* and *quarterly portfolio review records* are not built; they are outside the Phase 10 build-plan scope (ADR 0092).
- Gantt export uses the system Arabic font in the PNG/PDF (web fonts do not load on the export canvas).
- With swimlanes on, a board column appears once per lane.
- The Request page's Benefits tab is kept in local state (no deep link).
- The notification template `portfolio.sprintReviewed` is not yet sent; the generic `workflow.*` approval outcome mails cover the sprint review.

## How to demo
```bash
REGISTRY_PREFIX=mirror.gcr.io/ docker compose -f deploy/docker-compose.yml up -d postgres gotenberg mailpit
dotnet run --project src/Host/Strategy.Host
cd web && npm ci && npm run dev        # http://localhost:5173
```
1. Mariam: Settings → AI, add the demo provider. Settings → Portfolio: look at the switches (turn on Earned value).
2. Ahmed: create a request with a screening answer No (an initiative). Mariam approves → Ahmed takes ownership → Overview → *Choose methodology* → Agile. *Generate plan (AI)* to see the draft.
3. Backlog: a release, two epics (60 / 40), stories → Submit planning; Mariam approves twice → Execution.
4. Sprints: create, start; Board: drag stories (try a WIP limit); close with review / retrospective; Sprints tab shows the burndown and metrics.
5. Mariam: *Open window now*; Ahmed: Monthly → narrative (or *Draft with AI*) → Submit; Mariam approves → progress moves; download the status report PDF (Arabic / English).
6. A waterfall project: Gantt tab; a change request with *Re-plan* → approve → move a milestone → Apply with *Re-baseline*; Health tab shows RAG (override it), baseline variance and EVM; RAID, RACI.
7. Portfolio dashboard and Portfolio Gantt in the sidebar; switch the language to see RTL.

## Cloud environment notes
- PostgreSQL 16 local (`max_connections = 600`, `STRATEGY_TEST_POSTGRES`); Gotenberg / Mailpit containers through `mirror.gcr.io`; Tesseract installed during the session.
- E2E with `E2E_CHROMIUM_PATH=/opt/pw-browsers/chromium-1243/chrome-linux64/chrome` on a fresh `strategy` database.
- The web screens were built by three parallel helpers on separate files (agile, standards, views / settings) and wired into the project page afterwards.
- One issue found and fixed: a dependency cycle (approval engine → hooks → agile service → engine) through a factory registration made the integration host hang instead of failing; the agile service now resolves the engine lazily.
- Screenshots (`E2E_SCREENSHOTS=phase-10`) in [`docs/screenshots/phase-10/`](../screenshots/phase-10/) as `ar-*` / `en-*` pairs: `methodology-choice`, `ai-plan-generator-review`, `agile-board`, `sprint-burndown`, `monthly-rollup-approval`, `status-report-pdf`, `gantt-project`, `gantt-portfolio`, `rag-health`, `baseline-variance`, `evm-panel`, `raid-log`, `raci`, `portfolio-dashboard`.
