# Design note — MCP server (`Strategy.Platform.Mcp`, schema `mcp`)

**Phase 5 · SPEC §0.5 "MCP server" · CLAUDE.md §13.** Written before the code; decisions in ADRs 0031–0032.

## 1. Endpoint and transport
- `/mcp`, official C# SDK (`ModelContextProtocol.AspNetCore`), **Streamable HTTP, stateless** (any node can answer, no session affinity).
- Tools are listed and called through `ListTools` / `CallTool` handlers backed by the **tool registry** (not static attributes), so the list
  depends on the caller, the admin settings and the enabled systems.
- A gate in front of the SDK: MCP disabled (Settings → MCP, **off by default**) → 404; anonymous → 401 with
  `WWW-Authenticate: Bearer resource_metadata="<base>/.well-known/oauth-protected-resource/mcp"`; **vendor principals → 403**; personal access
  tokens without scope `mcp` → 403 (existing token-scope rule). Requests carry channel **`mcp`** (request context).

## 2. Authentication (ADR 0032)
1. **Personal access tokens** (Phase 1) with scope `mcp`: `Authorization: Bearer sst_…`.
2. **OAuth 2.1** via OpenIddict (Apache-2.0) as the built-in authorization server:
   - Authorization code + **PKCE (S256, required)**, public clients, refresh tokens; scope `mcp`.
   - Discovery: `/.well-known/oauth-authorization-server` and `/.well-known/openid-configuration` (same document, plus
     `registration_endpoint`), protected-resource metadata (RFC 9728) at `/.well-known/oauth-protected-resource[/mcp]`.
   - **Dynamic client registration** (RFC 7591) at `POST /connect/register` (on by default; redirect URIs must be `https` or loopback `http`,
     and the host must be in the allowed list — default `localhost`, `127.0.0.1`, `claude.ai`, `claude.com`; `*` allows any).
   - `/connect/authorize`: not signed in → the SPA login (`/login?redirect=…`); signed in → the SPA **consent page** `/oauth/consent`
     (client name, redirect host, "acts as you with your permissions"). Allow → `POST /api/v1/mcp/consent` (antiforgery) returns a short-lived
     protected consent ticket bound to user, client, redirect URI and PKCE challenge → the browser continues to `/connect/authorize` with it.
     A remembered consent (permanent OpenIddict authorization) skips the page.
   - Tokens are **ASP.NET Core Data Protection** tokens (keys in PostgreSQL, ADR 0012): they survive restarts and need no extra certificate.
     Lifetimes from settings (access 60 min, refresh 30 days); `offline_access` is granted with `mcp`, so clients get a refresh token.
     Validation checks the token and authorization entries, so revoking a client stops its tokens at once.
   - Resource indicators (RFC 8707): only `<this host>/mcp` is accepted and stamped on the token (the URL depends on the installation).
   - Validation: scheme `oauth` re-loads the user on every call (inactive / locked → refused) and builds the same principal as a session,
     plus `amr=oauth`, the scopes and the client id. OAuth tokens only reach `/mcp` (never the REST API).
3. Either way the call **runs as the token owner** through the same `IAccessService` (CLAUDE.md §7).

## 3. Tool registry (ADR 0031) — `Strategy.Platform.AI.Contracts`, namespace `Strategy.Platform.AI.Tools`
```csharp
public sealed record ToolDefinition(string Name, string SystemCode, ToolKind Kind /* Read | Write | Approve */, string TitleAr, string TitleEn,
    string DescriptionEn, JsonElement InputSchema, string? Permission, bool DefaultEnabled);
public interface IAppTool { ToolDefinition Definition { get; } Task<ToolResult> InvokeAsync(JsonElement args, ToolContext ctx, CancellationToken ct); }
services.AddAppTool<MyTool>();   // each component / module registers its tools in its Mcp/ folder
```
- Defaults: **read on**, write off, **approve off — and an approve tool can never be default-enabled** (registry refuses it at start-up).
- A tool is listed only when MCP is on, the tool is enabled, its system is enabled and the caller holds its permission (if any); the tool
  itself still checks record access (out of scope → `not_found`, i.e. the 404 semantics).
- Results are JSON (structured content + text). Errors are tool results with `isError` and a stable code (`not_found`, `forbidden`,
  `validation`, `rate_limited`, `disabled`).

### Core tools (Phase 5)
| Tool | Kind | Owner | What |
|---|---|---|---|
| `whoami` | read | Mcp | user, employee, unit, roles, accessible systems |
| `my_notifications` | read | Notifications | latest in-app notifications (unread filter) |
| `my_pending_approvals` | read | Workflow | My Work approvals (own + delegated) |
| `record_status` | read | Workflow | current status of a record via its `ICurrentStatusProvider` (404 semantics) |
| `search_records` | read | AI | full-text search over every `IRecordSearchSource` the caller may read |
| `get_record` | read | AI | a record's fields via `IAiRecordSource` (AI data-sharing exclusions applied) |
| `approval_decide` | **approve** | Workflow | approve / reject / return a task assigned to the token owner — **disabled by default** |

## 4. Settings (`settings` area `mcp`, Settings → MCP)
`enabled` (false) · `allowDynamicRegistration` (true) · `allowedRedirectHosts` · `accessTokenMinutes` (60) · `refreshTokenDays` (30) ·
`rateLimitPerMinute` (60 per user) · `tools: { <name>: { enabled?, rateLimitPerMinute? } }`. The page also lists registered OAuth clients
(revoke = tokens and authorizations revoked, client disabled) and shows the endpoint URL with copyable client configuration.
Permissions: `mcp.settings.manage` (role *MCP administrator*, MFA, platform administrator roles).

## 5. Rate limits and audit
- Per user and per tool, fixed one-minute windows in memory (single-instance host, ADR 0032 notes the multi-node caveat) on top of the
  global per-IP limiter (which now also covers `/connect`).
- **Every tool call** is an audit event `mcp.tool.call` (channel `mcp`): tool, client (OAuth client id or token prefix), arguments (truncated,
  secrets never accepted), result status, duration; refusals (disabled, rate limited, forbidden) too. OAuth events: `mcp.client.register`,
  `mcp.consent.grant`, `mcp.client.revoke`.

## 6. Tests
Integration with the real host: PAT with / without `mcp` scope; MCP disabled → 404; vendor → 403; tools list honours enable switches and
permissions; approve tool absent by default; `search_records` / `record_status` / `get_record` never return out-of-scope records (`not_found`);
every call audited with channel `mcp`; rate limit; full OAuth flow (register → authorize with PKCE → consent → token → `/mcp` → refresh);
redirect host refused; OAuth token refused on `/api`. E2E: Settings → MCP and the consent page (screenshots ar + en).
