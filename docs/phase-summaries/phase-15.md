# Phase 15 — Corrective Action Plan and Benchmarking: summary

Status: **built — awaiting owner acceptance**. Design notes: [`docs/design/cap.md`](../design/cap.md),
[`docs/design/benchmarking.md`](../design/benchmarking.md). ADRs **0120–0129** (status *Proposed*).

## What was built

| Area | Result |
|---|---|
| **CAP** (`Strategy.Modules.Cap` + `.Contracts`, schema `cap`, system `cap`) | **Sources** entered manually (workshop, audits, assessment, complaint…) or created by another system through the **source-system contract** `ICapRemarkSources` (live link to the originating record). **Remarks** `CAP-{UNIT}-{YYYY}-{SEQ:3}` with type, criterion, priority and **due dates by priority** (Critical 30 · High 60 · Medium 90 · Low 180 days, calendar or working days by the unit's business hours — Settings). Owner = section manager by default, reassignable (optionally within the unit). **Accept / reject** — rejection **final by default**, optional **PM Office confirmation** (confirm or send back). **Root cause** (5 Whys, fishbone, text) required by a **configurable rule** (sandboxed expression; default: critical / high priority or audit sources). **Actions** `{RemarkNo}-{n}`, new or **linked to several remarks** (many-to-many), type, budget required / available / cost, process, assignee, dates; **PM Office validation per action** (approve / reject / return, link project) through the approval engine (`cap.action-validation`); **budget routing**: an approved action that needs an unavailable budget creates a **draft Program Request with source "Action Plan"** in Portfolio, linked back. **Achievement updates** with history (MCP drafts confirmed by people), **late** status, owner notice and **PMO escalation** after N working days, **monthly reminders** on the configured day, **extension requests** (`cap.extension`), **evidence + completion approval** (`cap.completion`), optional **effectiveness verification** (off), derived remark status, completion reported back to the source system (`CapRemarkStatusChanged`, `CapActionCompleted`). Current-status messages, My Work cards (remarks to accept / plan / correct, actions to update, actions to validate, rejections to confirm), dashboard and bilingual exports, AI (extraction, root-cause assistant, action suggestions with duplicate detection, quality check, monthly summary), MCP (`cap_my_remarks`, `cap_my_actions`, `cap_remarks_by_source`, `cap_overdue_actions` on; `cap_update_achievement_draft` off), task-able / remark-source in Links, job `cap.monitor` |
| **Integrations wired** | **KPI target missed** → suggested remark (one click) or automatic (setting, consecutive periods); **Risk event** and **ineffective control** → remark on a source per risk via the Phase 13 contract; the risk event's chip now links to the CAP remark. KPI cards / results are remark sources in Links. **Portfolio** contract `IProgramRequestDrafts` (draft creation, progress read for optional mirroring) and the `action-plan` suggestion-source value (additive) |
| **Benchmarking** (`Strategy.Modules.Benchmarking` + `.Contracts`, schema `bench`, system `bench`) | **Plan** as built-in request type `bench.request` on the Request Framework (PMO approve / return / reject) → study `BMS-NNNN` in the **annual plan**, circulated by the PMO. **Search** form notice **30 days before the target quarter** (working or calendar days, configurable) — search form (entities from the register or new, reasons, code of conduct) → PMO approval (`bench.search`) → **outreach log** and ratings. **Acquire**: visit info (date, method, participants, questionnaire, notes) → Analysis. **Analysis**: practice rows, indicator rows (KPI links, optional KPI benchmark update), **development plan** → PMO approval (`bench.analysis`) creates a **CAP source with one remark per item and its proposed action**; best practices to the library. The study shows the **linked CAP actions live** and **closes only after PMO final approval (`bench.closure`) when all linked actions are completed with evidence**. Comparator register (history, ratings), best-practice library (keyword + AI search), impact follow-up, team link, dashboard, current status, My Work, AI (comparators, questionnaire, visit analysis, summary, library search), MCP (`bench_studies`, `bench_study_details`, `bench_best_practices`), job `bench.monitor` |
| **Web** (AR + EN, RTL / LTR) | CAP: dashboard (tiles, charts, by department, budget-dependent actions, exports, AI summary), sources, source page (remarks, add remark, AI extraction), remarks register (views / filters), remark page (stepper, current status with Accept / Reject / Confirm / Send back / Submit / Verify, overview with KPI trend, root cause editor, actions with budget questions, link existing, AI suggestions, PMO validation dialog with routing notice and project link, tasks, documents, history), actions list (views), action page (achievement update + history, evidence, extensions, completion, approvals, quality check), suggested remarks, Settings → Corrective action plan. Benchmarking: dashboard, annual plan + circulation, studies, study page (search form with outreach / ratings, visit info with AI questionnaire, analysis with development plan and AI analysis, linked CAP actions, impact, approvals, tasks, documents, history), comparator register, best-practice library, Settings → Benchmarking |

## Acceptance checks (BUILD_PLAN Phase 15)

| Check | Evidence |
|---|---|
| Workshop remarks → section manager accepts → actions validated → monthly updates → completion approved | Integration `CapApiTests.Workshop_remarks_are_accepted_validated_updated_monthly_extended_and_completed_with_evidence` (monthly reminder once per month, late notice + PMO escalation, extension, completion refused without evidence then approved, remark completed; register / departmental report exports). E2E (ar + en): `cap-remarks-source`, `cap-remark-accept-reject`, `cap-root-cause`, `cap-remark-actions`, `cap-pmo-validation`, `cap-achievement-update`, `cap-extension-request` |
| A missed KPI and a risk event each raise a CAP remark linked to their source | Integration `A_missed_kpi_target_and_a_risk_event_each_raise_a_remark_linked_to_their_source` (one-click suggestion for the KPI, automatic for the event, live link, the event's back-link) |
| A benchmark study runs Plan → Search → Acquire → Analysis; its actions appear in CAP; the study closes when they complete | Integration `BenchApiTests.A_study_runs_plan_search_acquire_analysis_hands_its_development_plan_to_cap_and_closes_when_the_actions_complete` (plan return / resubmit, circulation, closure refused while actions are open — `bench.actionsOpen`, approved once all are completed with evidence, best practice, KPI benchmark). E2E: `bench-plan`, `bench-search-form`, `bench-visit-info`, `bench-analysis`, `bench-linked-actions` |

| Requested test | Evidence |
|---|---|
| Rejection final by default; PMO confirmation when on | `Rejection_is_final_by_default_and_waits_for_the_pm_office_when_confirmation_is_on` |
| One action, two remarks | `One_action_linked_to_two_remarks_serves_both_and_completes_both` |
| Due dates by priority in working time | `Due_dates_follow_the_priority_in_working_time` (fake clock) + unit tests |
| Budget routing → draft Program Request, source "Action Plan" | `A_budget_needing_action_without_budget_creates_a_draft_program_request_with_source_action_plan`; E2E `cap-action-budget-routing`, `cap-budget-routed`, `cap-dashboard` |
| 30-day pre-quarter notification (fake clock) | `The_search_form_notice_is_sent_once_30_days_before_the_target_quarter` |
| Scope rules | `Scope_rules_follow_the_remark_owner_assignee_department_manager_and_pm_office`, `Scope_rules_follow_the_coordinator_topic_owner_unit_and_office` (outsider 404, forbidden action 403) |
| MCP read on / write off | `Mcp_tools_list_my_remarks_and_actions_and_the_write_tool_is_off_by_default` |

## Quality gates run

| Gate | Result |
|---|---|
| `dotnet build Strategy.slnx -c Release` · `dotnet format --verify-no-changes` | 0 warnings, 0 errors · clean |
| `dotnet test --solution Strategy.slnx` (after the rebase on Phase 11) | **4 350 / 4 350** — unit, architecture and integration on real PostgreSQL with the Mailpit / Gotenberg / Keycloak / OpenLDAP containers and Tesseract (OCR). New in this phase: 20 unit test methods (lifecycles, derived status, quality flags, settings / root-cause rule, study stages, quarters) and 11 integration tests; authorization matrix, job dashboard (now with `portfolio.wasel-access`, `cap.monitor`, `bench.monitor`) and access-administration tests updated |
| `tools/check-api-client.sh` · `tools/check-vulnerable.sh` | OpenAPI document and TS client regenerated after the rebase and up to date · no High / Critical |
| `npm run lint` (incl. RTL class check) · `format:check` · `typecheck` · `npm test` | clean · **316 / 316** Vitest (new: CAP and Benchmarking message keys in AR and EN, codes shown in the UI) |
| `npm run build` · `npm audit --audit-level=high` · `npm run lint:licenses` | OK · no High / Critical (8 moderate advisories, unchanged dependencies) · 617 packages allowed |
| `npx playwright test` (full suite, ar + en, fresh database) | **98 / 98** (49 specs × ar + en; the first run had 97 / 98 — the History / Documents export test, fixed as noted below) |

## Decisions (ADRs, *Proposed*)
0120 data model and numbering · 0121 lifecycle and approvals · 0122 due dates, late status and reminders · 0123 source-system contract ·
0124 budget routing to Portfolio · 0125 CAP access model · 0126 Benchmarking on the Request Framework (+ office holds `requests.office`) ·
0127 annual plan and pre-quarter notice · 0128 Benchmarking → CAP, closure, KPI benchmark, library · 0129 AI and MCP.

## Deviations
- **From the SPEC:** none known; interpretations are in the ADRs.
- **From the design notes (notes updated, §16 / §13 "As built"):** office permissions are `cap.office.manage` / `bench.office.manage`
  (three-part codes); configuration key `Modules:Bench`; removed Benchmarking form rows are soft-flagged (no hard deletes of audited
  rows); best-practice tags stored as text; ineffective-control suggestions keyed per control.
- **Shared files changed additively:** Host module catalogue, appsettings, dev seed (everyone may open `cap` and `bench`; Mariam holds both
  office roles); Portfolio contract + `action-plan` lookup value; KPI benchmark writer + remark-source capability; demo AI answers for the
  new prompts; web router, sidebar, settings navigation, History records, AI feature keys, i18n index, `BUILD_PLAN.md` checklist row.
  Three existing Vitest files loosen the typing of their `api.POST` spy: the larger OpenAPI client reaches TypeScript's instantiation-depth
  limit (TS2589); behaviour unchanged.

## Known gaps
- Actions created from a benchmarking development plan have no action type; the owner sets it before submitting (the server requires it).
- The study's KPI indicator picker lists the first 200 KPI cards (no search-as-you-type yet).
- Visit participants are added one by one with the employee picker; names of newly added participants show after saving.

## How to demo
```bash
REGISTRY_PREFIX=mirror.gcr.io/ docker compose -f deploy/docker-compose.yml --profile dev up -d postgres gotenberg mailpit
dotnet run --project src/Host/Strategy.Host
cd web && npm ci && npm run dev        # http://localhost:5173
```
1. **Mariam** (`mariam@strategy.local`, MFA — CAP and Benchmarking office): Corrective actions → Sources → *New source*, add remarks owned by Ahmed.
2. **Ahmed** (`ahmed@strategy.local`): My Work shows the remarks; open one → *Accept* (or *Reject* — final), Root cause, Actions → *New action* (try *needs a budget* / *not available*), *Submit actions*.
3. Mariam: the remark → Actions → *Decide* → Approve: the budget action gets a draft Program Request (source "Action Plan"). Dashboard shows it under budget-dependent actions.
4. Ahmed: the action → *Update achievement*, *Request extension*; at 100 % upload evidence and *Request completion*; Mariam approves.
5. Benchmarking: Ahmed → *New study request* (catalogue) → Mariam approves the request → Annual plan → *Circulate*. Fill the search form, visit info and the analysis with a development plan; Mariam approves each gate; the study's *Linked CAP actions* tab lists the new remarks and actions.

## Cloud environment notes
- PostgreSQL 16 local (`STRATEGY_TEST_POSTGRES`, `max_connections = 600`); Docker started manually for Gotenberg / Mailpit and the
  Testcontainers images through `mirror.gcr.io/`; Tesseract and poppler-utils installed. E2E against database `strategy` with
  `E2E_BASE_URL=http://localhost:5080` and `E2E_CHROMIUM_PATH=/opt/pw-browsers/chromium-1243/chrome-linux64/chrome`.
- Rebased on Phase 11 (Wasel): conflicts in the ADR index, job lists, shell translations, the i18n index and three Vitest files (main's
  shared `LooseApi` helper kept); the CAP navigation icon differs from Portfolio reviews. The platform E2E History / Documents test now
  exports only its own record's audit entries: exporting the whole log (up to 10 000 rows once the earlier specs have run) as a PDF
  exceeded the Gotenberg timeout.
- Screenshots (`E2E_SCREENSHOTS=phase-15`) in [`docs/screenshots/phase-15/`](../screenshots/phase-15/) as `ar-*` / `en-*` pairs:
  `cap-remarks-source`, `cap-remark-accept-reject`, `cap-remark-reject`, `cap-root-cause`, `cap-action-budget-routing`,
  `cap-remark-actions`, `cap-pmo-validation`, `cap-budget-routed`, `cap-achievement-update`, `cap-extension-request`, `cap-dashboard`,
  `bench-plan`, `bench-search-form`, `bench-visit-info`, `bench-analysis`, `bench-linked-actions`, `bench-comparators`,
  `bench-practices`.
