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
| SLA (Quartz `workflow.sla`, every 15 min) | Due time counted in **working hours** (default) of the default business hours, the record's unit or a specific record — or in calendar hours — through the shared calculator, snapshotted on the task with the business-hours name. Overdue task → one reminder to its approvers; the escalation target is added as an approver and notified; history + audit event; runs once per task |
| Business hours and holidays (`Strategy.Platform.Calendar` + `.Contracts`, schema `calendar`, ADR 0027 — added before the pull request at the owner's request, modelled on Salesforce) | **Business hours:** several records (names AR/EN, time zone, active, exactly one **default** that cannot be deactivated; no deletes); per weekday *closed*, *24 hours* or a start–end time. Seeded default: Monday–Friday 07:30–15:30, Saturday–Sunday closed, the installation's time zone (Asia/Dubai). **Units** may use their own record; child units inherit it, else the default. **Holidays:** names AR/EN, a date or range, all day or specific hours, one-time or every year on a fixed date (Hijri-based ones entered per year), for all records or chosen ones; two holidays sharing a record may not overlap (the message names the other holiday and the date); **Excel import per year** (template, check, report, all-or-nothing). **`IBusinessHoursCalculator`** (add working time, working time between two moments, is-working-time, resolve for a unit) with a pure, tested core, for every later deadline. Permissions `calendar.business-hours.read` / `.manage` (roles *Business hours administrator* — one of the platform administrator roles — and *viewer*) |
| Bulk role transfer (closes Phase 2) | `IRoleTransferHook` now receives the transfer's DbContext: pending tasks whose step rule is a transferred role are re-resolved inside the same transaction; old approvers removed (with reason), new ones added (`source = transfer`), approver rights moved, history `rerouted`, audit event `workflow.task.rerouted`, new approvers notified |
| Notifications (closes Phase 3) | Seven bilingual templates (`workflow.taskAssigned`, `taskOverdue`, `taskEscalated`, `approved`, `rejected`, `returned`, `actedOnBehalf`), editable in Settings → Notifications, sent through the outbox in the action's transaction, deep link to the record |
| Idempotency (CLAUDE.md §11, ADR 0026) | `.WithIdempotency()` on `POST …/submit` and `POST /workflow/tasks/{id}/act`: same key + same request → stored response replayed (`Idempotent-Replayed: true`), handler not run again; same key + other request → 422; concurrent → 409; failures are not remembered. Records kept 24 h (`workflow.idempotency-cleanup` job) |
| Current status + My Work | `ICurrentStatusProvider` per record type (`GET /workflow/status/{type}/{id}`): message key + bilingual params, who acts, the actions valid for the caller now. `GET /workflow/my-work`: pending approvals (own and delegated, overdue first), items needing my action from every `IMyWorkSource` of an enabled system, my requests, counters |
| Approval Settings centre API | List of every registered process (grouped by system), detail with variables, role / record-role / template options and functions, versions, **save with field-indexed validation**, **live condition check**, **"who would approve" preview** of unsaved steps for a sample submitter / unit / values, approval level settings |
| Sample module (worked example) | `amount`, `approval_status`, `POST /items/{id}/submit` (permission `sample.item.submit`, idempotent), process `sample.item-approval` (Q-P4 section manager → department director → item approver when `amount > 100000`), hook, current-status provider, My Work card (items returned for correction), approver record role |
| Web — business hours | **Settings → Business hours** (each record's week at a glance, editor with the weekly grid and time-zone list, make default / deactivate, units with their own hours, "try a deadline" calculator) and **Settings → Holidays** (year view, editor with dates, hours, yearly repeat and records, removal with confirmation, Excel import with row errors and report). Step editor: SLA in working / calendar hours and which business hours. Approval panel and My Work: "due ⟨date⟩ · ⟨business hours⟩" |
| Web | **My Work** home page (counters, pending approvals with step / due / overdue / on-behalf chips and deep links, needs-my-action, my requests, latest notifications). **Settings → Approvals** (processes by system, level settings) and the **process editor** (ordered step cards with move / add / remove, rule editor with role / unit / employee / record-role pickers, condition editor with live validation and the variable list, decisions + custom buttons, SLA + escalation, versions tab, **live preview panel**). Shared **approval panel** (steps with states, approvers, due / overdue, decision buttons, decision dialog with comment rules and "act as" for delegates, history timeline, earlier submissions) and **current-status banner**; sample record page with stage stepper, banner, submit dialog and *Approvals* tab. AR + EN, RTL / LTR |

## Acceptance checks (BUILD_PLAN Phase 4)

| Check | Evidence |
|---|---|
| Admin creates a 3-step process with a conditional step in the UI; a demo record follows it, including return for correction and resubmission | **Integration** `ApprovalSettingsTests.Admin_builds_a_three_step_chain_with_a_conditional_step_and_a_record_follows_it`: line manager → director (condition `amount >= 1000`, custom "approve with conditions") → finance; a small amount skips step 2; a large one is returned at step 2, resubmitted (attempt 2) and approved through, the custom decision recorded. `ApprovalFlowTests.Return_for_correction_and_resubmission_link_the_attempts`. **E2E** (ar + en) `approvals.spec`: the admin adds a conditional step in the editor, sees an invalid condition explained and then accepted, previews who approves, saves a version; Ahmed submits, Mariam returns (comment required), Ahmed resubmits, Mariam approves |
| Delegated approver approves "on behalf of"; history shows both names | `ApprovalFlowTests.Delegate_acts_on_behalf_and_both_names_are_recorded`: My Work lists the task "for" the director; the delegate approves; the step and the history show actor + on-behalf-of; the audit entries of the task change, the item's own change and the `workflow.task.approve` event carry `on_behalf_of`; the director is notified. `A_submitter_cannot_approve_their_own_request_as_a_delegate`. Vitest: the decision dialog sends `onBehalfOfUserId` |
| Overdue step escalates per SLA (time-travel test with fake clock) | `WorkingHoursSlaTests` with a frozen `IClock`: Friday 13:00 + 4 working hours → Monday 09:00 (Dubai); Sunday night not due, Monday 09:01 escalates. `SlaEscalationTests` (calendar hours) with a travelling `IClock`: not due → nothing; +25 h → reminder + escalation target added and notified, history `reminded` / `escalated`, My Work flags it overdue, a second run does nothing, the escalation target approves, the audit event has channel `job` |
| My Work lists pending approvals across modules with deep links; current-status banner shows next actions | `ApprovalFlowTests.Approvers_are_notified_and_my_work_lists_the_pending_approval_with_a_deep_link`, `Approver_gets_read_rights…` (banner actions for the approver), `Return_for_correction…` (banner "resubmit" for the owner). **E2E**: My Work → deep link → banner decision buttons; screenshots `my-work`, `record-status-draft`, `record-approved` |

**Also asked for in this phase:**

| Request | Evidence |
|---|---|
| Each approver rule type resolves correctly | `ApproverRuleTests`: section manager / director (and "of submitter", no section, no head), head of unit (specific, of record), employee, submitter's manager, role (direct + unit and below, "everyone" ignored, unknown role), owning unit (set through Settings → Systems), record role on the record, team role placeholder, people without account |
| Steps skipped by condition | `Item_follows_the_chain…` (amount ≤ 100 000 skips the item approver), `Section_manager_step_is_skipped_when_q_p4_does_not_apply`, `Section_manager_submitting_skips_their_own_step`, preview test |
| An approver outside the chain cannot act (403) | `Someone_outside_the_chain_cannot_act`: reader → 403 `workflow.notAnApprover`, outsider → 404 (task, panel, status), submitter → 403 |
| Every action audited | `Every_action_is_audited`: explicit events `workflow.instance.submit / resubmit`, `workflow.task.return / approve` per instance with user, reason, channel; field-level instance / task changes; transfer and escalation events in their own tests |
| Bulk role transfer moves pending tasks | `RoleTransferReroutingTests` through `POST /access/transfers`: approvers swapped, history `rerouted` with the reason, audit event with the new approver, assignee rows ended, old holder 403, new holder notified and approves |
| Business hours (owner request) | **Unit** `WorkingCalendarTests` (15): within a day, end exactly at closing, before / after hours, **across the weekend**, **all-day and partial holidays**, yearly holidays and 29 February, **a time zone other than UTC** (Dubai vs UTC inputs and records), daylight-saving change (London), **24-hour record** with and without a holiday, no working time at all, the holiday overlap table. **Integration** `WorkingHoursSlaTests` (frozen clock): weekend; **holiday** moves the due date while **a holiday of another record does not**; **a unit's own 24-hour record applies to a team two levels below it**, while a default-source step ignores it; **calendar hours**; **a specific record in UTC**; the business-hours name on the panel and in My Work. `CalendarSettingsTests`: seeded default, validation messages, exactly one default that cannot be deactivated, calculator endpoint, unit inheritance and "inherit" again, holidays for one record vs all with the overlap message, yearly holidays shown in later years, Excel import all-or-nothing with row errors, permissions |
| Same idempotency key does not double-approve | `Repeating_a_decision_with_the_same_idempotency_key_does_not_approve_twice`: replayed submit and approve (same body, header), one `submitted` and one `approved` history row, other request with the key → 422, a failed request (409) not remembered |

## Quality gates run

| Gate | Result |
|---|---|
| `dotnet build Strategy.slnx -c Release` | 0 warnings, 0 errors |
| `dotnet format --verify-no-changes` | clean |
| `dotnet test` | **1 028 / 1 028** passed: unit 350 (54 expression cases, 15 working-time cases) · architecture 30 · integration 648 on real PostgreSQL (28 workflow tests, 13 business-hours tests; the generated authorization matrix covers every new endpoint). In the full parallel run the Gotenberg PDF snapshot test failed once at container start-up; it passes when re-run, before and after this change |
| `tools/check-vulnerable.sh` · `tools/check-api-client.sh` | no High / Critical · OpenAPI document and TS client up to date |
| `npm run lint` · `format:check` · `typecheck` | clean |
| `npm test` (Vitest) | **75 / 75** (new: the shared approval panel in English and Arabic — steps, overdue, history, comment rule, decision on behalf with an idempotency key; every server message key has AR + EN text) |
| `npm run build` · `npm audit --audit-level=high` · `npm run lint:licenses` | OK · no High / Critical (the same 14 moderate dev-tool advisories as Phase 3) · 610 packages allowed |
| `npx playwright test` | **58 / 58** (29 scenarios × ar + en; 4 new Phase 4 scenarios: Approval Settings centre with preview, the approval flow, Settings → Business hours, Settings → Holidays). Two locators in the new specs were made stricter after the full run; those 8 tests were then re-run and pass |
| `--migrate` on an empty database | schema `workflow` added; seeder creates version 1 of each registered process |

**No dependency was added** (the expression evaluator is in-house, ADR 0022); `THIRD_PARTY_LICENSES.md` is unchanged.

## Decisions (ADRs)
- [0022](../decisions/0022-expression-sandbox.md) In-house sandboxed expression service instead of NCalc (typed save-time validation, positions, decimals, hard limits).
- [0023](../decisions/0023-shared-transaction-across-contexts.md) One transaction across a module's DbContext and the engine (`JoinTransactionAsync`); hooks run in it, not through the outbox.
- [0024](../decisions/0024-approval-process-versions.md) Process keys in code, steps versioned in the database; no draft state; in-flight instances keep their version.
- [0025](../decisions/0025-approver-rules-and-no-approver.md) Rule semantics (levels from settings, role holders, owning unit, team placeholder), self-approval excluded by default, "nobody can approve" blocks by default, any one approver decides.
- [0026](../decisions/0026-idempotency-keys.md) Idempotency keys: table in `workflow`, successful responses replayed for 24 h.
- [0027](../decisions/0027-business-hours-and-holidays.md) Business hours, holidays and working-time SLAs: new platform component `Calendar` (schema
  `calendar`), exactly one default, one interval per weekday, the holiday overlap rule, unit inheritance, steps choose working / calendar hours
  and which business hours, due times snapshotted; Sample defaults become 16 / 16 / 40 working hours.

## Deviations
- **From the SPEC:** none.
- **From the plan / prompt:**
  - Step escalation is **one level** (reminder + escalation target when overdue). Repeated reminders and multi-level escalation can be added as
    step options later.
  - "Any one of the approvers decides" per step; unanimous / quorum steps are not offered (ADR 0025).
  - Business hours allow **one interval per day** and no overnight shifts (a "24 hours" day covers round-the-clock work); a holiday's
    hours apply to every day of its range (ADR 0027).
  - An approver still needs **system access** to open the record page (CLAUDE.md §7 effective access); the decision itself is checked on being
    the approver, so My Work and the approval endpoints work without it.
- **Contracts changed:** `RoleTransfer` (units, reason) and `IRoleTransferHook` (transaction context); `ISystemState.GetOwningUnitAsync`; new
  `IRoleHolderQuery`; `IRecordRoleService.Grant/RevokeInTransactionAsync`; `IEmployeeDirectory.GetEmployeeIdsInUnitsAsync`;
  `IDelegationQuery.GetDelegationsToAsync`.
- **Platform administrators** now also receive the `workflow.administrator` and `calendar.administrator` roles when granted (first administrator, dev seed). Existing
  installations assign it in Settings → Assignments. The seeded *Sample editor* role gains `sample.item.submit` only on new installations
  (roles are seeded once).
- **CLAUDE.md:** the platform project list and schemas include `Calendar` / `calendar`.
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
6. **Business hours:** Settings → **Business hours** shows *Standard business hours* (Mon–Fri 07:30–15:30, Asia/Dubai). Create a 24-hour record,
   assign it to a department under *Units with their own business hours*, try a deadline in the calculator. Settings → **Holidays**: add a
   one-day holiday next Monday for all records; import a year from the Excel template. In Settings → Approvals the steps count their SLA in
   working hours of the default record; a new submission's due date (approval panel, My Work) now skips the weekend and the holiday and names
   the business hours.
7. Delegation: Profile → Delegations (Mariam → another employee, scope `sample`); that employee sees Mariam's approvals "for Mariam" and
   decides on her behalf; the History tab and the Audit Log show both names.

## Cloud environment notes
- Docker is started manually (`dockerd`); images come through `mirror.gcr.io/`. Integration tests reuse one PostgreSQL with `STRATEGY_TEST_POSTGRES`.
- The sandbox Chromium differs from `@playwright/test` 1.56.1: `E2E_CHROMIUM_PATH=/opt/pw-browsers/chromium`.
- Screenshots (generated by the E2E suite) are in [`docs/screenshots/phase-4/`](../screenshots/phase-4/) as `ar-*` / `en-*` pairs. **New in
  Phase 4:** `approval-settings`, `approval-step-editor-preview`, `approval-versions`, `my-work`, `record-status-draft`, `approval-panel`,
  `record-approved`, `settings-business-hours`, `business-hours-editor`, `settings-holidays`, `holiday-editor`. **Earlier phases, retaken** with the new home page.
