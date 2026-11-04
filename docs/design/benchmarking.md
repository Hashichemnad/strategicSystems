# Design note — Phase 15: Benchmarking (`Strategy.Modules.Benchmarking`)

SPEC §8 (all, §5–8 decision), §5 (Request Framework), §9.5 (CAP integration), §2.3 A (KPI card benchmark fields), §4 (team link).
Written before the code with [`cap.md`](cap.md); open choices are ADRs **0126–0129**. Source of the flow: the Salesforce Benchmarking guide
(four stages with PMO gates and its Current Status table).

## 1. Projects

| Project | Content |
|---|---|
| `Strategy.Modules.Benchmarking.Contracts` | link type and system code constants (no consumer yet) |
| `Strategy.Modules.Benchmarking` | `Domain/` (study, topic, comparator, study entity + outreach, visit, practice row, indicator row, development-plan item, best practice, impact review, plan circulation, stage machine) · `Application/` · `Infrastructure/` (`BenchDbContext`, schema `bench`) · `Api/` · `Jobs/` · `Ai/` · `Mcp/` |

System code `bench` (`Modules:Benchmarking:Enabled`). References the Contracts of Requests (built-in request type), CAP (development plan →
remarks), KPI (indicator links, benchmark fields) and Teams (team link). **Additive** change in KPI: `IKpiBenchmarkWriter` in KPI.Contracts and
its implementation (ADR 0128).

## 2. Stages, gates and the Request Framework (ADR 0126)

| Stage | Who fills | Gate |
|---|---|---|
| **Plan** | requester: **built-in request type `bench.request`** in the Request Framework catalogue (form below) | `bench.plan` (approval stage of the request, default PM Office) — approve / return / reject |
| **Search** | coordinator: search form (candidate comparators, reasons, contacts, available information) | `bench.search` (study) — PMO approval → outreach log |
| **Acquire** | coordinator: visit info (agreed date, method, participants, optional Team, pre-visit questionnaire) | none — *Send to analysis* moves the stage (SPEC §8.2.3) |
| **Analysis** | coordinator: practice comparisons, indicator comparisons, development plan (≥ 1 item with owner and due date) | `bench.analysis` (study) — PMO approval creates the CAP remarks |
| **Completed** | — | `bench.closure` (study) — PMO final approval when every linked CAP action is completed with evidence |

The **request** (`bench.request`, number prefix `BMREQ`) holds the Plan: titles (AR / EN), responsible unit, section, responsible employee
(coordinator), target year, target quarter (1–4), comparison type (lookup `bench.comparison-type`: internal, competitive, functional,
generic), purpose (lookup `bench.purpose`: policy development, process improvement, human skills development, service quality, technology,
other), scope, **comparison topics** (table, min 1 row: topic, owner employee, description). The handler (`IRequestTypeHandler`, inside the
framework transaction, ADR 0080 pattern):
- **Submitted** → the study `BMS-NNNN` is created (or refreshed from the request after a return), stage `plan`, status `active`; the request's
  subject is the study.
- **Approved** → stage `search`; the study joins the **annual benchmarking plan** of its target year; coordinator and topic owners get
  record roles; `bench.planApproved` to the requester / coordinator.
- **Returned** → the study shows "waiting user to submit"; **Rejected / Cancelled** → status `cancelled`.

Study status `active` · `cancelled` · `completed`; one pending process at a time (`bench.search`, `bench.analysis`, `bench.closure`).
The stage machine is an explicit table (unit-tested):

| From stage | Action | To |
|---|---|---|
| plan | plan-approved | search |
| search | submit-search → search-approved | acquire |
| search | search-returned | search |
| acquire | send-to-analysis (≥ 1 visit with date and method) | analysis |
| analysis | submit-analysis → analysis-approved | analysis (CAP follow-up; `analysis_approved_at` set) |
| analysis | analysis-returned | analysis |
| analysis (approved) | request-closure → closure-approved | completed |
| any open | cancel (PMO, reason) | — status `cancelled` |

## 3. Data model (schema `bench`; audited, `xmin`, no hard deletes)

| Table | Columns (main) |
|---|---|
| `study` (§8.4) | `number`, `request_id`, `title_ar/en`, `responsible_unit_id`, `section_unit_id`, `responsible_employee_id`, `target_year`, `target_quarter`, `comparison_type_code`, `purpose_code`, `scope`, `stage`, `status`, `pending_process`, `returned`, `search_submitted`, `search_notified_at`, `team_id`, `cap_source_id`, `analysis_approved_at`, `ready_notified_at`, `closed_at`, `impact_due_on`, `impact_notified_at`, `cancel_reason` |
| `topic` | `study_id`, `title`, `owner_employee_id`, `description`, `sort` |
| `comparator` (shared register, §8.3) | `name_ar/en`, `sector_code` (lookup `bench.sector`), `country`, `scope` (`local` · `regional` · `international`), `website`, `contact_name/email/phone`, `notes`, `active` |
| `study_entity` | `study_id`, `comparator_id`, `selected`, `reason`, `available_info`, `contact`, `code_of_conduct_ack`, `rating` (1–5 after the visit), `rating_note` |
| `outreach` | `study_entity_id`, `on`, `channel` (`email` · `phone` · `meeting` · `letter` · `other`), `summary`, `response` (`pending` · `accepted` · `declined` · `no-reply`) |
| `visit` | `study_id`, `comparator_id`, `agreed_on`, `method` (`site-visit` · `virtual` · `questionnaire`), `participant_ids` (jsonb), `team_id`, `questionnaire`, `questionnaire_sent_on`, `notes` (visit notes / report) |
| `practice_row` | `study_id`, `topic_id`, `subject`, `our_practice`, `their_practice`, `category` (`compared` · `not-applicable` · `outside-scope`), `applicable`, `add_to_library` |
| `indicator_row` | `study_id`, `kpi_card_id` (optional, `IKpiCatalog`), `indicator`, `our_value`, `their_value`, `period`, `source`, `update_kpi_benchmark` |
| `plan_item` (development plan) | `study_id`, `topic_id`, `title_ar/en`, `description`, `owner_employee_id`, `unit_id`, `due_on`, `priority`, `action_type_code`, `budget_required`, `remark_id` (set by the CAP hand-off) |
| `best_practice` (library) | `title_ar/en`, `description`, `topic`, `tags` (jsonb), `applicability`, `sector_code`, `study_id`, `comparator_id`, `origin` (`analysis` · `manual`), `active` |
| `impact_review` (§8.3) | `study_id`, `due_on`, `implemented`, `kpis_moved`, `notes`, `reviewed_at`, `reviewed_by` |
| `plan_circulation` | `year`, `circulated_at`, `circulated_by`, `recipients` |

Files: `bench.study-document` (owner study: questionnaire, visit report, analysis).

## 4. Annual plan and the 30-day pre-quarter notification (ADR 0127)
**Plan view**: approved studies (stage ≥ search, not cancelled) by year → quarter → unit with stage chips. PMO *Circulate plan* for a
year notifies the heads of all units under the organisation and the coordinators (`bench.planCirculated`), recorded in `plan_circulation`
(re-circulating after changes is allowed and recorded again).
**Search notice** (`bench.monitor`, hourly): for each active study in stage `search` whose search form was not submitted and not yet
notified, the notice moment is the start of the target quarter **minus `searchNoticeDays` (default 30)** — calendar days, or working days
when `countWorkingDays` is on — moved back to the **previous working day of the responsible unit's business hours**, from the start of
that day in the unit's time zone. At or after it the coordinator and the responsible unit's head get `bench.searchDue` once
(`search_notified_at`). A plan approved after the moment is notified on the next run.

## 5. Analysis approval → CAP (ADR 0128)
In the `bench.analysis` hook (engine transaction), through `ICapRemarkSources` with the transaction context:
1. `EnsureSourceAsync` — type `benchmarking`, linked `bench.study`, title "Benchmarking: <study>", owner = responsible employee, unit =
   responsible unit, viewers = coordinator + topic owners, team = the study's team.
2. One `AddRemarkAsync` per development-plan item: external key = item id, text = item title / description, type `improvement`, priority
   = item priority (default medium), department = item unit ?? responsible unit, section = study section when the department is the
   responsible unit, owner = CAP default (section manager), linked `bench.study`, **proposed action pre-filled** (details, type, assignee
   = item owner, planned end = item due date, budget required). `plan_item.remark_id` is stored.
3. Practice rows with `add_to_library` become **best-practice** entries (origin `analysis`).
4. Indicator rows with a KPI card and `update_kpi_benchmark` update the card's benchmark description / result through
   `IKpiBenchmarkWriter` (audited KPI change, ADR 0128).
The study then shows the **linked actions live** (read-only) through `GetProgressAsync`. `CapRemarkStatusChanged` for a benchmarking
source is handled by Benchmarking: when progress reports `allActionsCompleted`, the coordinator and PMO are notified once
(`bench.readyForClosure`). **Closure** (`bench.closure`): submit refused (`bench.errors.actionsOpen`) unless every linked action is
completed with evidence; the hook re-checks on approval. Approval → stage `completed`, status `completed`, `closed_at`;
`LinkedRecordClosed(bench.study, id, "completed")` (Teams evaluates a linked team when its type is configured); **impact follow-up** due
after `impactFollowUpMonths` (default 6; 0 = off) — `bench.impactDue` once, the review records implemented / KPIs moved / notes.

## 6. Comparator register and best-practice library
Shared across studies; the register shows each comparator's studies and the average rating; the search form picks from the register or
adds a new comparator. Library: full-text (ILIKE + trigram) search on titles, description, topic, tags, filter by sector / topic;
everyone with `bench.practice.read` reads it; PMO manages manual entries and deactivates.

## 7. Access (ADR 0126)
Permissions: `bench.study.read`, `bench.study.edit` (search / visit / analysis forms, outreach), `bench.office` (gates via approvals,
cancel, circulate, edit any, closure request, KPI benchmark apply), `bench.plan.read` (not scoped — the plan is circulated to all units),
`bench.comparator.read`, `bench.comparator.manage`, `bench.practice.read`, `bench.practice.manage`, `bench.dashboard.read`,
`bench.settings.manage`, AI `bench.ai.comparators / questionnaire / analysis / summary / search`.

| Role | Scope | Grants |
|---|---|---|
| `bench-office` (PMO, MFA, approval role key `bench.bench-office`) | All | everything |
| `coordinator` | Unit and below | read, edit, closure request, dashboard, comparators manage, AI |
| `participant` (everyone) | Own (responsible employee) | read, plan read, comparator / practice read, AI search |
| `viewer` | Unit and below | read, dashboard |

Record roles on `bench.study`: `coordinator` (responsible employee: read + edit), `topic-owner` and `participant` (read). Remark access in
CAP for the coordinator and topic owners comes from CAP's `source-viewer` record role (cap.md §7).

## 8. Current status (Salesforce messages, `bench.currentStatus.*`)
`addTopic` (no topic — only for a refreshed request) · `waitingPmo` with the stage name (plan / search / analysis / closure) ·
`waitingSubmit` (returned plan) · `waitingSearch` (search form, with the notice date) · `waitingVisit` · `waitingAnalysis` · `addAction`
(analysis without development-plan items) · `followActions` (CAP actions in progress, n of m completed) · `readyForClosure` ·
`completed` ("Completed & Closed") · `cancelled`.

## 9. Screens (AR + EN)
Benchmarking area: **Dashboard** (studies by stage / quarter / unit, actions completion, overdue actions, completed studies, practices
adopted) · **Annual plan** (year selector, quarters × units, circulate) · **Studies** list · **Study page** (stage stepper Plan › Search ›
Acquire › Analysis › Completed, banner with actions, tabs **Overview** (request, topics, team), **Search** (candidates from the register,
reasons, outreach log), **Visit** (visits, questionnaire with AI draft), **Analysis** (practice rows, indicator rows with KPI picker,
development plan; AI *visit notes → comparisons and gap analysis*), **Actions** (linked CAP remarks and actions, live, read-only, progress
bars), **Impact**, **History**) · **Comparator register** · **Best-practice library** (natural-language search) · **Settings →
Benchmarking**. New studies start from the Request catalogue (`bench.request`). My Work card: studies needing my action.

## 10. AI (ADR 0129) and MCP
`bench-comparators` (suggest comparator entities from topics and internal history; register entries first, model suggestions marked) ·
`bench-questionnaire` (pre-visit questionnaire from the topics, bilingual) · `bench-analysis` (visit notes → practice comparisons, gap
analysis and draft development-plan items — drafts the coordinator accepts) · `bench-summary` (leadership summary of a study) ·
`bench-search` (best-practice library in natural language: the model turns the question into keywords, the server searches; without a
provider the keywords are the question). MCP: `bench_studies`, `bench_study_details`, `bench_best_practices` (read, on). No write tool.

## 11. Jobs
`bench.monitor` (hourly): search notices, ready-for-closure catch-up, impact follow-up due.

## 12. Tests
Unit: stage table, notice moment (calendar / working days, previous working day). Integration: plan request → PMO approve / return /
reject; search form → approval; visit → analysis; **analysis approval creates the CAP source, one remark per plan item with the proposed
action, best practices and the KPI benchmark update**; closure refused while actions are open and approved once all are completed with
evidence; the **30-day pre-quarter notification with the fake clock** (none before, once at the moment, not twice); scope (coordinator,
topic owner, other unit 404, PMO); comparator register and library search; MCP.
