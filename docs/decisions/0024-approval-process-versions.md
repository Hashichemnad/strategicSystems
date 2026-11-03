# 0024 — Approval process definitions: code-registered keys, database-versioned steps

- **Status:** Accepted (Phase 4)
- **Context:** CLAUDE.md §9: versioned process definitions editable in the Approval Settings centre; modules register process keys.

## Decision
- A process **key**, its names, resource type, read permission, variables and default steps are declared in code by the owning module
  (`AddApprovalProcess`). The steps live in `workflow.process_version` / `process_step`.
- Every save in the editor creates **version N + 1** (with a note and the author); there is no draft state. The highest version is used by new
  submissions; an instance keeps the version it started with until it closes, so in-flight approvals never change under people's feet.
  Optimistic check: the save names the version it was based on (409 when someone saved meanwhile).
- Version 1 is seeded from the code's default steps when a process has no version (new installation or a newly added module); the code
  defaults never overwrite a saved configuration.
- Stages of the business records stay code (CLAUDE.md §5); only approvers and steps are configuration.

## Consequences
Old versions stay readable (Versions tab). Removing a process from code leaves its versions and instances in place, unlisted.
