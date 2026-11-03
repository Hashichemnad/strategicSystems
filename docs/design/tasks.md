# Design note — Phase 6: Task Management (`Strategy.Modules.Tasks`)

**Scope.** BUILD_PLAN Phase 6; SPEC §11 (all, incl. decisions §11.12), §0.0 cross-system links, §0.4 level 1 (My Tasks card on My Work);
CLAUDE.md §5–§16. Decisions: ADRs 0034 (links registry), 0035 (creator approval is a task-level decision, not an approval-engine
process), 0036 (workflow templates, state machines and Tasks as an always-on core system), 0037 (deadlines, reminders and escalation in
working time), 0038 (dependencies and recurrence), 0039 (confidential tasks and visibility).

This note is the Phase 6 plan. The session runs without an interactive approval step (as Phase 5): the plan is committed first, then
implemented as written; open choices are the ADRs above.

## 1. Projects

| Project | Contents |
|---|---|
| `Strategy.Modules.Tasks.Contracts` | `ITaskDirectory` (open-task counts of a linked record, for "closing the source record prompts to close its tasks"), `TaskSummary` |
| `Strategy.Modules.Tasks` | `Domain/` (TaskItem, AssignmentGroup, TimesheetPeriod, TaskComment, FollowUp, ChecklistItem, Dependency, TaskEvent, `TaskWorkflow` transition tables), `Application/` (commands, queries, validators, DTOs, deadlines, visibility, notifications, current status, My Work), `Infrastructure/` (`TasksDbContext`, schema **`tasks`**, migrations), `Api/`, `Jobs/` (reminders, recurrence, digest), `Ai/` (features + prompts), `Mcp/` (tools) |
| `Strategy.Platform.Links(.Contracts)` | see [links.md](links.md) |

The system code is `tasks`. It is a **core system** (SPEC §0.0 "Core (shared): … Tasks"): its catalogue declares `SystemKind.Platform`,
so it is always on and needs no system-access grant (ADR 0036); the code still lives under `src/Modules` with its own schema and
contracts, like any module.

## 2. Data model (schema `tasks`; every business table audited, `xmin`, audit columns; no hard deletes)

| Table | Columns (main) | Notes |
|---|---|---|
| `task` | `number` (`TASK-{SEQ:5}`), `title_ar`, `title_en`, `description`, `type_code` (lookup `tasks.type`), `workflow` (`full` / `simple`, snapshotted from the type), `source_code` (lookup `tasks.source`), `category_code` (lookup `tasks.category`), `priority` (`high` / `medium` / `low`), `status`, `confidential`, `creator_employee_id`, `creator_user_id`, `entered_by_employee_id` (the secretary when created on behalf), `assignee_employee_id`, `unit_id` (assignee's unit when assigned — the unit data scopes), `group_id`, `parent_task_id`, `linked_type`, `linked_id`, `planned_start`, `due_date`, `due_at`, `due_soon_at`, `escalate_at`, `business_hours_id`, `assigned_at`, `accept_remind_at`, `accepted_at` (starting), `submitted_at` / completion, `approved_at`, `closed_at`, `rejection_reason`, `cancel_reason`, `completion_comment`, `feedback_point`, `feedback_comment`, `percent_complete`, `estimated_hours`, `actual_seconds`, `time_to_accept_seconds`, `time_to_complete_seconds`, `completed_on_time`, `recurrence` (jsonb), `recurrence_source_id`, `next_occurrence_on`, `occurrence_count`, `reminder_sent_at`, `accept_reminded_at`, `overdue_notified_at`, `escalated_at` | Indexes on assignee, creator, unit, status, due_at, (linked_type, linked_id), group, parent |
| `assignment_group` | `title_ar`, `title_en`, `creator_employee_id`, `creator_user_id`, `completion_rule` (`all` / `any`) | Members = tasks with `group_id` |
| `timesheet_period` | `number` (`TS-{SEQ:7}`), `task_id`, `started_at`, `ended_at`, `duration_seconds`, `hold_reason`, `end_event` | Parent = task (History tab) |
| `comment` | `task_id`, `text`, `mentions` (employee ids), `file_ids`, `author_user_id`, `author_employee_id`, `kind` (`comment` / `group-message`) | Parent = task |
| `follow_up` | `task_id`, `employee_id`, `added_by_employee_id`, `can_upload`, `active`, `removed_at` | Parent = task; gives the `follow_up` record role |
| `checklist_item` | `task_id`, `text`, `sort`, `done`, `done_by_employee_id`, `done_at`, `active` | Parent = task |
| `dependency` | `task_id`, `depends_on_task_id`, `active` | Finish-to-start (ADR 0038) |
| `task_event` | `task_id`, `occurred_at`, `event`, `from_status`, `to_status`, `actor_employee_id`, `actor_user_id`, `comment`, `data` (jsonb) | The **timeline** (append-only, not edited) |

Files: purpose `tasks.attachment` (owner `tasks.task`, documents, 20 MB, versioned) through the Files service; comment attachments are
task attachments referenced by id. Changes and comments are in the audit trail (interceptor) and the History tab.

**Lookups (seeded, editable):** `tasks.type` (`manager-assignment`, `project`, `meeting-decision`, `personal`), `tasks.source`
(`manager-assignment`, `dg-office`, `committee-decision`, `project`, `other`), `tasks.category` (`general`). Priority codes are a fixed
logic enum with labels in the translation files.

**Settings area `tasks`** (Settings → Tasks, `tasks.settings.manage`): type → workflow map (default every type `full`, `personal` →
`simple`, SPEC §11.3 / §11.12), confidential categories (tasks of these categories are confidential by default), who may be assigned
(`anyone` / `unit` = creator's unit and below / `direct-reports`; self always), secretary may create on behalf (on), feedback scale
maximum (5), reminder N working days before due (2), overdue notice (on), escalate to the assignee's manager after N working days
overdue (3), accept reminder after N working hours in *Assigned* (8), digest (`off` / `daily` / `weekly` + weekday), workload threshold
(open tasks per employee, 10).

## 3. State machines (explicit code, `TaskWorkflow`; unit tests cover every allowed and every forbidden pair)

Actors: **C** = creator (assigner; for on-behalf tasks the manager, not the secretary), **A** = assignee. The actor is checked from the
task (employee ids), **and** the permission from the record role (ADR 0035): a system role can never approve.

### Full (default, SPEC §11.3)

| From | Action | By | To | Side effects |
|---|---|---|---|---|
| (new) | create | C | Assigned (or Draft) | assignee notified; `assigned_at`; deadlines computed |
| Draft | assign | C | Assigned | as create |
| Assigned | accept | A | In Progress | `accepted_at` = now, time to accept; timesheet period opened |
| Assigned | reject (reason) | A | Rejected | creator notified |
| Rejected | reassign (new assignee) | C | Assigned | new assignee notified, record roles moved |
| Rejected | cancel (reason) | C | Cancelled | |
| In Progress | pause (hold reason) | A | On Hold | period closed with the hold reason |
| On Hold | start | A | In Progress | new period |
| In Progress | submit (completion comment) | A | Submitted | period closed; completion = now; creator notified. Blocked while a predecessor is open (ADR 0038) |
| Submitted | approve (feedback point, comment) | C | Approved | `approved_at`; on-time flag; blocked while sub-tasks are open |
| Submitted | return (comment) | C | In Progress | new period; assignee notified |
| Submitted | reassign (new assignee, comment) | C | Assigned | |
| Draft, Assigned, Rejected, In Progress, On Hold, Submitted | cancel (reason) | C | Cancelled | open period closed |
| In Progress, On Hold | create sub-task | A | (sub-task Assigned) | the parent's assignee is the sub-task's creator |

### Simple (SPEC §11.3: no acceptance, no approval; codes fixed, labels per template in translations, overridable in Settings → Translations)

| From | Action | By | To |
|---|---|---|---|
| (new) | create | C | To Do |
| To Do | start | A | In Progress (period opened) |
| In Progress | pause (reason) | A | On Hold |
| On Hold | start | A | In Progress |
| In Progress | complete | A | Done (blocked by open sub-tasks / predecessors) |
| To Do, In Progress, On Hold | cancel | C | Cancelled |
| In Progress, On Hold | create sub-task | A | — |

When C and A are the same person (a personal task) they hold both roles. Closed statuses: Approved, Done, Cancelled. "Open" = any other.

## 4. Multi-assignment (SPEC §11.4)

`POST /tasks` takes `assigneeEmployeeIds` (1–50) and/or `unitId` (all active employees of the unit). Duplicates removed; the assign rule
(setting) is checked per person. **One task per assignee** (own number, status, timesheet, comments, approval); shared content (title,
description, due date, priority, link, checklist, confidentiality) copied. With more than one assignee an **Assignment Group** (completion
rule `all` / `any`) ties them. Group page: each assignee's status, progress "x of n approved" (Done for simple tasks), group complete
flag; actions for the creator: **message all** (a comment on each task + one notification each), **extend the due date for all open
tasks**, **cancel all open tasks** (reason). Group actions run in one transaction. Files uploaded on the New Task form are attached to
each task (copied through the Files service). Recurrence is single-assignee only.

## 5. Timesheet

A period opens on accept (Full), start (Simple / after hold) and return; it closes on pause (with the hold reason), submit, complete,
cancel and reassign. Duration = end − start (elapsed); `actual_seconds` = sum of closed periods (+ the running one when displayed).
The timeline shows each event with the time since the previous one; the timesheet tab lists the periods with hold reasons.

## 6. Access (SPEC §11.9) — record roles on `tasks.task`, granted and revoked only by this module

| Permission | Granted by |
|---|---|
| `tasks.task.read` (scopable) | role *Employee* (everyone): **Own** (assignee = owner employee, creator = owner user); role *Manager* (everyone, team = direct reports): **Team**; *Secretary*: **Unit**; *Administrator*: All; record roles creator / assignee / follow_up; the linked record's `FollowLinkedPermission` (links registry) |
| `tasks.task.create` | Employee (everyone) |
| `tasks.task.manage` (edit, reassign, cancel, approve, feedback, dependencies, group actions) | record role **creator** only |
| `tasks.task.work` (accept / reject / pause / start / submit / complete / sub-tasks) | record role **assignee** only |
| `tasks.task.comment`, `tasks.task.upload` | creator, assignee, follow_up (follow-ups without upload get comment only) |
| `tasks.task.follow-up` (add / remove follow-up employees) | record role assignee; role *Secretary* (**Unit** scope: tasks of their department) |
| `tasks.task.on-behalf` | Secretary: create a task as the head of their unit (setting) |
| `tasks.dashboard.read` · `tasks.performance.read` | Manager (Team); *HR* (performance, All); Administrator |
| `tasks.settings.manage` | Administrator (MFA) |

Record roles: `creator` (read, manage, comment, upload), `assignee` (read, work, comment, upload, follow-up), `follow_up` (read, comment,
upload) and `follow_up_comment` (read, comment). Reassigning revokes the old assignee's role and grants the new one. Handlers check
permission **and** actor: only the creator approves (an administrator role cannot), a follow-up employee can comment / upload but not
submit. 404 outside the read scope, 403 for a visible task and a forbidden action.

**Confidential tasks (ADR 0039):** visible only to creator, assignee and follow-up employees. Every read path (get, lists, counts, group
page, linked-record panel, dashboard, search source, record source / Ask, MCP tools, My Work, files) goes through one `TaskVisibility`
helper that adds `!confidential OR participant` to the database query, so scopes, managers, administrators and linked-record roles never
see them. Categories listed in the settings make tasks confidential by default.

**Who can be assigned:** setting `anyone` / `unit` / `direct-reports`. A secretary with `tasks.task.on-behalf` creates as the head of
their unit; the task records the secretary in `entered_by_employee_id` and the manager is the creator (approver).

## 7. Deadlines, reminders and escalation (ADR 0037)

Due **date** chosen by the user; `due_at` = the end of working time on that date in the business hours of the assignee's unit
(`IBusinessHoursCalculator.ResolveForUnitAsync`); a non-working due date (weekend / holiday) moves to the end of the next working day.
Snapshotted when set (as approval SLAs): `due_soon_at` = start of working time N working days before the due date; `escalate_at` = end of
working time M working days after the due date; `accept_remind_at` = `assigned_at` + H working hours. Status indicator (computed): *on
hold*, *overdue* (`now > due_at`), *due soon* (`now ≥ due_soon_at`), *on time*.

Jobs (Quartz, idempotent through the `*_at` stamps): `tasks.reminders` every 15 min — due-soon reminder to the assignee, overdue notice to
assignee and creator, escalation to the assignee's manager, accept reminder; `tasks.recurrence` daily 00:30 — next occurrences;
`tasks.digest` daily 06:45 — digest of open tasks when the setting is on. Each sends AR / EN templates through `INotificationService`.

## 8. Other rules

- **Sub-tasks** only while the parent is In Progress / On Hold, created by the parent's assignee; inherit link and confidentiality. A parent
  cannot be approved / done while a sub-task is open (`tasks.errors.openSubtasks`).
- **Dependencies** (ADR 0038): finish-to-start among tasks the creator can read; no cycles; a dependent task can be accepted but cannot be
  submitted / completed while a predecessor is open.
- **Recurrence** (ADR 0038): daily / weekly / monthly × interval, until date or count. The first task is occurrence 1; the job creates the
  next one (same assignee, dates shifted) when its planned start arrives; cancelling the first task ends the series.
- **Comments**: text ≤ 4000, **@mentions** limited to the task's participants (notified), attachments = task files.
- **Checklist**: creator and assignee add / tick items while the task is open.
- **Linked record** (links registry): the creator must be allowed to link it (`CanLinkAsync`, capability `task-able`). The task shows the
  badge; the record shows its tasks (`GET /tasks?linkedType=&linkedId=`, permission-aware; a person with the type's `FollowLinkedPermission`
  on the record sees all its non-confidential tasks). A link row is written in `links.record_link` with the task.
- **Employee performance input (§11.11)**: time to accept, time to complete, on-time flag and feedback point stored on each task;
  `GET /tasks/performance/{employeeId}` aggregates them for the employee, their manager (Team) and `tasks.performance.read` holders.
- **Creator approval** is a task-level decision with history in `task_event` (ADR 0035): no approval-engine process, nothing in Approval
  Settings.

## 9. API (`/api/v1/tasks`, minimal APIs, Problem Details, idempotency key on create / submit / approve)

| Endpoint | Notes |
|---|---|
| `GET /tasks?view=assigned\|created\|follow-up\|team\|all&status=&priority=&indicator=&search=&linkedType=&linkedId=&groupId=&parentId=&dueFrom=&dueTo=&page&pageSize&sort` | Lists, Kanban (by status) and calendar (due range) |
| `GET /tasks/counts` | Open, overdue, awaiting my approval, follow-up, team |
| `POST /tasks` | Single or multi-assign (+ group), draft, on behalf, link, checklist, recurrence, dependencies |
| `GET /tasks/{id}` · `PUT /tasks/{id}` | Detail with allowed actions · edit (creator, open) |
| `POST /tasks/{id}/{assign\|accept\|reject\|pause\|start\|submit\|complete\|approve\|return\|reassign\|cancel}` | Transitions (version for 409) |
| `POST /tasks/{id}/subtasks` | Sub-task |
| `GET /tasks/{id}/timeline` · `GET /tasks/{id}/timesheet` | Timeline with durations · periods |
| `GET/POST /tasks/{id}/comments` | Comments, mentions |
| `POST /tasks/{id}/follow-ups` · `POST /tasks/{id}/follow-ups/{employeeId}/remove` | Follow-up employees |
| `POST /tasks/{id}/checklist` · `PUT /tasks/{id}/checklist/{itemId}` · `POST …/remove` | Checklist |
| `POST /tasks/{id}/dependencies` · `POST …/{dependsOnId}/remove` | Dependencies |
| `GET /tasks/{id}/history` | Audit History tab |
| `GET /groups/{id}` · `POST /groups/{id}/message` · `/extend-due` · `/cancel` | Assignment group |
| `GET /dashboard` · `GET /performance/{employeeId}` | Dashboard, performance input |
| `GET/PUT /settings` | Settings → Tasks |
| `POST /ai/from-text` · `POST /ai/suggest-assignees` · `POST /tasks/{id}/ai/summary` · `GET /ai/workload` | AI (drafts only) |

Attachments use the generic files API (`IFileOwnerAccess`: read = can read the task; write = upload permission and task open).

## 10. Current status, My Work, search

- `ICurrentStatusProvider` (`tasks.task`): "Waiting for {assignee} to accept", "In progress with …", "On hold: {reason}", "Waiting for
  {creator}'s approval", … with the current user's next actions (same table as the API).
- **My Work → Tasks card** (`IMyWorkSource`): tasks assigned to me needing my action, tasks I created awaiting my approval, and overdue
  ones (`tasks.myWork.assigned / awaitingApproval / overdue`), plus the counts card on the home page.
- `IRecordSearchSource` and `IAiRecordSource` for `tasks.task` (search, Ask, `get_record`) through `TaskVisibility`.

## 11. AI (Platform.AI registries; drafts only — CLAUDE.md rule 6)

| Feature key | Permission (Employee role) | What |
|---|---|---|
| `tasks-from-text` | `tasks.ai.from-text` | Minutes / e-mail / memo → draft tasks (title, description, suggested assignee names matched to employees, due date, priority). Prompt `Ai/Prompts/tasks-from-text.v1.md`, schema-validated; the New Task review screen creates nothing until the creator confirms each draft |
| `task-summary` | `tasks.ai.summary` | Summarise the comment thread; draft the submission note (assignee) or the return comment (creator) |
| `task-assignee` | `tasks.ai.assignee` | Assignee suggestion without a model: same unit, current open workload, past similar tasks (trigram similarity of titles); reasons shown |
| `task-workload` | `tasks.ai.workload` | Workload insights without a model: overloaded employees (open ≥ threshold) and chronic overdue (≥ 30 % of last-90-day tasks late, ≥ 3) within the manager's scope |

## 12. MCP tools (§11.10; tool registry, ADR 0031)

`my_tasks` (read, on), `create_task` (write, single / multi-assign, **off**), `update_task_status` (write: accept, reject, start, pause,
submit, complete — **off**), `add_task_comment` (write, **off**), `approve_task` (approve, **never enabled by default**; the creator rule
still applies). All run as the token owner through the same handlers; confidential tasks follow `TaskVisibility`.

## 13. Screens (AR + EN, RTL / LTR)

My Tasks (`/tasks`: tabs Assigned to me / Created by me / Follow-up / Team, counts, **List · Kanban · Calendar**, quick actions),
**New Task** dialog (several assignees or a unit, type, source, category, priority, dates, description, files, linked record, checklist,
recurrence, confidential, on behalf; *Draft tasks from text* with AI review), **task page** (`/tasks/$id`: header, stage stepper,
current-status banner with actions, tabs Overview (description, checklist, sub-tasks, dependencies, linked record), Timeline & timesheet,
Comments, Documents, Follow-up, History), **assignment group page** (`/tasks/groups/$id`), **task dashboard** (`/tasks/dashboard`: open /
overdue by unit, source, status, priority; average time to accept / complete; on-time rate; average feedback; workload per employee;
workload insights), **Settings → Tasks**, the Sample item's **Tasks** tab (`LinkedTasksPanel`), the My Work Tasks card.
Kanban drag-and-drop with `@dnd-kit/core` (MIT); charts are simple accessible bars from the design tokens (no chart library yet).

## 14. Tests

Unit: transition tables (every allowed and forbidden pair for both templates and both actors), timesheet maths across pauses, status
indicator, recurrence dates, dependency cycle check, mention parsing. Integration (real PostgreSQL): create / multi-assign (3 people → 3
tasks + 1 group, independent accept / reject, group "1 of 3 approved"), sub-task only while parent In Progress / On Hold, only the creator
approves (an administrator gets 403), follow-up employee comments / uploads but cannot submit, confidential task hidden from a manager,
an administrator, search, Ask (record source) and MCP, timesheet periods and hold reasons, due dates and overdue across weekends and
holidays, reminders / overdue / escalation / accept reminder with the travelling clock, links (Sample item shows its tasks; outsider sees
a restricted badge), AI from text with the fake model (nothing created), MCP tools (approve off by default), audit entries, the
authorization matrix. E2E (ar + en): screenshots of My Tasks list, Kanban, calendar, new task with several assignees, task page timeline,
assignment group, dashboard, linked tasks on a sample item.
