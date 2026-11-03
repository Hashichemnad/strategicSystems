# 0039 — Confidential tasks and one visibility rule for every read path

- **Status:** Accepted (Phase 6)
- **Context:** SPEC §11.9: "Confidential task option: visible only to creator, assignee and follow-up employees". Tasks are read by many
  paths: lists, counts, task page, group page, linked-record panel, dashboard, search, Ask, MCP, My Work, files, current status.

## Decision
- One helper (`TaskVisibility`) builds every task query: data scope from `IAccessService.ApplyScopeAsync` **plus** "not confidential OR
  the user is the creator, the employee who entered it, the assignee or an active follow-up employee", evaluated in the database. The
  single-record check uses the same rule. No other code reads tasks for a user.
- Administrators, managers (Team / Unit scopes), secretaries and linked-record roles do **not** see confidential tasks; the dashboard and
  workload figures exclude them for those viewers. The audit log keeps its own access rules (auditors).
- Categories listed in Settings → Tasks make new tasks confidential by default (the "Accounts department" case of SPEC §11.1 without a cloned
  object); the creator can still change the flag.

## Consequences
A confidential task can still be counted by the people who are its participants. Assignment groups of confidential tasks show only the
member tasks the viewer may see.
