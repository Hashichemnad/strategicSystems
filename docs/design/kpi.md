# Design note — Phase 12: Performance Management (`Strategy.Modules.Kpi`)

SPEC §2 (all), Appendix A Q-K2 / Q-K6, CLAUDE.md §15. Written before the code; open choices are ADRs 0060–0065 (numbers from 0060 so the
Portfolio phase built in parallel keeps 0047 onwards).

The module is the organisation's **KPI register and catalog**: the Strategy Office defines KPI cards and yearly targets, owners submit
period results with evidence when a window opens, the Strategy Office / PMO verifies and approves, and only approved results feed
scorecards, the strategy map and dashboards.

## 1. Projects

| Project | Content |
|---|---|
| `Strategy.Modules.Kpi.Contracts` | `IKpiCatalog` (card search / summary / year performance for Portfolio's "Existing KPI"), `IKpiPerformance` (the K5 calculator and the RAG band for other modules — Q-P6 project KPI goal status), `IKpiLinkedRecordSource` (implemented by Portfolio / Risk: records linked to a card, shown on the card's *Linked projects / risks* tabs), integration events `KpiResultApproved`, `KpiTargetMissed`. No EF Core |
| `Strategy.Modules.Kpi` | `Domain/` (card, plan, result, strategy element, change request, calculator, aggregation, periods, windows, lifecycle) · `Application/` · `Infrastructure/` (`KpiDbContext`, schema `kpi`) · `Api/` · `Jobs/` · `Ai/` (+ `Ai/Prompts/*.v1.md`) · `Mcp/` |

System code `kpi`, `SystemKind.Module` (can be switched off in Settings → Systems; needs system access like any business system).

## 2. Data model (schema `kpi`; business tables audited, `xmin`, audit columns, no hard deletes)

### 2.1 Strategy hierarchy — `strategy_element` (SPEC §2.7, ADR 0062)
`kind` (`vision` · `perspective` · `objective`, code enum; labels translatable), `code`, `name_ar/en`, `description_ar/en`, `parent_id`
(vision → perspective → objective), `weight` (numeric(9,4), within the parent; null = equal), `sort`, `active`, `cycle_start_year`,
`cycle_end_year` (vision / strategy cycle, e.g. 2026–2030). A pillar is a perspective with another label (installations rename the kind in
Settings → Translations). Inactive elements are kept (historical cards still point at them).

### 2.2 KPI Card — `kpi_card` (SPEC §2.3 A)
- **Identity:** `code` (numbering `kpi.card`, default `KPI-{SEQ:4}`; manual entry when Settings → KPI *manual codes* is on, unique),
  `name_ar/en`, `description_ar/en`, `measuring_equation`, `process_name`, `status` (`active` · `inactive`), `initiated_on`, `closed_on`.
- **Classification (lookup codes):** `type_code` (`kpi.type`), `index_classification_code` (`kpi.index-classification`),
  `effectiveness_code` (`kpi.effectiveness`), `nature` (`quantitative` · `qualitative`, code enum — SPEC "Quantitative / Descriptive"),
  `source_codes` (`kpi.source`, multi, jsonb), `measuring_method_code` (`kpi.measuring-method`, mapped to an aggregation rule in Settings —
  §5.3), `competitiveness_indicator` (bool), `is_global_star` (bool). The balanced-scorecard **perspective** is the parent of the card's
  objective (§2.1) — not a second field that could disagree.
- **Measurement:** `unit_code` (`kpi.unit`; code `percentage` is the percentage unit), `measuring_period` (`quarterly` · `half_yearly` ·
  `annual` · `two_year`), `direction` (`increase` · `decrease` · `matching`), `target_type` default (`single` · `range`),
  `target_setting_mechanism`, `allows_negative` (ratio KPIs refuse negative values — K5).
- **Alignment:** `objective_id` (strategy element), `sector_goal`, `national_goal_code` (`kpi.national-goal`), `vision_link_code`
  (`kpi.vision-link`, the installation's "Link to vision" list), `gem_criteria_codes` (`kpi.gem-criterion`, multi), `criteria_text`,
  `excellence_link`.
- **Ownership:** `department_unit_id`, `responsible_unit_id`, `responsible_employee_id`, `responsible_team_id` (Teams, by id).
- **Benchmark:** `benchmark_description`, `benchmark_result` (ratio).
- **Standards (§2.7):** `level` (`organisation` · `department` · `section`), `parent_card_id` (cascading), `weight` (within the objective,
  null = equal), `baseline_value`, `baseline_year`, `green_from` / `amber_from` (per-KPI RAG override, ratios), `formula` + `data_elements`
  (composite KPI: jsonb list `{code, nameAr, nameEn}`; the formula is evaluated by the shared expression sandbox with the element codes as
  number variables), `scale` (qualitative KPI: jsonb list `{value, labelAr, labelEn}`, ascending).
- **Computed / stored:** `max_achieved` (best annual capped performance, for "Max Achieved %"), `definition_version` (int).
- `card_target` (multi-year targets): `card_id`, `year`, `target_type`, `target`, `range_min`, `range_max`, unique (card, year). Used as the
  default for that year's annual plan and for "vs target path" trends.
- `card_version`: `card_id`, `version_no`, `snapshot` (jsonb of the definition), `reason`, `change_request_id` — written on every saved
  definition change (full version history, SPEC §2.7).

### 2.3 Definition change control — `definition_change` (ADR 0063)
Changes to **target, equation, unit or direction** after the measuring year has started (an annual plan of the card for a year ≤ today's
year exists) are not applied directly: they create a change request (`proposed` jsonb, `reason`, `status`: `pending` · `approved` ·
`rejected` · `returned` · `withdrawn`) submitted to approval process **`kpi.definition-change`** (default single step: Strategy Office).
Approval applies the change (card and — for target changes — the current year's plan and not-yet-approved period targets), writes a
`card_version` and notifies the requester. Other fields are edited directly (a version is still written). Feature `kpi.change-control`
(Settings → Features, on by default): when off, all edits apply directly.

### 2.4 Annual Plan — `annual_plan` (SPEC §2.3 B)
`card_id`, `year`, `measuring_period`, `unit_code`, `direction` (copied from the card, overridable for the year), `is_accumulated`,
`target_type`, `target` · `range_min` · `range_max` (numeric), `responsible_employee_id`, `status` (`draft` → `confirmed`: Strategy Office
confirms the year's targets — K1 step 4), `confirmed_at/by`. **Computed** after every approval / reopen: `achieved_total`,
`actual_performance`, `capped_performance`, `rag`, `reported_count`, `approved_count`, `na_count`, `last_achieved`. Unique (card, year).

### 2.5 KPI Result — `result` (SPEC §2.3 C)
`plan_id`, `card_id`, `year`, `period_code` (`q1..q4` · `h1` · `h2` · `fy` · `ty`), `period_start`, `period_end`, `sort`, `target_type`,
`target`, `range_min`, `range_max` (per period, numeric), `achieve`, `element_values` (jsonb, composite), `scale_value` (qualitative),
`actual_performance`, `capped_performance` (numeric(9,4) ratios, null = cannot be calculated), `achieved` (bool), `causality`,
`corrective_actions`, `notes`, `not_applicable` + `na_reason`, `status` (§3), `opens_on`, `due_on`, `due_at` (end of working time of the due
date — §4), `submitted_at/by`, `late`, `days_late`, `approved_at/by`, `return_count`, `reopened_count`, `checklist` (jsonb answers of the
reviewer), `owner_employee_id` + `owner_unit_id` (copied from the card / plan for data scopes, updated when ownership changes),
`opened_notified_at`, `reminded_at`, `overdue_notified_at`, `escalated_at`. Evidence = files of purpose `kpi.evidence` owned by the result.
Unique (plan, period_code).

**Validation:** the period must match the plan's measuring period; targets and values are numbers **in the KPI's unit** — for unit
`percentage` they are numbers of percent (92.5 = 92.5 %), exactly as in the K5 table; the unit lives on the plan, so a target and its value
can never be in different units (SPEC "if unit is Percentage the target must be a %"). Performance values are ratios (CLAUDE.md §6). Ratio
KPIs refuse negatives unless `allows_negative`; range min ≤ max; evidence required on submit when Settings
says so (default on); causality and corrective actions required when the target is missed (capped < 100 %). Not Applicable needs a reason.

### 2.6 Period windows — `period_window` (K2, Q-K2, ADR 0060)
`year`, `period_code`, `opens_on`, `due_on`, `locked` (the PMO locks a period at year end). Rows are created by the new-year run from the
Settings rules and are **editable per year** by the Strategy Office (changing dates updates results not yet submitted).

Default rules (Settings → KPI → Period windows, Appendix A Q-K2): a window **opens the day after the period ends** and is **due on the 15th
day**; periods ending in December (Q4, H2, Full Year, Two Year) are due on **31 January** of the following year. So Q1 (Jan–Mar) opens
1 April, due 15 April; H1 opens 1 July, due 15 July; H2 / Full Year open 1 January, due 31 January. Each rule = `{periodCode,
opensDaysAfterEnd, dueDaysAfterOpen}`.

## 3. Result lifecycle (`ResultLifecycle`, explicit transition table, unit-tested — ADR 0061)

| From | Action | To | Who |
|---|---|---|---|
| `not_open` | `open-window` | `open` | window job (or Strategy Office *open now*) |
| `open` | `submit` | `pending_approval` | owner (`kpi.result.submit` in scope) — value or **Not Applicable** |
| `returned` | `submit` | `pending_approval` | owner (resubmission → new approval attempt) |
| `pending_approval` | `recall` | `open` | owner, while no decision was taken (instance cancelled) |
| `pending_approval` | `approved` | `approved` | approval engine hook (final step approved) |
| `pending_approval` | `returned` | `returned` | hook — reviewer **returned or rejected** (both unlock with a mandatory comment, SPEC K4.4) |
| `approved` | `reopen` | `open` | Strategy Office (`kpi.result.reopen`), reason required |
| `not_open` · `open` · `returned` | `lock` | `locked` | Strategy Office locks the period at year end |
| `locked` | `reopen` | `open` | Strategy Office, reason required |

`open` and `returned` are "Open for Update" (Pending Updates list). A submission after `due_at` sets `late` and `days_late` (calendar days
after the due date). Every other (status, action) pair is refused (`kpi.errors.invalidTransition`) and changes nothing.

## 4. Windows, reminders, late flag (job `kpi.period-windows`, every hour at minute 5)
Idempotent, one transaction per result, stamps prevent repeats; dates are the installation's time zone (Settings → Regional).
1. Results `not_open` whose `opens_on` ≤ today and whose plan is **confirmed** (or confirmation not required) → `open`; owners notified
   (`kpi.windowOpened`, one notification per owner listing the count).
2. `due_at` = end of working time of `due_on` in the business hours of the responsible unit (shared `IBusinessHoursCalculator`, ADR 0027);
   a weekend / holiday due date rolls to the next working day.
3. Reminder `kpi.reminder` N working days before `due_at` (default 3), once.
4. After `due_at`: overdue notice `kpi.overdue` to the owner, once; escalation `kpi.escalation` to the head of the responsible unit
   (department manager) M working days after due (default 2), once.
5. Late submission stays allowed; the flag counts against the department's on-time rate (§8).

## 5. Calculations (pure services, table-driven tests — CLAUDE.md §15)

### 5.1 `PerformanceCalculator` (SPEC K5 — exactly the SPEC pseudo-code)
`Calculate(targetType, direction, target, rangeMin, rangeMax, achieve) → (actual, capped)`; null = "cannot be calculated" (shown as "—",
excluded from roll-ups). All 14 SPEC rows are a unit-test table. Used everywhere: results, plans, scorecards, dashboards, reports, API, MCP,
and through `IKpiPerformance` by other modules.

### 5.2 RAG bands (`RagBands`)
Settings default: green ≥ 95 %, amber ≥ 80 %, red below (ratios 0.95 / 0.80), per-KPI override (`green_from`, `amber_from`). Applied to
**capped** performance; null → `none` ("not reported / cannot be calculated").

### 5.3 Annual aggregation (`AnnualAggregator`)
The card's Measuring Method code maps (Settings → KPI) to a rule: `sum` (seed code `cumulative`) or `average` (seed code `average`). Only
**approved, not N/A** results with a value count:
- `sum` → achieved total = Σ achieve; the comparable target = Σ period targets of the same periods (range: Σ mins, Σ maxes);
- `average` → achieved = average of achieves; target = average of the same periods' targets.
- When the reported periods have no period targets, the plan's annual target is used.
Plan performance = `PerformanceCalculator` on (plan target type, plan direction, aggregated target, aggregated achieve): **year-to-date**
performance, which equals the annual performance once every period is reported. Plans with nothing approved → null.

### 5.4 Period generation (`PeriodSchedule`)
Quarterly → Q1–Q4 · Half-yearly → H1, H2 · Annual → FY · Two-year → TY (period = the year and the next; due after the second year).
Default period targets from the annual target: `sum` methods split the target equally (rounded to 4 decimals, the remainder on the last
period); `average` methods (and ranges) repeat it. The Strategy Office adjusts them before confirming the plan.

### 5.5 Composite and qualitative KPIs
- **Composite:** the owner enters data elements; the system computes `achieve` with the card formula (expression sandbox — numbers only,
  whitelisted element variables, time / size limits). Division by zero → validation error, nothing saved.
- **Qualitative:** the owner picks a level of the card's scale; `achieve` = the level value; targets are levels of the same scale.

### 5.6 Scorecards (`ScorecardCalculator`, capped performance only)
- KPI score = plan capped performance for the year (year-to-date). Cascading: a card with child cards shows a **cascaded score** = weighted
  average of the children's scores (child `weight`, null = equal); a parent without own approved results uses the cascaded score.
- Objective = weighted average of its KPIs' scores (card `weight`); perspective = weighted average of its objectives (element `weight`);
  organisation (vision) = weighted average of perspectives. Elements / KPIs with no score are left out and the remaining weights are
  re-normalised; a node with no scored child has no score.
- Department scorecard = weighted average of the KPIs whose responsible unit is the department or below.
- One over-achieving KPI can never lift a node above 100 %: inputs are capped.

### 5.7 Data-quality score per department (`DataQualityCalculator`)
Over the year's submitted results of the department (and below): on-time rate = not late ÷ submitted; rejection rate = results with at
least one return ÷ submitted; evidence completeness = submitted with evidence ÷ submitted (N/A excluded). Score = average of on-time,
(1 − rejection) and completeness.

## 6. New measuring year (K1) — wizard + `StartMeasuringYear` command
1. Strategy Office opens *New year*, selects a year: every **active** card is listed with its period, unit, direction, target type and
   target (from the multi-year target of that year, else the previous year's plan, else empty); cards that already have a plan are shown as
   such.
2. Untick cards not needed; adjust period / unit / direction / targets; *Submit*.
3. One transaction: an annual plan per ticked card (`draft`), its results (`not_open`) with period targets, and the year's period windows
   from the Settings rules. Re-running for the same year only adds missing cards (idempotent).
4. The Strategy Office reviews each plan (period targets) and **confirms** it (single or bulk confirm per year). Results open only for
   confirmed plans (Settings toggle *require confirmation*, default on).

## 7. Approval — process `kpi.result` (and `kpi.definition-change`)
- `kpi.result`: subject = the result (unit = responsible unit, owner = responsible employee). Default chain **one step: Strategy Office /
  PMO** (approver rule: system role `kpi.strategy-office`); the admin adds a Department-manager review step (`departmentDirector` of the
  record) in the Approval Settings centre without code (SPEC K4.6). Comment required on return / reject. Variables: `capped` (number),
  `late` (boolean), `notApplicable` (boolean) for entry conditions.
- **Verification checklist** (Settings → KPI, configurable items with AR / EN labels and *required*): the reviewer's decision goes through
  `POST /kpi/results/{id}/decide` with the checklist answers; *approve* needs every required item ticked. The answers are stored on the
  result (audited). The approval hook refuses a final approval without the required checklist (`kpi.errors.checklistRequired`), so the
  generic approval screens cannot bypass it.
- **Bulk approval** (K4.5): reviewer selects year + department (+ period), sees the pending results where they are the current approver,
  ticks / unticks, confirms the checklist once for the selection and *Update*: each result is decided in its own transaction through the
  engine (`IApprovalEngine.DecideAsync`, a small additive Workflow contract method — ADR 0064); the answer lists approved / failed per
  result.
- Hook `kpi.result`: approved → result `approved`, plan recalculated, card max achieved, owner notified, integration events (§10) in the
  same transaction; returned / rejected → `returned` (`return_count`++), owner notified with the comment; cancelled (recall) → `open`.

## 8. Access (SPEC §2.6 — defaults, editable in Settings → Roles)

Permissions (`kpi.<object>.<action>`): `kpi.card.read`, `kpi.card.manage`, `kpi.strategy.manage`, `kpi.plan.manage` (new year, targets,
windows, confirm), `kpi.result.read`, `kpi.result.submit`, `kpi.result.review` (queue, checklist, bulk; the engine still requires being the
step's approver), `kpi.result.reopen` (reopen, lock), `kpi.scorecard.read`, `kpi.dashboard.read`, `kpi.report.export`,
`kpi.settings.manage`, `kpi.ai.evidence`, `kpi.ai.anomalies`, `kpi.ai.causality`, `kpi.ai.builder`, `kpi.ai.targets`, `kpi.ai.narrative`.

| Role (code) | Scope | Permissions |
|---|---|---|
| KPI owner `owner` (assigned to everyone) | Own (responsible person) | card / result read, result submit, AI evidence / causality / anomalies |
| Department manager `department-manager` | Unit and below | card / result read, scorecard, dashboard, export, AI anomalies / narrative |
| Strategy Office / PMO `strategy-office` (approval role key `kpi.strategy-office`, MFA) | All | everything except settings |
| Executive viewer `executive` | All | card / result read, scorecard, dashboard, export, narrative |
| Administrator `administrator` (MFA) | All | settings, strategy hierarchy, card manage, plan manage |

Data scope map of cards and results: owner employee = responsible person, unit = responsible unit (results carry copies). Lists are
filtered in the query; a record outside scope → 404; visible but not allowed → 403. Approvers get record role `approver` on the result while
their task is open. Strategy elements are readable by everyone with `kpi.card.read` (no personal data).

## 9. Evidence, tasks, links
- Files: purpose `kpi.evidence` (owner `kpi.result`, documents, 20 MB) — read by whoever reads the result, written by the owner while the
  result is open / returned; purpose `kpi.card-document` (owner `kpi.card`) for KPI description documents.
- Linkable types (Links registry): `kpi.card` and `kpi.result` are **task-able** (task to prepare evidence from the result page;
  followers: the owner). Live badge: code / period, title, status, RAG tone.
- **Linked projects / risks** (SPEC §2.3 D): `IKpiLinkedRecordSource` implementations (Portfolio in Phase 9, Risk in Phase 13) return the
  linked record references for a card; the card page resolves them through the Links registry (visible / restricted / unavailable). Proved
  now with a fake source in tests.

## 10. Integration events (ADR 0043 outbox; ADR 0065)
- `KpiResultApproved(ResultId, CardId, CardCode, Year, PeriodCode, Achieve, Actual, Capped, Rag, NotApplicable, ResponsibleUnitId,
  ApprovedAt)` — after every final approval (Risk: KRI thresholds, Phase 13).
- `KpiTargetMissed(… , ConsecutiveRed)` — when an approved result's RAG is **red** (Settings: also *amber*, off by default); carries how many
  consecutive approved periods are red (CAP raises remarks from it, Phase 15). Published in the approval transaction (exactly when the
  approval commits). No consumer in this phase; tests subscribe a recording handler.

## 11. Current status, My Work, notifications, search
- Current-status provider `kpi.result`: not open (opens on …), open / returned (owner: *submit*), pending (step + approvers + my decision),
  approved, locked. My Work items: my open / returned results (due date), plus the engine's approval tasks; a **KPI card** on My Work with
  counts (open, overdue, returned, awaiting my approval).
- Notifications (AR / EN, editable): `kpi.windowOpened`, `kpi.reminder`, `kpi.overdue`, `kpi.escalation`, `kpi.resultApproved`,
  `kpi.resultReturned`, `kpi.resultReopened`, `kpi.changeDecided`.
- Search source `kpi.card` (code, names; scope-filtered).

## 12. AI (Platform.AI registries — drafts and advice only, CLAUDE.md rule 6)
| Feature | Model? | What |
|---|---|---|
| `kpi-evidence-check` | yes | Reads the result's evidence (text extraction + OCR for scans, ADR 0030 / 0033) as untrusted data; says whether the value appears, whether the period matches, what is missing. Advisory card for the reviewer |
| `kpi-anomalies` | no | Flags: jump > threshold vs the previous approved period (default 50 %), identical value to the previous period, value outside the plausible range (negative ratio, percentage > 100 % for capped-at-100 units), far beyond the target (> 3×) |
| `kpi-causality` | yes | Drafts causality and corrective actions from the owner's notes for a missed target; flags weak / generic justifications to the reviewer |
| `kpi-card-builder` | yes | Drafts a KPI card from a description or an uploaded document; a deterministic **SMART check** (specific name, measurable unit, equation present, unit consistent with the equation / percentage, direction set, period set, target present, owner set) plus the model's suggestions |
| `kpi-target-suggestion` | no | Next-year target from the linear trend of past annual achievements, the baseline, the benchmark and the multi-year target, with the reasons listed (message keys) — direction-aware |
| `kpi-narrative` | yes | Bilingual quarterly performance report draft (highlights, under-performers, reasons, actions) from the scorecard of the user's scope |
Prompts: `Ai/Prompts/kpi-*.v1.md`; structured JSON validated against a schema; the offline demo provider answers each.

## 13. MCP tools (ADR 0031)
`my_pending_kpi_results` (read, on), `kpi_card_lookup` (read, on), `department_scorecard` (read, on), `kpi_approval_queue_summary` (read,
on), `submit_kpi_result_draft` (write, **off**: saves the value / notes as a draft on an open result, never submits). No approve tool.

## 14. API (`/api/v1/kpi`, minimal APIs)
Strategy: `GET/POST/PUT /strategy/elements`, `GET /strategy/map?year`. Cards: `GET /cards` (filters code, department, type, objective,
status, search; paging), `GET /cards/options`, `POST /cards`, `GET/PUT /cards/{id}`, `POST /cards/{id}/activate|deactivate`,
`GET /cards/{id}/performance` (yearly + period trend), `GET /cards/{id}/versions`, `GET /cards/{id}/linked`, `PUT /cards/{id}/targets`
(multi-year). Change requests: `GET /changes`, `POST /cards/{id}/changes`, `GET /changes/{id}`, `POST /changes/{id}/submit|withdraw`.
Year: `GET /years/{year}/preview`, `POST /years/{year}/start`, `GET/PUT /years/{year}/windows`, `POST /years/{year}/periods/{period}/lock`.
Plans: `GET /plans?year`, `GET /plans/{id}`, `PUT /plans/{id}`, `POST /plans/{id}/confirm`, `POST /plans/confirm` (bulk). Results:
`GET /results/pending` (mine), `GET /results/{id}`, `PUT /results/{id}/draft`, `POST /results/{id}/submit`, `POST /results/{id}/recall`,
`POST /results/{id}/decide` (checklist), `POST /results/{id}/reopen`, `POST /results/{id}/open-now`. Approvals: `GET /approvals` (queue,
filters year / period / department), `POST /approvals/bulk`. Reports: `GET /scorecard?year&unitId`, `GET /dashboard?year`,
`GET /data-quality?year`, `GET /excellence?year&gem&globalStar`, `GET /export?kind&format`. AI: `POST /ai/...`. Settings:
`GET/PUT /settings`. Idempotency keys on submit / decide / bulk.

## 15. Screens (AR + EN, RTL / LTR; shared components)
KPI home dashboard · KPI cards register (filters, saved view) · card page (header with code / status / RAG chip, tabs Details, Measuring,
Alignment, Plans & results, Performance (yearly bars of actual and capped + period trend), Documents, Linked projects / risks, Versions &
changes, History) · card form (+ AI card builder with SMART check) · **New year wizard** · plan page (period targets, confirm) · **My pending
updates** · result page with the **Submit result wizard** (value or elements or level → causality / corrective actions / notes → evidence,
*Not applicable*), current-status banner, approval panel, reviewer panel with **checklist**, AI evidence check, anomaly flags, tasks ·
**KPI approval** queue + **Bulk approval** tab · **Scorecard** (organisation → perspective → objective → KPI, department view) · **Strategy
map** (objectives by perspective, coloured by capped performance) · Reports (data quality, excellence, exports) · Settings → KPI (general,
windows rules, RAG, checklist, measuring methods, strategy hierarchy, period windows per year). Charts are accessible SVG / CSS built on
the design tokens (as Phases 6–7; ECharts arrives with Phase 17).

## 16. Tests
- Unit: the 14-row K5 table (actual and capped, plus null cases), RAG bands, aggregation by method (N/A excluded, targets of reported
  periods), period generation and default period targets, window rules (Q-K2 dates), late flag, lifecycle (every allowed and forbidden
  transition), scorecard roll-up (weights, capped, re-normalisation, cascading), data quality, composite formula, target suggestion,
  anomalies, SMART check.
- Integration (real PostgreSQL): new year → plans + results + windows; travelling clock opens Q1 on 1 April with notifications, reminder,
  overdue, escalation, late flag; submit with evidence / N/A; checklist enforced; approve and bulk approve (year + department); return;
  reopen; scorecards / strategy map with capped values; scope rules (owner sees own, department manager unit, Strategy Office all, outsider
  404); change control through approval; integration events written to the outbox; fake linked-record source; MCP tools registered (write
  off); AI with a fake model (drafts only, untrusted data).
- E2E (ar + en): KPI cards, new year wizard, pending updates, submit result, approval with checklist, bulk approval, scorecard, strategy
  map, KPI dashboard — screenshots in `docs/screenshots/phase-12/`.

## 17. KPI card picker (Phase 19, SPEC-ADDENDUM A8, ADR 0169)

- **`GET /api/v1/kpi/cards/lookup`** (`kpi.card.read`, scope applied in the query like every list): `search` (number / code, name AR / EN),
  `ids` (resolve the selected cards' labels, even when they are not on the current page), `status` (default active; `all`),
  `page`, `pageSize` ≤ **50** → `PagedResult<KpiCardLookupDto(Id, Code, NameAr, NameEn, Status)>`, ordered by code.
- Arabic normalisation in the query (no new column): the term goes through `SearchText.Normalise`; the code and names are normalised in SQL
  the same way (tashkeel and tatweel removed, alef forms → ا, ى → ي, ة → ه, lower case) — so `مؤشر الأداء` finds `مُؤَشِّر الاداء`.
- **`KpiCardPicker`** (`web/src/features/kpi/KpiCardPicker.tsx`, the one picker for every module): combobox (ARIA 1.2: `role=combobox`,
  `listbox`, `aria-activedescendant`), 250 ms debounced server search, 20 per page with *Show more*, ↑ ↓ Home End Enter Esc, logical CSS (RTL),
  the selected card's label from `ids=` (not from the full card endpoint), clear / change. Used by: Risk KRIs, Portfolio project KPIs
  (existing card), Benchmarking indicators, Audit Management QAIP KPIs, and the KPI excellence report filter keeps its own list (it is a
  report, not a picker). Portfolio's own `/portfolio/kpi-cards` stays for API clients.
