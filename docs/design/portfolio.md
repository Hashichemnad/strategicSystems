# Design note — Portfolio Management (`Strategy.Modules.Portfolio`)

**Scope.** The whole Portfolio Management system (SPEC §1), built over three phases:

| Phase | Part | Status of this note |
|---|---|---|
| **8** | Program Requests up to the automatic creation of the Project / Initiative record (SPEC §1.1–§1.4 A, §1.5 W1, §1.6–§1.9 for requests, §1.15 A, §1.19, Appendix A Q-P4 / Q-P5 / Q-P6) | **Detailed below — the Phase 8 plan** |
| 9 | Project / Initiative lifecycle (charter, planning, monthly cycle, closure, impact, change control) | Outline (§12); detailed when Phase 9 starts |
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
