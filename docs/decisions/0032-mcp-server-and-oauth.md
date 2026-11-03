# 0032 — MCP server, OAuth 2.1 with OpenIddict, dynamic client registration and consent

- **Status:** Accepted (Phase 5)
- **Context:** CLAUDE.md §13 / SPEC §0.5: MCP server at `/mcp` (official C# SDK, Streamable HTTP), OAuth 2.1 (per the MCP specification) or
  personal access tokens, acting as the user, admin switches, rate limits, every call audited, vendors refused. Claude Desktop / claude.ai
  connectors and Claude Code discover the authorization server and register themselves.

## Decision
- **Transport**: `ModelContextProtocol.AspNetCore` (Apache-2.0), **stateless** Streamable HTTP; `tools/list` and `tools/call` handlers backed
  by the tool registry (ADR 0031). Handlers use the HTTP request's scope, so the request context (token owner, channel `mcp`) is the one the
  audit trail and access checks see.
- **Gate** before the SDK: MCP off (default) → 404 `mcp.disabled`; no **token** → 401 with `WWW-Authenticate: Bearer resource_metadata=…`
  (a browser session is not accepted on `/mcp`); vendor principal → 403 `mcp.vendorRefused`; PAT without scope `mcp` → 403 (existing rule).
- **Authorization server**: OpenIddict (Apache-2.0; Duende is not allowed, CLAUDE.md §4.3) in schema `mcp`: authorization code + **PKCE
  required**, refresh tokens (`offline_access` granted with `mcp`), public clients only, scope `mcp`. **Tokens use ASP.NET Core Data
  Protection** (key ring in PostgreSQL, ADR 0012), so they survive restarts without a certificate; ephemeral signing keys only satisfy
  OpenIddict (no identity token is issued). Validation is local, with **token and authorization entry checks**, so revoking a client takes
  effect at once. Lifetimes from Settings → MCP.
- **Discovery**: `/.well-known/oauth-authorization-server` and `/.well-known/openid-configuration` (same document, plus
  `registration_endpoint`), protected-resource metadata (RFC 9728) at `/.well-known/oauth-protected-resource[/mcp]`.
- **Resource indicators** (RFC 8707): OpenIddict's static resource list is disabled because the URL depends on the installation's host; the
  authorization endpoint accepts only `<this host>/mcp` and stamps it on the token.
- **Dynamic client registration** (RFC 7591) at `POST /connect/register`, on by default while MCP is on: redirect URIs must be `https`, or
  `http` on a loopback host, and the host must be in the allowed list (default `localhost`, `127.0.0.1`, `claude.ai`, `claude.com`; `*.x`
  wildcards and `*` possible). Custom URI schemes are not accepted.
- **Consent**: `/connect/authorize` sends a signed-out user to the SPA login (and back), and a signed-in user without a remembered consent to
  the SPA page `/oauth/consent`. *Allow* calls `POST /api/v1/mcp/consent` (antiforgery-protected) for a **ticket** protected with Data
  Protection, valid 5 minutes and bound to user, client, redirect URI and PKCE challenge; the browser continues to `/connect/authorize` with
  it. The grant is stored as a permanent OpenIddict authorization, so later authorizations skip the page.
- **Principal**: scheme `oauth` re-reads the user on every call (inactive / locked → refused) and builds the same claims as a session plus
  `amr=oauth`, the scopes and the client id. `IsToken()` covers PAT and OAuth, so OAuth tokens (scope `mcp` only) never reach `/api`.
- **Rate limits**: per user per minute for all tools and per tool, fixed windows in memory (the host is single-instance; a multi-node install
  would move the counters to a shared store). `/connect/*` joins the per-IP limiter.
- **Audit**: `mcp.tool.call` per call and refusal, `mcp.client.register`, `mcp.consent.grant`, `mcp.client.revoke`. Revoking a client
  deletes the OAuth client registration with its authorizations and tokens (configuration, not a business record).

## Consequences
- `Mcp:RequireHttps` (default true) enforces HTTPS on OAuth endpoints; behind a TLS-terminating proxy the forwarded headers must reach the
  host (deploy guide). Development and tests set it to false.
- Client ID Metadata Documents (the newer MCP alternative to registration) are not supported yet; clients that need them register
  dynamically or use a personal access token.
