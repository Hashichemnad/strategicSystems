# 0191 — Integration clients: service accounts, credentials and the access snapshot

- **Status:** Proposed (Phase 22) — awaiting owner acceptance
- **Context:** SPEC-ADDENDUM §F: named service accounts for other systems with their own permissions and data scope "in the same access
  model", OAuth 2.0 client credentials (OpenIddict) or API key, allowed systems / record types, IP allow-list, rate limit, expiry,
  rotation, usage log, audit channel `api`; a person's PAT is not used for integrations. CLAUDE.md §7: access is implemented exactly once.

## Decision
- **Client** (`integration.client`) + **service account**: an `identity.user` with `principal_type = integration` (no password, no e-mail, no
  employee; excluded from user search and interactive sign-in). Permissions and data scopes are **role assignments of the service account**
  (principal kind `user`), managed on the client's Permissions tab through `AccessAdministration`.
- The client principal carries `strategy:principal = internal`, the service user id and the claim `strategy:integration`. The access loader
  builds its snapshot with three rules: only its own assignments (no "everyone" / unit-wide assignments or system grants), Unit and
  Unit-and-below scopes resolve against the client's **acting unit**, system access is intersected with the client's **allowed systems**.
  Everything else (`CanAsync`, `ApplyScopeAsync`, `GetScopeAsync`, 404 semantics) is unchanged.
- **Credentials**: API keys `sik_<prefix>_<secret>` (SHA-256 hash, constant-time compare) sent as Bearer or as HTTP **Basic**
  (`client_id:key`, for Power BI / Excel). OAuth: a confidential OpenIddict application per client (grant `client_credentials`, scopes
  `feeds` / `api.read`); the token endpoint delegates to `IIntegrationClientAuthority` (Identity contract, implemented by Integration).
  Rotation issues a new credential; API keys may overlap 0–30 days (default 7); OAuth secrets cannot overlap (OpenIddict keeps one).
- Every call re-checks client status, expiry, credential and service account (revocation is immediate). Gate: IP allow-list (CIDR) → rate
  limit per client per minute (in memory, single instance, like ADR 0032) → path rules per scope (`feeds`: `/odata`, extracts; `api.read`:
  REST GET). Usage log row per call; audit channel `api`. Clients never reach MCP, AI, Wasel or `/api/v1/me`. People cannot use `/odata`.
- A revoked client is never reactivated (create a new one); no hard delete.

## Consequences
Admins reuse the roles they know; a client sees exactly what a person with the same roles in the acting unit would see. Multi-node
installs would move the rate-limit counters to a shared store (as for MCP).
