# Design note — Phase 15: Corrective Action Plan (`Strategy.Modules.Cap`)

SPEC §9 (all, decisions §9.9), §0.0 / §0.2 / §0.5, §2.8 (KPI miss → CAP), §3.8 (risk events → CAP), §8.2 (benchmarking → CAP), CLAUDE.md §15.
Written before the code; open choices are ADRs **0120–0125** (numbers from 0120 so Phase 11 — Wasel, built in a parallel session — keeps its
range). Benchmarking (the first consumer of the source-system contract) is in [`benchmarking.md`](benchmarking.md).

CAP is the organisation's **single place for improvement opportunities, observations and findings** from any source and the corrective
actions taken on them. Other systems create their actions here instead of tracking actions themselves. The **PM Office** (a role, not a
department: `cap.cap-office`) registers sources, validates actions and approves completion; the **remark owner** (section manager by
default) responds, analyses the root cause and plans actions; the **assigned employee** implements and reports monthly.

## 1. Projects

| Project | Content |
|---|---|
| `Strategy.Modules.Cap.Contracts` | `ICapRemarkSources` (the **source-system contract**: ensure a source for a record, add remarks with proposed actions, read live progress), integration events `CapRemarkStatusChanged`, `CapActionCompleted`, constants (`CapSourceTypes`, `CapPriorities`, `CapRemarkStatuses`, `CapLinkTypes`). No EF Core |
| `Strategy.Modules.Cap` | `Domain/` (source, remark, action, remark ↔ action link, achievement update, extension request, effectiveness check, suggestion, lifecycles and pure rules) · `Application/` · `Infrastructure/` (`CapDbContext`, schema `cap`) · `Api/` · `Jobs/` · `Ai/` (+ `Ai/Prompts/cap-*.v1.md`) · `Mcp/` |

System code `cap`, `SystemKind.Module` (switchable, `Modules:Cap:Enabled`, enabled by default). CAP references the Contracts of Portfolio
(budget routing, ADR 0124), KPI (`KpiTargetMissed`, card read), Risk (`RiskEventLogged`, `RiskControlRatedIneffective`, `IRiskDirectory`)
and Teams (`ITeamsDirectory` for the optional team of a source). Nothing references CAP internals.

**Additive changes in other modules** (kept minimal for the parallel Phase 11 session): Portfolio.Contracts gets `IProgramRequestDrafts`
(+ `IPortfolioDirectory.SearchProjectsAsync`), Portfolio implements them in one new file and seeds the suggestion-source value
`action-plan`; KPI cards and results get the `remark-source` link capability; the Risk web *events* tab shows the real remark link.

## 2. Configuration

### 2.1 Settings → CAP (`CapSettings`, settings area `cap`)

| Setting | Default | Notes |
|---|---|---|
| `dueDays` (critical / high / medium / low) | 30 / 60 / 90 / 180 | SPEC §9.9; days from the assigned date (ADR 0122) |
| `countWorkingDays` | false | false: calendar days, the due day rolled forward to a working day; true: working days of the unit's business hours |
| `rejectionNeedsPmoConfirmation` | **false** | SPEC §9.1 / §9.9: rejection is final by default |
| `rootCauseRule` | `priority == "critical" \|\| priority == "high" \|\| sourceType == "internal-audit" \|\| sourceType == "external-audit"` | sandboxed expression (ADR 0022), variables `priority`, `type`, `sourceType`, `criterion`; true → root cause required before actions are submitted |
| `effectivenessVerification` | **false** | SPEC §9.3 D |
| `effectivenessMonths` | 3 | verification due = completion + N months |
| `monthlyReminderDay` | 1 | day of month (1–28) of the monthly status notification |
| `escalatePmoWorkingDays` | 5 | late action: owner notified at once, PM Office after N working days |
| `mirrorProjectProgress` | false | budget-routed actions copy the linked project's progress (ADR 0124) |
| `kpiMissMode` / `kpiMissConsecutive` | `manual` / 1 | `manual` = suggestion for one-click by PMO, `automatic` = remark raised at once, `off`; raised when the consecutive red periods ≥ N |
| `riskEventMode` / `riskControlMode` | `manual` / `manual` | same three modes for risk events and ineffective controls |
| `reassignWithinUnit` | true | the owner may reassign only to employees of the remark's department and below |

### 2.2 Lookups (seeded, editable, codes in logic)
`cap.source-type` (workshop, benchmarking, internal-audit, external-audit, excellence-assessment, kpi-performance, risk-event,
customer-complaint, employee-survey, management-review, other) · `cap.remark-type` (nonconformity, observation, improvement) ·
`cap.criterion` (generic GEM-style set, empty-able) · `cap.action-type` (correction, corrective, preventive, improvement) ·
`cap.priority` (critical, high, medium, low — labels only, codes are logic). Source types used by integrations are constants in Contracts.

### 2.3 Numbering (`settings.numbering`)
`cap.source` `CAPS-{YYYY}-{SEQ:3}` · `cap.remark` `CAP-{UNIT}-{YYYY}-{SEQ:3}` (unit = the concerned department's code, SPEC example
`CAP-SPMO-2025-002`). Action numbers are `{RemarkNo}-{n}` with `n` a counter on the **home remark** (the remark the action was created on)
— not a numbering format, because the prefix is another record's number (ADR 0120). Generated in the creating transaction.

## 3. Data model (schema `cap`; audited, `xmin`, audit columns, no hard deletes except explicit discard of never-submitted draft actions)

| Table | Columns (main) |
|---|---|
| `source` (§9.3 A) | `number`, `title_ar/en`, `source_type_code`, `description`, `received_on`, `owner_employee_id`, `unit_id`, `origin` (`manual` · `system`), `linked_type` + `linked_id` (unique when set — one source per linked record), `team_id`, `viewer_employee_ids` (jsonb: source-system viewers, §9.7), `status` (`active` · `closed`) |
| `remark` (§9.3 B) | `number`, `source_id`, `text_ar/en`, `criterion_code`, `type_code`, `priority`, `department_unit_id`, `section_unit_id`, `owner_employee_id`, `assigned_on`, `due_on`, `due_at`, `responded_at`, `status`, `rejection_reason`, `pmo_decision` + note, `root_cause_method` (`five-whys` · `fishbone` · `text`), `root_cause` (jsonb: whys[], fishbone {category: causes[]}), `root_cause_text`, `linked_type` / `linked_id` (the specific source record, e.g. a KPI result), `external_key` (idempotency of source-system remarks, unique per source), `action_seq`, `all_rejected`, `reopened_count`, `completed_at`, `verification_due_on`, `overdue_notified_at`, `cancel_reason` |
| `action` (§9.3 C) | `number`, `home_remark_id`, `details_ar/en`, `type_code`, `budget_required`, `budget_available`, `estimated_cost` numeric(18,2) + `currency`, `related_process`, `assigned_employee_id`, `planned_start/end`, `revised_end`, `validation_status` (`draft` · `pending` · `approved` · `rejected` · `returned`), `validation_note`, `project_id`, `program_request_id` + `program_request_number`, `actual_start/end`, `achieved_pct` numeric(9,4) ratio, `status` (`not_started` · `in_progress` · `late` · `completed` · `cancelled`), `pending_process`, `completion_note`, `completed_at`, `monthly_reminded_for` (yyyy-MM), `late_notified_at`, `pmo_escalated_at` |
| `remark_action` | `remark_id`, `action_id` (PK both), `linked_on`, `linked_by` — **many-to-many** (§9.3 C "link an existing action") |
| `achievement_update` | `action_id`, `pct`, `note`, `actual_start/end`, `at`, `by`, `draft` (MCP drafts, confirmed in the UI) — history |
| `extension_request` (§9.3 E) | `action_id`, `previous_end`, `requested_end`, `justification`, `status` (`pending` · `approved` · `rejected` · `cancelled`), `decided_at`, `decision_note` |
| `effectiveness_check` (§9.3 D) | `remark_id`, `due_on`, `effective`, `comment`, `verified_at`, `verified_by` |
| `suggestion` | `kind` (`kpi-miss` · `risk-event` · `risk-control`), `resource_type` / `resource_id` (unique per kind — idempotent events), parent link (`parent_type` / `parent_id`: KPI card, risk), `title_ar/en`, `details` (jsonb facts: period, value, target, consecutive), `unit_id`, `status` (`open` · `raised` · `dismissed`), `remark_id`, `dismiss_reason` |

Files (Phase 3 store, allow-listed, audited): purposes `cap.source-document` (owner source), `cap.root-cause` (owner remark),
`cap.action-evidence` (owner action — **proof of completion**).

## 4. Lifecycles (explicit transition tables, unit-tested — ADR 0121)

### 4.1 Remark
| From | Action | To |
|---|---|---|
| `assigned` | accept | `planning` |
| `assigned` | reject | `rejected` — or `rejection_pending` when *PMO confirmation* is on |
| `rejection_pending` | confirm-rejection (PMO) | `rejected` |
| `rejection_pending` | send-back (PMO) | `assigned` |
| `planning` / `correction` | submit (actions for validation) | `validation` |
| `validation` | validated (derived) | `implementation` · `correction` · `planning` (all rejected) |
| `implementation` | actions-completed (derived) | `completed` — or `verification` when effectiveness verification is on |
| `verification` | verify effective | `completed` |
| `verification` | verify not effective | `planning` (reopened, `reopened_count`+1, new actions) |
| `implementation` | action added / reopened | `planning` |
| any open | cancel (PMO, reason) | `cancelled` |

Stage (stepper, SPEC §9.6): **Analysis** (`assigned`, `rejection_pending`) › **Corrective Action** (`planning`, `validation`,
`correction`) › **Implementation** › **Verification** › **Closed** (`completed`, `rejected`, `cancelled`).

Derived state after each validation decision (`RemarkProgress`, pure): ignoring cancelled actions — any `pending` → `validation`; else any
`draft`/`returned` → `correction`; else none approved (all rejected) → `planning` with `all_rejected`; else → `implementation`. In
implementation, when every approved action is `completed` the remark completes. Reassignment (owner or PMO) is allowed while the remark is
open and not in validation; the new owner is notified and gets the record role.

### 4.2 Action
Validation status `draft → pending → approved | rejected | returned`, `returned → pending` on resubmission. Execution status for approved
actions: `not_started → in_progress → completed`; `late` is set by the monitor when past `revised_end ?? planned_end` below 100 % and
cleared when the end moves (approved extension) or the action completes. `cancelled` by the owner (draft / returned) or the PM Office (any
open, reason). One pending process at a time per action (`cap.action-validation` · `cap.completion` · `cap.extension`).

**Linking an existing action** (§9.3 C): the owner picks an open action of another remark (search by number / text, AI duplicate check);
the link is stored in `remark_action`. An already approved action keeps its validation (it was validated once by the PMO) and counts as
approved for the new remark; an action still in validation counts with its current status. Unlink is allowed while the remark is in
planning / correction; the home remark cannot be unlinked.

## 5. Workflows (SPEC §9.4)

**C1 — Analysis.** PM Office creates a source (title, type, received date, owner, documents, optional team) and adds remarks (text AR / EN,
criterion, type, priority, department, section, owner — defaulted to the **head of the section, else of the department**, from the
Organisation module; editable). **Due date** = assigned date + `dueDays[priority]` (ADR 0122); changing the priority recomputes it unless an
action is already approved. The owner is notified (`cap.remarkAssigned`), accepts or rejects (reason required). Rejection is final
(`cap.remarkRejected` to the PMO) unless confirmation is on (`cap.rejectionToConfirm` → PMO confirms or sends back). Root cause: method,
5 whys (up to 7) / fishbone categories (people, process, equipment, materials, environment, management — lookup-free fixed codes with
labels) / free text, attachments; required before submit when `rootCauseRule` is true.

**C2 — Planning and validation.** The owner adds actions (new or linked), then **submits for validation**: every `draft` / `returned`
action of the remark is submitted as its own approval instance (`cap.action-validation`, subject = the action, variables `priority`,
`budgetRequired`, `budgetAvailable`, `estimatedCost`). The **Validate Actions** screen (PMO) lists all actions of a remark side by side with
approve / reject / return (comment) and *Link project* — each decision goes through `IApprovalEngine.DecideAsync` (the same checks as the
task screen; ADR 0064 precedent), so delegation, audit and history are the engine's. The approval hook moves the action and recomputes
the remark (§4.1). **Budget routing** on approval: budget not required, or required and available → implementation; **required and not
available** (and no project linked) → a **draft Program Request** is created in Portfolio through `IProgramRequestDrafts` inside the same
transaction (ADR 0124): name / description / justification from the action and remark, unit = department, section, requester = remark
owner, estimated budget = estimated cost, expected dates = planned dates, **suggestion source `action-plan`**, link back to the action
(Links registry, kind `remark-source`). The action keeps its number and shows the request badge (live status); with *mirror project
progress* on, the monitor copies the progress of the project created from the request.

**C3 — Implementation.** Monthly notification (`monthlyReminderDay`, once per month per assignee: `cap.monthlyReminder` with counts by
status). The assignee updates achievement (%, actual start / end, note) — each update kept in `achievement_update` (history). Past the end
below 100 % → `late`, `cap.actionLate` to assignee and remark owner, then `cap.actionEscalated` to the PM Office after
`escalatePmoWorkingDays` working days. **Extension request** (new end > current, justification) → `cap.extension` (default PMO): approved
→ `revised_end` set (original kept in `planned_end`, history in the request), late cleared. **Completion**: at 100 % with at least one
evidence file the assignee requests completion → `cap.completion` (default PMO) → `completed`; returned → back to `in_progress` at 100 %
for more evidence. All actions completed → remark `completed` (or `verification` with due = completion + N months, `cap.verificationDue`
to the PMO) → `completed` or reopened. **Report back**: `CapRemarkStatusChanged` and `CapActionCompleted` (outbox) carry the source's
linked record; the source page and the consumers' pages read live progress through `ICapRemarkSources.GetProgressAsync`.

**Current status messages** (SPEC §9.4, `cap.currentStatus.*`): waitingAccept (owner) · waitingPmoConfirm · waitingActions ("fill actions
and submit") · rootCauseRequired · waitingPmo (validation, approvers) · correct (returned) · actionsRejected · actionsApproved (none
started) · inProgress · waitingCompletion · waitingVerification · completed · rejected · cancelled; for actions: waitingValidation ·
returned · inProgress · late · waitingCompletion · waitingExtension · completed.

## 6. Approval processes (seeded version 1, editable in the Approval Settings centre — SPEC §9.9: PMO is the only approver)
| Key | Resource | Default steps |
|---|---|---|
| `cap.action-validation` | `cap.action` | PM Office (role `cap.cap-office`), SLA 24 working hours, approve / reject / return |
| `cap.completion` | `cap.action` | PM Office |
| `cap.extension` | `cap.action` | PM Office |

Approver record role `approver` on `cap.action`. Hooks run in the engine transaction (ADR 0023).

## 7. Access (SPEC §9.7 — seeded, editable — ADR 0125)
Permissions: `cap.remark.read`, `cap.source.manage` (sources and remarks: create / edit / assign / cancel), `cap.remark.respond`
(accept / reject / reassign / root cause / actions / submit), `cap.action.update` (achievement, evidence, extension, completion request),
`cap.office` (validation screen, confirm rejection, link project, cancel actions, verify effectiveness, raise suggestions),
`cap.dashboard.read`, `cap.report.export`, `cap.settings.manage`, AI `cap.ai.extract / root-cause / suggest / quality / summary`.

| Role | Scope | Grants |
|---|---|---|
| `cap-office` (PM Office, MFA, approval role key `cap.cap-office`) | All | everything |
| `department-manager` | Unit and below | read, dashboard, export |
| `participant` (everyone) | Own | read, respond, action update, AI root cause / suggest |
| `viewer` (leadership) | All | read, dashboard, export, AI summary |

Scope of a remark: own = owner employee; unit = department unit (or section). Record roles on `cap.remark`, granted / revoked by the module:
`remark-owner` (read, respond), `action-assignee` (read, action update — the handler also checks the assignee is the caller),
`source-viewer` (read — e.g. the Benchmarking coordinator and topic owners for the remarks of their study). Actions are readable when any
of their remarks is readable or the caller is the assignee. 404 outside scope, 403 for visible-but-forbidden.

## 8. Source-system contract (`ICapRemarkSources`, ADR 0123)
```
EnsureSourceAsync(CapSourceRequest{linkedType, linkedId, sourceTypeCode, titles, description, receivedOn, owner, unit, viewers, teamId},
                  transactionContext?) → CapSourceRef(id, number, created)        // idempotent by (linkedType, linkedId)
AddRemarkAsync(sourceId, CapRemarkRequest{externalKey, texts, typeCode, priority, criterion, departmentUnitId, sectionUnitId, owner?, dueOn?,
               linkedType?, linkedId?, proposedActions[]{details, type, assignee, plannedStart/End, budget…}}, transactionContext?)
               → Result<CapRemarkRef(id, number, created)>                         // idempotent by externalKey within the source
GetProgressAsync(linkedType, linkedId) → CapSourceProgress(remarks with live actions, counts, allActionsCompleted) | null
```
Any module calls it (as the system principal, channel of the caller); with `transactionContext` the CAP DbContext joins the caller's
transaction (ADR 0023) so the consumer's state and the remarks commit together. Proposed actions are created as `draft` actions on the
remark (pre-filled; the owner reviews and submits them). `allActionsCompleted` = at least one completed action, every remark final
(`completed`, `rejected`, `cancelled`) and every approved action completed — completion requires evidence, so *completed* means *completed
with evidence*. Events (outbox): `CapRemarkStatusChanged(remarkId, number, sourceId, sourceLinkedType/Id, remarkLinkedType/Id, externalKey,
status, stage)` on every status change, `CapActionCompleted(actionId, number, remarkIds)`.

**Live link back:** the source and the remark show their linked record through the Links registry (permission-aware badge, deep link);
CAP declares `cap.source`, `cap.remark` and `cap.action` as linkable (`task-able`; remarks also `remark-source`).

## 9. Integrations wired now
| From | Reaction |
|---|---|
| **KPI** `KpiTargetMissed` (ADR 0065) | `kpiMissMode`: `manual` → an open **suggestion** (PMO one-click *Raise remark* on the CAP suggestions inbox; also offered on the KPI result through the deep link); `automatic` → remark at once; both only when `ConsecutiveMissed ≥ kpiMissConsecutive`. Source = one per KPI card (type `kpi-performance`, linked `kpi.card`), remark per result (`linked kpi.result`, external key = result id, department = responsible unit, priority high when consecutive ≥ 2 else medium, text from the card name / period / achieved vs target). The remark page shows the card's year performance (`IKpiCatalog.GetPerformanceAsync`) — "remark shows the KPI trend" |
| **Risk** `RiskEventLogged` | `riskEventMode` likewise: source per risk (type `risk-event`, linked `risk.risk`), remark per event (external key = event id), then `IRiskDirectory.LinkCapRemarkAsync(eventId, remarkId)` — the risk's Events tab shows the remark as a **link** (replacing the Phase 13 chip) |
| **Risk** `RiskControlRatedIneffective` | `riskControlMode`: source per risk, remark per control rating (external key = control id + date), priority high |
| **Portfolio** | budget routing (draft request, ADR 0124); PMO links an action to an existing project (`IPortfolioDirectory.SearchProjectsAsync` picker, badge) |
| **Benchmarking** | through `ICapRemarkSources` (benchmarking.md §6) |
| **Teams** | optional `team_id` on a source (e.g. the workshop committee) shown as a badge (`ITeamsDirectory`) |
| **Tasks** | remarks and actions are `task-able`: the record pages show the Tasks panel (create / list linked tasks, Phase 6) |

Suggestions are idempotent per (kind, resource id); dismissing needs a reason; raising twice returns the existing remark.

## 10. Screens (AR + EN, RTL / LTR, shared components)
CAP area with sub-navigation: **Dashboard** (tiles; remarks by source type / department / status / priority; actions by status; overdue and
ageing buckets 0–30 / 31–60 / 61–90 / > 90 days; closure rate per unit; budget-dependent actions with their request badges; effectiveness
results — CSS / SVG bars as in Risk, ADR 0105 precedent) · **Sources** list → **source page** (header, linked record badge, team, documents,
remarks table, *Add remark*, AI *Extract remarks from a report*) · **Remarks** list with saved views (mine, to accept, my unit, overdue,
all) · **Remark page**: stage stepper Analysis › Corrective Action › Implementation › Verification › Closed, current-status banner with
actions (accept / reject dialog with reason, reassign, submit), tabs **Overview** (source record live badge, KPI trend when from KPI),
**Root cause** (5 Whys / fishbone / text editor + AI assistant), **Actions** (cards with progress, *New action* dialog with budget
questions, *Link existing action* search with AI duplicate check and suggestions), **Tasks**, **History** · **Validate Actions** (PMO,
per remark: actions side by side, AI quality flags, approve / reject / return with notes, link project, budget routing preview) · **Action
page** (progress, achievement history, evidence, extension requests, completion request, program request / project badge, linked remarks,
tasks, history) · **My actions** (quick monthly update with evidence upload) · **Suggestions** (from KPI / Risk, raise / dismiss) ·
**Reports** (action plan register by source, departmental plan, overdue — PDF / Excel, bilingual) · **Settings → CAP**. My Work card:
remarks to accept, actions to update (late / due this month), actions to validate (PMO), rejections to confirm.

## 11. AI (Phase 5 gateway, drafts only — ADR 0129)
`cap-extract` (report / text → remarks list: observation AR / EN, criterion, suggested department, priority — PMO confirms each into the
source) · `cap-root-cause` (5 Whys guide and likely causes for the remark; drafts the root-cause fields) · `cap-suggest` (actions for a
remark + **duplicate check without a model**: trigram similarity over open actions the user may see, offered as *link instead*) ·
`cap-quality` (deterministic flags — vague verbs, no measurable output, no assignee, end before start / in the past / later than the
remark due date, budget needed without cost — plus model wording advice; shown on Validate Actions) · `cap-summary` (monthly bilingual
leadership summary from the dashboard figures; chronic late units). Prompts `Ai/Prompts/cap-*.v1.md`; untrusted text delimited; demo
provider answers them.

## 12. MCP tools (ADR 0031: read on, write off, no approve tool)
`cap_my_remarks`, `cap_my_actions`, `cap_remarks_by_source`, `cap_overdue_actions` (read, on); `cap_update_achievement_draft` (write,
**off**: stores a draft achievement update the assignee confirms in the UI).

## 13. Jobs (Quartz, idempotent, stamped)
`cap.monitor` (hourly): late detection and escalation, remark overdue notice, monthly reminders on the configured day, verification due,
project-progress mirroring (when on).

## 14. API (`/api/v1/cap`)
Options / settings / dashboard; `sources` (list, create, get, update, close, remarks); `remarks` (list views, get, update, accept, reject,
confirm-rejection, send-back, reassign, root-cause, submit, cancel, verify, actions, link-action, unlink-action, validation view, kpi
trend); `actions` (list views, search, get, update, cancel, validate (PMO decision + project), link-project, progress, updates, confirm
draft update, request-completion, extensions, my-actions); `suggestions` (list, raise, dismiss); `reports/{kind}`; `ai/*`; record history
for sources, remarks, actions. Commands named by intent, idempotency keys on submit / validate / completion / extension.

## 15. Tests
Unit: remark and action lifecycles (every allowed and forbidden transition), `RemarkProgress` derivation table, due-date rule (calendar
and working days), quality flags, root-cause rule evaluation. Integration (real PostgreSQL): rejection final by default and PMO
confirmation when switched on; one action linked to two remarks (both complete when it completes); due dates by priority in working time
(weekend roll, unit business hours); budget-needing action without budget → draft Program Request with source `action-plan` linked back;
KPI target missed (one-click) and a risk event (automatic) each raise a remark linked to their source (and the event back-link); workshop
flow source → remarks → accept → actions → validation → monthly updates → extension → evidence → completion; scope rules (remark owner,
assignee, department manager, PM Office, outsider 404); MCP tools; authorization matrix entries for every endpoint.
