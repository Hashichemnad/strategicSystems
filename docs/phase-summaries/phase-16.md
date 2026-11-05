# Phase 16 — Audit Management: summary

Status: **built — awaiting owner acceptance**. Design note: [`docs/design/audit.md`](../design/audit.md) (§13 "As built").
ADRs **0130–0137** (status *Proposed*).

## What was built

| Area | Result |
|---|---|
| **Operating model** (`Strategy.Modules.AuditManagement` + `.Contracts`, schema `audit_mgmt`, system `auditmgmt` — `audit` is the platform audit trail) | Only the **Audit Office** (role `auditmgmt.audit-office`, plus *Chief audit executive*, *Audit committee*, *Viewer*) writes. Every employee holds **Department user** (read-only, `UnitAndBelow`): they see the findings of their **own section** or their **department and below** (setting `departmentScope`, default *section*), through **record roles granted to a unit** (ADR 0130), re-synchronised when the setting or a finding's department changes. Non-office readers never see findings of draft reports. Every write — UI, API and MCP — answers 403 for them; out-of-scope records answer 404 and are hidden from search and Ask |
| **Layer 1 — findings follow-up** | **Reports** (internal / external audit, special, revenue audit, consultancy… — lookup), **findings** `AR-NNNN` (department, priority, criteria, cause, effect, risk links), **recommendations** `AA-NNNN` (owner unit, **no default due date** — the auditor sets it), **follow-ups** `AAQ-NNNN` (percentage, actions implemented, evidence, comment; **next follow-up date required below 100 %**), optional review step by the Chief audit executive (approval process `auditmgmt.follow-up-review`, off by default). **Log** of due-date revisions and risk acceptances (reason + approver); **cancel with a reason**. Derived status: recommendation *late* after its due date; finding *closed* when every recommendation is completed, risk-accepted or cancelled; report *closed* when all findings are. The original due date may change until the first follow-up, afterwards only through a logged revision. **Excel import** of findings + recommendations with a row-by-row validation report, all or nothing. **Follow-up calendar** and reminders to the auditor (job `auditmgmt.monitor`, once per item and day), optional **notices to departments** (setting, off by default) |
| **Screens and reports** | Home, reports list and report page (findings, issue, AI extraction review), findings register (views: all, open, late, closed, risk accepted, mine, my unit, repeat, due soon), finding page (stepper, current status, recommendations with follow-up form and log, risks, documents, tasks, history), department read-only view, calendar, dashboard (tiles, by department / priority / ageing, trends), Settings → Audit management. **Exports** in PDF and Excel: findings register, department follow-up, revenue audit full report, **audit committee pack** (with AI summary draft) |
| **Layer 2 — audit lifecycle** (platform feature `auditmgmt.lifecycle`, **off by default**; endpoints answer 403 first, then 404 `auditmgmt.lifecycleOff`) | **Audit universe** (auditable entities pulled from the organisation, rating, last audit), **risk-based annual plan** (score = rating weight × (1 + years since last audit, capped at 5)) with approval `auditmgmt.plan`, **engagements** with stages (planned → planning → fieldwork → reporting → follow-up → closed, or cancelled with a reason; transition table), **working papers** visible to the Audit Office only, sign-off that refuses self-review, **draft-to-final report** (moving to *reporting* creates the engagement's draft report in Layer 1; issuing it makes it final and starts the follow-up), **QAIP KPIs** (plan completion, report timeliness, findings closure rate, client satisfaction) published to KPI cards through `IKpiResultFeed` |
| **Integrations** | **Risk**: findings link to risks and controls; a link flags the risk for reassessment and can lower the control effectiveness one level (`LowerControlEffectivenessAsync`; the lowest level raises *control rated ineffective*). **Document Registry**: criteria links to registry documents. **CAP**: *improvement observations* become remarks through the Phase 15 source contract (`ICapRemarkSources`), live link back. **Teams**: engagement team (shown live). **KPI**: QAIP results proposed as drafts on the latest open period. Links registry (task-able, remark source), My Work (follow-ups due, findings to follow), search, current-status messages, History |
| **AI and MCP** | AI features `auditmgmt-extract` (findings from a report file / text — drafts reviewed and confirmed by the office, nothing saved before), `auditmgmt-evidence` (evidence review and follow-up comment draft), `auditmgmt-repeat` (repeat-finding detection with trigram similarity), `auditmgmt-summary` (committee summary). MCP read tools on: `audit_findings_by_department`, `audit_overdue_findings`, `audit_my_finding_status`, `audit_finding_details`, `audit_followups_due`; write tool `audit_record_follow_up_draft` **off by default** and Audit-Office only |
| **Phase 15 fixes** | The Benchmarking KPI indicator picker is now a **search-as-you-type** picker (`features/kpi/KpiCardPicker`, server search, 20 matches); actions created from a benchmark development plan default to the CAP action type **Improvement**, editable in the plan row |
| **Web** (AR + EN, RTL / LTR) | `web/src/features/auditmgmt/*`, namespace `auditmgmt` in both languages (message-key test against the C# sources), sidebar entry, settings page, History records, AI feature keys |

## Acceptance checks and requested tests

| Requested test | Evidence (integration `AuditApiTests` unless noted) |
|---|---|
| Department-user scope; 403 on every write including MCP | `Department_users_read_only_their_own_units_findings_in_both_scope_modes_and_every_write_is_refused` |
| 404 out of scope; hidden from search and Ask | `Out_of_scope_findings_are_hidden_from_search_ask_and_mcp_and_the_write_tool_refuses_department_users` |
| Closure rules for findings and reports | `A_finding_closes_when_all_recommendations_are_completed_risk_accepted_or_cancelled_and_the_report_when_all_findings_are_closed` + unit tests |
| Late status with the fake clock; reminders to the auditor | `Late_status_follows_the_due_date_with_the_fake_clock_and_reminders_go_to_the_auditor_once` |
| Next follow-up date required below 100 % | unit tests (`FollowUp.Validate`) + the closure test (400 `auditmgmt.errors.nextFollowUpRequired`) |
| Layer 2 unavailable while off; working papers invisible to non-audit users | `Layer_2_is_unavailable_while_the_switch_is_off_and_working_papers_are_invisible_to_non_audit_users` |
| Excel import with a validation report | `Excel_import_validates_every_row_reports_errors_and_commits_all_or_nothing` |
| AI extraction never saves without confirmation | `Ai_extraction_returns_drafts_and_nothing_is_saved_until_the_audit_office_confirms` |
| Optional review step | `The_optional_review_step_applies_a_follow_up_only_after_the_chief_audit_executive_approves` |
| Integrations (Risk, CAP) and exports | `Risk_links_flag_the_risk_and_lower_the_control_improvement_observations_go_to_cap_and_reports_export` |
| E2E (ar + en) | `web/e2e/audit.spec.ts`: Layer 1 flow (report, AI extraction review, finding, follow-up, calendar, dashboard, department view, committee pack) and Layer 2 (switch on → plan → engagement with working papers → switch off hides it) |

## Quality gates run

| Gate | Result |
|---|---|
| `dotnet build Strategy.slnx -c Release` · `dotnet format --verify-no-changes` | 0 warnings, 0 errors · clean |
| `dotnet test --solution Strategy.slnx` | BACKEND_RESULT |
| `tools/check-api-client.sh` · `tools/check-vulnerable.sh` | OpenAPI document and TS client up to date · no High / Critical |
| `npm run lint` (incl. RTL class check) · `format:check` · `typecheck` · `npm test` | clean · **322 / 322** Vitest (new: Audit Management message keys and UI codes in AR and EN) |
| `npm run build` · `npm audit --audit-level=high` · `npm run lint:licenses` | OK · no High / Critical (moderate advisories only, unchanged dependencies) · 617 packages allowed |
| `npx playwright test e2e/audit.spec.ts` (ar + en) | **4 / 4** |

## Decisions (ADRs, *Proposed*)
0130 record roles granted to units · 0131 data model, numbering and closure · 0132 Audit-Office-only write model ·
0133 follow-ups, late status and reminders · 0134 Excel import and AI extraction · 0135 integrations (Risk, Document Registry, CAP, KPI,
Teams) · 0136 Layer 2 behind a feature switch · 0137 AI, MCP and reports.

## Deviations
- **From the SPEC:** none known; interpretations are in the ADRs.
- **From the design note (updated, §13):** the KPI feed takes `(cardId, asOf, …)` and writes to the latest open period that has ended;
  the Layer 2 permission check precedes the switch; the original due date is editable only until the first follow-up.
- **Shared files changed additively:** Access (`RecordPrincipalKinds.Unit`, unit grants in effective access), Workflow (unit holders
  skipped when resolving record-role approvers), Organisation directory (unit by code, units at a level), Risk directory (controls,
  lower effectiveness), KPI contract (`IKpiResultFeed`), demo AI answers, Host module catalogue / appsettings / dev seed (everyone may open
  `auditmgmt`; Mariam holds the Audit Office and Audit committee roles), web router, sidebar, settings navigation, History records, AI
  feature keys, i18n index, Benchmarking study tabs. Test lists updated: job dashboard (`auditmgmt.monitor`), access administration
  (`auditmgmt.department-user`), the authorization-matrix request builder (multipart body for `/evidence-review`). The CAP due-date
  test now releases its frozen clock before restoring settings (it previously relied on test ordering).

## Known gaps
- Engagement teams are chosen by Teams id: no team search contract exists yet (the team and its members are shown live).
- The follow-up calendar lists dates by week / month and auditor; it has no drag-and-drop rescheduling.
- QAIP KPI results are proposed as drafts; the KPI owner submits them as usual.

## How to demo
```bash
REGISTRY_PREFIX=mirror.gcr.io/ docker compose -f deploy/docker-compose.yml --profile dev up -d postgres gotenberg mailpit
dotnet run --project src/Host/Strategy.Host
cd web && npm ci && npm run dev        # http://localhost:5173
```
1. **Mariam** (`mariam@strategy.local`, MFA — Audit Office): Audit management → Reports → *New report*; add findings by hand, by
   **Excel import** or by **AI extraction** (review the drafts, confirm). Set due dates on the recommendations, *Issue* the report.
2. On a recommendation: *Record follow-up* (below 100 % the next follow-up date is required), revise a due date, accept a risk, cancel
   with a reason. Watch the finding and report close. Calendar and dashboard show the follow-ups; export the committee pack.
3. **Ahmed** (`ahmed@strategy.local`): Audit management shows only his section's issued findings, read-only.
4. Settings → Features → *Audit lifecycle* on: universe, risk-based plan (approve), engagement stages, working papers, final report.

## Cloud environment notes
- PostgreSQL 16 local (`STRATEGY_TEST_POSTGRES`, `max_connections = 600`); Docker for Gotenberg / Mailpit and the Testcontainers images
  through `mirror.gcr.io/`; Tesseract and poppler-utils installed. E2E against a fresh `strategy` database (the new roles are seeded on an
  empty database), host started with `RateLimiting__PermitLimit=10000`, `E2E_CHROMIUM_PATH=/opt/pw-browsers/chromium`.
- ESLint on the whole web tree needs `NODE_OPTIONS=--max-old-space-size=6144` in this container.
- Screenshots (`E2E_SCREENSHOTS=phase-16`) in [`docs/screenshots/phase-16/`](../screenshots/phase-16/) as `ar-*` / `en-*` pairs:
  `audit-report`, `audit-ai-extraction`, `audit-finding`, `audit-record-follow-up`, `audit-calendar`, `audit-dashboard`,
  `audit-department-view`, `audit-committee-pack`, `audit-plan`, `audit-engagement`.
