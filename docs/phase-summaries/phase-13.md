# Phase 13 — Risk Management: summary

**Status:** built and verified in the build session · awaiting owner acceptance
**Branch:** `claude/upbeat-albattani-bdkh5p`
**Planning:** the plan is the design note [`docs/design/risk.md`](../design/risk.md), written and committed before the code (commit
`docs(risk): Phase 13 design note…`). This session ran without an interactive approval step (as Phases 5–14), so the plan was written first and
then implemented; its open choices are ADRs **0100–0107** (numbered from 0100 so Phase 10 — Portfolio Agile, built in a parallel session — keeps
its range). They are **proposed** and need the owner's acceptance.

## What was built

| Area | Result |
|---|---|
| **Module** (`Strategy.Modules.Risk` + `.Contracts`, schema `risk`, system `risk`, switchable) | `framework_version`, `category` (multi-level, **appetite empty by default — Q-R3**), `risk`, `control`, `treatment_action`, `action_update`, `kri`, `assessment` (snapshots), `risk_event` + `event_risk`, `review`, `committee_decision`. All audited, `xmin`, no hard deletes except an explicit, audited discard of a never-submitted draft |
| **Framework & shared scoring** (ADR 0100) | Versioned framework (a saved version is immutable; saving creates v n+1 with a reason): likelihood / impact scales 1–5 with descriptions and **impact criteria per impact area** (financial, reputation, service, compliance, safety), scoring **multiplicative (default) / additive / custom formula** with `L`, `I`, `C` through the shared expression sandbox (validated on every matrix cell), **bands Low 1–4, Medium 5–9, High 10–16, Critical 20–25** (editable; a score in a gap takes the higher band), residual-status mapping, control-effectiveness scale (Effective / Partially / Ineffective with a `C` factor), review frequency per band (Critical / High monthly, Medium quarterly, Low half-yearly), project residual rule. **Preview** returns the matrix and the number of open risks (and project risks) whose rating would change, with examples; save with **recalculate now** (one audit entry + snapshot per re-rated risk) or **keep until next review** |
| **Register** (SPEC §3.4 A–F) | Cause → event → consequence statement, categories, type, emerging / opportunity (opportunities switchable), owning unit, owner / coordinator / follow-up, objectives from the KPI strategy hierarchy, **inherent → residual → target** ratings (residual from residual L / I and overall control effectiveness = lower median of the controls, Risk Office override), residual status derived with override + justification, appetite check, response strategy, Top-10 with manual rank, escalation, review dates, reassessment flag with reason |
| **Numbering** | `risk.risk` `{UNIT}{YY}-R{SEQ:3}` — the counter partition is the rendered prefix, so units whose codes render alike share one sequence per year and numbers never collide (unit codes are unique in this installation; the test uses codes differing only by case); `risk.action` `ORA-{SEQ:4}`, `risk.event` `RE-{YYYY}-{SEQ:3}` |
| **Workflows R1–R5** (ADR 0102) | Explicit lifecycle (draft · under review · active · closed · rejected), one pending process at a time. `risk.review` (R1) and `risk.periodic-review` (R3) default Risk Office; `risk.closure` (R5) Risk Office then **Risk Committee when `residualBand == "critical"`** — all editable in the Approval Settings centre. Return / reject / recall, Risk Office adjustments while under review, reopen with reason, committee decisions, manual and automatic escalation (Critical / outside appetite) |
| **Treatment & monitoring** | Controls separate from actions; action progress with history, evidence files, **convert completed action to control** (rating chosen, risk flagged for reassessment), task-able actions (Task Management through the Links registry). Review wizard with per-control ratings and residual L / I. Hourly job **`risk.monitor`**: review reminders N working days before due and overdue notice; action reminders and escalation to the risk owner / coordinator. **Review due dates** = last review + months by rating, rolled to the next working day of the unit's business hours, due at the end of working time |
| **KRIs** (ADR 0104) | Risk ↔ KPI card links with basis (value / capped performance), direction and warning / breach thresholds. Subscribes to **`KpiResultApproved`** (ADR 0065): the reading is stored; a breach flags the risk for reassessment, notifies owner, coordinator and Risk Office and publishes `RiskKriBreached`. The KPI card lists its linked risks (`IKpiLinkedRecordSource`) |
| **Project Risks** (ADR 0103) | Live, permission-filtered read of Portfolio project risks through `IProjectRiskDirectory` (Phase 9 contract) — never copied; each row opens the risk inside its project; on the dashboard as the separate category `project` (inherent heat-map + rating counts) behind *Include project risks* |
| **Portfolio on the shared framework** (ADR 0101) | Portfolio references `Risk.Contracts`; `IProjectRiskScoring` became async and uses `IRiskScoring` (same scorer as enterprise risks); Portfolio subscribes to `RiskFrameworkChanged` and re-rates active project risks through EF (one audit entry per change). No data migration. Changes in Portfolio are minimal and additive |
| **CAP / Audit hooks** (Phase 15 / 16) | Events `RiskEventLogged`, `RiskControlRatedIneffective`; `IRiskDirectory` (risk / event summaries, `LinkCapRemarkAsync`, `FlagForReassessmentAsync`); risks are remark sources and task-able |
| **Access** (SPEC §3.7, ADR 0107) | Roles *Risk Office* (All, MFA, approval role `risk.risk-office`), *Department risk coordinator* (unit and below), *Risk owner* (everyone, own), *Department risk viewer*, *Risk Committee / leadership* (`risk.risk-committee`), *Internal Audit (viewer)*; record roles action-owner / control-owner / approver granted by the module; 20 permissions with AR / EN labels; 404 outside scope, 403 for visible-but-forbidden. Risk Office is a role; the owning unit is configuration (ADoF: Internal Audit Office now, Strategy Office later by reassignment) |
| **AI** (ADR 0106) | Risk identification from text / an uploaded document or charter, statement quality check (deterministic flags + model rewrite), control & treatment suggestions using similar risks, **duplicate detection without a model** (other units' matches only as a count), review assistant, bilingual quarterly report / Top-10 briefing. Drafts only; prompts `Ai/Prompts/risk-*.v1.md`; the demo provider answers them |
| **MCP** | `risk_register_search`, `risk_details`, `my_risk_actions`, `risk_heatmap_summary`, `risk_top10`, `risks_outside_appetite` (read, on); `update_risk_action_progress_draft` (write, **off**, draft note only). No approve tool |
| **Reports** | Register, Top-10, unit profile, action tracker (PDF via Gotenberg / Excel via ClosedXML, bilingual, audited) and the generated **Risk Assessment Mechanism** PDF |
| **Web** (AR + EN, RTL / LTR) | Risk area with sub-navigation: **dashboard** (tiles, **inherent vs residual heat-maps** — click a cell to list its risks, by rating / category, Top-10, outside appetite, overdue actions and reviews, movement this quarter, KRI breaches, AI quarterly report), **register** with saved views and exports, **risk wizard** with the clickable matrix, live chips, impact criteria, statement check and duplicate check, **risk page** (chips inherent → residual → target, current-status banner, approvals, tabs Overview, Assessment with history chart, Controls, Treatment actions, KRIs with KPI results, Events, Reviews, Documents, Activity), **My actions** with quick progress update, **Project Risks**, **mechanism** page (printable / PDF), **reports**, **Settings → Risk framework** (scales, formula editor with live preview and affected count, bands, effectiveness, review frequency, versions, categories with appetite, settings), **My Work** risk card. Heat-maps and charts are CSS / SVG (no new dependency, ADR 0105) |

## Acceptance checks (BUILD_PLAN Phase 13)

| Check | Evidence |
|---|---|
| Admin changes the formula; preview shows impact; chosen recalculation option applied and audited | Integration `Framework_preview_counts_affected_risks_and_recalculation_is_versioned_and_audited` (additive preview: 2 × 5 High → 7 Medium listed, 1 × 1 not; invalid formula reported; *next review* keeps the old version; custom formula *now* re-rates, snapshot `recalculation`, audit entry with the new score; versions listed). E2E `framework-formula-preview` (the payment risk listed as changing) |
| Heat-maps (inherent vs residual) and Top-10 work; project risks appear as a filterable category | Integration `Dashboard_heatmaps_top10_reports_and_mcp_tools` and `RiskProjectTests` (category `project`, inherent cell counts). E2E `dashboard-heatmaps` (cell click lists the risk), `top10`, `project-risks` |
| A KPI result breaching a KRI threshold notifies the risk owner | Integration `A_kri_breach_on_an_approved_kpi_result_flags_the_risk_and_notifies_the_owner` (85 = warning, no flag; 120 = breach → flagged, `risk.kriBreach` notification with deep link, dashboard breach, My Work item). E2E: a real KPI card, year, submission with evidence and Strategy Office approval → `kri-breach` |

**Also asked for in the prompt:**

| Request | Evidence |
|---|---|
| Scoring multiplicative, additive, custom formula; band mapping | Unit `RiskScoringTests` (incl. `C`, rounding, gaps 17–19 → Critical, additive bands, matrix), `FrameworkRulesTests` |
| Formula change preview counts affected risks; recalculation audited | Integration (above) |
| KRI breach on an approved KPI result flags the risk | Integration (above); unit `Kri_levels` |
| Review due dates by rating in working time | Integration `Review_due_dates_follow_the_rating_and_roll_to_the_next_working_day` (Thu 10 Sep + 1 month = Sat → Mon 12 Oct, due 15:30 Dubai; 3 and 6 months) and the approval test (next review = framework months from approval) |
| Numbering unique for units sharing a code | Integration `Register_submit_and_approve…` (`RAxxxx26-R001`, then `R002` for the unit whose code renders the same; `27-R001` next year) |
| Project Risks tab shows only project risks the user may see; none copied | `RiskProjectTests` (requester sees their project's risk, another department none, outsider 403, `risk.risk` row count unchanged) |
| Project risks scored by the shared framework | `RiskProjectTests` (same score as the shared service; framework change re-rates the project risk with an audit entry) |
| Critical closure needs the Risk Committee step | Integration `Closure_of_a_critical_risk_needs_the_risk_committee_step_and_a_medium_one_does_not` |
| Every transition | Unit `RiskLifecycleTests` (5 statuses × 10 actions) |
| Access rules | Integration `Controls_actions_reviews_and_record_roles_follow_the_access_rules` (action owner reads via record role, updates only their action, cannot edit the risk; viewer read-only; outsider 404 on every sub-resource) |

## Quality gates run

| Gate | Result |
|---|---|
| `dotnet build Strategy.slnx -c Release` · `dotnet format --verify-no-changes` | 0 warnings, 0 errors · clean |
| `dotnet test --solution Strategy.slnx` | **3 268** tests on real PostgreSQL (local server, `STRATEGY_TEST_POSTGRES`): 3 251 green in the full run. The 17 others: 4 Phase 13 facts fixed and re-run green (job dashboard lists `risk.monitor`, everyone holds `risk.owner`, the heat-map route in the authorization matrix — now query parameters); **13 need Docker sidecars not available in this sandbox** (Gotenberg PDF snapshot ×2, Mailpit ×1, external identity providers ×5 + class cleanups ×5) — unchanged code, green in earlier phases with Docker. New: **119** risk unit tests, **8** risk integration tests |
| `tools/check-api-client.sh` · `tools/check-vulnerable.sh` | OpenAPI document and TS client up to date · no High / Critical |
| `npm run lint` (incl. RTL class check) · `format:check` · `typecheck` · `npm test` | clean · **218 / 218** Vitest (new: risk message keys AR / EN, client score / band helper, heat-map component) |
| `npm run build` · `npm audit --audit-level=high` · `npm run lint:licenses` | OK · no High / Critical · allowed |
| `npx playwright test e2e/risk.spec.ts` | **2 / 2** (ar + en), screenshots in [`docs/screenshots/phase-13`](../screenshots/phase-13) with `E2E_SCREENSHOTS=phase-13`. No Docker here: PostgreSQL ran locally and the PDF sidecar was replaced by a local Chromium renderer speaking the Gotenberg HTTP API (not committed); the rest of the E2E suite was not re-run (only `platform.spec.ts`'s job count changed: 18 jobs) |

Existing tests updated for Phase 13 facts: the job dashboard lists `risk.monitor` (API test and E2E: 18 jobs); the effective roles of a new user
include `risk.owner`; `ProjectLifecycleTests.ApprovedProjectAsync` is `internal` so the risk tests reuse it.

**Dependencies added:** none (NuGet or npm). `THIRD_PARTY_LICENSES.md` unchanged.

## Decisions (ADRs) — proposed, awaiting the owner
- [0100](../decisions/0100-risk-framework-versions-and-scoring.md) Framework versions, one scoring engine, band gaps → higher band, recalculation now / at next review.
- [0101](../decisions/0101-portfolio-project-risks-on-the-shared-framework.md) Portfolio project risks on the shared framework; re-rating on `RiskFrameworkChanged`; inherent stays an integer column.
- [0102](../decisions/0102-risk-lifecycle-and-approvals.md) Lifecycle, one pending process, number on first save, approvals R1 / R3 / R5.
- [0103](../decisions/0103-project-risks-tab-live-read.md) Project Risks live and scoped; separate dashboard category; inherent heat-map only.
- [0104](../decisions/0104-kris-and-kpi-results.md) KRIs react to approved KPI results.
- [0105](../decisions/0105-risk-screens-charts-reports.md) CSS / SVG heat-maps and charts (no ECharts this phase); reports through the platform renderers.
- [0106](../decisions/0106-risk-ai-and-mcp.md) AI features and MCP tools.
- [0107](../decisions/0107-risk-office-role-and-system-ownership.md) Risk Office is a role; owning unit configurable.

## Deviations from the SPEC
None without an ADR. Interpretations recorded: overall control effectiveness = lower median of the controls (design §5.4); scores between bands
take the higher band; project risks (no residual L / I) are not placed on the residual heat-map.

## Known gaps
- The Project Risks table shows the Portfolio risk *category* code (the `portfolio.risk-category` lookup is not yet returned by `/risk/options`).
- The full E2E suite and the Docker-dependent integration tests were not run in this sandbox (no Docker); run them in CI.
- CAP (Phase 15) and Audit (Phase 16) consume the contracts; until then the CAP remark on an event shows as a chip only.
- Shared files touched additively for the parallel Phase 10: solution, host references / module catalog / appsettings / dev seed, Portfolio
  (one reference, async scoring, one handler), the demo AI provider, router / sidebar / settings navigation / home page / i18n index, audit and AI
  feature key lists in the web, `validation.required` / `expressions.errors.invalid` translations.

## How to demo
1. Start the dependencies and the host (CLAUDE.md §18); sign in as `mariam@strategy.local` (Risk Office and Risk Committee in the demo).
2. **Settings → Risk framework:** switch to *Custom formula* `(L * I) * (1 - C/10)` — the matrix and "N of M open risks would change rating" update
   live; save as a new version choosing *recalculate now* or *keep until next review*.
3. **Risk Management → Register → New risk** (as a coordinator, e.g. after assigning *Department risk coordinator* to Ahmed's unit): statement,
   classification, click the matrix, save, submit; approve as Mariam on the risk page.
4. On the risk page add a control, a treatment action (progress to 100 % → *Convert to control*), a KRI on a KPI card; approve a KPI result above
   the breach threshold → the risk shows *Reassessment required* and the owner is notified; start the periodic review.
5. **Dashboard:** inherent vs residual heat-maps (click a cell), Top-10, *Include project risks*; **Project Risks**, **Mechanism**, **Reports** (PDF / Excel).
