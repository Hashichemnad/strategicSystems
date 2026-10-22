# 0005 — Phase 0 access placeholder that fails closed

- **Status:** Accepted, temporary (replaced in Phase 2)
- **Context:** CLAUDE.md §7 requires every endpoint to go through `IAccessService`. Users and roles arrive in
  Phases 1–2, but Phase 0 must already prove the pattern end to end.

## Decision
- `Strategy.Platform.Access` defines the final contract now: `CanAsync(principal, permission, resource?)` and `ApplyScope(query, principal, permission)`.
- Endpoints already declare permissions (`.RequirePermission(...)`). Handlers already check records (404 outside scope)
  and filter lists with `ApplyScope` **in the database query**.
- The Phase 0 implementation `PlaceholderAccessService` **allows everything only in the Development and Testing environments**.
  In every other environment it **denies everything**: 403 on guarded endpoints, empty scopes, 404 on records.
  The host logs a warning at startup whenever the allow-all mode is active.

## Why
The code paths that Phase 2 fills in are exercised from day one. A production install of this phase cannot expose data by mistake.

## Consequences
- Integration tests prove fail-closed behaviour in `Production`.
- Phase 2 replaces the registration in `AddPlatformAccess` and adds the full negative tests (wrong role, each data scope, record roles).
- Antiforgery is enforced on every state-changing `/api` call now. Token-authenticated clients (PAT, MCP) get their exemption with token authentication (Phases 1 and 5).
