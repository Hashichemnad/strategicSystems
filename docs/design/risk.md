# Design note — Phase 13: Risk Management (`Strategy.Modules.Risk`)

SPEC §3 (all, decisions §3.10), Appendix A Q-R3 / Q-P6, CLAUDE.md §15. Written before the code; open choices are ADRs **0100–0107**
(numbers from 0100 so Phase 10 — Portfolio Agile, built in a parallel session — keeps the 0089–0099 range).

The module is the organisation's **enterprise risk register**: every unit registers its risks in cause → event → consequence form,
assesses them on the shared framework (inherent → residual → target), records existing controls and treatment actions, and reviews them
periodically. The **Risk Office** (a role, not a department) owns the framework, approves registrations, reviews and closures, keeps the
Top-10 and reports to leadership / the Risk Committee.

## 1. Projects

| Project | Content |
|---|---|
| `Strategy.Modules.Risk.Contracts` | `IRiskScoring` (the **shared scoring service**: framework scales, formula, bands — used by Portfolio project risks, Q-P6), `IRiskDirectory` (scope-filtered risk / event summaries + the CAP remark back-link for Phase 15), integration events `RiskFrameworkChanged`, `RiskEventLogged`, `RiskControlRatedIneffective`, `RiskKriBreached`. No EF Core |
| `Strategy.Modules.Risk` | `Domain/` (framework, scoring, risk, control, action, KRI, assessment snapshot, event, review, committee decision, lifecycle) · `Application/` · `Infrastructure/` (`RiskDbContext`, schema `risk`) · `Api/` · `Jobs/` · `Ai/` (+ `Ai/Prompts/*.v1.md`) · `Mcp/` |

System code `risk`, `SystemKind.Module` (switchable, `Modules:Risk:Enabled` seeds the first value, enabled by default). Portfolio references
**only** `Strategy.Modules.Risk.Contracts` (§10).

## 2. Risk framework (Settings → Risk framework, SPEC §3.3, ADR 0100)

### 2.1 Versioned framework — `framework_version`
One row per saved framework; **a saved version is immutable** and the newest one is current. Saving (admin, `risk.framework.manage`)
always creates the next version (`version_no` + 1) with a reason. Columns (jsonb where structured):

| Field | Content / default |
|---|---|
| `scoring_method` | `multiplicative` (default, `L × I`) · `additive` (`L + I`) · `custom` |
| `formula` | custom only: expression over `L`, `I` and optional `C` (control effectiveness factor 0–10), evaluated by the **shared expression sandbox** (ADR 0022, number result, limits apply); validated on save (unknown variable / function, type, division by zero on any matrix cell, negative or non-finite result) |
| `likelihood_levels` | 1–5 `{value, labelAr, labelEn, descriptionAr, descriptionEn}` — Rare · Unlikely · Possible · Likely · Almost certain |
| `impact_areas` | `{code, labelAr, labelEn}` — financial · reputation · service · compliance · safety |
| `impact_levels` | 1–5 `{value, labelAr, labelEn, criteria: {areaCode: {ar, en}}}` — Insignificant · Minor · Moderate · Major · Catastrophic with criteria per area |
| `bands` | ordered `{code, labelAr, labelEn, color, min, max, residualStatus}` — **Low 1–4 green → `no-issue`, Medium 5–9 yellow → `continuous-review`, High 10–16 orange → `active-management`, Critical 20–25 red → `active-management`**. Count, names, colours, ranges, mapping editable |
| `control_effectiveness` | `{code, labelAr, labelEn, factor}` — Effective (factor 8) · Partially effective (5) · Ineffective (1). `factor` is the `C` value 0–10 (the Salesforce 1–10 evaluation is configured by replacing the scale with ten levels 1–10) |
| `review_frequency_months` | per band code — Critical 1, High 1, Medium 3, Low 6 |
| `project_residual_rule` | how Portfolio project risks (which have a 1–10 control evaluation, not a residual L/I) get a residual: `control-reduction` (default: score × (11 − C) ÷ 10, ADR 0074) or `formula` (custom formula with `C`) |
| `recalculation` | the admin's choice when saving: `now` (every open risk re-rated, one audit entry per changed risk) or `next-review` (risks keep their version until their next review / reassessment) |

**Band mapping** (`BandMapper`, pure): a score maps to the band whose `[min, max]` contains it; a score that falls **between** two bands
(e.g. 17–19, or fractions after control reduction) maps to the **higher** band (safest reading — ADR 0100); below the first band → the
first, above the last → the last. Validation: ≥ 2 bands, codes unique, `min ≤ max`, ranges ascending and non-overlapping, each band maps to
a residual status code.

**Scoring** (`RiskScoringEngine`, pure, table-tested): `Score(method, formula, L, I, C?)` → decimal rounded to 2 dp. Multiplicative `L × I`,
additive `L + I`, custom: sandbox with `L`, `I`, `C` (C = 0 when no control rating is given for an *inherent* score; residual uses the
residual L / I and the risk's overall control effectiveness factor). Every score (enterprise and project) goes through this one class.

**Preview** (`POST /framework/preview`, no save): validates the draft, returns the full **matrix** (score + band per L × I cell) and the
**number of open risks** (draft / under review / active) — and of active project risks — whose inherent, residual or target **band**
would change, with up to 20 examples. The editor calls it live (debounced) while the admin edits.

**Saving** writes the version and, with `now`, re-rates every open risk in the same transaction (EF tracked changes → the audit
interceptor writes one audit entry per changed risk, reason `framework v{n}`) and snapshots a `recalculation` assessment; with
`next-review` risks keep `framework_version_id` until their next approved review / reassessment. Both publish `RiskFrameworkChanged`
(Portfolio re-rates project risks with `now`, §10). Version 1 is seeded on first start (seeder, idempotent) and also publishes it.

### 2.2 Other configuration (module tables / lookups, seeded once, editable)
- **Categories** `category` (multi-level: `parent_id`, `code`, `name_ar/en`, `sort`, `active`, **`appetite`** = max acceptable residual
  score, **empty by default — Q-R3**). Seed: Strategic, Operational, Financial, Legal & Compliance, Technical, IT & Information Security,
  Reputation, Health & Safety, Environmental.
- Lookups: `risk.response` (accept · avoid · reduce · transfer · exploit · enhance — the last two only offered when Settings → Risk
  *opportunities* is on), `risk.control-type` (preventive · detective · corrective · directive), `risk.control-nature` (manual · automated ·
  hybrid), `risk.control-frequency` (continuous · daily · weekly · monthly · quarterly · annually · ad-hoc), `risk.closure-reason` (no longer
  relevant · fully treated · transferred · other), `risk.committee-decision` (accept · additional-treatment · transfer · other), `risk.determined-by`
  (risk workshop · management · audit · project · other).
- **`RiskSettings`** (typed settings area `risk`): `topCount` 10, `reviewReminderWorkingDays` 10, `actionReminderWorkingDays` 3,
  `actionEscalationWorkingDays` 2, `opportunitiesEnabled` false, `escalateCritical` true, `escalateOutsideAppetite` true,
  `kriWarningNotifies` false, `includeProjectRisksByDefault` true, `organisationCode` for reports.
- **Numbering** `risk.risk` `{UNIT}{YY}-R{SEQ:3}` (the counter partition is the rendered prefix, so units sharing a code — the GRD case —
  share one sequence per year and numbers never collide), `risk.action` `ORA-{SEQ:4}`, `risk.event` `RE-{YYYY}-{SEQ:3}`.

## 3. Data model (schema `risk`; business tables audited, `xmin`, audit columns, no hard deletes — drafts may be discarded)

### 3.1 Risk — `risk` (SPEC §3.4 A)
- **Identity:** `number`, `register_year`, `title_ar/en`, `cause`, `event_text`, `consequence` (structured statement), `determined_by_code`,
  `identified_on`, `exposure_start`, `risk_type` (`internal` · `external`), `category_codes` (jsonb, multi; the first is primary),
  `status`, `emerging`, `opportunity`.
- **Ownership:** `unit_id` (the register), `owner_employee_id`, `coordinator_employee_id`, `follow_up_employee_id`.
- **Alignment:** `objective_codes` (jsonb, codes of the KPI strategy hierarchy via `IKpiStrategyCatalog`, ADR 0075), `processes`.
- **Assessments:** `inherent_likelihood / impact / impact_area / score / band`, `control_effectiveness_code` + `control_effectiveness_override`
  (computed from the active controls, §5.4, or set by the Risk Office), `residual_likelihood /
  impact / score / band`, `residual_status` (derived from the band) + `residual_status_override` + `residual_status_justification`,
  `target_likelihood / impact / score / band`, `target_date`, `framework_version_id`.
- **Response:** `response_code`, `within_appetite` (computed, null when no category has an appetite), `needs_follow_up`, `top10`,
  `top10_rank` (manual adjustment), `escalated` + `escalation_reason` (`critical` · `outside-appetite` · `manual`).
- **Review:** `last_review_on`, `next_review_on`, `review_reminded_at`, `review_overdue_notified_at`, `reassessment_required` +
  `reassessment_reason` (`kri-breach` · `event` · `appetite` · `audit-finding` · `framework`).
- **Workflow:** `pending_process` (`risk.review` · `risk.periodic-review` · `risk.closure`, null when none), `returned` flag + comment.
- **Closure:** `closure_reason_code`, `closure_note`, `closed_at`, `closed_by`; `rejection_reason`.

### 3.2 Children
| Table | Fields |
|---|---|
| `control` (§3.4 B) | `risk_id`, `name_ar/en`, `description`, `type_code`, `nature_code`, `owner_employee_id`, `frequency_code`, `effectiveness_code`, `last_assessed_on`, `evidence` (text; files through Files purpose `risk.control-evidence`), `source_action_id`, `status` (`active` · `retired`) |
| `treatment_action` (§3.4 C) | `number` (`ORA-NNNN`), `risk_id`, `title_ar/en`, `owner_employee_id`, `responsible_unit_ids` (jsonb), `planned_start/end`, `actual_start/end`, `completion_pct` (ratio), `status` (`not_started` · `in_progress` · `completed` · `cancelled`; **overdue** is derived: planned end passed, not completed), `cost`, `converted_control_id`, `reminded_at`, `escalated_at`. Evidence: Files purpose `risk.action-evidence` |
| `action_update` | `action_id`, `note`, `completion_pct`, `at`, `by` — every progress update kept as history |
| `kri` (§3.4 D) | `risk_id`, `kpi_card_id`, `card_code` (snapshot for labels), `basis` (`value` = the result's achieved value · `performance` = capped performance ratio), `direction` (`above` = breach when the value rises to / above the threshold · `below`), `warning_threshold`, `breach_threshold`, `status` (`active` · `removed`), `last_result_id`, `last_value`, `last_level` (`normal` · `warning` · `breach`), `last_period`, `last_at` |
| `assessment` (§3.4 E) | snapshot: `risk_id`, `kind` (`registration` · `approval` · `review` · `reassessment` · `recalculation` · `closure` · `reopen`), inherent / residual / target L, I, score, band, `residual_status`, `status`, `control_effectiveness_code`, `framework_version_no`, `comment`, `at`, `by` (append-only: never updated) |
| `risk_event` (§3.4 F) | `number`, `occurred_on`, `description`, `financial_impact` numeric(18,2) + `currency`, `other_impact`, `near_miss`, `root_cause`, `lessons`, `unit_id`, `cap_remark_id` (set by CAP, Phase 15), `status` (`open` · `closed`) |
| `event_risk` | `event_id`, `risk_id` (many-to-many; a realised event flags each linked risk for reassessment) |
| `review` (R3) | `risk_id`, `status` (`draft` · `submitted` · `approved` · `returned`), `commentary`, proposed `residual_likelihood / impact`, `control_ratings` (jsonb `{controlId: code}`), `action_notes`, `submitted_at`, `decided_at`, snapshot id |
| `committee_decision` (R4) | `risk_id`, `decision_code`, `note`, `decided_on`, `recorded_by` |

KRIs are also exposed to Performance Management as **linked risks** of a KPI card (`IKpiLinkedRecordSource`, linkable type `risk.risk`).

## 4. Lifecycle (`RiskLifecycle`, explicit transition table, unit-tested — ADR 0102)

| From \ action | submit | approve | return | reject | request-closure | closure-approved | closure-refused | reopen | discard |
|---|---|---|---|---|---|---|---|---|---|
| `draft` | → `under_review` | – | – | – | – | – | – | – | → removed (never submitted) |
| `under_review` | – | → `active` | → `draft` (returned) | → `rejected` | – | – | – | – | – |
| `active` | – | – | – | – | pending `risk.closure` | → `closed` | stays `active` | – | – |
| `closed` | – | – | – | – | – | – | – | → `active` (Risk Office, reason) | – |
| `rejected` | – | – | – | – | – | – | – | → `draft` (Risk Office, reason) | – |

Periodic review (`risk.periodic-review`) is a pending process on an **active** risk; one pending process per risk at a time.

## 5. Workflows (SPEC §3.5)

### 5.1 R1 — Identify & register
Guided wizard (statement → classification / owner / objectives → inherent rating on the clickable matrix → controls → actions → KRIs).
**Saved as Draft; the number is assigned on the first save** (numbering in the save transaction, unit code of the owning unit, year of
`identified_on`). Submit → approval process **`risk.review`** (default one step: role **`risk.risk-office`**). The Risk Office decides on the
generic approval panel; before approving it may adjust ratings, response strategy and Top-10 through the Risk-Office edit (allowed while
under review). Approve → `active`, snapshot `approval`, next review date computed (§5.3); return → `draft` with the comment; reject →
`rejected` with the reason.

### 5.2 R2 — Treat
Action owners update progress (`POST /actions/{id}/progress`: note, %, actual dates, evidence) — each update is an `action_update` row.
100 % → `completed` → the response offers **"convert to control"** (`POST /actions/{id}/convert-to-control` with the effectiveness rating:
creates a `control` with `source_action_id`, links back, recomputes the risk's control effectiveness and sets `reassessment_required`
reason `control-added`). Job: reminder to the owner N working days before the planned end, escalation to the risk owner and coordinator
M working days after it (stamps → idempotent). Treatment actions are **task-able** and a "Create task" button links a Task Management
task to the action (Links registry), so teams can use Tasks for the work while the action keeps the risk view.

### 5.3 R3 — Monitor & review
- `next_review_on` = last review date (approval date for a new risk) + `review_frequency_months[residual band]`, then **rolled forward
  to the next working day of the owning unit's business hours** (`IBusinessHoursCalculator`, ADR 0027); the review is due at the end of
  working time that day. Reminder `reviewReminderWorkingDays` working days before; overdue notice after (job `risk.monitor`, hourly).
- Review wizard (`review` draft): confirm / update each control's effectiveness, residual L / I, notes on actions, commentary → submit →
  **`risk.periodic-review`** (default Risk Office). Approval applies the ratings, writes a `review` snapshot, clears
  `reassessment_required`, moves the risk to the current framework version and computes the next review date. Return keeps the draft.
- Unscheduled-review triggers set `reassessment_required` + reason: KRI breach (§9), risk event logged, appetite breach, audit finding
  linked (contract for Phase 16), framework recalculation that changed the band.

### 5.4 Computations (pure, table-tested — CLAUDE.md §15)
- Scores: §2.1 engine; inherent uses `C = 0`; residual uses the overall control effectiveness factor; target uses the target L / I and `C`.
- **Overall control effectiveness** = the **lower median** of the active controls' rating factors (conservative: with two controls rated
  Effective and Ineffective the overall is Ineffective), i.e. one of the scale's levels; no rated control → none. The Risk Office override
  wins (`control_effectiveness_override`).
- Residual status = band's mapped status unless overridden (justification required).
- **Within appetite** = residual score ≤ the smallest appetite among the risk's categories (and their ancestors) that have one; null when
  none has an appetite.
- **Top-10** ranking: risks flagged `top10` first by `top10_rank` (manual) then residual score desc, inherent score desc, number; the list
  shows the first `topCount`.

### 5.5 R4 — Escalate
On every rating change: residual band = Critical (`escalateCritical`) or outside appetite (`escalateOutsideAppetite`) → `escalated = true`
with reason and a notification to holders of `risk.risk-committee` and the Risk Office; manual escalation by the Risk Office. Committee
decisions (`risk.committee.decide`) are recorded on the risk (`committee_decision`).

### 5.6 R5 — Close
Risk owner (or coordinator / Risk Office) requests closure with a reason → **`risk.closure`**: step 1 Risk Office; step 2 **Risk
Committee** (role `risk.risk-committee`) with entry condition `residualBand == "critical"` (variables `residualBand`, `inherentBand`,
`residualScore`, `top10`) — editable in the Approval Settings centre. Approved → `closed` (snapshot `closure`), kept in history;
Risk Office can **reopen** with a reason.

## 6. Approval processes (seeded version 1, editable)
| Key | Default steps | Variables |
|---|---|---|
| `risk.review` | Risk Office (role `risk.risk-office`), SLA 40 working hours | `inherentBand`, `residualBand`, `residualScore`, `withinAppetite` |
| `risk.periodic-review` | Risk Office | same |
| `risk.closure` | Risk Office → Risk Committee *if* `residualBand == "critical"` | same + `top10` |

Hooks move the risk (inside the engine transaction, ADR 0023); recall = cancel by the submitter. Approver record role `approver` on
`risk.risk` grants read while the task is open.

## 7. Access (SPEC §3.7 — seeded, editable in Settings → Roles)
Permissions (AR / EN labels): `risk.risk.read`, `risk.risk.create`, `risk.risk.edit`, `risk.risk.submit`, `risk.risk.office` (Risk-Office
edits: ratings / response / Top-10 / escalation / reopen / control-effectiveness override), `risk.risk.close-request`, `risk.committee.decide`,
`risk.action.update`, `risk.control.update`, `risk.event.manage`, `risk.dashboard.read`, `risk.report.export`, `risk.project-risks.read`,
`risk.framework.manage`, AI `risk.ai.identify / quality / suggest / duplicates / review / report`.

| Role (code) | Scope | Grants |
|---|---|---|
| `risk-office` (MFA) | All | everything incl. framework, approve (approval role key `risk.risk-office`), Top-10, reports |
| `coordinator` | Unit and below | read / create / edit / submit / close-request / action / control / events, dashboard, project risks, AI |
| `owner` (everyone) | Own (risk owner) | read, edit, submit, close-request, action / control update, AI quality / suggest |
| `viewer` | Unit and below | read, dashboard |
| `risk-committee` | All | read, dashboard, reports, committee decisions (approval role key `risk.risk-committee`) |
| `internal-audit` | All | read (incl. History), dashboard, reports, project risks |

Record roles on `risk.risk`: `action-owner` (read + `risk.action.update`), `control-owner` (read + `risk.control.update`), `approver`.
Granted / revoked by the module when an action / control owner is set or changes. Handlers also check that an action / control owner only
updates their own item. 404 outside scope, 403 for visible-but-forbidden. **System ownership:** the Risk Office approval role is
resolved through role holders; the system's owning unit (Settings → Systems) is seeded from `Modules:Risk:OwningUnitCode` (empty → none;
ADoF sets the Internal Audit Office now and moves it to the Strategy Office later by role / ownership reassignment — no code).

## 8. Project Risks (SPEC §3.4 G, ADR 0103)
`GET /project-risks` reads `IProjectRiskDirectory.ListAsync(user, …)` **live** (Portfolio filters by the caller's project read scope) —
nothing is stored in schema `risk`. Filters: project, unit (through `IPortfolioDirectory` project unit when available, else project),
band. Each row links to `/portfolio/projects/{id}?tab=risks`. Dashboards / heat-maps add them as a **separate category `project`**
(toggle *Include project risks*), scored with the same framework so the same ratings always land in the same cell.

## 9. KRIs and KPI results (ADR 0104)
`KpiResultApproved` handler (outbox, idempotent by `last_result_id`): for every active KRI of the card, value = `Achieve` (basis `value`) or
`Capped` (`performance`); level = breach when `value ≥ breach` (`above`) / `value ≤ breach` (`below`), warning likewise, else normal. Stores
the reading on the KRI. **Breach** → risk `reassessment_required` (reason `kri-breach`), notification `risk.kriBreach` to the risk owner,
coordinator and Risk Office holders, event `RiskKriBreached`. Warning notifies only when `kriWarningNotifies`. N/A results are ignored.
The KRIs tab shows the latest KPI results through `IKpiCatalog.GetPerformanceAsync` (caller's KPI scope).

## 10. Portfolio uses the shared framework (ADR 0101)
- `IRiskScoring` (Risk.Contracts): `ScoreProjectRiskAsync(probability, impact, controlEvaluation)` → inherent score / band, residual score /
  band, framework version — the current framework, `project_residual_rule`.
- Portfolio's `DefaultProjectRiskScoring` becomes async and asks `IRiskScoring` when it is registered (Risk module enabled), falling back to
  the Phase 9 rule otherwise. Band codes are the framework's (`low` · `medium` · `high` · `critical` by default — the Phase 9 codes).
- Portfolio subscribes to `RiskFrameworkChanged`: with `now` (and on the seeded version 1) it re-scores every active project risk through
  EF, so each changed row gets **one audit entry** (no data migration; inputs are already stored, ADR 0074). Changes are minimal and
  additive in Portfolio (one handler, one package reference, the scoring call made async).

## 11. CAP and Audit hooks (contracts only — Phase 15 / 16)
`RiskEventLogged(eventId, number, riskIds, unitId, …)` and `RiskControlRatedIneffective(controlId, riskId, …)` integration events; CAP
subscribes in Phase 15 to raise remarks. `IRiskDirectory.LinkCapRemarkAsync(eventId, remarkId)` lets CAP write the back-link (shown on the
event). `IRiskDirectory.FlagForReassessmentAsync(riskId, reason)` for audit findings (Phase 16).

## 12. Screens (AR + EN, RTL / LTR, shared components — ADR 0105)
Risk area with sub-navigation: **Dashboard** (tiles, inherent vs residual **heat-maps** — CSS grid, click a cell to list its risks;
by category / unit / rating; outside appetite; Top-10; overdue actions / reviews; movement improved / worsened this quarter; KRI breaches;
filters unit, category, residual status, year, include project risks) · **Register** (saved views My risks · My unit · Top-10 · Outside
appetite · Overdue review · Under review; rating chips; export) · **Risk page** (header chips inherent → residual → target, status,
current-status banner with actions; tabs Overview, Assessment (matrix + history chart), Controls, Treatment actions, KRIs, Events, Reviews,
Documents, Activity) · **Risk wizard** · **Review wizard** · **My actions** (quick progress update) · **Project Risks** · **Risk
Assessment Mechanism** (generated from the current framework, bilingual, printable / PDF) · **Reports** (register, Top-10, unit profile,
action tracker: PDF via Gotenberg, Excel via ClosedXML, audited) · **Settings → Risk framework** (versions, scales with criteria, scoring
method + formula editor with live matrix preview and affected count, bands, residual mapping, control scale, review frequency,
categories with appetite, recalculation choice) · **My Work** cards (my risks due for review, my overdue actions). Charts are accessible
SVG / CSS on the design tokens (no new chart dependency — ECharts stays for Phase 17, ADR 0105).

## 13. AI (Phase 5 gateway; drafts only — CLAUDE.md rule 6, ADR 0106)
Features (per-feature switch + permission): `risk.identify` (from text or an uploaded document / project charter text → risks in cause →
event → consequence form with category, L / I, reasoning; document text passed as untrusted data), `risk.quality` (statement check:
deterministic vague-word flags + model rewrite), `risk.suggest` (controls / actions using similar register risks), `risk.duplicates`
(**no model**: trigram similarity across all units, titles only for risks outside the caller's scope are not revealed — count + numbers the
user may read), `risk.review-assistant` (what changed since the last review + draft commentary), `risk.report` (quarterly risk report and
Top-10 briefing, bilingual). Prompts `Ai/Prompts/risk-*.v1.md`, JSON output validated; nothing is saved without the user.

## 14. MCP tools (ADR 0031: read on, write off, no approve tool)
`risk_register_search`, `risk_details`, `my_risk_actions`, `risk_heatmap_summary`, `risk_top10`, `risks_outside_appetite` (read, on);
`update_risk_action_progress_draft` (write, **off**: stores a progress note as a draft update the owner confirms). All through
`IAccessService` with the caller's scope.

## 15. Current status, notifications, My Work, search, links
- Current status of a risk: draft (submit), returned, under review (approvers), active with review due / overdue / reassessment required,
  closure pending, closed, rejected.
- Templates (AR / EN): `risk.submitted`, `risk.approved`, `risk.returned`, `risk.rejected`, `risk.reviewDue`, `risk.reviewOverdue`,
  `risk.actionDue`, `risk.actionOverdue`, `risk.kriBreach`, `risk.escalated`, `risk.closed`, `risk.reassessment`.
- My Work: my risks due for review / reassessment, my overdue actions. Global search: risks (number, titles). Linkable types `risk.risk`
  and `risk.action` (task-able).

## 16. API (`/api/v1/risk`)
Framework: `GET /framework`, `GET /framework/versions`, `POST /framework/preview`, `POST /framework` (save new version), categories
`GET|POST /categories`, `GET|PUT /settings`, `GET /mechanism`. Risks: `GET /risks` (views, filters, paging), `POST /risks`, `GET|PUT
/risks/{id}`, `POST /risks/{id}/submit|recall|office-update|request-closure|reopen|escalate|committee-decisions|discard`, `GET
/risks/{id}/assessments`, controls / actions / KRIs / reviews / events sub-resources, `POST /actions/{id}/progress|convert-to-control`,
`GET /my-actions`, `GET /dashboard`, `GET /heatmap/cells`, `GET /top10`, `GET /project-risks`, `GET /reports/{kind}.{pdf|xlsx}`, AI
endpoints, History via `MapRecordHistory`.

## 17. Tests
- **Unit:** scoring multiplicative / additive / custom formula (incl. `C`), band mapping at every boundary and gaps (17 → Critical),
  framework validation (formula errors, bands overlapping / not ascending), residual status derivation and override, control
  effectiveness aggregation, appetite, Top-10 ordering, KRI level evaluation (above / below, warning / breach), lifecycle table (every
  allowed and forbidden transition), review frequency → months.
- **Integration (real PostgreSQL):** register → submit → Risk Office approves (number `GRD26-R001` / `GRD26-R002` for two units sharing
  code GRD); framework preview counts affected risks and saving with `now` re-rates and writes audit entries, `next-review` keeps the
  version; review due dates by rating rolled to a working day; KRI breach on an approved KPI result flags the risk and notifies the owner;
  Project Risks shows only visible projects' risks and nothing is stored in `risk`; project risks scored by the shared framework (and
  re-rated with audit entries); closure of a Critical risk needs the Risk Committee step (Medium does not); authorization negatives
  (outsider 404, viewer 403 on write, action owner limited to own action); MCP tools registered (write off).
- **E2E (ar + en):** framework settings with formula preview, risk wizard with matrix, risk page tabs, heat-maps, Top-10, KRI breach,
  Project Risks tab, mechanism page, report PDF — screenshots in `docs/screenshots/phase-13/`.
