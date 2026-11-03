# Phase 6 — Links registry and Task Management: summary

**Status:** built and verified in the build session · awaiting owner acceptance
**Branch:** `claude/amazing-hawking-29lz6v`
**Planning:** the plan is the design notes [`docs/design/links.md`](../design/links.md) and [`docs/design/tasks.md`](../design/tasks.md),
written and committed before the code (commit `docs(tasks): Phase 6 design notes…`). This session ran without an interactive approval
step, so the plan was written first and then implemented as it stands; its open choices are ADRs 0034–0039.

## What was built

| Area | Result |
|---|---|
| **Links registry** (`Strategy.Platform.Links` + `.Contracts`, schema `links`, ADR 0034) | A module declares a linkable type in code (`LinkableTypeDefinition`: type, system, AR / EN name, capabilities `task-able` / `team-able` / `remark-source`, read permission, deep link, optional **`FollowLinkedPermission`**) and implements `ILinkTargetSource` (batched badges — number, title, live status label and tone, closed flag — **only for records the user may read**). `ILinkRegistry` resolves references into `visible` / `restricted` (no title, no link) / `unavailable` (system switched off); `CanLinkAsync` (capability + system on + readable record) and `CanFollowLinkedAsync` (SPEC §11.9 "linked-record roles": the permission **on that record**). Link rows in `links.record_link` (audited, soft removal, written in the owner's transaction). API `/api/v1/links/types`, `/resolve`, `/to/{type}/{id}` (from-side resolved, restricted only counted). Web `LinkBadge` |
| **Sample proves it** | Sample items are `task-able`; their editors follow every linked task (`sample.item.update`). The item page has a **Tasks** tab (`LinkedTasksPanel`) that lists its tasks and creates tasks with the item linked |
| **Task Management** (`Strategy.Modules.Tasks` + `.Contracts`, schema `tasks`) | `task` (`TASK-NNNNN`), `assignment_group`, `timesheet_period` (`TS-NNNNNNN`), `comment` (@mentions, attachments), `follow_up`, `checklist_item`, `dependency`, `task_event` (timeline). Lookups `tasks.type` / `tasks.source` / `tasks.category` (seeded, editable). Settings → Tasks. Always-on core system (ADR 0036) |
| State machines (ADR 0036) | `TaskWorkflow` transition tables for **Full** (draft → assigned → accept / reject → in progress ⇄ on hold → submitted → approve / return / reassign; cancel from any open status) and **Simple** (to do → in progress ⇄ paused → done). Template per task type from Settings, snapshotted on the task. Status codes fixed, names per template in translations |
| Actors and access (§11.9) | Record roles `creator`, `assignee`, `follow_up`, `follow_up_comment`, `entered_by`, granted / revoked only by the module. Every action needs the **permission and the actor of the table**, so an administrator cannot approve (ADR 0035, creator approval is a task-level decision). Default roles *Employee* (everyone: own tasks, create, AI drafts), *Manager* (everyone: team = direct reports, dashboard, performance), *Secretary* (unit: follow-ups, create on behalf), *HR*, *Administrator* |
| Multi-assignment (§11.4) | N assignees (or a unit) → N independent tasks + 1 group (rule *all* / *any*). Group page: "x of n approved", **message all**, **extend due date for all**, **cancel all** (one transaction each) |
| Timesheet | Periods open on accept / start / return and close on pause (hold reason), submit, complete, cancel, reassign; actual effort, time to accept, time to complete, on-time flag |
| Sub-tasks, dependencies, recurrence (ADR 0038) | Sub-tasks by the parent's assignee only while In Progress / On Hold; a parent cannot be approved / done with open sub-tasks. Finish-to-start dependencies (no cycles; submit / complete blocked while a predecessor is open). Daily / weekly / monthly recurrence, by count or date, created by a nightly job |
| Confidential tasks (ADR 0039) | One `TaskVisibility` rule for every read path (lists, counts, task, group, linked panel, dashboard, files, current status, My Work, search, Ask / `get_record`, MCP): **scope AND (not confidential OR participant)** in the database query. Categories can be confidential by default |
| Deadlines, reminders, escalation (ADR 0037) | Due date → end of working time of that day in the **assignee's unit's business hours** (shared `IBusinessHoursCalculator`), weekends and holidays roll to the next working day; due-soon (N working days before), escalation (M working days after, to the assignee's manager) and accept reminder (H working hours) snapshotted. Jobs `tasks.reminders` (15 min), `tasks.recurrence` (00:30), `tasks.digest` (06:45, when on). AR / EN templates through the Notifications service |
| Collaboration | Comments with @mentions of participants (notified), attachments through the Files service (`tasks.attachment`, versioned, upload permission), checklist, follow-up employees (comment / upload), secretary on-behalf creation (manager approves) |
| My Work and current status | `IMyWorkSource` items (to accept, assigned, overdue, awaiting my approval, rejected) and a **Tasks card** on My Work with counts; `ICurrentStatusProvider` for `tasks.task` with the user's next actions |
| Employee performance input (§11.11) | Time to accept / complete, on-time flag and feedback point stored per task; `GET /tasks/performance/{employee}` for the employee, their manager (Team) and `tasks.performance.read` holders |
| AI (Phase 5 registry) | `tasks-from-text` (prompt `tasks-from-text.v1.md`, schema-validated drafts; assignee names matched to employees; **nothing created until the creator confirms**), `task-summary` (thread summary, draft submission / return note), `task-assignee` and `task-workload` (deterministic, no model). Permissions `tasks.ai.*`. The offline demo provider handles both prompts |
| MCP (Phase 5 registry, §11.10) | `my_tasks` (read, on), `create_task`, `update_task_status` (accept / reject / start / pause / submit / complete), `add_task_comment` (write, **off**), `approve_task` (**approve, never on by default**; the creator rule still applies). Search source and record source for `tasks.task` |
| Web | **My Tasks** (tabs Assigned / Created / Follow-up / Team, counts, **list · Kanban (drag-and-drop, `@dnd-kit/core`) · calendar**, filters, quick actions), **New Task** (several assignees with suggestions, type / source / category / priority, dates, checklist, recurrence, files copied to each task, confidential, on behalf, *Draft tasks from text*), **task page** (header, stepper, current-status banner actions, overview with checklist / sub-tasks / dependencies / link badge, **timeline with durations + timesheet**, comments with mentions and AI summary, documents, follow-ups, history), **assignment group page**, **task dashboard** (tiles, bars by unit / source / status / priority, workload, insights), **Settings → Tasks**, My Work card, Sample **Tasks** tab. AR + EN, RTL / LTR |

## Acceptance checks (BUILD_PLAN Phase 6)

| Check | Evidence |
|---|---|
| Manager assigns one task to 3 employees; each accepts / rejects independently; group view shows "1 of 3 approved" | Integration `Multi_assign_to_three_people_creates_three_independent_tasks_and_one_group` (3 tasks + 1 group, distinct numbers, own visibility, A accepts → submits → approved, B rejects, C accepts; group `Finished 1 / Total 3`; message all, extend due for open tasks only, cancel all; notifications). E2E (ar + en) creates the three-assignee task in the UI and checks "1 of 3 approved" on the group page; screenshots `tasks-new-multi-assign`, `task-assignment-group` |
| Assignee pauses and restarts; timesheet shows periods and hold reasons; creator approves with feedback point | Integration `Full_lifecycle_with_pause_restart_return_and_creator_approval_with_feedback` (timeline events in order with durations, 3 `TS-` periods, hold reason, totals; assignee 403 on approve; return needs a comment; audit `tasks.task.approve` + `FeedbackPoint` change; performance input); unit `Timesheet_durations_are_correct_across_pauses_and_returns`. E2E approves from the status banner with a feedback point; screenshot `task-page-timeline` |
| Kanban and calendar views work in AR and EN | E2E screenshots `tasks-kanban`, `tasks-calendar`, `tasks-list`, `tasks-dashboard` in both languages |
| AI turns pasted meeting notes into draft tasks that the creator confirms | Integration `Ai_turns_meeting_notes_into_drafts_without_creating_anything` (fake model: drafts mapped, due date parsed, unknown name not matched, **no task created**, text passed as untrusted data). E2E with the demo provider: two "AI draft" cards; screenshot `tasks-from-text` |

**Also asked for in the prompt:**

| Request | Evidence |
|---|---|
| Every allowed and forbidden transition | Unit `TaskWorkflowTests`: the code table equals the specified table; 29 allowed rows reach their status; **every other (template, status, action, actor) combination (≈ 450) is refused and changes nothing** |
| Sub-task only while parent is In Progress / On Hold | Integration `Sub_tasks_only_while_…` (Assigned 422, creator 403, In Progress and On Hold OK, Submitted 422, approval blocked by open sub-tasks until they are approved) |
| Only the creator can approve | Integration `Only_the_creator_approves_even_an_administrator_…` (administrator with All scope sees it, 403 on approve; a colleague 404; the team manager sees but cannot cancel a task created by someone else) and the secretary test (the secretary who entered it cannot approve; the manager does) |
| Follow-up employee can comment / upload but not submit | Integration `Follow_up_employee_comments_and_uploads_but_cannot_submit…` (comment with mention, upload OK, submit / pause 403, comment-only follow-up 403 on upload, secretary of the unit adds follow-ups, removal ends access) |
| Confidential task hidden from others (incl. search, Ask and MCP) | Integration `Confidential_task_is_hidden_…` (manager and administrator: 404 on task / timeline / audit, absent from lists and search; search source, similar records, AI record source, `my_tasks` tool and current status hide it; participants find it) |
| Timesheet durations correct across pauses | Unit (above) + integration timeline totals |
| Due dates and overdue respect business hours and holidays | Integration `Due_dates_move_off_weekends_and_holidays…` (Saturday due → Tuesday 15:30 because Monday is a holiday; reminder Thursday 07:30; escalation Friday 15:30; accept reminder after 8 working hours) |
| Reminders and escalation with the fake clock | Integration `Reminders_overdue_notice_escalation_and_accept_reminder_run_once_each…` (travelling clock: accept and due-soon reminders, overdue notice, escalation to the manager after 3 working days, each once) and `Recurring_task_creates_its_next_occurrence…` |
| Links registry proven with the Sample module | Integration `Sample_item_shows_its_tasks_through_the_links_registry…` (owner sees both linked tasks, outsider cannot link, restricted badge without title, `/links/to` and `/resolve` 404 for outsiders, types by capability). E2E screenshot `sample-linked-tasks` |
| MCP tools, approve off by default | Integration `Mcp_task_tools_are_registered_…` |
| My Work Tasks card | Integration `My_work_shows_tasks_…` (to accept / awaiting approval items, counts, current status actions, dashboard workload) |
| Every server message key in AR and EN | Vitest `tasks/messageKeys.test` (also every action and status name of both templates); `TaskActionDialog.test` (reason / comment / feedback required, AR) |

## Quality gates run

| Gate | Result |
|---|---|
| `dotnet build Strategy.slnx -c Release` · `dotnet format --verify-no-changes` | 0 warnings, 0 errors · clean |
| `dotnet test` | **1 778 / 1 778** (unit, architecture and integration on real PostgreSQL). New: **502** Task unit tests (transition tables, timesheet, recurrence, dependencies, settings), **15** Task / Links integration tests; the authorization matrix covers every new endpoint. Two existing tests were updated for the new jobs (`tasks.*`) and the two task roles assigned to everyone |
| `tools/check-api-client.sh` · `tools/check-vulnerable.sh` | OpenAPI document and TS client up to date · no High / Critical |
| `npm run lint` · `format:check` · `typecheck` · `npm test` | clean · **89 / 89** Vitest (8 new) |
| `npm run build` · `npm audit --audit-level=high` · `npm run lint:licenses` | OK · no High / Critical (the same 14 moderate dev-tool advisories as Phases 3–5) · 613 packages allowed |
| `npx playwright test e2e/tasks.spec.ts` | **6 / 6** (3 scenarios × ar + en) with `E2E_SCREENSHOTS=phase-6`. The other specs were not re-run in this session |
| `--migrate` on an empty database | schemas `links` and `tasks` added (run by every integration test host and the E2E host) |

**Dependencies added:** `@dnd-kit/core` 6.3.1 (MIT, with `@dnd-kit/accessibility` and `@dnd-kit/utilities`) — listed in
[`THIRD_PARTY_LICENSES.md`](../THIRD_PARTY_LICENSES.md). No NuGet package added.

## Decisions (ADRs)
- [0034](../decisions/0034-links-registry.md) Links registry: declared types, permission-aware badges (visible / restricted / unavailable),
  `FollowLinkedPermission` for linked-record roles, link storage in schema `links`.
- [0035](../decisions/0035-task-approval-by-creator.md) Creator approval is a task-level decision (permission **and** actor), not an
  approval-engine process.
- [0036](../decisions/0036-task-workflow-templates.md) Full / Simple transition tables, fixed status codes with names per template, template
  per type in settings and snapshotted; Tasks is an always-on core system; reassign only from Rejected and Submitted (as in the SPEC table).
- [0037](../decisions/0037-task-deadlines-and-reminders.md) Due dates, reminders and escalation in working time of the assignee's unit.
- [0038](../decisions/0038-task-dependencies-and-recurrence.md) Finish-to-start dependencies (block submit / complete); recurrence rules.
- [0039](../decisions/0039-confidential-tasks.md) One visibility rule for every read path.

## Deviations
- **From the SPEC:** none known. Interpretations are in the ADRs above.
- **From the plan / CLAUDE.md:**
  - Dashboard charts are accessible CSS bars built on the design tokens, not ECharts (CLAUDE.md §4.2): the task dashboard needs only bars;
    ECharts arrives with the reporting phase (17), where heat-maps and trends need it.
  - Assigning to "a team's members" (§11.4) waits for the Teams module (Phase 7); employees and whole units work now.
  - Comment attachments reference files uploaded to the task (Documents tab) rather than an upload inside the comment box.
- **Contracts added:** `Strategy.Platform.Links.Contracts` (new); `ITaskDirectory` in `Strategy.Modules.Tasks.Contracts`.
- **CLAUDE.md:** platform schema list includes `links`.
- **Development demo AI provider** also drafts tasks from list lines ("- … @Name by YYYY-MM-DD") and writes a demo task summary.

## Fixes found along the way
- An audit event written inside an open transaction waited for the audit chain lock held by that same transaction (timeout → 500): task
  events are now audited after the commit.
- The links registry and the tasks link source depended on each other through DI; the registry now resolves sources on first use.

## Known gaps (by design: later phases or follow-ups)
- Bulk role transfer does not re-route open tasks: tasks are assigned to people, not roles; re-assigning a leaver's tasks stays a manual
  creator action (or a later "reassign all tasks of an employee" admin action).
- Portfolio (milestone / project-level tasks, task-driven milestone progress), Teams (meeting decisions, committee members) and the other
  systems register their own linkable types and panels in their phases.
- Personal leave calendars are not part of the working-time arithmetic (ADR 0027 / 0037).

## How to demo
```bash
REGISTRY_PREFIX=mirror.gcr.io/ docker compose -f deploy/docker-compose.yml --profile dev up -d postgres gotenberg mailpit
dotnet run --project src/Host/Strategy.Host
cd web && npm ci && npm run dev        # http://localhost:5173
```
1. **Mariam** (`mariam@strategy.local` / `Strategy#Demo2026`, MFA): add two employees under the Strategy department (Employees). My tasks →
   **New task** → pick Ahmed and the two new employees → *Create 3 tasks*. Open one task → *Open group*: each assignee's status.
2. **Ahmed** (`ahmed@strategy.local`): My Work shows the Tasks card; open the task → **Accept**, **Pause** (reason), **Start**, **Submit**.
   Timeline tab: events with durations and the timesheet with the hold reason. Kanban: drag a card from *Assigned* to *In progress*.
3. Mariam: the task shows *Submitted: waiting for the creator's approval* → **Approve** with a feedback point; the group reads "1 of 3
   approved". Task dashboard: open / overdue by unit, source, status and priority, workload and insights.
4. New task → **Draft tasks from text** → paste minutes with "- … @Ahmed by 2026-12-01" lines → review the AI drafts → create.
5. Sample module → an item → **Tasks** tab → *Create task* (linked); the task shows the item's live badge.
6. Settings → **Tasks**: workflow per type (personal = Simple), assignment rule, reminders, escalation, digest, confidential categories.

## Cloud environment notes
- Docker started manually (`dockerd`); Gotenberg and Mailpit through `mirror.gcr.io/`. Integration tests reuse the local PostgreSQL 16 with
  `STRATEGY_TEST_POSTGRES`; E2E ran against database `strategy` with `E2E_CHROMIUM_PATH=/opt/pw-browsers/chromium`.
- Screenshots (`E2E_SCREENSHOTS=phase-6`) are in [`docs/screenshots/phase-6/`](../screenshots/phase-6/) as `ar-*` / `en-*` pairs:
  `tasks-list`, `tasks-kanban`, `tasks-calendar`, `tasks-new-multi-assign`, `task-page-timeline`, `task-assignment-group`, `tasks-dashboard`,
  `tasks-from-text`, `sample-linked-tasks`. The lifecycle actions in the E2E run back to back, so the timesheet durations read 0:00 there.
