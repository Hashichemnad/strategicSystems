# Design note — Phase 16: Audit Management (`Strategy.Modules.AuditManagement`)

SPEC §10 (all, decisions §10.8), §0.0 / §0.2 / §0.4 / §0.5, §3 (risk links), §6 (criteria documents), §9 (CAP source-system contract),
§2 (KPI publishing), §4 (engagement teams), Appendix A. Written before the code; open choices are ADRs **0130–0137**.

**Operating model (confirmed by the owner, SPEC §10.1 / §10.8).** The system is operated **only by the Audit Office**. The Audit Office
follows up with departments outside the system (meeting, email, letter) and records status, progress, evidence and comments itself.
**Department users are read-only**: they see the audit reports, findings, recommendations, status, completion and Audit Office comments of
their own section / department and take no action. Recommendations are followed up **here**, not in CAP (audit independence). No default
due dates. **Layer 2** (the full audit lifecycle) is built behind a feature switch that is **off by default**.

## 1. Projects

| Project | Content |
|---|---|
| `Strategy.Modules.AuditManagement.Contracts` | Constants (`AuditLinkTypes`, `AuditFindingStatuses`, `AuditPriorities`), integration events `AuditFindingClosed`, `AuditFindingLinkedToRisk`. No EF Core |
| `Strategy.Modules.AuditManagement` | `Domain/` (report, finding, finding ↔ risk link, recommendation, follow-up, revision / risk-acceptance log; Layer 2: auditable entity, plan, plan item, engagement, working paper, survey; pure status rules) · `Application/` · `Infrastructure/` (`AuditMgmtDbContext`, schema `audit_mgmt`) · `Api/` · `Jobs/` · `Ai/` (+ `Ai/Prompts/auditmgmt-*.v1.md`) · `Mcp/` |

System code **`auditmgmt`** (`SystemKind.Module`, `Modules:AuditMgmt:Enabled`, enabled by default). The platform audit trail already owns
the code `audit`; `auditmgmt` keeps permission codes unambiguous (`auditmgmt.finding.read` vs `audit.entry.read`). API
`/api/v1/auditmgmt/...`, SPA `/audit-management/...`.

References: Contracts of Risk (`IRiskDirectory` — links, reassessment flag, control effectiveness), Document Registry (`IDocumentDirectory`
— criteria document badge / search), CAP (`ICapRemarkSources` — improvement observations), Teams (`ITeamsDirectory` — engagement team),
KPI (`IKpiCatalog`, new `IKpiResultFeed` — QAIP / closure-rate publishing). Nothing references the module's internals.

**Additive changes elsewhere** (minimal, ADRs):
- **Platform.Access — record roles granted to a unit** (`RecordPrincipalKinds.Unit`): a grant to unit U matches every user whose employee is
  placed in U or below it (or heads U), the same meaning as a role assignment to a unit. Used for the "department and below" visibility
  setting (ADR 0130). The approval engine ignores unit holders when resolving record-role approvers.
- **Risk.Contracts** `IRiskDirectory`: `ListControlsAsync(riskId, user)`, `LowerControlEffectivenessAsync(controlId, reason)` (one level
  down the framework's effectiveness scale, audited, flags the risk for reassessment; ineffective → existing `RiskControlRatedIneffective`).
- **Kpi.Contracts** `IKpiResultFeed.ProposeAsync(cardId, year, periodCode, achieve, note)`: writes the value as a **machine draft** on the
  open result of that period (the existing `DraftFromMcp` flag); the KPI owner reviews and submits as usual (ADR 0135).
- **Phase 15 fixes:** benchmark study KPI picker searches as you type (server search, no 200-card list); actions proposed from a benchmark
  development plan default to the CAP action type `improvement` (editable).

## 2. Configuration

### 2.1 Settings → Audit management (`AuditMgmtSettings`, area `auditmgmt`)

| Setting | Default | Notes |
|---|---|---|
| `departmentScope` | `section` | `section`: department users see findings of their own unit and the units below it (a department director therefore sees the department and its sections; section staff their section). `department`: staff of a section also see every finding of their **department** (the nearest ancestor at `departmentLevelCode`) and below — granted as a unit record role (ADR 0130) |
| `departmentLevelCode` | `department` | the org level that counts as "department" for the setting above |
| `notifyDepartments` | **false** | optional notices to department users (head of the responsible unit + the recommendation contacts) when a finding concerning them is created, updated (status / completion) or closed |
| `followUpReview` | **false** | optional internal review of follow-ups (auditor → Chief Audit Executive) through the approval engine (`auditmgmt.follow-up-review`); a follow-up applies to its recommendation only once approved |
| `followUpReminderDays` | 3 | reminder to the responsible auditor N days before the next follow-up date |
| `dueReminderDays` | 7 | reminder to the responsible auditor N days before a recommendation's (revised) due date |
| `reminderWorkingDays` | true | the reminder offsets count working days of the responsible unit's business hours (ADR 0027) |
| `capObservations` | `manual` | `off` / `manual`: the Audit Office may send an *improvement* observation (not a formal finding) to CAP as a remark (ADR 0135) |
| `kpiPublishing` | empty | map of measure → KPI card (`closure-rate`, `overdue-findings`, Layer 2 `plan-completion`, `report-timeliness`, `satisfaction`); empty = not published |
| `reportTimelinessDays` | 30 | Layer 2 QAIP: a report is timely when issued within N days after fieldwork ends |

The Layer 2 switch is a platform feature flag `auditmgmt.lifecycle` (Settings → Features, **off by default**, ADR 0136).

### 2.2 Lookups (seeded, editable; logic only on codes)
`auditmgmt.report-type` (internal-audit, external-audit, special-report, revenue-audit, consultancy, other) · `auditmgmt.priority`
(low, medium, high, critical — **configurable**: the codes are the default set; dashboards use lookup order) · `auditmgmt.finding-type`
(finding = formal finding, improvement = improvement observation) · `auditmgmt.management-response` (agree, partially-agree, disagree) ·
`auditmgmt.entity-kind` (unit, process, service, system, project, risk).

### 2.3 Numbering
`auditmgmt.report` `AUD-{YYYY}-{SEQ:3}` · `auditmgmt.finding` **`AR-{SEQ:4}`** · `auditmgmt.recommendation` **`AA-{SEQ:4}`** ·
`auditmgmt.follow-up` **`AAQ-{SEQ:4}`** · `auditmgmt.engagement` `ENG-{YYYY}-{SEQ:3}`. All editable formats, generated in the creating
transaction (SPEC defaults for findings / actions / updates).

## 3. Data model (schema `audit_mgmt`; audited, `xmin`, audit columns, no hard deletes — cancel with reason)

| Table | Main columns |
|---|---|
| `report` | number, title_ar/en, type_code, auditor_body (text), lead_auditor_employee_id (responsible auditor in the Audit Office), audited_unit_ids (jsonb), period_from / period_to, report_date, status (`draft` · `issued` · `under_follow_up` · `closed` · `cancelled`), distribution_employee_ids (jsonb), distribution_note, summary, engagement_id (Layer 2), cap_source_id, cancel_reason, issued_at, closed_at |
| `finding` | number, report_id, title_ar/en, type_code, condition, criteria, criteria_document_id (Document Registry), cause, effect, finding_date, year (= finding date year), responsible_unit_id, department_unit_id (nearest ancestor at the department level, stored for scope / reporting), priority, repeat (bool), repeat_of_finding_id, expected_closing_on, actual_closed_on, status (`open` · `late` · `closed` · `risk_accepted` · `cancelled`), completion (numeric(9,4), mean of live recommendations' latest %), auditor_employee_id (defaults to the report's lead auditor), cap_remark_id, cancel_reason, `text_search` (normalised Arabic, trigram — repeat detection) |
| `finding_risk` | finding_id, risk_id, control_id (nullable), flagged_for_reassessment, control_lowered, linked_at, linked_by. Unique (finding, risk, control) |
| `recommendation` | number, finding_id, action_ar/en (recommended action), management_response_code + comment (optional), implementation_plan, contact_employee_id, due_on (**required, no default**), revised_due_on + revised_reason, status (`not_started` · `continuing` · `late` · `completed` · `risk_accepted` · `cancelled`), latest_pct, completed_on, risk_acceptance_justification / reference / on, notes, cancel_reason |
| `follow_up` | number, recommendation_id, follow_up_on, achieved_on, pct, actions_implemented, evidence_text, next_follow_up_on (**required when pct < 100 %**), comment (Audit Office follow-up comment), recorded_by_employee_id, review_status (`none` · `pending` · `approved` · `returned`), applied (bool), review_note. Evidence files: purpose `auditmgmt.evidence`, owner the follow-up — unlimited, previewable |
| `recommendation_log` | recommendation_id, kind (`due-revision` · `risk-acceptance` · `cancellation`), previous_due_on, new_due_on, justification, reference, recorded_by, at |
| *Layer 2* `auditable_entity` | kind_code, name_ar/en, unit_id, risk_id, inherent_rating (priority codes), last_audited_on, active, origin (`manual` · `org` · `risk`) |
| `plan` | year, title, status (`draft` · `submitted` · `approved` · `returned`), approved_at, note. Unique per year |
| `engagement` | number, plan_id, entity_id, title_ar/en, objectives, scope, team_id (Teams), lead_auditor_employee_id, planned_quarter, planned_start / end, budget_hours, fieldwork_end_on, stage (`planned` · `planning` · `fieldwork` · `reporting` · `follow_up` · `closed` · `cancelled`), risk_score (priority ordering), report_id, satisfaction (1–5) + comment |
| `work_paper` | engagement_id, reference, title, test_step, sample, result, conclusion, status (`draft` · `prepared` · `reviewed`), prepared_by, reviewed_by, reviewed_at. Files purpose `auditmgmt.work-paper` |

Reports readable by department users are those with at least one finding they may read; Audit Office comments are part of the follow-up
rows they see. Working papers, plan, universe and surveys are Audit-Office-only.

## 4. Rules and state machines (pure, table-tested)

### 4.1 Recommendation status (`RecommendationStatus.Derive`)
Final statuses `completed`, `risk_accepted`, `cancelled` are kept. Otherwise: latest % ≥ 100 → `completed` (completed_on = achieved date);
effective due (`revised_due_on ?? due_on`) **before today** and % < 100 → `late`; % > 0 → `continuing`; else `not_started`. "Today" is the
installation's date (Settings → Regional) from `IClock` — the fake clock in tests. Manual transitions: **risk-accept** (justification +
reference, logged), **cancel** (reason, logged), **revise due date** (new date + reason, logged, original kept), **reopen** a risk-accepted
recommendation (logged).

### 4.2 Finding status (`FindingStatus.Derive`)
`cancelled` is manual (reason) and kept. Over the recommendations: none → `open`. All recommendations in {completed, risk_accepted,
cancelled}: if every non-cancelled one is `risk_accepted` → **`risk_accepted`**, otherwise **`closed`** (all cancelled → `closed`);
`actual_closed_on` = today, cleared on reopening. Otherwise any `late` recommendation (or expected closing date passed) → `late`, else
`open`. Completion = mean latest % of the non-cancelled recommendations (risk-accepted count as their last %).

### 4.3 Report status
`draft` → **issue** → `issued` (distribution notified). Derived after any finding change: issued / under follow-up with ≥ 1 open finding →
`under_follow_up`; every finding in {closed, risk_accepted, cancelled} and at least one not cancelled → **`closed`** (closed_at). A closed
report reopens to `under_follow_up` when a finding reopens. `cancelled` manual (only without open findings or with reason; findings stay).

### 4.4 Follow-up
Recorded by the Audit Office: follow-up date, achieved date, %, actions implemented, evidence (text + files), comment, recorded by.
Validation: % in 0–100, **next follow-up date required below 100 %** and after the follow-up date, follow-up date not in the future.
With review off the follow-up applies at once (recommendation latest %, status, finding / report derivation, notices). With review on it
is `pending` until the approval engine approves (`auditmgmt.follow-up-review`, default step: role `auditmgmt.chief-audit-executive`).

### 4.5 Late status and reminders (job `auditmgmt.monitor`, hourly, idempotent)
Recomputes recommendation / finding / report statuses with today's date (late by due date). Sends once per date (stamps on the row):
reminder to the responsible auditor before the next follow-up date (`followUpReminderDays`) and before a due date (`dueReminderDays`),
late notice when a recommendation turns late; department notices if `notifyDepartments`. Publishes configured KPIs once per period.

### 4.6 Layer 2 engagement stages
`planned` → start → `planning` → `fieldwork` → `reporting` (creates the draft audit report linked to the engagement) → issue report →
`follow_up` → `closed` (all findings closed or the Audit Office closes it). `cancelled` from any open stage with reason. Plan:
`draft` → submit (approval `auditmgmt.plan`, default step: role `auditmgmt.audit-committee`) → `approved` / `returned` (edit, resubmit).

## 5. Access (SPEC §10.6, ADR 0132)

| Permission | Scope | Audit Office | Department user (everyone) | Audit committee / leadership | Viewer |
|---|---|---|---|---|---|
| `auditmgmt.finding.read` | scopable (unit = responsible unit) | All | **UnitAndBelow** (+ department grant, ADR 0130) | All | All (narrow in Settings → Roles) |
| `auditmgmt.office.manage` — every write (reports, findings, recommendations, follow-ups, revisions, risk acceptance, cancel, import, risk / CAP links, AI confirm) | All | ✓ | — | — | — |
| `auditmgmt.dashboard.read` · `auditmgmt.report.export` | — | ✓ | — | ✓ | — |
| `auditmgmt.settings.manage` | — | ✓ | — | — | — |
| `auditmgmt.lifecycle.manage` (Layer 2 universe, plan, engagements) · `auditmgmt.workpaper.manage` | — | ✓ | — | plan read via approval only | — |
| `auditmgmt.ai.extract` · `.ai.evidence` · `.ai.repeat` · `.ai.summary` | — | ✓ | — | summary | — |

Roles: `audit-office` (MFA, approval role key `auditmgmt.audit-office`), `chief-audit-executive` (follow-up review approver; same
permissions as the office), `department-user` (assigned to everyone; read only), `audit-committee` (read All, dashboard, export, AI
summary; plan approver), `viewer`. **Every write endpoint requires `auditmgmt.office.manage`** → department users get **403**; reads of a
finding / report outside the scope return **404**, lists / search / Ask / MCP filter in the query (`IAccessService.ApplyScopeAsync`).
Department-scope grants: in `department` mode the module grants record role `department-reader` on each finding to the unit principal
of its department; a settings change re-syncs all grants (in the save transaction).

## 6. Import and AI extraction (ADR 0134)

**Excel import** (`POST /reports/{id}/import?mode=validate|commit`, template `GET /import/template`, validation report
`POST /reports/{id}/import/report` → xlsx with an *errors* column). One row per recommendation; rows with the same `finding_ref` form one
finding. Columns: `finding_ref`*, `title_ar`, `title_en`, `type`, `condition`*, `criteria`, `cause`, `effect`, `finding_date`*,
`responsible_unit_code`*, `priority`*, `repeat_of` (AR number), `expected_closing_date`, `recommendation_ar`, `recommendation_en`,
`implementation_plan`, `due_date`, `contact_employee_no`, `management_response`. Every row is validated first; commit is all-or-nothing in one
transaction (channel `import`). A recommendation needs its own due date (no default).

**AI extraction** (`auditmgmt-extract`): upload the report (PDF / Word / scan — OCR through the platform, Arabic + English) or paste text →
**draft** findings with condition / criteria / cause / effect, recommendations, suggested responsible unit and priority. Nothing is saved:
the Audit Office reviews, edits, sets due dates and confirms selected rows, which go through the same create path as the form
(`POST /reports/{id}/findings/batch`).

## 7. Integrations (ADR 0135)

| System | Behaviour |
|---|---|
| Risk | Link a finding to a risk (and optionally one of its controls) — options *flag the risk for reassessment* (`FlagForReassessmentAsync`, reason `audit-finding`) and *lower the control's effectiveness* (one level). The risk / control must be readable by the auditor in Risk. Universe pulls risks |
| Document Registry | Criteria link to a document (badge shown only if the viewer may see it); reports may also be stored as controlled documents in DocReg (manual) |
| CAP | Optional (`capObservations`): a finding of type *improvement* can be **sent to CAP** as a remark (source per audit report `auditmgmt.report`, external key = finding id); the finding shows the remark's live status. Formal findings never go to CAP |
| KPI | Measures closure rate (closed ÷ closable findings in the year) and overdue findings (count), Layer 2 plan completion, report timeliness and satisfaction, proposed as machine drafts on the mapped KPI cards' open periods |
| Teams | Engagement team = an existing team (picker; shown live through `ITeamsDirectory`) |
| Organisation | Responsible units, department ancestor, contacts, auditors, universe units |
| Links / Search / My Work | Linkable types `auditmgmt.finding` (task-able) and `auditmgmt.report`; record search source for findings (scope-filtered → hidden from search and Ask); My Work cards for auditors: follow-ups due (next follow-up date ≤ today + reminder days), recommendations late, follow-ups to review |

## 8. AI (ADR 0137) — drafts only

`auditmgmt-extract` (above) · `auditmgmt-evidence`: evidence text + file text of a follow-up being recorded → assessment (supports the %?),
gaps, **draft follow-up comment** the auditor edits · `auditmgmt-repeat`: similar earlier findings (trigram similarity, same or other
departments; available without a model) with an optional model rationale · `auditmgmt-summary`: audit committee narrative (AR / EN) from
the dashboard figures (status, overdue high / critical, trends, risk acceptances), accepted text goes into the committee pack.

## 9. MCP tools

Read (on by default): `audit_findings_by_department`, `audit_finding_details`, `audit_followups_due` (Audit Office), `audit_overdue_findings`,
`audit_my_finding_status` (department users: their own scope). Write: `audit_record_follow_up_draft` — **off by default**, requires
`auditmgmt.office.manage` (department users are refused even when an admin enables it). All run through the same handlers / scope.

## 10. Screens (AR + EN, RTL / LTR)

Audit dashboard (tiles; by status, year, department, priority, report type; open vs closed trend by month; overdue and ageing; closure rate
by department; repeat findings; risk accepted; filters) · **department view** (read-only: my unit's audits total / closed / active, active
findings with status, completion, last Audit Office comment, due soon) · reports list and **report page** (header, findings register,
distribution, file, import, AI extraction review, issue / cancel) · **finding page** (header number / priority / status / completion; condition
/ criteria / cause / effect; recommendations with follow-up timeline; evidence viewer; linked risks / controls / document; repeat; CAP;
history) · **record follow-up** dialog (with AI evidence review) · revise due date / risk acceptance / cancel dialogs · **follow-up calendar**
(this week / this month, by auditor, overdue) · reports (findings register per report, department follow-up report, revenue audit full report,
audit committee pack — PDF / Excel) · Settings → Audit management · Layer 2 (only while the switch is on): audit universe, annual plan
(prioritised by risk and last audit, submit for approval, quarterly status), engagement page (stages, objectives / scope, team, programme and
working papers, report, survey) and QAIP panel.

## 11. API (`/api/v1/auditmgmt`)

Options, settings, dashboard, department view, calendar, exports `/exports/{kind}` · reports CRUD + issue / cancel / files / findings /
import · findings CRUD + cancel / risks / cap / similar · recommendations CRUD + revise / risk-accept / reopen / cancel · follow-ups (record,
list) · AI (`/ai/reports/{id}/extract`, `/ai/recommendations/{id}/evidence-review`, `/ai/findings/{id}/repeat`, `/ai/summary`) · Layer 2
`/universe`, `/plans`, `/engagements`, `/engagements/{id}/work-papers` (filter: feature on, else 404 problem `auditmgmt.lifecycleOff`) ·
History tabs through `MapRecordHistory`.

## 12. Tests

Unit: recommendation / finding / report derivation tables, settings validation, engagement stage table, import row grouping. Integration:
department users read only their own unit's findings in both scope modes and every write endpoint answers 403 (incl. MCP write tool
enabled); out-of-scope finding 404, hidden from search and Ask; closure rules (finding + report); late by due date with the travelling
clock and reminders once; next follow-up required below 100 %; optional review; Layer 2 404 while off, available when on; working papers
invisible to non-audit users; Excel import with validation report (all-or-nothing); AI extraction returns drafts and saves nothing until
confirmed; risk link flags reassessment and lowers the control; CAP send for improvement observations; committee pack PDF. E2E (ar + en)
with Phase 16 screenshots.
