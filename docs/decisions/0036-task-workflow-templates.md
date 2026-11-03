# 0036 — Task workflow templates, status codes, and Tasks as an always-on core system

- **Status:** Accepted (Phase 6)
- **Context:** SPEC §11.3: *Full* and *Simple* workflow templates chosen per task type, "status names configurable per template", draft
  status, sub-tasks; SPEC §0.0 lists Tasks among the shared core services.

## Decision
- Two templates as explicit code (`TaskWorkflow`): transition tables keyed by (template, status, action) with the acting role (creator /
  assignee). Status **codes** are fixed (`draft`, `assigned`, `in_progress`, `on_hold`, `submitted`, `approved`, `rejected`, `to_do`,
  `done`, `cancelled`); **names** per template are translation keys (`tasks.status.full.*`, `tasks.status.simple.*`) that administrators
  can override in Settings → Translations. Business logic checks codes only.
- The template is chosen from the task type through the **Tasks settings** map (`type code → full | simple`, default `full`, seeded
  `personal → simple`) and **snapshotted on the task**, so changing the map never changes a running task.
- `Rejected` is kept for the Full template only; *Reassign* is allowed from Rejected and Submitted as in the SPEC table (not from
  Assigned: the creator cancels and creates a new task instead).
- The `tasks` system is declared `SystemKind.Platform`: always on, no system-access grant, like Organisation. Its code is still a module
  (own project, schema `tasks`, contracts). Roles *Employee* and *Manager* are assigned to everyone (Own / Team scopes), so every employee
  has My Tasks and managers see their direct reports' tasks; administrators can narrow these in Settings → Assignments.

## Consequences
New templates (e.g. with a review step) are code changes with their own transition table and tests.
