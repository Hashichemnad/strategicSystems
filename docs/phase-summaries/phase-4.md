# Phase 4 — Approval engine, Approval Settings centre, My Work: summary

**Status:** built and verified in the build session · awaiting owner acceptance
**Branch:** `claude/nice-ptolemy-nk8i2q`
**Planning:** the plan is the design note [`docs/design/workflow.md`](../design/workflow.md) (plus the Phase 4 section of
[`docs/design/sample.md`](../design/sample.md)), written before the code. This session ran without an interactive approval step, so the plan was
written first and then implemented as it stands; its open choices are recorded as ADRs 0022–0026.

## What was built

| Area | Result |
|---|---|
| Expression sandbox (`Strategy.Platform.Kernel/Expressions`) | The shared service for admin-written conditions and later formulas (CLAUDE.md §15, ADR 0022): tokenizer → parser → **static type check against whitelisted variables** → bounded evaluator. Decimals, text, booleans, dates, null; `and / or / not / in`, comparisons, arithmetic; 15 functions. Errors carry a code, a translatable message key and a **position**. Limits: 2 000 characters, 300 nodes, depth 40, 10 000 steps. No reflection, no member access |
| Approval engine (`Strategy.Platform.Workflow` + `.Contracts`, schema `workflow`) | **Definitions:** process keys registered in code by modules; steps stored as **versions** (every save = version N + 1, in-flight approvals keep their version, ADR 0024). **Step:** names AR/EN, approver rule, entry condition, allowed decisions + labelled custom decisions, comment rules, SLA hours + escalation rule, "nobody can approve" behaviour (block / skip), exclude submitter, notification template. **Instances / tasks / assignees / history**, resubmission linked to the returned attempt. **Hooks** run in the engine's transaction; the module's DbContext joins it, so record state, instance, audit rows and notifications commit together (ADR 0023) |
| Approver rules (ADR 0025) | Role holders (direct + unit assignments and below; "everyone" ignored), section manager, department director (nearest unit at the configured level, of the record's or the submitter's unit), head of a unit, head of the system's owning unit, a specific employee, a record role on the record, a team role (placeholder resolver until Phase 7), the submitter's manager. Resolved at runtime and snapshotted on the task |
| Built-in condition facts | `submitter`, `submitterManager`, `sectionManager`, `departmentDirector`, `unitHead`, `unitLevel`, `submitterIsUnitHead` + the module's variables (Sample: `amount`). **Q-P4** default: section-manager step only when the section has a manager distinct from the director and the submitter |
| Decisions | Approve / reject / return (+ custom), comments per configuration, **403 for someone who can see the record but is not an approver, 404 for someone who cannot see it**. Approvers get a temporary **approver record role** on the record while their task is open (CLAUDE.md §7) |
| Delegation (closes Phase 1) | A delegate of an assignee (delegation in force, scope covering the process or its system) may act; the task, the history, **every audit row of the save** (`on_behalf_of`) and the explicit event carry both people; the delegator is notified. A submitter can never decide on their own request, not even as a delegate |
| SLA (Quartz `workflow.sla`, every 15 min) | Overdue task → one reminder to its approvers; the escalation target is added as an approver and notified; history + audit event; runs once per task |
| Bulk role transfer (closes Phase 2) | `IRoleTransferHook` now receives the transfer's DbContext: pending tasks whose step rule is a transferred role are re-resolved inside the same transaction; old approvers removed (with reason), new ones added (`source = transfer`), approver rights moved, history `rerouted`, audit event `workflow.task.rerouted`, new approvers notified |
| Notifications (closes Phase 3) | Seven bilingual templates (`workflow.taskAssigned`, `taskOverdue`, `taskEscalated`, `approved`, `rejected`, `returned`, `actedOnBehalf`), editable in Settings → Notifications, sent through the outbox in the action's transaction, deep link to the record |
| Idempotency (CLAUDE.md §11, ADR 0026) | `.WithIdempotency()` on `POST …/submit` and `POST /workflow/tasks/{id}/act`: same key + same request → stored response replayed (`Idempotent-Replayed: true`), handler not run again; same key + other request → 422; concurrent → 409; failures are not remembered. Records kept 24 h (`workflow.idempotency-cleanup` job) |
| Current status + My Work | `ICurrentStatusProvider` per record type (`GET /workflow/status/{type}/{id}`): message key + bilingual params, who acts, the actions valid for the caller now. `GET /workflow/my-work`: pending approvals (own and delegated, overdue first), items needing my action from every `IMyWorkSource` of an enabled system, my requests, counters |
| Approval Settings centre API | List of every registered process (grouped by system), detail with variables, role / record-role / template options and functions, versions, **save with field-indexed validation**, **live condition check**, **"who would approve" preview** of unsaved steps for a sample submitter / unit / values, approval level settings |
| Sample module (worked example) | `amount`, `approval_status`, `POST /items/{id}/submit` (permission `sample.item.submit`, idempotent), process `sample.item-approval` (Q-P4 section manager → department director → item approver when `amount > 100000`), hook, current-status provider, My Work card (items returned for correction), approver record role |
| Web | **My Work** home page (counters, pending approvals with step / due / overdue / on-behalf chips and deep links, needs-my-action, my requests, latest notifications). **Settings → Approvals** (processes by system, level settings) and the **process editor** (ordered step cards with move / add / remove, rule editor with role / unit / employee / record-role pickers, condition editor with live validation and the variable list, decisions + custom buttons, SLA + escalation, versions tab, **live preview panel**). Shared **approval panel** (steps with states, approvers, due / overdue, decision buttons, decision dialog with comment rules and "act as" for delegates, history timeline, earlier submissions) and **current-status banner**; sample record page with stage stepper, banner, submit dialog and *Approvals* tab. AR + EN, RTL / LTR |

## Acceptance checks (BUILD_PLAN Phase 4)

| Check | Evidence |
|---|---|
| Admin creates a 3-step process with a conditional step in the UI; a demo record follows it, including return for correction and resubmission | **Integration** `ApprovalSettingsTests.Admin_builds_a_three_step_chain_with_a_conditional_step_and_a_record_follows_it`: line manager → director (condition `amount >= 1000`, custom "approve with conditions") → finance; a small amount skips step 2; a large one is returned at step 2, resubmitted (attempt 2) and approved through, the custom decision recorded. `ApprovalFlowTests.Return_for_correction_and_resubmission_link_the_attempts`. **E2E** (ar + en) `approvals.spec`: the admin adds a conditional step in the editor, sees an invalid condition explained and then accepted, previews who approves, saves a version; Ahmed submits, Mariam returns (comment required), Ahmed resubmits, Mariam approves |
| Delegated approver approves "on behalf of"; history shows both names | `ApprovalFlowTests.Delegate_acts_on_behalf_and_both_names_are_recorded`: My Work lists the task "for" the director; the delegate approves; the step and the history show actor + on-behalf-of; the audit entries of the task change, the item's own change and the `workflow.task.approve` event carry `on_behalf_of`; the director is notified. `A_submitter_cannot_approve_their_own_request_as_a_delegate`. Vitest: the decision dialog sends `onBehalfOfUserId` |
| Overdue step escalates per SLA (time-travel test with fake clock) | `SlaEscalationTests` with a travelling `IClock`: not due → nothing; +25 h → reminder + escalation target added and notified, history `reminded` / `escalated`, My Work flags it overdue, a second run does nothing, the escalation target approves, the audit event has channel `job` |
| My Work lists pending approvals across modules with deep links; current-status banner shows next actions | `ApprovalFlowTests.Approvers_are_notified_and_my_work_lists_the_pending_approval_with_a_deep_link`, `Approver_gets_read_rights…` (banner actions for the approver), `Return_for_correction…` (banner "resubmit" for the owner). **E2E**: My Work → deep link → banner decision buttons; screenshots `my-work`, `record-status-draft`, `record-approved` |

**Also asked for in this phase:**

| Request | Evidence |
|---|---|
| Each approver rule type resolves correctly | `ApproverRuleTests`: section manager / director (and "of submitter", no section, no head), head of unit (specific, of record), employee, submitter's manager, role (direct + unit and below, "everyone" ignored, unknown role), owning unit (set through Settings → Systems), record role on the record, team role placeholder, people without account |
| Steps skipped by condition | `Item_follows_the_chain…` (amount ≤ 100 000 skips the item approver), `Section_manager_step_is_skipped_when_q_p4_does_not_apply`, `Section_manager_submitting_skips_their_own_step`, preview test |
| An approver outside the chain cannot act (403) | `Someone_outside_the_chain_cannot_act`: reader → 403 `workflow.notAnApprover`, outsider → 404 (task, panel, status), submitter → 403 |
| Every action audited | `Every_action_is_audited`: explicit events `workflow.instance.submit / resubmit`, `workflow.task.return / approve` per instance with user, reason, channel; field-level instance / task changes; transfer and escalation events in their own tests |
| Bulk role transfer moves pending tasks | `RoleTransferReroutingTests` through `POST /access/transfers`: approvers swapped, history `rerouted` with the reason, audit event with the new approver, assignee rows ended, old holder 403, new holder notified and approves |
| Same idempotency key does not double-approve | `Repeating_a_decision_with_the_same_idempotency_key_does_not_approve_twice`: replayed submit and approve (same body, header), one `submitted` and one `approved` history row, other request with the key → 422, a failed request (409) not remembered |

## Quality gates run

| Gate | Result |
|---|---|
| `dotnet build Strategy.slnx -c Release` | 0 warnings, 0 errors |
| `dotnet format --verify-no-changes` | clean |
| `dotnet test` | **946 / 946** passed: unit 335 (54 new expression cases) · architecture 30 · integration 581 on real PostgreSQL (28 new workflow tests; the generated authorization matrix covers every new endpoint) |
| `tools/check-vulnerable.sh` · `tools/check-api-client.sh` | no High / Critical · OpenAPI document and TS client up to date |
| `npm run lint` · `format:check` · `typecheck` | clean |
| `npm test` (Vitest) | **75 / 75** (new: the shared approval panel in English and Arabic — steps, overdue, history, comment rule, decision on behalf with an idempotency key; every server message key has AR + EN text) |
| `npm run build` · `npm audit --audit-level=high` · `npm run lint:licenses` | OK · no High / Critical (the same 14 moderate dev-tool advisories as Phase 3) · 610 packages allowed |
| `npx playwright test` | **54 / 54** (27 scenarios × ar + en; 2 new Phase 4 scenarios) |
| `--migrate` on an empty database | schema `workflow` added; seeder creates version 1 of each registered process |

**No dependency was added** (the expression evaluator is in-house, ADR 0022); `THIRD_PARTY_LICENSES.md` is unchanged.

## Decisions (ADRs)
- [0022](../decisions/0022-expression-sandbox.md) In-house sandboxed expression service instead of NCalc (typed save-time validation, positions, decimals, hard limits).
- [0023](../decisions/0023-shared-transaction-across-contexts.md) One transaction across a module's DbContext and the engine (`JoinTransactionAsync`); hooks run in it, not through the outbox.
- [0024](../decisions/0024-approval-process-versions.md) Process keys in code, steps versioned in the database; no draft state; in-flight instances keep their version.
- [0025](../decisions/0025-approver-rules-and-no-approver.md) Rule semantics (levels from settings, role holders, owning unit, team placeholder), self-approval excluded by default, "nobody can approve" blocks by default, any one approver decides.
- [0026](../decisions/0026-idempotency-keys.md) Idempotency keys: table in `workflow`, successful responses replayed for 24 h.

## Deviations
- **From the SPEC:** none.
- **From the plan / prompt:**
  - Step escalation is **one level** (reminder + escalation target when overdue). Repeated reminders and multi-level escalation can be added as
    step options later.
  - "Any one of the approvers decides" per step; unanimous / quorum steps are not offered (ADR 0025).
  - SLA hours are calendar hours (no business calendar yet).
  - An approver still needs **system access** to open the record page (CLAUDE.md §7 effective access); the decision itself is checked on being
    the approver, so My Work and the approval endpoints work without it.
- **Contracts changed:** `RoleTransfer` (units, reason) and `IRoleTransferHook` (transaction context); `ISystemState.GetOwningUnitAsync`; new
  `IRoleHolderQuery`; `IRecordRoleService.Grant/RevokeInTransactionAsync`; `IEmployeeDirectory.GetEmployeeIdsInUnitsAsync`;
  `IDelegationQuery.GetDelegationsToAsync`.
- **Platform administrators** now also receive the `workflow.administrator` role when granted (first administrator, dev seed). Existing
  installations assign it in Settings → Assignments. The seeded *Sample editor* role gains `sample.item.submit` only on new installations
  (roles are seeded once).
- **Development seed:** Mariam is now head of the Strategy & Performance Department, so the demo chain has a department director.

## Fixes found along the way
- History rows of one action were ordered by random Guid v7 suffixes (several rows share a millisecond and EF inserts by key): each row
  now gets a strictly increasing timestamp within the unit of work, plus an identity `seq`.
- The integration suite ran out of PostgreSQL connections (default 100) with the five new parallel test classes: the Testcontainers
  database now starts with `max_connections=400` (test infrastructure only).
- The Phase 3 job-dashboard E2E counted 5 jobs; there are 7 with the two workflow jobs.
- A submitter delegated by their approver could have approved their own request: delegations no longer let a submitter act on their own
  request (engine and My Work), with a test.

## Known gaps (by design: later phases)
- Team roles resolve once the Teams module (Phase 7) registers an `ITeamRoleResolver`; the preview says so.
- Open tasks of the Task Management module (Phase 6) are not yet re-routed by bulk transfers (the hook is in place).
- My Work cards for tasks, KPIs, risks… arrive with their modules through `IMyWorkSource`.
- The preview resolves record roles against no record (a sample record has none); real submissions resolve them.

## How to demo
```bash
REGISTRY_PREFIX=mirror.gcr.io/ docker compose -f deploy/docker-compose.yml --profile dev up -d postgres gotenberg mailpit
dotnet run --project src/Host/Strategy.Host          # Development: migrations, seeders, demo seed on an empty database
cd web && npm ci && npm run dev                       # http://localhost:5173
```
1. **Mariam** (`mariam@strategy.local` / `Strategy#Demo2026`, MFA): Settings → **Approvals**. Open *Sample item approval*: three steps,
   the section-manager condition (Q-P4). Add a step, type `amount >` (the error explains), then `amount > 500000`; in the preview choose
   *Ahmed* and amount 25 000 → section manager skipped, Mariam approves, the other steps skipped. Save with a note; see **Versions**.
2. **Ahmed** (`ahmed@strategy.local`): Sample module → new item with amount 25 000 → the banner says *waiting for the owner to submit* →
   **Submit for approval**.
3. **Mariam**: **My Work** lists it; open it → the banner offers *Approve / Reject / Return*; **Return** with a comment.
4. **Ahmed**: My Work → *Needs your action* → the item → **Resubmit**.
5. **Mariam**: *Approvals* tab → **Approve**; the panel shows both submissions and the history; Ahmed's item shows *Approved*.
6. Delegation: Profile → Delegations (Mariam → another employee, scope `sample`); that employee sees Mariam's approvals "for Mariam" and
   decides on her behalf; the History tab and the Audit Log show both names.

## Cloud environment notes
- Docker is started manually (`dockerd`); images come through `mirror.gcr.io/`. Integration tests reuse one PostgreSQL with `STRATEGY_TEST_POSTGRES`.
- The sandbox Chromium differs from `@playwright/test` 1.56.1: `E2E_CHROMIUM_PATH=/opt/pw-browsers/chromium`.
- Screenshots (generated by the E2E suite) are in [`docs/screenshots/phase-4/`](../screenshots/phase-4/) as `ar-*` / `en-*` pairs. **New in
  Phase 4:** `approval-settings`, `approval-step-editor-preview`, `approval-versions`, `my-work`, `record-status-draft`, `approval-panel`,
  `record-approved`. **Earlier phases, retaken** with the new home page.
