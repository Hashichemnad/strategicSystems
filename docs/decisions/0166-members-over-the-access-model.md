# 0166 — Members page over the existing access model

- **Status:** Proposed (Phase 19, stream D1) — awaiting owner acceptance
- **Context:** SPEC-ADDENDUM §D1 asks for a simple *Members* screen per system (administrators, users — people, units incl. sub-units,
  everyone —, optional extra roles) without the permission matrix. The access model (ADRs 0015, 0016) already has everything needed:
  roles with scopes, role assignments with principal kinds user / unit / everyone and dates, system-access grants, cache invalidation, the
  last-administrator guard, transfers and the access review. A second model would split the truth.

## Decision
- **No new access rule and no new grant type.** A member list is a *view* over role assignments and system-access grants of one system;
  adding / removing calls `AccessAdministration` (`CreateAssignmentAsync`, `EndAssignmentAsync`, `GrantSystemAccessAsync`,
  `RevokeSystemAccessAsync`) inside one transaction, so validation, overlap rules, the last-administrator guard, auditing and the
  access-change signal are the existing ones.
- **Which roles a list means is configuration per system.** Code defaults come from each catalogue (`IAccessCatalogueSource.MemberRoles`,
  a `SystemMemberRoles` record in `Strategy.Platform.Access.Contracts`: administrator, user and extra role codes chosen from the roles the
  module already seeds). A default interface member keeps the declaration next to the seeded roles and needs no extra DI registration. The
  platform administrator overrides it per system (`access.system_member_roles`, auditable, jsonb code lists); the table in
  `docs/design/members-responsibilities.md` §1 lists the defaults.
- **Mapping.** Administrators → the administrator role(s) + system access; Users → system access + the user role(s); extra list → that
  role + system access. Platform-kind systems (Teams, Tasks) take no system-access grant (the existing rule), so their lists are
  assignments only.
- **Removal is ending, never deleting**: assignments end yesterday (an unstarted one before it starts); the principal's own grant is revoked
  when they hold no other current or future role of the system. The optional reason is the ambient audit reason of the change.
- **Dates**: optional from / to on the assignments. Grants have no dates in the model and are given at once.
- **Notifications**: `access.members.added` / `access.members.removed` to an added / removed person, once, in their language; units and
  everyone are only audited (no mass e-mail).

## Consequences
- The permission matrix, assignments and system-access screens stay as *Advanced permissions* and show the same rows.
- A grant created by the Members page outlives an assignment that ends by its `validTo` date; such a principal can open the system but
  holds no role of the list (record roles still apply). Removing them from the Users list revokes it.
- Removing someone from Administrators / an extra list also revokes system access they had through a direct grant when no other role of
  the system remains, including access that was only used for record roles; the person is added back as a User when still needed.
