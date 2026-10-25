# 0009 — Interim access: signed-in users and a system-administrator flag

- **Status:** Accepted (Phase 1, temporary until Phase 2). Supersedes the environment-based behaviour of [0005](0005-phase-0-access-placeholder.md).
- **Context:** Phase 1 brings sign-in and admin screens (users, organisation, identity providers). Roles, data scopes and the permission catalogue arrive in Phase 2. Until then, these screens still need a server-side rule that also works in Production. The owner chose this option during Phase 1 planning.

## Decision
`InterimAccessService` replaces `PlaceholderAccessService` and is registered in every environment.

| Caller / permission | Result |
|---|---|
| Not authenticated | refused (401 from the authentication fallback, never reaching handlers) |
| In-process system principal (`StrategyClaims.CreateSystemPrincipal()`: seeding, `--create-admin`, later scheduled jobs) | allowed; never issued to an HTTP caller |
| Vendor principal (Phase 11) | refused |
| `*.read` permissions of `org` and `identity` | allowed for any signed-in internal user |
| any other permission of `org` / `identity` (`*.manage`) | allowed only for users with `is_system_admin` |
| other systems' permissions (only `sample.*` today) | allowed for signed-in users in Development / Testing; refused elsewhere (unchanged fail-closed default) |

- `ApplyScope` returns the query unchanged when `CanAsync` would allow the permission, and an empty query otherwise.
- The flag reaches the principal as claim `strategy:sysadmin`. It is issued at sign-in and refreshed when the security stamp is validated, so removing the flag takes effect within 5 minutes or immediately after "revoke sessions".
- Self-service endpoints (`/me/*`, own delegations) check ownership in the handler. Another user's record returns 404 (CLAUDE.md §7).
- The bootstrap admin is created by `Strategy.Host --create-admin`, or by the Development seed.

## Consequences
- Phase 2 maps `is_system_admin` to a seeded "System administrator" role, drops the column, and replaces this class behind the same `IAccessService` contract.
- The authorization tests written now (anonymous → 401; non-admin → 403; other user's record → 404) stay valid in Phase 2.
