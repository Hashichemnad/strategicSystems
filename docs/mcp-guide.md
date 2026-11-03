# Connecting Claude and other MCP clients

The Integrated Strategy System includes a **Model Context Protocol (MCP) server**. An MCP client — Claude Desktop, claude.ai,
Claude Code or any other MCP-capable assistant — can connect to your installation and use its tools to read data: your pending approvals,
your notifications, the status of a record, a search across the records you may see.

**The assistant always acts as the person who connected it.** It sees exactly what that person sees in the application, never more. Every
call is written to the audit log (channel `mcp`) with the person, the client and the tool.

Contents: [1. Before you start (administrator)](#1-before-you-start-administrator) ·
[2. Claude Code](#2-claude-code) · [3. Claude Desktop and claude.ai](#3-claude-desktop-and-claudeai) ·
[4. Other MCP clients](#4-other-mcp-clients) · [5. Tools](#5-tools) · [6. Security and control](#6-security-and-control) ·
[7. Troubleshooting](#7-troubleshooting)

---

## 1. Before you start (administrator)

1. **Serve the application over HTTPS** with its public address, e.g. `https://strategy.example.gov.ae`. OAuth sign-in for MCP clients
   requires HTTPS. Behind a reverse proxy, let the application see the original address (`FORWARDED_HEADERS_ENABLED`, see
   [`deploy/README.md`](../deploy/README.md#ai-and-the-mcp-server-phase-5)).
2. Sign in as an administrator and open **Settings → MCP server**:
   - switch **Turn on the MCP server** on;
   - note the **Server address** — `https://<your host>/mcp`. This is the address users give their client;
   - under **Sign-in for MCP clients**, keep **Let clients register themselves** on and check **Allowed redirect hosts**. The defaults allow
     Claude Code and other local clients (`localhost`, `127.0.0.1`) and claude.ai / Claude Desktop (`claude.ai`, `claude.com`). Remove
     the hosts you do not want to allow;
   - choose token lifetimes (default: access 60 minutes, stay connected 30 days) and the call limit per user;
   - review the **Tools** table. Read tools are on. The **approve** tool (`approval_decide`) is **off**; turn it on only if your policy allows
     people to record their own approval decisions from an AI client.
3. Optional: decide which data may reach AI clients in **Settings → AI → Data sharing** (systems and fields never sent). These exclusions
   also apply to the `get_record` tool.

Users need nothing else: their existing account, roles and data scopes apply. Vendor (Wasel) accounts cannot use MCP.

## 2. Claude Code

Claude Code runs on the user's computer, so it works for installations reachable only inside your network.

```bash
claude mcp add --transport http strategy https://strategy.example.gov.ae/mcp
```

Then, in Claude Code, run `/mcp`, choose **strategy** and **Authenticate**. Your browser opens the installation's sign-in page (with MFA
if your account uses it), then asks **"Allow Claude Code to use your account?"**. Choose **Allow**; the browser returns to Claude Code.

Alternative without the browser flow — a **personal access token** (Profile → Access tokens, scope **AI assistants (MCP)**):

```bash
claude mcp add --transport http strategy https://strategy.example.gov.ae/mcp --header "Authorization: Bearer sst_xxxxxxxx_xxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxx"
```

Keep the token secret; revoke it in your profile when it is no longer needed.

## 3. Claude Desktop and claude.ai

Custom connectors are added in **Settings → Connectors → Add custom connector** (organisation owners of a Team or Enterprise plan may have
to allow custom connectors first). Enter a name (e.g. *Strategy*) and the server address `https://strategy.example.gov.ae/mcp`, then
**Connect**: you sign in to the installation and allow the connection, as above.

> **Network note.** Remote connectors added in claude.ai or Claude Desktop are reached **from Anthropic's cloud**, so the installation's
> `/mcp`, `/connect/*` and `/.well-known/*` addresses must be reachable from the internet over HTTPS. If your installation is only reachable
> inside your network, use Claude Code (section 2) or another client that runs on the user's computer.

## 4. Other MCP clients

Any client that supports the MCP **Streamable HTTP** transport works:

| Setting | Value |
|---|---|
| Server URL | `https://<your host>/mcp` |
| Authentication | OAuth 2.1, discovered automatically from the server, **or** a personal access token with the MCP scope as `Authorization: Bearer sst_…` |
| Protected resource metadata | `https://<your host>/.well-known/oauth-protected-resource/mcp` |
| Authorization server metadata | `https://<your host>/.well-known/oauth-authorization-server` (also `/.well-known/openid-configuration`) |
| Client registration | dynamic (RFC 7591) at `https://<your host>/connect/register`; public clients with PKCE (S256); redirect URI `https`, or `http` on `localhost` / `127.0.0.1`, on an allowed host |
| Scope | `mcp` (a refresh token is issued with it) |

A client that cannot register dynamically can use a personal access token instead.

## 5. Tools

| Tool | What it does | On by default |
|---|---|---|
| `whoami` | the person the connection acts as (user, employee, unit) | yes |
| `my_pending_approvals` | approval tasks waiting for you (including delegated ones), with links | yes |
| `my_notifications` | your latest in-app notifications | yes |
| `record_status` | where a record stands, who acts next, the actions available to you | yes |
| `search_records` | records you may see, by number or title (Arabic or English) | yes |
| `get_record` | the fields of one record you may see (fields excluded from AI are left out) | yes |
| `approval_decide` | records **your own** decision (approve / reject / return) on one of your approval tasks | **no** — administrator choice |

Each system adds its own tools as it is delivered (projects, KPIs, risks…); they appear in **Settings → MCP server** with the same rules:
read tools on, write tools off until enabled, approve tools never on by default.

Examples to ask Claude once connected: *"What approvals are waiting for me?"*, *"Find the sample items about the digital archive and tell
me their status"*, *"Do I have unread notifications?"*.

## 6. Security and control

- **Same permissions as the application.** Every tool runs through the same access checks as the screens. A record outside your scope is
  answered as "not found".
- **Consent.** The first connection of each client asks the person to allow it. A remembered consent can be withdrawn by an administrator
  (Settings → MCP server → Connected clients → **Disconnect**), which stops the client's tokens at once for everyone.
- **Tokens.** Access tokens are short-lived (default 60 minutes); refresh tokens keep a client connected (default 30 days). A deactivated or
  locked account loses access immediately. OAuth tokens issued to MCP clients work only on `/mcp`, never on the REST API.
- **Limits.** Calls per user per minute (all tools, and per tool if set). Exceeding them answers `rate_limited`.
- **Audit.** Every tool call — and every refused one — is in the Audit Log with channel `mcp`, the person, the client (OAuth client id or
  the personal access token's prefix), the tool, its arguments (shortened) and the outcome. Client registrations, consents and
  disconnections are audited too.
- **AI does not decide.** The server never approves anything on its own; `approval_decide` only records a decision the person asked their
  client to make, under their own account, and only if an administrator enabled it.

## 7. Troubleshooting

| Symptom | Cause and fix |
|---|---|
| `404` on `/mcp` | The MCP server is off: Settings → MCP server → **Turn on the MCP server**. |
| `401` and the client does not start sign-in | The client does not support OAuth discovery: use a personal access token with the MCP scope. |
| `403 auth.tokenScope` | The personal access token lacks the **AI assistants (MCP)** scope: create a new token with it. |
| `403` for a supplier account | Vendor (Wasel) accounts cannot use MCP. |
| Sign-in page says the request is not valid | The redirect host is not allowed (Settings → MCP server → Allowed redirect hosts) or the client registration was removed; reconnect from the client. |
| OAuth error mentioning HTTPS, or `http://` addresses in the metadata | The application does not see the original HTTPS address: enable forwarded headers behind the proxy (deploy/README.md). |
| A tool answers `disabled` | An administrator turned the tool off, or its system is switched off. |
| A tool answers `not_found` | The record does not exist or is outside your access. |
