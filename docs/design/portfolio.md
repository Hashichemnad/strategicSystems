# Design note — Portfolio Management (`Strategy.Modules.Portfolio`)

**Scope.** The whole Portfolio Management system (SPEC §1), built over three phases:

| Phase | Part | Status of this note |
|---|---|---|
| **8** | Program Requests up to the automatic creation of the Project / Initiative record (SPEC §1.1–§1.4 A, §1.5 W1, §1.6–§1.9 for requests, §1.15 A, §1.19, Appendix A Q-P4 / Q-P5 / Q-P6) | **Detailed below — the Phase 8 plan** |
| **9** | Project / Initiative lifecycle (charter, planning, monthly cycle, closure, impact, change control) — SPEC §1.3, §1.4 B–D, §1.5 W2–W8, §1.6–§1.9, §1.16, §1.17, Q-P1/P2/P3/P9 | **Detailed in §15–§28 — the Phase 9 plan** (ADRs 0070–0076) |
| 10 | Agile / Hybrid, PM standards, Gantt, portfolio views | Outline (§12) |
| 11 | Wasel vendor portal | Outline (§12) |

Reuses Phase 1 (organisation, employees, delegation), Phase 2 (access: system access, roles with scopes, record roles), Phase 3 (audit,
files, notifications, numbering, lookups, settings), Phase 4 (approval engine, Approval Settings centre, current status, My Work, business
hours), Phase 5 (AI gateway, document-to-form extraction with OCR and the review UI, similar records, approver brief, Ask, tool registry /
MCP), Phase 6 (links registry: requests are **task-able**). Decisions: ADRs **0047** (request validation rules and when they apply),
**0048** (approval chain, step-to-status mapping and the approved budget), **0049** (project auto-creation and `PROJ-…` numbering),
**0050** (Proposal Assessment and dynamic choices for AI forms), **0051** (Administration Dept quotation hand-off).

This session runs without an interactive approval step (as Phases 5–7): the plan is committed first, then implemented as written; open
choices are the ADRs above.

## 1. Projects

| Project | Contents |
|---|---|
| `Strategy.Modules.Portfolio.Contracts` | `IPortfolioDirectory` (badge of a request / project the caller may read). No EF Core. Phase 9 adds the project contracts used by Teams / KPI / Risk |
| `Strategy.Modules.Portfolio` | `Domain/` (ProgramRequest, ProposalAssessment, Project; `RequestLifecycle` transition table; `RequestRules` validation; `ClassificationCalculator`, `PriorityCalculator`), `Application/`, `Infrastructure/` (`PortfolioDbContext`, schema **`portfolio`**, migrations, seeder), `Api/`, `Ai/` (features, prompts, record / search sources, AI form), `Mcp/` |
| Platform addition (additive) | `IAiFormChoiceSource` in AI.Contracts: a module supplies the choices of an AI-form field at run time (strategic objectives are a lookup, not code — ADR 0050) |

System code **`portfolio`**, `SystemKind.Module` (a business system: enabled in Settings → Systems, opened through system access).
Resource types `portfolio.request`, `portfolio.project`.

## 2. Configuration

### 2.1 Settings area `portfolio` (Settings → Portfolio, permission `portfolio.settings.manage`)
| Value | Default | Use |
|---|---|---|
| `thresholdAmount` | 50 000 (in the installation currency) | Classification (Q-P5), DG Office step condition (variable `threshold`), screening question 5 label. "PM Office maintains the threshold" (§1.2) |
| `priorityCriteria` | the four ADoF criteria, weight 1 each: `vision-alignment` (link to the 2030 vision and the department), `impact` (expected impact and added value), `complexity` (complexity level), `radical-change` (driving radical change) | Weighted 1–5 scoring (SPEC §1.12 "configurable prioritisation model"); code, AR / EN labels, weight > 0, active |
| `priorityBands` | `high` ≥ 0.80 · `medium` ≥ 0.60 · `low` ≥ 0 | Q-P6 bands (ratios); each band code, AR / EN label, minimum; must start at 0, strictly increasing minimums, unique codes |
| `startYearsAhead` | 3 | Start date within the current year or the next N years |
| `endYearsAhead` | 3 | End date at most N years after the current year |
| `budgetStepCode` | `budget-section` | The approval step whose approvers may set the **Approved Budget** (ADR 0048) |
| `projectScopeTemplateFileId` | none | "Download Project Scope template" (uploaded by PM Office, purpose `portfolio.template`) |
| `assessmentOnCreate` | true | When a request is created from documents, the Proposal Assessment is generated right after the user confirms (if AI is on) |

Validation is server-side (`portfolio.errors.*` / `validation.*` keys); the record snapshots what it computed (classification, score,
band) at each save, so a later settings change does not silently change submitted requests (they are recomputed on their next edit).

### 2.2 Lookups (Settings → Lookups; codes in logic, labels AR / EN)
| Type | Seed | Notes |
|---|---|---|
| `portfolio.strategic-objective` | **empty** (Q-P7) | Each organisation loads its own objectives (Excel import). The development seed adds three demo objectives. The KPI system (Phase 12) may later supply objectives; requests store the code |
| `portfolio.suggestion-source` | idea, benchmarking, reports, global-practices, questionnaire, meeting-outputs, leadership-guidance, **other** | `other` requires free text (ADR 0047) |
| `portfolio.program-type` | digital, non-digital | |
| `portfolio.delivery-option` | contracting, internal, outsourcing-employee, ppp, **other** | Main and alternative option; `other` requires text |

Fixed logic enums (C# constants, labels in translation files): program nature `new` / `continuous`; cost path `cost-estimation` /
`project-scope`; procurement status `waiting-quotations` / `quotations-uploaded`; classification `project` / `initiative`; statuses.

### 2.3 Numbering (Settings → Numbering)
| Key | Default pattern | Context |
|---|---|---|
| `portfolio.request` | `PR-{SEQ:4}` | SPEC "PR-XXXX" |
| `portfolio.project` | `PROJ-{UNIT}-{YYYY}-{SEQ:3}` | `{UNIT}` = code of the **owning unit of the Portfolio system** (Settings → Systems, e.g. `SPMO` gives `PROJ-SPMO-2026-001`); when no owning unit is set, the requesting unit's code. `{YYYY}` = year of the final approval (ADR 0049) |

### 2.4 Approval process `portfolio.request` (seeded version 1, editable in the Approval Settings centre)
Variables (snapshotted at submission): `requiresBudget` (bool), `estimatedBudget` (number), `threshold` (number, from settings),
`classification` (text), `evaluationScore` (number, 0–100), plus the built-in ones.

| # | Step code | Approver rule | Condition (Q-P4 / W1) | SLA |
|---|---|---|---|---|
| 1 | `section-manager` | Section manager of the record's unit | `sectionManager != null and sectionManager != departmentDirector and sectionManager != submitter` | 16 h |
| 2 | `department-director` | Department director of the record's unit | always | 16 h |
| 3 | `pm-office` | role `portfolio.pm-office` | always | 24 h |
| 4 | `dg-office` | role `portfolio.dg-office` | `requiresBudget and estimatedBudget > threshold` | 24 h |
| 5 | `budget-section` | role `portfolio.budget-section` | `requiresBudget` | 24 h |

Routes: **no budget** → 1? → 2 → 3 → approved; **budget ≤ threshold** → 1? → 2 → 3 → 5; **budget > threshold** → 1? → 2 → 3 → 4 → 5.
Every step: Approve / Reject / Return for correction, comment required on reject and return. Approver record role `approver`.

## 3. Data model (schema `portfolio`; business tables audited, `xmin`, `created_*` / `updated_*`, no hard deletes)

### 3.1 `request` — Program Request (`PR-XXXX`)
| Group | Columns |
|---|---|
| Identity / workflow | `number`, `status` (`draft` / `in-approval` / `approved` / `rejected` / `cancelled`), `approval_status` (last result: null / `approved` / `rejected` / `returned`), `approval_step_code`, `approval_step_name_ar/en` (step in progress), `submitted_at`, `last_action_at`, `cancel_reason`, `cancelled_at`, `requester_employee_id`, `requester_user_id`, `source` (`manual` / `document`), `project_id` (set on approval) |
| Core | `name_ar`, `name_en` (≤ 300; at least one at submission), `description` (≤ 5 000), `justifications` (≤ 5 000), `strategic_objective_code`, `suggestion_source_code`, `suggestion_source_other`, `program_type_code`, `requires_budget` (bool?), `estimated_budget`, `year1_cost`…`year4_cost` (`numeric(18,2)`), `expected_start_date`, `expected_end_date` (`date`), `unit_id` (requesting unit: defaults to the requester's unit), `section_unit_id` (section to implement) |
| Nature / scope | `program_nature` (`new` / `continuous`), `previous_program_name`, `scope_of_work` (≤ 5 000) |
| Screening (yes / no / unanswered) | `screen_core_work`, `screen_one_time`, `screen_time_bound`, `screen_unique_output`, `screen_min_cost`, `screen_operational_costs` |
| Classification | `classification` (computed), `classification_override` (`project` / `initiative` / null), `override_reason`, `override_by_user_id`, `override_at`; effective = override ?? computed |
| Priority | `priority_scores` (jsonb `{criterionCode: 1..5}`), `evaluation_score` (`numeric(9,4)` ratio), `priority_band` |
| Delivery | `main_option_code`, `main_option_other`, `main_advantages`, `main_disadvantages`, `alt_option_code`, `alt_option_other`, `alt_advantages`, `alt_disadvantages` |
| Cost sharing | `others_bear_costs`, `others_cover_implementation` (bool?), `shared_cost_amount`, `partner_name`, `partner_share` (ratio 0–1) |
| Operating costs | `annual_operation_cost`, `annual_licence_cost`, `annual_maintenance_cost`, `annual_other_cost`, `operating_costs_from` (date) |
| Financial return | `return_expected` (bool?), `return_value`, `return_year` (int), `reduction_expected` (bool?), `reduction_value`, `reduction_year` (int) |
| Internal practices | `practice_operation`, `practice_service`, `practice_project`, `practice_initiative` (≤ 2 000 each) |
| Procurement | `cost_path` (`cost-estimation` / `project-scope`), `cost_proposals_requested` (bool?), `procurement_status`, `cost_estimation_uploaded`, `project_scope_uploaded`, `quotations_handed_off_at`, `quotations_uploaded_at` |
| Budget decision | `approved_budget` (set at the budget step; defaults to the estimated budget on final approval) |

Computed on read: total expected time frame (months between start and end, inclusive), year costs total, the current-status message.
Files (owner type `portfolio.request`): purposes `portfolio.request-document` (attachments and the documents a smart request started
from), `portfolio.cost-estimation`, `portfolio.project-scope`, `portfolio.quotation` — PDF / Office / images, 20 MB, versioned.

### 3.2 `proposal_assessment` — versioned Proposal Assessment
`request_id`, `version_no` (unique per request), `content` (jsonb, schema in §8.2), `language`, `documents` (names of the documents read),
`usage_id` (AI usage log), `generated_by_user_id`, `generated_at`. Append-only: regenerate = version N + 1; every version stays readable.

### 3.3 `project` — Project / Initiative (record only in Phase 8)
`number` (`PROJ-…`), `classification` (`project` / `initiative`), `request_id`, `name_ar/en`, `description`, `scope_of_work`, `stage`
(`initiation` for a project, `planning` for an initiative — Q-P2), `status` (`active`), `unit_id` (department concerned = the request's
unit), `section_unit_id`, `strategic_objective_code`, `program_type_code`, `budget` (= approved budget), `year1..4_planned_cost`,
`planned_start`, `planned_end`, `owner_employee_id` (Project Owner = department director of the unit at approval, SPEC §1.9),
`requester_employee_id`, `requester_user_id`. Phase 9 adds the PM, charter, gates, milestones and the rest of §1.4 B.

## 4. Calculators (pure, deterministic, table-tested — CLAUDE.md §15)

**`ClassificationCalculator`** (Q-P5): `Project` when **all six** screening answers are Yes **and** `requires_budget` and
`estimated_budget > threshold`; otherwise `Initiative` (an unanswered question is not Yes). The PM Office may **override** with a reason
(draft or in approval); the effective classification is the override when present. Tests: every single "No", unanswered, budget = threshold
(→ Initiative), budget threshold + 0.01 (→ Project), no budget, override.

**`PriorityCalculator`** (Q-P6, §1.12): `score = Σ(sᵢ × wᵢ) / Σwᵢ / 5` over the **active** criteria (ratio; 1.0 = 100 %); band = the
highest band whose minimum ≤ score (rounded to 4 decimals). Missing any score → no score and no band (submission requires all). Defaults
(equal weights) give the SPEC formula "average ÷ 5 × 100". Tests: all 5 → 100 % High; 4,4,4,4 → 80 % High (boundary); 4,4,4,3 → 75 %
Medium; 3,3,3,3 → 60 % Medium (boundary); 3,3,3,2 → 55 % Low; 1,1,1,1 → 20 % Low; custom weights (2,1,1,1); scores outside 1–5 refused.

## 5. Validation rules (`RequestRules`, ADR 0047) — message keys AR / EN, field paths for the wizard

Every save (draft or not): lengths; amounts ≥ 0 with **at most 2 decimals**; `return_year` / `reduction_year` are **4-digit** numbers
(1000–9999); `partner_share` 0–1; scores 1–5 for known active criteria; lookup codes active; `other` requires its text (source, main and
alternative delivery option); end ≥ start when both are given.

At **submission** (and resubmission) in addition:

| Rule (SPEC §1.4 A) | Key |
|---|---|
| Name (AR or EN), description, justifications, strategic objective, source, program type, requires-budget answer, start, end, unit, nature, all six screening answers, all priority scores required | `validation.notEmpty` / `portfolio.errors.screeningRequired` / `portfolio.errors.priorityRequired` |
| **Year 1 + 2 + 3 + 4 costs = Estimated Budget exactly** (when budget is required; the estimated budget is then required and > 0) | `portfolio.errors.yearCostsMismatch` `{total, budget}` |
| **Expected start within the current year or the next three years** | `portfolio.errors.startOutOfRange` `{from, to}` |
| **Expected end ≥ start and ≤ 3 years from the current year** (end year ≤ current year + 3) | `portfolio.errors.endBeforeStart` / `portfolio.errors.endOutOfRange` `{to}` |
| Return / reduction years 4-digit; value and year required when "return expected" / "reduction expected" is Yes | `portfolio.errors.fourDigitYear` / `validation.notEmpty` |
| **Either Operation or Service must be filled** (link with internal practices) | `portfolio.errors.operationOrService` |
| **Previous Program Name required when nature = Continuous** | `portfolio.errors.previousProgramRequired` |
| **"Other" option / source requires free text** | `portfolio.errors.otherTextRequired` |
| Currency: 2 decimal places | `portfolio.errors.twoDecimals` |
| Cost path complete when budget is required: cost-estimation file uploaded, or (project-scope path) quotations uploaded | `portfolio.errors.costEstimationMissing` / `portfolio.errors.quotationsMissing` |

The current year comes from `IClock` in the installation time zone. The wizard shows the same rules inline (zod) and a live "Year 1–4
total vs budget" indicator; the server is the authority.

## 6. Request lifecycle (`RequestLifecycle`, unit-tested table)

| From \ action | edit | submit | approve (final) | reject | return | cancel | override class. | hand-off / quotations |
|---|---|---|---|---|---|---|---|---|
| `draft` | ✓ | → `in-approval` | ✗ | ✗ | ✗ | → `cancelled` | ✓ (PM Office) | ✓ |
| `in-approval` | ✗ | ✗ | → `approved` | → `rejected` | → `draft` (approval status `returned`) | → `cancelled` (approval cancelled) | ✓ (PM Office) | ✗ |
| `approved` / `rejected` / `cancelled` | ✗ | ✗ | ✗ | ✗ | ✗ | ✗ | ✗ | ✗ |

Stage stepper on the record page: Draft › Manager approval › PM Office › DG Office › Budget › Approved (steps not in the route greyed).
"Reject → Stop / Cancelled" (W1.6): the rejected request is final. Cancel needs a reason (`validation.notEmpty`), allowed to the requester
on their own **draft** and to the PM Office on any draft or request in approval (the approval instance is cancelled in the same
transaction). Approve / reject / return come only from the approval engine's hook.

## 7. Cost estimation, project scope and the Administration Dept hand-off (W1.3, ADR 0051)

1. When **Requires budget = Yes**, the wizard asks *Do you have a cost estimation file?* → `cost_path`.
2. **Cost estimation:** the requester uploads the file (purpose `portfolio.cost-estimation`) → `cost_estimation_uploaded`, then fills the
   multi-year budget.
3. **Project scope:** the requester downloads the template (Settings → Portfolio), uploads the filled scope (`portfolio.project-scope`) →
   *Send to Administration* → `procurement_status = waiting-quotations`; record role **`procurement`** granted to the current holders of
   role `portfolio.administration`; template `portfolio.scopeSubmitted` to them. Administration uploads quotations
   (`portfolio.quotation`, permission `portfolio.request.quotations` through the record role) → *Quotations uploaded* →
   `quotations-uploaded`, template `portfolio.quotationsUploaded` to the requester → requester fills the budget details and submits.
4. Both paths are optional when no budget is required. The current-status banner names who acts at every point (§9).

## 8. AI (Phase 5 gateway; drafts only — CLAUDE.md rule 6)

### 8.1 "Start from a document" smart request (§1.15 A)
- *New Program Request* offers **Fill manually** or **Start from a document**.
- The user drops one or more files (PDF, Word, PowerPoint, scans with OCR when on). Each file goes through the shared extraction
  (`POST /ai/forms/portfolio.request/extract`), form **`portfolio.request`** (permission `portfolio.request.create`) with fields: names,
  description, justifications, scope, strategic objective (**choices = active objective codes, with their labels in the hint, supplied at
  run time by `IAiFormChoiceSource`** — ADR 0050), program type, source, nature, previous program name, start / end, requires budget,
  estimated budget, Year 1–4, delivery options with advantages / disadvantages, cost sharing, operating costs, financial return, internal
  practices, the six screening answers (`yes` / `no`) and the priority scores (1–5).
- Proposals from several files are merged in the browser (highest confidence wins; ties → first file). The wizard opens with AI-filled
  fields **highlighted** ("AI draft", confidence, source quote with page / slide, "from scan"), **missing fields flagged** (a "Missing
  from the documents" list), each value accepted or edited; **Create draft stays disabled until every AI value is reviewed** (the Phase 5
  review rule). **Nothing is saved until the user confirms.**
- On confirm, the request is created (`source = document`) and the documents are attached to it (purpose `portfolio.request-document`),
  then — when `assessmentOnCreate` and the feature are on — the first Proposal Assessment is generated.

### 8.2 Proposal Assessment (versioned)
Feature **`portfolio-assessment`** (permission `portfolio.ai.assessment`, AI switch per feature). Input: the request's fields (through the
data controls / masking of the gateway), the text of its attached documents (`DocumentTextExtractor`, untrusted data blocks, total
≤ 60 000 characters), the configured strategic objectives and priority criteria (trusted values), and — computed **without the model** —
similar requests / projects within the caller's scope (trigram similarity, the Phase 5 similar-records sources). Output schema (validated):

```json
{ "summary": "…", "gaps": ["…"], "strengths": ["…"], "risks": ["…"],
  "alignment": { "objectives": [{ "code": "…", "strength": "strong|moderate|weak", "reason": "…" }], "summary": "…" },
  "suggestedClassification": { "value": "project|initiative", "reasoning": "…" },
  "suggestedPriority": { "scores": { "<criterion>": 1-5 }, "reasoning": "…" },
  "recommendations": ["…"] }
```
Unknown objective / criterion codes are dropped; the similar records list is added by the server. Saved as a new `proposal_assessment`
version; **visible to everyone who may read the request** (creator, approvers through their record role, PM Office, department viewers).
Shown on the request page tab *Proposal Assessment* with "AI-generated" label, version picker, *Regenerate* (requester while draft, PM Office
any time before a final decision). Suggestions are never applied automatically; the requester may copy the suggested scores into the form.

### 8.3 Other AI hooks
- **Approver brief** (shared): `IAiRecordSource` for `portfolio.request` (fields + latest assessment summary).
- **Similar records** / global search / Ask: `IRecordSearchSource` for requests and projects (scope-filtered in SQL).
- Demo provider (Development / E2E only): answers the assessment prompt deterministically.

## 9. Current status (§1.6), notifications (§1.7), My Work

`ICurrentStatusProvider` for `portfolio.request` (message keys `portfolio.currentStatus.*`, actor + next actions):

| State | Message | Who | Next actions |
|---|---|---|---|
| draft, budget required, cost path = cost-estimation, no file | Waiting user to upload cost estimation file | requester | upload |
| draft, project-scope path, no scope file | Waiting user to upload project scope | requester | download template, upload |
| draft, waiting quotations | Waiting Administration Dept to upload quotations | administration holders | upload quotations (them) |
| draft (otherwise) | Waiting user to submit for approval | requester | submit |
| draft, returned | Waiting user to submit after corrections | requester | resubmit |
| in approval | Waiting ⟨step⟩ approval (Manager / PM Office / DG Office / Budget) | step approvers | approve / reject / return (approver) |
| approved | Approved — project ⟨number⟩ created | — | open project |
| rejected | Rejected & cancelled | — | — |
| cancelled | Cancelled: ⟨reason⟩ | — | — |

`portfolio.project` provider (Phase 8): "Waiting user to take ownership (any employee in the section)" — Take Ownership arrives in Phase 9.

**Notifications** (system `portfolio`, AR / EN, editable): `portfolio.scopeSubmitted` (Administration: upload quotations),
`portfolio.quotationsUploaded` (requester), `portfolio.requestApproved` (requester: project ⟨number⟩ created), `portfolio.requestCancelled`
(requester, when someone else cancels). Approver assignment, approved / rejected / returned outcomes use the engine's `workflow.*` templates
(sent to approvers and the submitter).

**My Work** (`IMyWorkSource`, system `portfolio`): my returned requests (resubmit), my drafts waiting for my upload, requests waiting
quotations for Administration holders. Pending approvals already appear through the engine.

## 10. Access (SPEC §1.9; seeded, editable in Settings → Roles)

| Permission | Scopable | Meaning |
|---|---|---|
| `portfolio.request.read` | ✓ | View requests |
| `portfolio.request.create` | — | Create requests (and use the AI form) |
| `portfolio.request.edit` | ✓ | Edit drafts, upload files, hand off to Administration, submit, cancel own drafts |
| `portfolio.request.manage` | ✓ | PM Office: override classification, cancel any request, regenerate assessments, set the approved budget at the budget step only through the step |
| `portfolio.request.quotations` | ✓ | Upload quotations (Administration) |
| `portfolio.project.read` | ✓ | View projects / initiatives |
| `portfolio.settings.manage` | — | Settings → Portfolio |
| `portfolio.ai.assessment` | — | Proposal Assessment feature |

| Role | Grants |
|---|---|
| `requester` (everyone with system access) | request read **Own**, create, edit **Own**, project read **Own**, AI assessment |
| `department-viewer` | request read / project read **Unit and below** |
| `viewer` (Portfolio Viewer / leadership) | request read / project read **All** |
| `pm-office` | read **All**, create, edit **All**, manage **All**, project read **All**, AI assessment — approver role of step 3 |
| `administrator` (PMS Administrator) | pm-office grants + `portfolio.settings.manage`; requires MFA |
| `dg-office`, `budget-section` | request read **Own** (system access); approvers of steps 4 / 5 — the engine's `approver` record role gives the record |
| `administration` | request read **Own**; the `procurement` record role (granted at hand-off) gives read + quotations on that request |

Record roles on `portfolio.request`: `requester` (read, edit), `approver` (read — engine), `procurement` (read, quotations).
Record roles on `portfolio.project`: `owner` (read), `requester` (read). Scope map: own = requester employee / user, unit = `unit_id`.
Out-of-scope record → **404**; visible but not allowed → **403**.

## 11. API (`/api/v1/portfolio`) and screens

| Method | Route | Guard |
|---|---|---|
| GET | `/requests` (filters: status, unit, classification, priority band, text, mine; paging, sorting) | `request.read` (scoped in SQL) |
| POST | `/requests` | `request.create` |
| GET / PUT | `/requests/{id}` (PUT with `version`) | read / edit (draft only) |
| POST | `/requests/{id}/submit` (idempotent) | edit |
| POST | `/requests/{id}/cancel` `{reason, version}` | edit (own draft) or manage |
| POST | `/requests/{id}/classification` `{value, reason, version}` | manage |
| POST | `/requests/{id}/approved-budget` `{amount, version}` | current assignee of the budget step |
| GET | `/requests/{id}/files`; POST `/requests/{id}/files/{kind}` (`document`, `cost-estimation`, `project-scope`, `quotation`) | read / edit or quotations |
| POST | `/requests/{id}/hand-off` · `/requests/{id}/quotations-uploaded` | edit · quotations |
| GET | `/requests/{id}/assessments`; POST `/requests/{id}/assessments` (generate) | read · edit (draft) or manage |
| GET | `/projects`, `/projects/{id}` | `project.read` |
| GET / PUT | `/settings`; GET `/settings/template`; POST `/settings/template` | read for all signed-in (labels, criteria) · `settings.manage` |
| GET | `/lookups` (objectives, sources, program types, delivery options, criteria, bands, threshold) | signed-in with system access |

Generic: `/workflow/subjects/portfolio.request/{id}` (approval panel), `/workflow/status/...` (banner), `/files/{id}/content` (downloads,
owner access), `/links/...` (tasks linked to a request), `/ai/forms/portfolio.request/extract`, `/ai/brief/portfolio.request/{id}`.

**Screens (AR + EN, RTL / LTR, shared components):** *Program Requests* list (saved filters by status, unit, classification, priority, "my
requests", search); *New request* choice (manual / from a document → upload → review); **request wizard** (steps: Basics · Budget &
cost files · Screening & priority (live classification and score) · Delivery & partners · Operating costs & return · Practices & review),
save as draft at every step; **record page** (header with number / name / status and classification chips, stage stepper, current-status
banner with actions, tabs Overview · Proposal Assessment · Approvals (shared approval panel incl. approver brief) · Documents · Tasks ·
History); *Projects* list and a minimal project page (header, stepper Initiation › Planning › Execution › Closure › Impact, current status,
request link); **Settings → Portfolio** (threshold, criteria and weights, bands, date windows, budget step, project scope template).
Sidebar: "Portfolio" group (Requests, Projects) shown when the user can open the system; My Work card through the shared source.

## 12. Later phases (outline, detailed in their phases)
- **Phase 9 — project lifecycle:** Take Ownership, charter with minimums gate, LPO, team formation through Teams (`portfolio.project`
  team-able, `LinkedRecordClosed` on closure), planning items, milestones with weights, monthly cycle job, closure, Impact (switch), change
  requests with semi-automatic apply, initiative template (Q-P2), approval processes `portfolio.charter` / `.planning` / `.monthly-update`
  / `.closure` / `.impact` / `.change-request`, KPI and Risk contracts, project tasks via Tasks.
- **Phase 10:** methodologies, agile objects and boards, standards enhancements behind feature flags, in-house SVG Gantt, portfolio
  dashboards, remaining AI assists.
- **Phase 11:** Wasel vendor principals, `/api/wasel`, vendor-visible DTOs, plan proposals and update submissions, PM review with diff.

## 13. MCP tools (ADR 0031: read on, write off, no approve tool)
`portfolio_search_requests` (read; filters, scope-filtered), `portfolio_get_request` (read; fields, status, current step, latest
assessment summary; `not_found` outside the scope), `portfolio_my_requests` (read), `portfolio_create_draft_request` (**write, off by
default**; creates a **draft** only — never submits), `portfolio_get_project` (read).

## 14. Tests
- **Unit:** `ClassificationCalculatorTests`, `PriorityCalculatorTests` (tables above), `RequestRulesTests` (every rule of §5 incl.
  boundaries: Dec 31 of year + 3, Jan 1 of year + 4, previous year, 999 / 10000 years, 0.005 amounts, `other` without text, continuous
  without previous name, neither operation nor service), `RequestLifecycleTests` (every allowed and forbidden pair), settings validation.
- **Integration (PostgreSQL):** the three approval routes (no budget → approved after PM Office; ≤ threshold → Budget; > threshold → DG
  Office → Budget) with the section-manager step included / skipped; return for correction → resubmit → approve; reject is final; cancel
  with reason (own draft; PM Office cancels in approval → instance cancelled); final approval creates the Project / Initiative with the
  `PROJ-{UNIT}-{YYYY}-NNN` number, copied data, approved budget, owner, stage; year-cost mismatch blocks submission with the key; the
  Administration hand-off with the procurement record role and notifications; AI extraction of a case study fills drafts and **creates
  nothing**; Proposal Assessment versions (regenerate → v2, v1 kept) visible to an approver and the department viewer, 404 for outsiders;
  visibility: requester only own, department viewer their unit and below, PM Office all; MCP tools (scope, write tool off); authorization
  matrix covers every endpoint.
- **Vitest:** wizard year-total indicator and "AI value must be reviewed" rule; Portfolio message keys exist in AR and EN.
- **E2E (ar + en):** wizard → submit → approval chain → project created; start from a document with AI review; Proposal Assessment;
  approval routes; return for correction. Screenshots in `docs/screenshots/phase-8/`.

---

# Phase 9 — Project / Initiative lifecycle and change control (detailed plan)

Reuses everything above plus Phase 6 (Task Management: project and milestone tasks), Phase 7 (Teams: formation, members, evaluation on
`LinkedRecordClosed`), Phase 12 (KPI: `IKpiCatalog`, `IKpiPerformance`, `IKpiLinkedRecordSource`, the strategy hierarchy). Decisions:
**0070** (stage templates, gates, one approval at a time per project, high-level vs detailed plan), **0071** (monthly cycle, window, lock,
progress formulas), **0072** (team formation through Teams and project access from team membership; closure events), **0073** (change requests:
conditional approvers, one under review per project, semi-automatic apply), **0074** (project risks scoring interface and project KPIs),
**0075** (strategic objectives from the KPI strategy hierarchy), **0076** (Phase 9 AI and MCP scope). The session runs without an interactive
approval step (as earlier phases): this plan is committed first, then implemented as written.

## 15. Lifecycle overview

| Classification | Stage path (template defaults, Q-P2 / Q-P3) |
|---|---|
| Project | Initiation (Take Ownership → charter → charter approval → LPO → team formation) › Planning › Execution (monthly cycle) › Closure › Impact › Completed |
| Initiative | Planning (Take Ownership → planning → planning approval) › Execution (monthly cycle) › Completed (PM Office confirms at 100 %) |

**Stage templates** (Settings → Portfolio → `stageTemplates`, one per classification, ADR 0070): `charter`, `lpo`, `teamFormation`,
`closure`, `impact` (booleans). Defaults: project all on; initiative all off. Rules: `lpo` and `teamFormation` need `charter`; `impact` needs
`closure`. The **Impact switch** (Q-P3) is the project template's `impact` flag — off: closure approval → Completed. The stage path shown on the
stepper is derived from the template: `[initiation if charter] planning execution [closure if closure] [impact if impact] completed`. The
template is **snapshotted on the project** (`template` jsonb) when it is created/first read in Phase 9, so a later settings change does not move
running projects (PM Office can re-apply the current template — not in this phase).

Statuses (SPEC §1.3): `active`, `on-hold`, `cancelled`, `completed` (`draft` is unused for projects: they are born active).

## 16. Project state machine (`ProjectLifecycle`, unit-tested transition table)

Every project command is `(stage, sub-state, status) × action → guard → new state`. Sub-states: `ownership` (no PM), `charter-draft`,
`charter-approval`, `lpo` (waiting Administration), `team-formation` (waiting PM Office / Teams), `planning-draft`, `planning-approval`,
`execution` (monthly cycle; see §19), `completion` (initiative at 100 %, waiting PM Office), `closure-draft`, `closure-approval`,
`impact-draft`, `impact-approval`, `done`.

| From | Action (who) | Guard | To |
|---|---|---|---|
| any stage, no PM (status active) | `take-ownership` (employee of the section, ADR 0070) · `assign-pm` (PM Office) | user's unit is the section (or below it) | PM set, record role `pm`; project → `charter-draft` (charter on) / `planning-draft` |
| `charter-draft` | `edit-charter`, children CRUD (PM) | — | same |
| `charter-draft` | `submit-charter` (PM) | **charter gate** (§17) | `charter-approval` (process `portfolio.charter`) |
| `charter-approval` | approved / returned / rejected (engine) | — | `lpo` (if `lpo`) → else `team-formation` (if on) → else Stage **Planning** · returned → `charter-draft` · rejected → `charter-draft` with approval status `rejected` (PM may correct and resubmit; PM Office may cancel) |
| `lpo` | `lpo-uploaded` (record role `procurement`) | ≥ 1 LPO file | `team-formation` (if on) else Stage Planning; PM Office notified |
| `team-formation` | linked project team becomes **Active** in Teams (event, ADR 0072) | team linked to the project | Stage **Planning** (`planning-draft`); PM notified |
| `planning-draft` | planning items, milestones (PM) · `submit-planning` | **planning gate** (§17) | `planning-approval` (`portfolio.planning`) |
| `planning-approval` | approved / returned / rejected | — | Stage **Execution** (monthly cycle on) · back to `planning-draft` |
| `execution` | monthly cycle (§19) | — | when approved total achievement = 100 %: Stage **Closure** (`closure` on) or `completion` (initiative) |
| `completion` | `confirm-completion` (PM Office) | performance = 100 % | Stage / status **Completed** |
| `closure-draft` | closure form, lessons · `submit-closure` | summary + main deliverables, ≥ 1 lesson | `closure-approval` (`portfolio.closure`) |
| `closure-approval` | approved | — | events `ProjectClosureApproved` + `LinkedRecordClosed(closure-approved)`; Stage **Impact** (`impact` on) else **Completed** |
| `impact-draft` | impact form · `submit-impact` | cost performance + the three satisfactions | `impact-approval` (`portfolio.impact`) |
| `impact-approval` | approved | — | Stage / status **Completed** |
| active (not completed) | `hold` (PM Office, reason) | no approval in progress is cancelled: the open instance is cancelled | status `on-hold` (everything read-only; monthly cycle skipped) |
| on-hold | `resume` (PM Office) | — | status `active` (sub-state unchanged; re-submit any cancelled approval) |
| active / on-hold | `cancel` (PM Office, reason) or approved cancellation / merge change request | open approval cancelled | status `cancelled` (final), `LinkedRecordClosed(cancelled)` |
| completed / cancelled | anything | — | refused `portfolio.errors.projectClosed` (409 / 422) |

Only one approval is in progress per project (ADR 0070): every project process uses the project as the approval subject. The project stores
the **approval status** shown in the header (process key + step names AR / EN, last result), e.g. "Department Director – Charter".

## 17. Gates (server-side, all failures returned together with keys)

| Gate | Rules (keys `portfolio.errors.*`) |
|---|---|
| **Charter** (`charterGate`) | ≥ 1 active KPI (`gateKpi`), ≥ 1 active risk (`gateRisk`), ≥ 1 active stakeholder (`gateStakeholder`), ≥ 3 proposed team members (`gateMembers`), PM, scope, the high-level plan: ≥ 1 high-level milestone and **high-level weights = 100 %** (`gateHighLevelPlan`). The record shows "Charter mandatory done" = gate passes |
| **Planning** (`planningGate`) | ≥ 1 planning item of category *in scope* (`gateScope`); ≥ 1 detailed milestone; **detailed leaf weights total exactly 100 %** (`weightsNot100` `{total}`); every leaf has planned start / end within each other (end ≥ start); deliverable fields complete when "is deliverable" (`deliverableFields`) |
| **Closure** | summary of achievement, main deliverables (`validation.notEmpty`), ≥ 1 lesson learned (`gateLessons`) |
| **Impact** | cost performance and the three satisfactions (1–5) (`validation.notEmpty`) |
| Milestone save (always) | weight 0 < w ≤ 100 %; Σ leaf weights of the plan ≤ 100 % and Σ high-level weights ≤ 100 % (`weightsOver100`); header phases carry no own weight (sum of children); end ≥ start; one level of phases (phase → milestone) |

## 18. Approval processes (seeded version 1, editable in the Approval Settings centre; subject = the project)

Variables for every project process: `classification`, `budget`, plus the built-ins. Steps: Approve / Reject / Return; comment on reject
and return; approver record role `approver` (read). `SM` = section-manager step with the Q-P4 condition of §2.4.

| Key | Default chain | Notes |
|---|---|---|
| `portfolio.charter` | SM → Department Director → PM Office | W2.3 |
| `portfolio.planning` | SM → Department Director → PM Office | W3.3 (initiatives use it too) |
| `portfolio.monthly-update` | **Department Director → PM Office** (Q-P1) | variables `period`, `performance` |
| `portfolio.closure` | SM → Department Director → PM Office | W5.2 |
| `portfolio.impact` | PM Office | W6.2 |
| `portfolio.change-request` | Department Director → PM Office → **Finance Service Section** (`costAffected`) → **GM Office** (`changeType == 'cancellation' or changeType == 'merge' or costChange > gmThreshold`) | Q-P4; variables `changeType`, `costAffected`, `costChange`, `gmThreshold` (Settings → Portfolio, default 100 000) |

New roles: `portfolio.finance-section`, `portfolio.gm-office` (approver roles, request read Own).

## 19. Monthly cycle (W4, ADR 0071)

- **Record** `monthly_update` (`portfolio.monthly-update`): `project_id`, `period` (`YYYY-MM`), `status` (`open` · `submitted` · `returned` ·
  `approved`), `opened_at`, `due_at`, `opened_by` (`job` / user), `submitted_at`, `approved_at`, `reminded_at`, `overdue_notified_at`,
  `items` (jsonb snapshot of the submitted milestone updates), unique (project, period).
- **Window** (job `portfolio.monthly-cycle`, hourly, idempotent): for each **active** project in Execution, the period is the month of *today*
  in the installation time zone. The window **opens on the 1st at the start of the first working moment** of the project unit's business
  hours (a 1st that is a weekend / holiday rolls to the next working day; `IBusinessHoursCalculator`). One row per project and period;
  PM notified (`portfolio.monthlyOpened`). **Due** = end of working time `monthlyDueWorkingDays` (default 10) working days after opening;
  reminder `monthlyReminderWorkingDays` (default 3) working days before due (`portfolio.monthlyReminder`); overdue notice once
  (`portfolio.monthlyOverdue`). A previous period still open stays open (no second window until it is approved). PM Office may **open now**.
- **Milestone update** (PM while the period is `open` / `returned`): pending achieve (0 ≤ a ≤ weight, **≥ approved Max Achieved** — the approved
  achievement is locked), actual start (required with the first achievement > 0), actual end + **evidence file** (purpose
  `portfolio.milestone-evidence`) required at **100 %** (a = weight), **causality + corrective actions required when delayed** (today > planned
  end and a < weight). Open linked tasks at 100 % → warning (`portfolio.warnings.openTasks`), not a block (SPEC §1.16). Header phases aggregate.
- **Submit** (PM): at least one milestone updated or confirmation that nothing changed; starts `portfolio.monthly-update`; milestones
  → `pending-approval`. **Approved:** each pending value becomes approved (Max Achieved, actual dates; completed at 100 %), the update is
  `approved` with its snapshot, project progress recalculated, `monthly_approved_at` set; **100 % total → Closure / completion**.
  **Returned / rejected:** the period goes back to `returned`, milestone pending values stay for correction.

## 20. Calculators (pure, table-tested; ratios 1.0 = 100 %)

| Calculator | Rule |
|---|---|
| `MilestoneCalculator.CompletionRate` | achieve ÷ weight (0 when weight 0) |
| Header aggregation | phase weight = Σ child weights; phase achieve = Σ child achieve |
| `ProgressCalculator.Performance` | Σ approved achieve (Max Achieved) of the detailed **leaf** milestones (not cancelled), capped at 1 |
| `ProgressCalculator.PlannedToDate(d)` | Σ over leaves of weight × planned fraction at d: 0 before start, weight on / after end, else weight × (days elapsed + 1) ÷ (duration + 1) — linear (ADR 0071) |
| `ProgressCalculator.Status` (Q-P6) | **Delayed** when performance < planned-to-date − tolerance (`progressTolerance`, default 0.05 = 5 points) **or** any active leaf is past its planned end below 100 %; else **On Track** |
| `RiskScoring` (ADR 0074) | inherent = probability × impact (1–25); residual = inherent × (11 − control) ÷ 10 (control 1–10, none = inherent); bands Low 1–4 · Medium 5–9 · High 10–16 · Critical 17–25 |
| KPI goal status | `IKpiPerformance.Calculate("single", direction, target, …, current)` capped → `BandAsync`; existing KPI: the card's year performance from `IKpiCatalog` |

## 21. Data model additions (schema `portfolio`; audited, `xmin`, no hard deletes — children are deactivated)

| Table | Columns |
|---|---|
| `project` (+) | `pm_employee_id`, `ownership_taken_at`, `sub_state`, `template` (jsonb), `operational_themes` (jsonb codes, lookup `portfolio.operational-theme`), `structural_changes`, `resources_human/financial/technical/contract/other`, contracted / actual Year 1–4 costs, `contracted_initial_cost`, annual operation / maintenance / licence / other cost + `operating_costs_from`, `lpo_uploaded`, `team_id`, `team_formed_at`, `approval_process`, `approval_step_code`, `approval_step_name_ar/en`, `approval_result`, `performance`, `planned_progress`, `progress_status`, `summary_of_achievement`, `main_deliverables`, `cost_performance`, `community_satisfaction`, `employee_satisfaction`, `vendor_satisfaction` (1–5), `previous_operational_cost`, `impact_notes`, `update_opened_at`, `monthly_approved_at`, `cancel_reason`, `hold_reason`, `merged_into_project_id`, `completed_at` |
| `milestone` | `project_id`, `code` (`<Number>-1.1`), `parent_id` (phase), `is_header`, `high_level`, `title_ar/en`, `planned_start/end`, `weight`, `max_achieved`, `pending_achieve`, `actual_start/end` (approved) + `pending_actual_start/end`, `cost`, `status` (`not-started` · `active` · `completed` · `cancelled`), `update_state` (`waiting-update` · `draft` · `pending-approval` · `approved` · `returned`), `causality`, `corrective_actions`, `evidence_note`, `is_deliverable`, `main_deliverable`, `acceptance_criteria`, `quality_met`, `is_innovative`, `innovative_reason`, `sort` |
| `project_kpi` | `nature` (`new` / `existing`), `kpi_card_id`, `project_goal`, `name_ar/en`, `equation`, `direction`, `index_class_code`, `target`, `target_year`, `when_measured` (`during` / `after`), `current_result`, `created_stage`, `status` |
| `project_risk` | `title`, `risk_code` (lookup `portfolio.risk-category`), `probability`, `impact`, `risk_factor`, `control_evaluation`, `inherent_score`, `inherent_band`, `residual_score`, `residual_band`, `response` (`accept` / `avoid` / `mitigate` / `transfer`), `mitigation_actions`, `implementation_pct`, `implementation_start/end`, `owner_employee_id`, `rms_status`, `created_stage`, `status` |
| `stakeholder` | `name`, `classification_code` (lookup `portfolio.stakeholder-class`, incl. *contractor / supplier*), `interest_level`, `impact_level` (low/medium/high), `communication_method`, `communication_frequency`, `email`, `phone`, `status` |
| `team_member` (proposed, charter) | `employee_id`, `role_ar/en`, `responsibilities`, `status` |
| `planning_item` | `category` (`in-scope` · `out-of-scope` · `assumption` · `constraint` · `deliverable` · `added-value` · `resource-human` · `resource-technical` · `resource-financial` · `resource-contract` · `resource-other`), `description`, `created_stage`, `status` |
| `challenge` | `challenge`, `impact`, `achieved_pct`, `recommendation`, `report_date`, `milestone_id`, `status` |
| `lesson` | `stage`, `lesson`, `type_code` (lookup `portfolio.lesson-type`), `recommendation`, `status` |
| `meeting` | `meeting_at`, `title`, `notes` (sanitised rich text), `status` |
| `contract` | `name`, `number`, `supplier`, `issue_date`, `amount`, `status` (`active` / `closed` / `cancelled`) |
| `change_request` | `number` (`<Number>-CR-n`), `type` (`details` · `cancellation` · `postponement` · `ownership` · `merge`), `areas` (jsonb: `name`, `scope`, `dates`, `budget`, `description`), `reason`, `impact_of_non_response`, `proposed_name_ar/en`, `proposed_description`, `proposed_scope`, `proposed_start/end`, `proposed_budget`, `cost_affected`, `new_pm_employee_id`, `new_owner_employee_id`, `merge_into_project_id`, `status` (`draft` · `under-review` · `approved` · `rejected` · `cancelled` · `completed`), `submitted_at`, `decided_at`, `implemented_at`, `applied` (jsonb log of field / old / new), `cancel_reason` |
| `monthly_update` | §19 |

Files (owner `portfolio.project` unless noted): `portfolio.project-document` (scope / quotations / general documents), `portfolio.lpo`,
`portfolio.closure`, `portfolio.milestone-evidence` (owner `portfolio.milestone`).

## 22. Access (SPEC §1.9; seeded, editable)

| Permission | Scopable | Who (default) |
|---|---|---|
| `portfolio.project.read` | ✓ | as Phase 8 + record roles `pm`, `team-member`, `approver`, `procurement`; **employees of the section see a project that has no PM yet** (to take ownership) |
| `portfolio.project.edit` | ✓ | record role `pm`; PM Office / administrator **All** — charter, planning, milestones, monthly update, closure, impact, files |
| `portfolio.project.contribute` | ✓ | record roles `owner`, `pm`, `team-member`; PM Office — challenges, meetings / minutes, lessons |
| `portfolio.project.change` | ✓ | record roles `owner`, `pm`; PM Office — raise / edit / submit change requests |
| `portfolio.project.manage` | ✓ | PM Office / administrator — assign PM, hold / resume / cancel, open the monthly window now, confirm initiative completion |
| `portfolio.project.lpo` | ✓ | record role `procurement` (granted on charter approval to holders of `portfolio.administration`) |

Record roles on `portfolio.project`: `owner`, `pm`, `team-member` (synced from the linked team, ADR 0072), `requester`, `approver` (engine),
`procurement`. Out-of-scope → **404**; visible but not allowed → **403**.

## 23. Integrations

- **Teams (ADR 0072):** `portfolio.project` is declared linkable **task-able** and **team-able**; `portfolio.milestone` task-able. The project's
  Team tab is the shared `LinkedTeamsPanel` (PM Office: *Request a team* with the project linked → Teams formation approval → memo). Teams
  publishes **`TeamMembershipChanged`** (Teams.Contracts, additive) whenever it syncs member record roles of a formed team; Portfolio's handler
  (a) moves a project waiting in `team-formation` to Planning when a linked team is Active, stores `team_id`, notifies the PM; (b) syncs record
  role `team-member` on the project with the team's active members (project-team members get project access). Closure approval publishes
  `LinkedRecordClosed(portfolio.project, id, closure-approved)` → Teams opens the project-team evaluation, and `ProjectClosureApproved`
  (Portfolio.Contracts) for other subscribers.
- **Tasks:** project-level tasks and tasks under milestones are Task Management tasks linked to `portfolio.project` / `portfolio.milestone`
  (the shared `LinkedTasksPanel`; Full workflow template; creator approves — ADR 0035). The PM follows every linked task
  (`FollowLinkedPermission = portfolio.project.edit`). `ITaskDirectory.CountLinkedAsync` feeds the 100 % warning.
- **KPI (Phase 12 contracts):** Existing KPI picker = `IKpiCatalog.SearchAsync` (caller's scope); goal status from `IKpiPerformance`; Portfolio
  implements **`IKpiLinkedRecordSource`** (projects whose active KPIs link the card) so the card's *Linked records* tab lists them.
- **Strategic objectives (ADR 0075):** single source = the KPI strategy hierarchy. New Kpi.Contracts **`IKpiStrategyCatalog`**
  (`ListObjectivesAsync`, `EnsureObjectivesAsync`). Requests / projects keep storing the **code**; options, validation and AI choices read the
  KPI objectives. A Portfolio seeder copies every Phase 8 `portfolio.strategic-objective` lookup value missing from the hierarchy into it as an
  objective (same code, labels, sort, active flag) — no request or project data changes; the Portfolio lookup type is no longer registered.
- **Risk (Phase 13):** `IProjectRiskScoring` (default 5×5, §20) and **`IProjectRiskDirectory`** (Portfolio.Contracts: project risks with scores
  and bands, scope-filtered) for Risk's future *Project Risks* tab.

## 24. Change requests (W7, Q-P9, ADR 0073)

Draft (PM / owner: `portfolio.project.change`) → *Submit* → `under-review` (`portfolio.change-request`, subject = the project; one change
request under review per project) → `approved` / `rejected`; returned → `draft`. Cancel a draft or an approved-not-applied request with a
reason. **Apply changes** (PM, approved only): the dialog is pre-filled from the request (new PM / owner, dates, budget, name, scope); the PM
may adjust and **confirms** → the system applies each change and logs it (`applied`: field, old, new) in one transaction → status
`completed` ("Changes implemented", `implemented_at`). Nothing changes before that confirmation. Per type:

| Type | Apply |
|---|---|
| details | name AR / EN, description, scope, planned start / end, budget (as filled) |
| postponement | planned start / end (leaf milestones are not moved automatically — the PM re-plans; noted in the log) |
| ownership | new PM: revoke `pm` from the old, grant to the new; new owner: revoke / grant `owner` (record roles move) |
| cancellation | project status `cancelled` (reason = the request's reason) + `LinkedRecordClosed(cancelled)` |
| merge | project `cancelled` with `merged_into_project_id` (the target must be readable and active) |

## 25. Current status (§1.6), notifications, My Work

`ProjectCurrentStatus` messages (`portfolio.currentStatus.project.*`): take ownership (section employees) · fill minimums (PM, lists what is
missing) · submit charter (PM) · waiting ⟨step⟩ – ⟨process⟩ (approvers, approve / reject / return) · waiting Administration to upload the LPO ·
waiting PM Office to form the team · update planning details / add milestones to 100 % (PM) · monthly update open / returned (PM, due date) ·
waiting monthly approval · no window open (next opening) · confirm completion (PM Office) · submit closure · enter impact · on hold (reason) ·
cancelled (reason) · completed. Change request status on its own panel.

Templates (system `portfolio`, AR / EN): `portfolio.ownershipTaken`, `portfolio.charterApproved` (Administration: upload LPO),
`portfolio.lpoUploaded` (PM Office: form the team), `portfolio.stageChanged` (PM), `portfolio.monthlyOpened`, `portfolio.monthlyReminder`,
`portfolio.monthlyOverdue`, `portfolio.changeRequestSubmitted` / `portfolio.changeRequestDecided` (PM, owner), `portfolio.projectOnHold`,
`portfolio.projectCancelled`. Approver / outcome mails come from the engine's `workflow.*` templates.

My Work (Portfolio source, extended): **projects needing my action** (take ownership in my section, charter / planning / closure / impact to
submit, returned approvals, LPO to upload, team to form, initiative to confirm, approved change requests to apply) and **monthly updates
due** (open / returned periods of projects I manage, with the due date). A *Projects* card on My Work groups them (shared source, no new UI
plumbing).

## 26. API additions (`/api/v1/portfolio`)

`GET /projects/{id}` (+ PM, template, sub-state, gates, progress, approval status, permissions) · `POST /projects/{id}/{take-ownership |
assign-pm | submit-charter | lpo-uploaded | submit-planning | submit-closure | submit-impact | confirm-completion | hold | resume | cancel}` ·
`PUT /projects/{id}/{charter | closure | impact}` · children `GET|POST /projects/{id}/{kpis | risks | stakeholders | members | planning-items |
challenges | lessons | meetings | contracts}`, `PUT …/{itemId}`, `POST …/{itemId}/deactivate` · milestones `GET|POST /projects/{id}/milestones`,
`PUT /milestones/{mid}`, `POST /milestones/{mid}/cancel`, `POST /milestones/{mid}/progress`, `POST /milestones/{mid}/evidence` ·
`GET /projects/{id}/monthly`, `POST /projects/{id}/monthly/{open | submit}` · `GET /projects/{id}/files`, `POST /projects/{id}/files/{kind}` ·
`GET|POST /projects/{id}/change-requests`, `PUT /change-requests/{cid}`, `POST /change-requests/{cid}/{submit | cancel | apply}` ·
`GET /kpi-cards?search=` (Existing KPI picker) · history `GET /projects/{id}/history`. Submit / apply endpoints accept the idempotency key.

## 27. Screens (AR + EN, RTL / LTR, shared components)

**Project record page** `/portfolio/projects/$id`: header (number, name, classification / stage / status / progress chips, PM, owner), **stage
stepper** from the template, **current-status banner** with actions (take ownership, submit…, upload LPO, request team, open window, confirm,
hold / resume / cancel), progress strip (performance, planned to date, status), tabs **Overview** (charter details, budget & costs, edit
charter) · **KPIs** · **Risks** (scores and bands) · **Stakeholders** · **Team** (proposed members + linked teams panel) · **Scope** (in / out) ·
**Assumptions & constraints** · **Added values** · **Milestones** (phases tree, weights total, high-level plan, deliverable fields, monthly
update dialog with evidence / causality) · **Monthly updates** (current window, submit, history) · **Tasks** · **Challenges** · **Meetings** ·
**Lessons** · **Contracts** · **Change requests** (create, submit, apply dialog) · **Closure** · **Impact** · **Approvals** · **Documents** ·
**History**. Simplified tab set for initiatives. Projects list gains PM / progress / status filters. Settings → Portfolio gains stage templates,
monthly cycle values, progress tolerance, GM threshold.

## 28. Tests (Phase 9)

- **Unit:** `ProjectLifecycleTests` (transition table incl. forbidden pairs and hold / cancel), `ProgressCalculatorTests` (performance,
  linear planned-to-date at boundaries, tolerance 5 points, past-end milestone), `MilestoneRulesTests` (achieve ≤ weight, ≥ approved, 100 % needs
  evidence + actual end, delayed needs causality + corrective actions, weights), `GateTests` (charter minimums one by one, planning weights
  99.99 / 100 / 100.01), `RiskScoringTests` (bands at 4/5, 9/10, 16/17; residual), `StageTemplateTests` (paths, invalid combinations),
  `ChangeRequestRulesTests`.
- **Integration (PostgreSQL, fake clock):** full project Take Ownership → Completed with every approval and notification; gate blocks until
  minimums met; weights 100 %; monthly window opens on the 1st (fake clock, a Friday 1st rolls to Monday with the business hours), reminder,
  update with evidence / delay rules, approval locks achievement; closure → Impact → Completed and → Completed with Impact off; initiative
  template; change request applies only after PM confirms, ownership moves record roles; closure approval publishes the event that opens the
  team evaluation; team members get project access; strategic objectives read from the KPI catalogue (and the lookup migration); KPI linked
  records; authorization (outsider 404, team member 403 on edit, procurement only LPO); MCP tools.
- **E2E (ar + en):** take ownership, charter tabs and gate, team formation, planning items, milestones with weights, monthly update with evidence
  and delay, closure, impact, change request apply, record page with stage stepper — screenshots `docs/screenshots/phase-9/`.
