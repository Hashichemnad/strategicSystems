# 0016 — Roles replace the interim system-administrator flag

- **Status:** Accepted (Phase 2). Supersedes [0009](0009-interim-system-admin-access.md).
- **Context:** ADR 0009 used `identity.users.is_system_admin` until roles existed. Phase 2 brings system access, roles with scope and record roles.

## Decision
- **Administrator roles:** each platform area has a seeded, built-in administrator role that requires MFA:
  - `org.administrator`
  - `identity.administrator`
  - `access.administrator` (locked: its permissions cannot be removed and it cannot be deactivated)
  - `settings.administrator`
- **Migration:** the Identity migration `DropSystemAdminFlag` inserts a `user` assignment of all four roles for every user with the flag, then drops the column. The `access` schema is migrated first because platform components migrate in catalogue order, with Access before Identity.

  This is the only cross-schema statement. It is a one-off data migration, not a runtime dependency (no foreign key).
- **"Administrators" for the MFA policy** means holders of any role with `requires_mfa`.
- **Last administrator:** the last active holder of `access.administrator` cannot be removed, ended or deactivated.
- **Everyone reads the organisation:** `org.viewer` (unit and employee read, scope all) is assigned to `everyone`, as in Phase 1. Administrators can change it.
- **Development / Testing** no longer open modules to every signed-in user. The development seed gives the demo users roles and system access. Integration tests grant roles explicitly.
- **`--create-admin`** assigns the four administrator roles.

## Consequences
- **Removed:**
  - the `strategy:sysadmin` claim, `IsSystemAdmin()` and `InterimAccessService`;
  - `isSystemAdmin` in `/me` and in user creation;
  - the `set-admin` endpoint.
- **Phase 1 tests:** their 401 / 403 / 404 expectations still hold. Their set-up now uses role grants.
