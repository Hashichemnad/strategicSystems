# Design note — Phase 4: Approval engine, Approval Settings centre, My Work

**Scope.** BUILD_PLAN Phase 4; SPEC §0.1 (delegation), §0.2 (configurable workflow & approval engine, global Approval Settings centre,
bulk role transfer re-routing, "Current Status" engine), §0.3 / §0.4 level 1 (My Work); Appendix A Q-P4 (conditional approvers);
CLAUDE.md §9, §11 (idempotency), §15 (shared expression service).

Also closes items left open by Phases 2–3:

| Open item | Phase 4 result |
|---|---|
| Delegation records (Phase 1) not consumed | The engine lets a delegate act for an assignee; the action and every audit row of that save carry **on behalf of** |
| `IRoleTransferHook` without implementation (Phase 2) | Pending approval tasks whose approvers came from a transferred role are re-routed to the new holders, in the transfer's transaction, with history and audit entries |
| Workflow notifications (Phase 3) | Bilingual templates `workflow.*` through `INotificationService` (outbox, e-mail + in-app) |
| Idempotency keys (CLAUDE.md §11) | `Idempotency-Key` header on submit / approve / reject / return endpoints (`.WithIdempotency()`) |

## 1. Projects

| Project | Contents |
|---|---|
| `Strategy.Platform.Kernel` → `Expressions/` | The shared **sandboxed expression service**: tokenizer, parser, static type check, evaluator. Pure code, no reflection (ADR 0022) |
| `Strategy.Platform.Workflow.Contracts` | What modules use: `ApprovalProcessDefinition` + `AddApprovalProcess`, `IApprovalEngine` (submit, cancel, read the open instance), `IApprovalHook`, `ICurrentStatusProvider`, `IMyWorkSource`, `ITeamRoleResolver` (placeholder for Teams), DTOs |
| `Strategy.Platform.Workflow` | `WorkflowDbContext` (schema `workflow`), definitions and versions, the engine, approver resolution, SLA job, role-transfer hook, idempotency middleware + store, Approval Settings API, My Work API, current-status API |
| `Strategy.Platform.Http` | `IdempotentEndpoint` metadata + `.WithIdempotency()` |
| `Strategy.Platform.Persistence` | `JoinTransactionAsync(DbContext, object)` — a second DbContext joins the caller's open transaction (ADR 0023) |
| Access / Organisation contracts | `IRoleHolderQuery` (who holds a role: users and units), `ISystemState.GetOwningUnitAsync`, `IEmployeeDirectory.GetEmployeeIdsInUnitsAsync`, `IDelegationQuery.GetActiveDelegatorsAsync`; `RoleTransfer` gains units and reason; `IRoleTransferHook` receives the transaction |

## 2. Data model (schema `workflow`)

| Table | Columns | Notes |
|---|---|---|
| `process_version` | `process_key`, `version_no`, `note`, `created_*` | One row per **saved** configuration of a process (ADR 0024). The highest number is the one new submissions use. Unique `(process_key, version_no)` |
| `process_step` | `version_id`, `sort`, `code`, `name_ar`, `name_en`, `rule` (jsonb: kind + parameters), `condition` (text, null = always), `actions` (text[]: `approve`, `reject`, `return`), `custom_actions` (jsonb: code, labels, outcome), `comment_on_reject`, `comment_on_return`, `comment_on_approve`, `sla_hours`, `escalation_rule` (jsonb, null = reminder only), `on_no_approver` (`block` / `skip`), `exclude_submitter`, `notification_template` | Copied with every new version; never edited in place |
| `approval_settings` | in `settings.setting` area `workflow.approvals`: level code of "section" and of "department" (defaults `section`, `department`) | Approver rules use them; nothing organisation-specific in code |
| `instance` | `process_key`, `version_id`, `resource_type`, `resource_id`, `subject_number`, `title_ar`, `title_en`, `link`, `subject_owner_employee_id`, `subject_owner_user_id`, `subject_unit_id`, `submitter_user_id`, `submitter_employee_id`, `variables` (jsonb snapshot), `status`, `current_sort`, `attempt`, `previous_instance_id`, `submitted_at`, `completed_at`, `comment` | Auditable, `xmin`. Partial unique index: one `in_progress` instance per (process, resource) |
| `task` | `instance_id`, `step_sort`, `step_code`, `step_name_ar/en`, `status` (`pending` / `completed` / `cancelled`), `opened_at`, `due_at`, `reminded_at`, `escalated_at`, `completed_at`, `outcome`, `acted_by_user_id`, `acted_by_name`, `on_behalf_of_user_id`, `on_behalf_of_name`, `comment` | Auditable, `xmin` (two approvers clicking at once → one wins, the other 409) |
| `task_assignee` | `task_id`, `user_id`, `employee_id`, `source` (`rule` / `escalation` / `transfer`), `active`, `added_at`, `removed_at`, `removed_reason` | Any active assignee (or their delegate) may act. Audited, parent = task |
| `history` | `instance_id`, `task_id`, `occurred_at`, `event`, `step_name_ar/en`, `actor_user_id`, `actor_name`, `on_behalf_of_user_id`, `on_behalf_of_name`, `comment`, `data` (jsonb) | Timeline of the approval panel. Events: `submitted`, `resubmitted`, `step-entered`, `step-skipped`, `approved`, `rejected`, `returned`, `custom` (with its code), `escalated`, `reminded`, `rerouted`, `cancelled`, `completed` |
| `idempotency_record` | `user_id`, `key`, `fingerprint` (SHA-256 of method + path + body), `status_code`, `content_type`, `body`, `completed`, `created_at` | Unique `(user_id, key)`; kept 24 h (clean-up job). Not business data |

Status codes are stable codes; labels in i18n. No hard deletes of instances / tasks / history.

### Instance lifecycle (code, unit-tested table)
| From \ event | step approved | last step approved | rejected | returned | cancelled |
|---|---|---|---|---|---|
| `in_progress` | stays (`current_sort` = next included step) | → `approved` | → `rejected` | → `returned` | → `cancelled` |
| `approved` / `rejected` / `returned` / `cancelled` | ✗ `workflow.instance.closed` | ✗ | ✗ | ✗ | ✗ |

**Resubmission** after Return = a new instance (`attempt + 1`, `previous_instance_id` = the returned one). Only allowed when no instance is
in progress for the same process and resource. A rejected instance may not be followed by a resubmission unless the module allows it
(Sample: rejected is final).

## 3. Process registration (module API)

```csharp
services.AddApprovalProcess(new ApprovalProcessDefinition(
    Key: "sample.item-approval", SystemCode: "sample", NameAr, NameEn,
    ResourceType: "sample.item", ReadPermission: "sample.item.read", ApproverRecordRole: "approver",
    LinkTemplate: "/sample/items/{id}",
    Variables: [ new("amount", ExpressionType.Number, "المبلغ", "Amount") ],
    DefaultSteps: [ ... ]));            // seeded as version 1 when the process has no version yet
services.AddApprovalHook<SampleApprovalHook>();          // IApprovalHook: ProcessKeys + OnEventAsync
services.AddCurrentStatusProvider<SampleCurrentStatus>(); // per resource type
services.AddMyWorkSource<SampleMyWork>();                // "needs my action" items
```

`IApprovalEngine` (used by module handlers):
- `SubmitAsync(ApprovalSubmission, object transactionContext)` — the module opens a transaction on its DbContext, changes its own state, calls
  the engine with that DbContext; the engine's context **joins the same transaction** (ADR 0023), so the record state, the instance, the first
  task, the notifications (outbox) and all audit rows commit together.
- `CancelAsync(instanceId, reason, transactionContext)`; `GetCurrentAsync(processKey, resourceId)`; `GetStatusAsync(...)` (helper for
  current-status providers: step names, approvers, the caller's actions).

**Hooks.** After every transition the engine calls the hooks registered for the process key **inside the engine's transaction**:
`StepEntered`, `Approved`, `Rejected`, `Returned`, `Cancelled`. The hook joins the transaction with its own DbContext. A failing hook
rolls back the whole action. (No outbox hop: the record state must change with the approval, and the UI reads it right after.)

## 4. Approver rules (resolved at runtime against Organisation and Access; snapshotted on the task)

| Kind | Parameters | Resolves to |
|---|---|---|
| `role` | `roleKey` (`<system>.<code>`) | Users holding the role today: direct user assignments + users whose employee sits in an assigned unit or below. "Everyone" assignments are never used for approvals |
| `sectionManager` | `of`: `record` (default) / `submitter` | Head of the nearest unit at the configured **section level** on the path of the record's (submitter's) unit, if any |
| `departmentDirector` | `of` | Head of the nearest unit at the configured **department level** on that path |
| `headOfUnit` | `unitId` (specific) or `of` (`record` / `submitter`: the unit itself) | Head of that unit |
| `owningUnit` | `systemCode` (default: the process's system) | Head of the system's owning unit (Settings → Systems) |
| `employee` | `employeeId` | That employee's user |
| `recordRole` | `roleCode` | Holders of that record role on the record (employees and users) |
| `teamRole` | `teamRoleCode` | Through `ITeamRoleResolver` (Teams module, Phase 7). Until then: nobody, with the note `workflow.preview.teamRoleUnavailable` |
| `submitterManager` | — | The submitter's current manager |

Employees are mapped to their active user accounts. **Exclude submitter** (default on) removes the submitter from the approvers.
**No approver** (empty after exclusion, inactive head, unit without head): `block` (default — the submission or the transition fails with
`workflow.errors.noApprover` naming the step; nothing is approved by nobody) or `skip` (history records `step-skipped` with reason
`noApprover`). ADR 0025.

**Built-in condition variables** (every process, computed at submission and snapshotted): `submitter`, `submitterManager`,
`sectionManager`, `departmentDirector`, `unitHead` (employee ids as text, or null), `unitLevel` (level code of the record's unit),
`submitterIsUnitHead` (bool). Plus the module's variables (Sample: `amount`).

**Q-P4 default** (seeded for the Sample process, editable): the Section Manager step has the condition
`sectionManager != null and sectionManager != departmentDirector and sectionManager != submitter`.

## 5. Expression sandbox (ADR 0022)
Language: numbers (decimal), text (`'…'` / `"…"`), `true` / `false` / `null`, dates via `date('2026-01-31')`; operators
`+ - * / %`, `== != < <= > >=`, `and or not` (also `&& || !`), `in [a, b]`, parentheses; functions `if`, `min`, `max`, `abs`, `round`,
`coalesce`, `isEmpty`, `contains`, `startsWith`, `lower`, `upper`, `len`, `today`, `daysBetween`. Variables are whitelisted per use (a
process's variables); unknown names, unknown functions, type errors and non-boolean conditions are refused **when the step is saved**
(errors with a position and a message key). Limits: 2 000 characters, 300 nodes, depth 40, 10 000 evaluation steps. Runtime failure of a
condition (e.g. division by zero) includes the step (the safe side: an extra approver, never a missing one) and records it in history.

## 6. Actions, delegation, SLA, transfer

- **Act** (`POST /workflow/tasks/{id}/act`, `{ action, comment, onBehalfOfUserId?, version }`): allowed actions of the step only; comments
  required per configuration (`workflow.errors.commentRequired`). Custom actions are labelled aliases of approve / reject / return.
- **Who may act:** an active assignee, or a **delegate** of an active assignee — the assignee's employee has an active delegation to the
  caller's employee today whose scope covers the process key or its system code. Then `on_behalf_of` = the assignee; the engine calls
  `RequestContext.SetOnBehalfOf`, so every audit row of the save carries it, and the delegator is notified.
- **Visibility (404 vs 403):** the instance is visible to assignees and their delegates, past actors, the submitter, and anyone who may read the
  subject record (process read permission against a `ResourceRef` built from the snapshot). Not visible → **404**; visible but not an
  assignee / delegate → **403**.
- **Approver rights on the record** (CLAUDE.md §7): when a task opens, the engine grants the process's approver **record role** to each
  assignee; it is revoked when the task closes.
- **SLA** (`workflow.sla`, Quartz, every 15 minutes, idempotent): a pending task past `due_at` (opened + `sla_hours`, calendar hours) gets
  one **overdue reminder** to its assignees; if the step has an **escalation rule**, its users are added as assignees (`source = escalation`)
  and notified, history `escalated`. `escalated_at` makes it run once.
- **Bulk role transfer:** the Access transfer calls `IRoleTransferHook` before its commit, passing its DbContext. The Workflow hook joins
  that transaction, finds pending tasks whose step rule is `role` with a transferred role key, re-resolves the holders (reading the
  uncommitted assignments on the same connection) and moves the assignees (old ended with reason, new added with `source = transfer`),
  approver record roles moved, history `rerouted` with the transfer reason, explicit audit event `workflow.task.rerouted`, new holders notified.

## 7. Idempotency (ADR 0026)
`.WithIdempotency()` on submit / act endpoints. With header `Idempotency-Key` (≤ 100 chars): first request stores an in-progress record
(unique per user + key), runs, then keeps a successful response (status, content type, body ≤ 64 KB). A repeat with the same fingerprint
replays it with `Idempotent-Replayed: true` and does not run the handler again; a different request with the same key → 422
`idempotency.keyReused`; a repeat while the first still runs → 409 `idempotency.inProgress`; a failed response is not kept, so a corrected
retry may run.
Without the header nothing changes (row versions still stop double actions).

## 8. Notifications (system `workflow`, editable in Settings → Notifications)
`workflow.taskAssigned` (approvers, button opens the record) · `workflow.taskOverdue` (assignees) · `workflow.taskEscalated` (escalation
target) · `workflow.approved` / `workflow.rejected` / `workflow.returned` (submitter, with comment and actor) · `workflow.actedOnBehalf`
(the delegator). A step may name another template for "task assigned". All through the outbox in the action's transaction.

## 9. Current status and My Work
- `ICurrentStatusProvider` per resource type → `{ messageKey, params, actorKey, actorNames, nextActions[] }`. `GET
  /workflow/status/{resourceType}/{id}` (provider checks access → 404). The Sample provider covers draft, pending (step + approvers + the
  caller's approval actions), returned, approved, rejected.
- `GET /workflow/my-work`: pending approval tasks (own and delegated, with "on behalf of", due / overdue), my submissions in progress,
  "needs my action" items from every `IMyWorkSource` of a system the user can open, counters. Notifications come from the existing API.

## 10. Access
| Permission | Use | Default role |
|---|---|---|
| `workflow.process.read` | Approval Settings centre, preview | `workflow.administrator`, `workflow.viewer` |
| `workflow.process.manage` | Save process steps, approval settings | `workflow.administrator` (requires MFA; one of the platform administrator roles) |
Acting on tasks, My Work, approval panel, current status: signed-in users; record-level checks in the handlers (§6).

## 11. API (`/api/v1/workflow`)
| Method | Route | Guard |
|---|---|---|
| GET | `/processes` | `workflow.process.read` |
| GET | `/processes/{key}` (definition, current steps, versions, rule options) | read |
| GET | `/processes/{key}/versions/{versionNo}` | read |
| PUT | `/processes/{key}` (`{ steps, note, baseVersionNo }` → new version; 409 when stale; 400 with step/field errors) | manage |
| POST | `/processes/{key}/validate-condition` | read |
| POST | `/processes/{key}/preview` (unsaved steps + sample submitter, unit, variables → per step: included / skipped / error, approvers, notes) | read |
| GET / PUT | `/settings` | read / manage |
| GET | `/my-work` | signed-in |
| GET | `/subjects/{resourceType}/{resourceId}` (approval panel: instances of all attempts, steps, history, my actions) | signed-in + visibility |
| POST | `/tasks/{taskId}/act` (idempotent) | signed-in + assignee / delegate |
| GET | `/status/{resourceType}/{resourceId}` | signed-in + provider |
Sample: `POST /api/v1/sample/items/{id}/submit` (`sample.item.submit`, idempotent).

## 12. Screens
- **Settings → Approvals** (Approval Settings centre): every registered process grouped by system (name, key, steps, version, last
  change); approval settings (section / department levels).
- **Process editor**: ordered step cards (move up / down, add, remove), each with names, approver rule editor (role / employee / unit
  pickers, record role select), condition editor with the variable list and live validation, actions and comment rules, SLA and escalation,
  no-approver behaviour; **live preview** side panel ("who would approve this sample record": submitter, unit, variable values → step by step
  result). Save with a note → new version; versions tab.
- **Approval panel** (shared component, record pages): status, stepper of steps (done / current / skipped / upcoming) with approvers and
  due date, action buttons for the current approver (comment dialog; "on behalf of" choice for delegates), history timeline across attempts.
- **Current-status banner** on record pages (`StatusBanner` fed by the status endpoint, next actions as buttons).
- **My Work** (home page `/`): counters, pending approvals (system, number, title, step, submitted by, waiting since, due chip, on behalf
  of, deep link), needs my action, my submissions, latest notifications. Cards appear only for systems the user can open.

## 13. Tests
Unit: expression tokenizer / parser / type check / evaluator / limits; instance state machine table; approver rule resolution with fakes.
Integration (PostgreSQL): every rule kind resolves; condition skips a step; return → resubmit links attempts; delegate approves on behalf
(task, history and audit rows carry both names); SLA escalation with a fake clock; non-assignee → 403, outsider → 404; every action audited;
bulk transfer re-routes pending tasks with history + audit; repeated idempotency key does not approve twice; definitions versioned and
in-flight instances keep their version; Approval Settings validation and preview; authorization matrix covers the new endpoints.
E2E (ar + en): admin edits a 3-step process with a conditional step and previews it; Ahmed submits, Mariam returns, Ahmed resubmits,
Mariam approves; My Work; screenshots in `docs/screenshots/phase-4/`.
