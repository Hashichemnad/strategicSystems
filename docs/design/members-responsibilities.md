# Design note — Members and Responsibilities per system (Phase 19, SPEC-ADDENDUM §D)

## §1 Members

**Purpose.** SPEC-ADDENDUM §D1: *System → Settings → Members*, a simple screen over the **existing** access model
(`docs/design/access.md`, ADRs 0015 / 0016). It never changes the access rules: it only creates / ends role assignments and grants / revokes
system access through `AccessAdministration`, so the audit trail, the access-change signal (cache invalidation), bulk transfers, the
access review and the permission matrix keep working unchanged. ADR 0166 (Members over the access model), ADR 0167 (delegated
administration).

### Lists and how they map
| List (key) | Who | Add creates | Remove does |
|---|---|---|---|
| **Administrators** (`administrators`) | people only | an assignment of every *administrator role* of the system (its scopes come from the role, seeded All) + system access for the person (module systems) | ends those assignments; revokes the principal's own system-access grant when they hold no other current / future role of the system |
| **Users** (`users`) | a person, a unit (incl. sub-units, principal kind `unit`) or everyone | system access (module systems) + an assignment of every *user role* | ends the user-role assignments and revokes the grant (same rule) |
| **Extra role** (`extra:<roleCode>`) | person, unit or everyone | an assignment of that role + system access (module systems) | ends the assignment (+ grant rule) |

- A row is one principal (kind + id). Its `id` is the grant id (Users list, when a grant exists) or the id of the principal's first matching
  assignment; `POST …/members/{id}/remove` finds the list from it (the lists of one system never share a role).
- The Users list also shows principals that have system access but no role of any list (e.g. people with record roles only), so every grant
  is visible and removable. Principals that only hold an administrator / extra role are not repeated under Users.
- **Effective dates**: `validFrom` / `validTo` (inclusive, optional) go on the role assignments. System access has no dates in the model: it
  is granted at once; until `validFrom` the person can open the system but holds no role of the list.
- **Removal** ends the assignments with yesterday as the last day (access ends now, nothing is deleted); an assignment not yet started ends
  before it starts. The optional reason is the ambient audit reason (`IAuditReason`) of the change.
- **Platform-kind systems** (Teams, Tasks) need no system-access grant (the access model refuses one): their lists are assignments only.
- Existing rules stay: an overlapping assignment of a role is skipped (all skipped → 409 `access.members.alreadyMember`); the last access
  administrator cannot be removed (`access.lastAdministrator`; not reachable from a module list, but the call path is the same).

### Member-role configuration (per system)
Code defaults: each catalogue declares `IAccessCatalogueSource.MemberRoles` (`SystemMemberRoles` in `Strategy.Platform.Access.Contracts`:
administrator role codes, user role codes, extra role codes). The platform administrator overrides them per system in Settings
(`access.system_member_roles`, one auditable row per system, jsonb code lists; no row = code defaults). Validation: codes exist in that
system, the three lists are disjoint, at least one administrator role.

| System | Administrators | Users | Extra lists |
|---|---|---|---|
| `portfolio` | `administrator` | `requester` | `pm-office`, `viewer`, `department-viewer` |
| `kpi` | `administrator` | `owner` | `strategy-office`, `department-manager`, `executive` |
| `risk` | `risk-office` | `owner` | `coordinator`, `viewer`, `risk-committee`, `internal-audit` |
| `teams` (platform kind) | `administrator` | `employee` | `department-manager`, `viewer` |
| `tasks` (platform kind) | `administrator` | `employee` | `secretary`, `hr` |
| `requests` | `office` | `requester` | `type-administrator` |
| `docreg` | `administrator` | `reader` | `office`, `leadership` |
| `ip` | `administrator` | `requester` | `office`, `viewer` |
| `bench` | `bench-office` | `participant` | `coordinator`, `viewer` |
| `cap` | `cap-office` | `participant` | `department-manager`, `viewer` |
| `auditmgmt` | `audit-office` | `department-user` | `chief-audit-executive`, `audit-committee`, `viewer` |
| `sample` | `editor` | `viewer` | `approver` |

Where a module has both an *office* role and an *administrator* role (Portfolio, KPI, Document Registry, IP) the administrator role (it
holds the system's settings permission) is the system administrator and the office role is an extra list. Several user roles are also
seeded for *everyone*; a Users entry still adds the role for the principal (harmless, and it survives an edit of the everyone assignment).

### Delegated administration (ADR 0167)
- Permission per system **`<system>.members.manage`** («إدارة أعضاء النظام» / "Manage the system's members"). It is not written into
  every module catalogue: the access seeder synthesises it for every catalogue that declares `MemberRoles` and grants it (scope All) to the
  system's seeded administrator roles once, when the permission is new (administrators' later edits stay).
- **Who may manage a system's Members**: `access.assignment.manage` (the platform access administrator — a role of `access` cannot hold
  module permissions in the model, so the platform administrator role is covered through its existing permission) **or**
  `<system>.members.manage` checked through `IAccessService.CanAsync` for *that* system code. Otherwise 403; an unknown system, a system
  without member roles or a disabled module → 404.
- A delegated administrator (without `access.assignment.manage`) may add / remove administrators **of their own system**, but never a
  role of the platform administrator set (`AdministratorRoles`, e.g. `teams.administrator`, `tasks.administrator`) → 403
  `access.members.platformRole`; for Teams / Tasks only the platform administrator manages the Administrators list. The lists only hold roles
  of the path's system, so another system's role or a platform role cannot be named. The member-role configuration, role permissions and
  every other access / settings screen keep their own `access.*` / `settings.*` permissions.
- People search for delegated administrators: `GET …/members/user-search?search=` (the directory search of `/access/users`).

### Endpoints (`/api/v1/access`)
| Method & path | Permission |
|---|---|
| `GET /members/systems` | signed in; the systems the caller may manage |
| `GET /systems/{code}/members` | members manage (above): lists by kind, principal names, role names, `canManage` per list |
| `POST /systems/{code}/members` `{list, principalKind, principalId, validFrom, validTo, note}` | members manage |
| `POST /systems/{code}/members/{id}/remove` `{reason}` | members manage |
| `GET /systems/{code}/members/user-search?search=` | members manage |
| `GET /systems/{code}/member-roles` · `PUT /systems/{code}/member-roles` | `access.role.read` / `access.role.manage` |

Errors are Problem Details with message keys (`access.errors.*`).

### Notifications
Templates `access.members.added` / `access.members.removed` (system `access`, AR / EN, editable in Settings → Notifications): sent once
to an added / removed **person** in their language (system, list, dates / reason). Units and everyone are audited only. Queued in the
outbox inside the change's transaction.

### Audit
Every change is an ordinary EF change of `role_assignment` / `system_access` (audit interceptor, channel of the request) with the reason;
the configuration row is auditable too.

### Screens
- **Settings → Members** (`/settings/members`): the systems the user can manage; **`/settings/members/$system`**: sections
  Administrators, Users and one per extra role, each with *Add* (dialog: a person through search, a unit through the unit picker — sub-units
  included —, or all employees; optional from / to; note) and rows with principal, period, state and *Remove* (confirm dialog + optional
  reason); empty states; *Advanced permissions* link to the matrix (`/settings/access/roles?system=`) for holders of `access.role.read`;
  platform administrators edit the member-role configuration there (dialog).
- Each system's settings page shows a small link bar *Members · Advanced permissions*; the Settings navigation entry *Members* is visible
  with `access.assignment.manage` or any `<system>.members.manage`, so a delegated administrator sees Settings with this page only.
- The existing settings entry "Roles & permissions" is renamed "Advanced permissions" / «الصلاحيات المتقدمة».

### Tests
Integration (`tests/Strategy.IntegrationTests/Access/MembersTests.cs`): delegated Portfolio administrator adds a person, a unit (a sub-unit
user gets access) and everyone; KPI members 403; platform settings / role permissions / member-role configuration 403; platform role via
Members refused; removal ends access; notification once in the person's language; audit entries; vendor refused; unknown system 404.
Unit: every catalogue's default mapping resolves to its seeded roles with disjoint lists. Vitest: page and dialog. Playwright
`web/e2e/members.spec.ts` (ar + en).

## §2 Responsibilities — see stream D2
