# Phase 12 — Performance Management (KPI): summary

**Status:** built and verified in the build session · awaiting owner acceptance
**Branch:** `claude/admiring-noether-cz1pbd`
**Planning:** the plan is the design note [`docs/design/kpi.md`](../design/kpi.md), written and committed before the code (commit
`docs(kpi): Phase 12 design note…`). This session ran without an interactive approval step (as Phases 5–7), so the plan was written first and
then implemented as it stands; its open choices are ADRs 0060–0065 (numbered from 0060 so the Portfolio phase built in parallel keeps 0047+).

## What was built

| Area | Result |
|---|---|
| **Module** (`Strategy.Modules.Kpi` + `.Contracts`, schema `kpi`) | `strategy_element` (vision → perspective → objective), `kpi_card` (all SPEC §2.3 A fields + cascading parent, weight, baseline, RAG override, composite formula and data elements, qualitative scale), `card_target` (multi-year), `card_version` (every saved definition), `definition_change`, `annual_plan`, `period_window`, `result`. System `kpi` (switchable module, enabled by default via `Modules:Kpi:Enabled`) |
| **PerformanceCalculator** (Contracts, CLAUDE.md §15) | The SPEC K5 pseudo-code exactly: single (increase / decrease / matching, target 0 rules) and explicit range targets; actual and capped stored on results and plans; null = "cannot be calculated", excluded from roll-ups. Exposed to other modules through `IKpiPerformance` (with the RAG bands) |
| **Aggregation** | Measuring Method codes map (Settings → KPI) to `sum` (seed *cumulative*) or `average`; only approved, not-N/A periods count; the comparable target is the targets of the same periods (year-to-date); the annual target when periods have none |
| **New measuring year** (K1) | Preview of active cards with targets from the multi-year target, else the previous plan; untick / adjust period, unit, direction, targets; one transaction creates plans, results per measuring period (Q1–Q4 · H1/H2 · FY · TY) with default period targets (split for cumulative, repeated for average / ranges) and the year's windows; idempotent; plans confirmed singly or in bulk |
| **Period windows** (K2, Q-K2, ADR 0060) | Rules in Settings (opens the day after the period, due the 15th; December periods due 31 January), editable per year; due moment = end of working time in the responsible unit's business hours; hourly job `kpi.period-windows` opens confirmed plans' results, notifies owners once, reminds 3 working days before, overdue notice, escalation to the head of the unit 2 working days after; late submission accepted with `late` and days late; lock / reopen a period |
| **Results** (K3, ADR 0061) | Explicit lifecycle `not_open · open · pending_approval · returned · approved · locked`; draft save, submit (value, data elements for composite KPIs computed by the expression sandbox, scale level for qualitative), evidence required (setting), causality + corrective actions required below target, **Not Applicable** with reason (a submission, verified, excluded); recall; reopen with reason; open now |
| **Approval** (K4, ADR 0064) | Process `kpi.result` (default one Strategy Office step, editable in the Approval Settings centre; variables capped / late / notApplicable); **verification checklist** from Settings enforced on the KPI decision endpoint *and* in the approval hook (generic screens cannot bypass it); **bulk approval** by year + department (+ period) with one checklist confirmation, each result in its own transaction, partial success reported (Workflow contract `IApprovalEngine.DecideAsync`, additive) |
| **Scorecards & strategy map** (§2.7, ADR 0062) | Weighted roll-up of **capped** performance KPI → objective → perspective → vision → organisation, missing scores re-normalised, cascading parents use their children when they have no own results; department scorecards; strategy map (objectives by perspective coloured by RAG); trends and top performers use **actual** |
| **Change control** (ADR 0063) | Target / equation / unit / direction changes after the year started → `kpi.definition-change` approval; approval applies card, plan and open period targets and writes a version; other edits apply directly with a version; feature `kpi.change-control` |
| **Reports** | Dashboard (totals, by type / department / objective, achievement distribution, pending updates, overdue, approval backlog, top / bottom), data quality per department (on-time, rejection, evidence completeness), excellence (GEM criteria / Global Star), exports xlsx / bilingual PDF (audited) |
| **Integrations** (ADR 0065) | `IKpiCatalog` (scope-filtered search / get / year performance for Portfolio's "Existing KPI"), `IKpiLinkedRecordSource` (Portfolio / Risk will implement; the card's *Linked records* tab resolves them through the Links registry), integration events `KpiResultApproved` and `KpiTargetMissed` (with consecutive missed periods) through the outbox in the approval transaction. Cards and results are **task-able** (task to prepare evidence) |
| **Access** (§2.6) | Roles *KPI owner* (everyone, Own), *Department manager* (Unit and below), *Strategy Office / PMO* (All, MFA, approval role `kpi.strategy-office`), *Executive viewer*, *Performance administrator*; 18 permissions with AR / EN labels; 404 outside scope, 403 for visible-but-forbidden |
| **AI** (§2.9) | Evidence checker (text extraction + OCR of the evidence as untrusted data), anomaly flags (no model), causality & corrective-action assistant with weak-justification flags, KPI card builder (text or document) with a deterministic SMART check, target suggestion (no model: trend, cycle target, baseline, benchmark), bilingual performance narrative. Prompts `Ai/Prompts/kpi-*.v1.md`; the demo provider answers all four model prompts |
| **MCP** | `my_pending_kpi_results`, `kpi_card_lookup`, `department_scorecard`, `kpi_approval_queue_summary` (read, on), `submit_kpi_result_draft` (write, **off**, saves a draft, never submits). No approve tool |
| **My Work, status, notifications** | Current status of a result; My Work items (open, overdue, returned) and a KPI card on My Work; 8 AR / EN templates; search source for cards |
| **Web** (AR + EN, RTL / LTR) | KPI area with its own sub-navigation: **dashboard** (tiles, achievement distribution, by type / department / objective, top / bottom, exports, AI narrative), **cards register** (filters incl. GEM / Global Star; capped figure with "actual x %" beside it, "—" with "cannot be calculated"), **card page** (Details, Measuring, Alignment, Plans & results, Performance chart of actual and capped, Documents, Linked records, Versions & change requests, Tasks, History; SMART check, target suggestion, multi-year targets), **card form** (composite formula / data elements, qualitative scale, AI card builder from text or document), **new year wizard** and year plans with bulk confirm, **plan page**, **My pending updates**, **result page** (current-status banner, **submit wizard** value / causality / evidence with live performance preview, N/A, AI causality draft; reviewer panel with the **checklist**, AI evidence check, anomaly flags, previous periods, approvals, tasks, history; reopen / open now), **KPI approval** queue and **bulk approval**, **scorecard** tree with department scores, **strategy map**, **reports** (data quality, excellence, exports), **Settings → KPI** (general, window rules, RAG, checklist, measuring methods, strategy hierarchy, period windows per year with lock / reopen), **My Work KPI card**. Charts are accessible SVG / CSS on the design tokens (ECharts in Phase 17) |

## Acceptance checks (BUILD_PLAN Phase 12)

| Check | Evidence |
|---|---|
| New measuring year generates plans and results; Q1 window opens per Appendix A; owners notified | Integration `Q1_opens_on_1_April_with_notification_reminder_overdue_escalation_and_a_late_flag` (travelling clock: windows 1 Apr / 15 Apr, FY 1 Jan / 31 Jan; due moment 15:30 Asia/Dubai; 31 March nothing, 1 April open for the confirmed plan only, `kpi.windowOpened`, idempotent rerun, reminder, overdue, escalation to the department head, late submission with 5 days late). Unit `WindowRulesTests`, `PeriodScheduleTests`. E2E wizard: `kpi-new-year` |
| Owner submits with evidence; PMO uses the checklist and bulk-approves a department | Integration `Owner_submits_with_evidence_and_the_pmo_approves_with_the_checklist…` (evidence / causality validation, partial checklist refused, generic task approval refused by the hook, approval stores answers, plan recalculated, events) and `Bulk_approval_by_year_and_department_approves_only_the_ticked_results`. E2E: `kpi-submit-result`, `kpi-approval-checklist`, `kpi-bulk-approval` |
| Scorecard and strategy map show capped performance; trend charts show actual | Integration `Scorecard_and_strategy_map_use_capped_performance_while_trends_show_actual` (125 % and 60 % → objective 80 %, not 92.5 %; trend 125 %). Unit `ScorecardCalculatorTests`. E2E `kpi-scorecard`, `kpi-strategy-map`, `kpi-card-performance`, `kpi-dashboard` |
| Portfolio project KPIs can link to KPI Cards; the card shows linked projects | Contracts `IKpiCatalog` + `IKpiLinkedRecordSource`; integration with a fake Portfolio source: the card's linked records resolve through the Links registry (badge `PROJ-T`, outsider 404). **Portfolio itself wires it in Phase 9** (being built in a parallel session), so this check is proven with the test double only |

**Also asked for in the prompt:**

| Request | Evidence |
|---|---|
| The 14-row K5 table (actual and capped) exactly | Unit `PerformanceCalculatorTests.Spec_K5_table_row` (18 cases: rows 1 and 2 for every direction) + null cases + rounding |
| Aggregation by Measuring Method (cumulative / average, N/A excluded) | Unit `AnnualAggregatorTests`; integration `Aggregation_follows_the_measuring_method…` (average 80 / N/A / 100 → 90, 100 %) and the cumulative plan in the first test |
| Period generation per measuring period | Unit `PeriodScheduleTests` (Q / H / FY / TY, dates, default period targets) |
| Window opening with the fake clock; late flag | Integration window test (above); unit `WindowRulesTests.Late_is_after_the_due_moment…` |
| Bulk approval | Integration (above), including a result no longer pending reported as failed while the others pass |
| Scorecards using capped values | Integration and unit (above) |
| Scope rules | Integration `Owners_see_their_kpis_managers_their_unit_and_the_strategy_office_all` (owner own only, other owner 404, manager unit read but 403 on submit, outsider 404 on card / result / history / evidence, 403 on create / year / dashboard / settings, Strategy Office all; catalog contract scope-filtered) |
| Every allowed / forbidden transition | Unit `ResultLifecycleTests` (6 statuses × 7 actions) |
| Change control with versions | Integration `Definition_changes_after_the_year_starts…` (422 on a direct direction change, wording change → version 2, one pending change at a time, another Strategy Office member approves, plan and open periods follow, version 3 linked to the request) |
| Composite and qualitative KPIs | Unit `CompositeFormulaTests`; integration (45 / 50 × 100 = 90; division by zero refused; level 2 not on the scale refused; level 5 of target 3 → 166.67 % / 100 %) |
| Integration events | `KpiResultApproved` and `KpiTargetMissed` (red, consecutive 1, target 25) received by recording handlers through the outbox |
| AI and MCP | Integration `KpiAiTests` (fake model: evidence text wrapped as untrusted data, reported value in the system prompt, nothing decided; causality draft not saved; card builder values validated + SMART; narrative; anomalies and target suggestion without a model; outsider 404). MCP tools registered, write tool off, no approve tool |
| Every server message key in AR and EN | Vitest `kpi/messageKeys.test` |

## Quality gates run

| Gate | Result |
|---|---|
| `dotnet build Strategy.slnx -c Release` · `dotnet format --verify-no-changes` | 0 warnings, 0 errors · clean |
| `dotnet test --solution Strategy.slnx` | **2 241** tests (2 240 + 1 after the fix) (unit, architecture and integration on real PostgreSQL); the full run had one failure, an expected Phase 12 fact (everyone now also holds `kpi.owner`), fixed and re-run green. New: **61** KPI unit tests, **9** KPI integration tests |
| `tools/check-api-client.sh` · `tools/check-vulnerable.sh` | OpenAPI document and TS client up to date · no High / Critical |
| `npm run lint` · `format:check` · `typecheck` · `npm test` | clean · **102 / 102** Vitest |
| `npm run build` · `npm audit --audit-level=high` · `npm run lint:licenses` | OK · no High / Critical (the same 14 moderate dev-tool advisories as earlier phases) · 613 packages allowed |
| `npx playwright test` | KPI scenario **2 / 2** (ar + en) with screenshots (`E2E_SCREENSHOTS=phase-12`). Whole suite **80 / 80** in Arabic and English on a fresh database (Phase 12 adds 1 scenario × ar + en; the job dashboard spec expects 13 jobs). |

Existing tests updated for Phase 12 facts: the job dashboard lists `kpi.period-windows` (API test and E2E: 13 jobs); the authorization matrix fills
`{year}` / `{period}` route parameters and sends a file to `/document` uploads; the effective roles of a new user include `kpi.owner`.

**Dependencies added:** none (NuGet or npm).

## Decisions (ADRs)
All six were **accepted by the owner** as proposed before the pull request.
- [0060](../decisions/0060-kpi-period-windows.md) Window rules (Q-K2), Q4 due 31 January with the year-end periods, due moment in working
  time, reminders / escalation, late flag.
- [0061](../decisions/0061-kpi-result-lifecycle-and-values.md) Result lifecycle; reject and return both unlock; **N/A is a verified
  submission**; values in the KPI's unit, performance as ratios.
- [0062](../decisions/0062-kpi-strategy-hierarchy-and-scorecards.md) Fixed vision / perspective / objective kinds (labels translatable);
  perspective derived from the objective; weights, re-normalisation, cascading.
- [0063](../decisions/0063-kpi-definition-change-control.md) "Year started" = a plan for this or an earlier year; controlled items; approval
  applies the change; feature switch.
- [0064](../decisions/0064-kpi-approval-checklist-and-bulk.md) Checklist enforced in the hook; bulk approval per result; additive Workflow
  contract `DecideAsync`.
- [0065](../decisions/0065-kpi-contracts-and-integration-events.md) Catalog, performance and linked-record contracts; the two integration
  events.

## Deviations
- **From the SPEC:** none known. Interpretations are in the ADRs (notably N/A goes through approval, Q4 due date, perspective derived from the
  objective).
- **From the plan / CLAUDE.md:**
  - Charts are accessible SVG / CSS on the design tokens, not ECharts (as Phases 6–7); ECharts arrives with Phase 17.
  - Feature switches (§2.7 "switchable"): only definition change control has a switch; the other enhancements are simply unused when not
    configured (no hierarchy, weights or composite formula → plain behaviour).
  - The new-year wizard proposes **next year** by default; the Strategy Office can pick another year.
- **Platform changes (additive):** `IApprovalEngine.DecideAsync` (Workflow contract); the demo AI provider answers the four KPI prompts;
  `Modules:Kpi:Enabled` true in `appsettings*.json` and the test factory; dev seed: everyone may open Performance Management, Mariam holds the
  Strategy Office and performance administrator roles. A subscription is one handler type per integration event (the outbox message type is
  the handler's name) — noted for Risk / CAP.

## Known gaps (by design: later phases or follow-ups)
- Portfolio (Phase 9) implements `IKpiLinkedRecordSource` and uses `IKpiCatalog` / `IKpiPerformance`; Risk (Phase 13) subscribes to
  `KpiResultApproved`, CAP (Phase 15) to `KpiTargetMissed`. Until then they are proven with test doubles.
- Benchmark results linked to the card's benchmark fields (§2.8) wait for Benchmarking (Phase 15).
- Employee-level KPIs are out of scope (§2.7).
- The KPI My Work card counts from the pending and approval lists (no dedicated counts endpoint).

## How to demo
```bash
REGISTRY_PREFIX=mirror.gcr.io/ docker compose -f deploy/docker-compose.yml --profile dev up -d postgres gotenberg mailpit
dotnet run --project src/Host/Strategy.Host
cd web && npm ci && npm run dev        # http://localhost:5173
```
1. **Mariam** (`mariam@strategy.local` / `Strategy#Demo2026`, MFA — Strategy Office): Settings → KPI → strategy hierarchy (vision, perspectives,
   objectives), checklist, window rules. Performance → KPI cards → *New card* (or *Draft with AI*), responsible person Ahmed.
2. Performance → **New year** → year, targets per KPI → *Create the year*; confirm the plans. Adjust the year's windows in Settings → KPI if
   you want them open today.
3. **Ahmed** (`ahmed@strategy.local`): My Work shows the KPI card; **My pending updates** → a result → *Submit result* wizard (value,
   causality, evidence) → submit.
4. Mariam: **KPI approval** → the result → tick the checklist → approve (try *Check the evidence with AI*). *Bulk approval* tab: year +
   department, untick what to keep, confirm the checklist, *Update*.
5. **Scorecard**, **Strategy map** and **Dashboard** show capped performance; the card's Performance tab shows actual and capped per year.

## Cloud environment notes
- PostgreSQL 16 local (`max_connections = 600`, `STRATEGY_TEST_POSTGRES`); Docker started manually for Gotenberg / Mailpit through
  `mirror.gcr.io/`; E2E against database `strategy` with `E2E_CHROMIUM_PATH=/opt/pw-browsers/chromium`.
- Screenshots (`E2E_SCREENSHOTS=phase-12`) in [`docs/screenshots/phase-12/`](../screenshots/phase-12/) as `ar-*` / `en-*` pairs: `kpi-cards`,
  `kpi-new-year`, `kpi-pending-updates`, `kpi-submit-result`, `kpi-approval-checklist`, `kpi-bulk-approval`, `kpi-scorecard`,
  `kpi-strategy-map`, `kpi-dashboard`, `kpi-card-performance`.
