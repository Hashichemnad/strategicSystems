# 0167 — Delegated system administration (`<system>.members.manage`)

- **Status:** Proposed (Phase 19, stream D1) — awaiting owner acceptance
- **Context:** SPEC-ADDENDUM §D1: a system administrator manages the Members of **their** system only — never another system or a platform
  setting. Until now only holders of `access.assignment.manage` (the platform access administrator) could assign roles.

## Decision
- **One permission per system**, `<system>.members.manage` («إدارة أعضاء النظام» / "Manage the system's members", not scopable). The access
  seeder synthesises it for every catalogue that declares member roles (so the twelve module catalogues do not repeat it) and grants it, once
  when the permission is new, to the system's seeded administrator role(s) (builtin roles only; administrators' later edits stay).
- **The platform administrator is covered by `access.assignment.manage`.** In the model a role belongs to one system and holds only that
  system's permissions, so the `access.administrator` role cannot hold `portfolio.members.manage` without changing a rule; the Members
  endpoints therefore accept `access.assignment.manage` **or** `<system>.members.manage`.
- **Checked on the server for the path's system**: every Members endpoint resolves `{code}` (unknown, disabled or without member roles →
  404), then calls `IAccessService.CanAsync(user, "<code>.members.manage")` / `access.assignment.manage` (403 otherwise; vendors are
  refused by the access service). Lists only ever contain roles of that system, so no role of another system can be named.
- **Escalation guard (safe default).** A delegated administrator **may** add and remove administrators of their own system (the system
  administrator team, e.g. three people of the Strategy Office, maintains itself). They may **never** assign or end a role of the platform
  administrator set (`org`, `identity`, `access`, `settings`, `audit`, `workflow`, `calendar`, `ai`, `mcp`, `tasks`, `teams`, `platform`
  administrators) → 403 `access.members.platformRole`; for Teams and Tasks (whose administrator roles are platform administrator roles) the
  Administrators list is managed by the platform administrator only. They cannot change role permissions, the member-role configuration
  (`access.role.manage`) or any other setting — those endpoints keep their own permissions. The last access administrator guard is kept.
- **UI**: the Settings navigation shows *Members* to holders of any `<system>.members.manage`, so a delegated administrator sees Settings
  with this page only; people search for them goes through a Members endpoint (same directory search), the unit picker uses the
  organisation read that everyone has.

## Consequences
- A platform administrator who wants a delegated administrator without the full system administrator role creates a role with only
  `<system>.members.manage` (matrix) and assigns it.
- Revoking the permission from the administrator role in the matrix switches delegation off for that system.
