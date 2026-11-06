# Connecting Claude and other MCP clients

For users and administrators. Arabic version: [`../ar/mcp.md`](../ar/mcp.md). This guide supersedes and extends
[`docs/mcp-guide.md`](../../mcp-guide.md) (kept for reference).

The Integrated Strategy System includes a **Model Context Protocol (MCP) server**. An MCP client — Claude Desktop, claude.ai, Claude Code or
any MCP-capable assistant — can connect to your installation and use its tools: your pending approvals, notifications and tasks, the status
of a record, searches across the records you may see, system summaries, and (only when your administrator allows it) drafts.

**The assistant always acts as the person who connected it.** It sees exactly what you see in the application — never more — and every
call is in the audit log (channel `mcp`) with you, the client and the tool. Vendor (Wasel) accounts cannot use MCP.

## 1. Before you start (administrator)

1. **Serve the application over HTTPS** at its public address (e.g. `https://strategy.example.gov.ae`). OAuth sign-in for MCP clients
   requires HTTPS. Behind a reverse proxy, let the application see the original address (forwarded headers — installation guide /
   `deploy/README.md`).
2. **Settings → MCP server**: switch **Turn on the MCP server** on; note the **Server address** (`https://<host>/mcp`); under **Sign-in for
   MCP clients (OAuth 2.1)** keep **Let clients register themselves** on and review **Allowed redirect hosts** (defaults: `localhost` and
   `127.0.0.1` for Claude Code and local clients, `claude.ai` and `claude.com` for claude.ai / Claude Desktop); choose the token lifetimes
   (access 60 minutes, stay connected 30 days) and **Tool calls per user per minute**.
3. Review the **Tools** table. Read tools are on; **write tools are off** until you enable them; **approve tools** (`approval_decide`,
   `approve_task`) are never on by default — enable them only if your policy allows people to record their own decisions from an AI client.
4. Optional: **Settings → AI → Data sharing and limits** — systems and fields never sent to AI; these exclusions also apply to the
   `get_record` tool.

![MCP settings](../../screenshots/phase-5/en-mcp-settings.png)

## 2. OAuth or a personal access token?

| | OAuth 2.1 (recommended) | Personal access token |
|---|---|---|
| How | the client opens the sign-in page of the installation (with two-step verification) and asks you to **allow** it | **My profile → Access tokens → New token** with access **AI assistants (MCP)**; paste it into the client as `Authorization: Bearer sst_…` |
| Lifetime | access token 60 min, refreshed automatically up to 30 days | the days you choose (default 90, max 365) |
| Revoke | administrator: Settings → MCP server → Connected clients → **Disconnect** | you: **Revoke** in your profile |
| Works where | every client that supports MCP OAuth discovery | clients that allow a custom header (useful when OAuth is not available) |

OAuth tokens issued to MCP clients work only on `/mcp`, never on the REST API. A token with the MCP scope works on `/mcp`.

![Consent](../../screenshots/phase-5/en-mcp-consent.png)

## 3. Claude Code

Claude Code runs on your computer, so it also works for installations reachable only inside your network.

```bash
claude mcp add --transport http strategy https://strategy.example.gov.ae/mcp
```

In Claude Code run `/mcp`, choose **strategy** → **Authenticate**. The browser opens the installation's sign-in page, then asks whether to
allow Claude Code to use your account; choose **Allow** and return to Claude Code.

With a personal access token instead of the browser flow:

```bash
claude mcp add --transport http strategy https://strategy.example.gov.ae/mcp --header "Authorization: Bearer sst_xxxxxxxx_xxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxx"
```

Keep the token secret and revoke it when you no longer need it.

## 4. Claude Desktop and claude.ai

**Settings → Connectors → Add custom connector** (on Team / Enterprise plans an owner may first have to allow custom connectors). Enter a
name (e.g. *Strategy*) and the server address `https://strategy.example.gov.ae/mcp`, then **Connect**: you sign in to the installation and
allow the connection.

> **Network note.** Connectors added in claude.ai or Claude Desktop are reached **from Anthropic's cloud**, so `/mcp`, `/connect/*` and
> `/.well-known/*` of your installation must be reachable from the internet over HTTPS. If your installation is internal only, use Claude
> Code or another client that runs on the user's computer.

## 5. Other MCP clients

| Setting | Value |
|---|---|
| Server URL | `https://<host>/mcp` (Streamable HTTP) |
| Authentication | OAuth 2.1 discovered from the server, or `Authorization: Bearer sst_…` (MCP scope) |
| Protected resource metadata | `https://<host>/.well-known/oauth-protected-resource/mcp` |
| Authorization server metadata | `https://<host>/.well-known/oauth-authorization-server` (also `/.well-known/openid-configuration`) |
| Client registration | dynamic (RFC 7591) at `https://<host>/connect/register`; public clients with PKCE (S256); redirect URI `https`, or `http` on `localhost` / `127.0.0.1`, on an allowed host |
| Scope | `mcp` (a refresh token is issued with it) |

## 6. Tools (release 1.0)

Settings → MCP server shows the authoritative list; a system that is switched off hides its tools.

| System | Read tools (on by default) | Write tools (off by default) |
|---|---|---|
| Platform | `whoami`, `my_pending_approvals`, `my_notifications`, `record_status`, `search_records`, `get_record` | — (approve: `approval_decide`, never on by default) |
| Portfolio | `portfolio_summary`, `portfolio_my_projects`, `portfolio_get_project`, `portfolio_project_milestones`, `portfolio_my_requests`, `portfolio_search_requests`, `portfolio_get_request`, `portfolio_project_backlog`, `portfolio_sprint_summary` | `portfolio_create_draft_request`, `portfolio_update_milestone_draft`, `portfolio_create_story_draft` |
| KPI | `my_pending_kpi_results`, `kpi_card_lookup`, `kpi_approval_queue_summary`, `department_scorecard` | `submit_kpi_result_draft` |
| Risk | `risk_register_search`, `risk_details`, `my_risk_actions`, `risk_heatmap_summary`, `risk_top10`, `risks_outside_appetite` | `update_risk_action_progress_draft` |
| Teams | `my_teams`, `team_details`, `my_pending_evaluations`, `evaluation_status` | `add_meeting_minutes` |
| Tasks | `my_tasks` | `create_task`, `update_task_status`, `add_task_comment` (approve: `approve_task`, never on by default) |
| Requests / Documents / IP | `my_requests`, `request_status`, `search_documents`, `get_document`, `document_expiry_list`, `search_ip_records`, `get_ip_record` | `submit_request_draft` |
| Benchmarking | `bench_studies`, `bench_study_details`, `bench_best_practices` | — |
| CAP | `cap_my_remarks`, `cap_my_actions`, `cap_overdue_actions`, `cap_remarks_by_source` | `cap_update_achievement_draft` |
| Audit Management | `audit_my_finding_status`, `audit_findings_by_department`, `audit_finding_details`, `audit_overdue_findings`, `audit_followups_due` | `audit_record_follow_up_draft` |

Write tools create **drafts** that the person confirms in the application. Examples to ask once connected: *"What approvals are waiting for
me?"*, *"Which of my KPI results are due this month?"*, *"Summarise the top-10 risks"*, *"Which of my projects are delayed?"*.

## 7. Security and control

- **Same permissions as the application** — every tool goes through the same access checks as the screens; a record outside your scope is
  answered *not found*. Confidential tasks, document sensitivity, masked Emirates IDs and hidden cost fields apply here too.
- **Consent** on the first connection of each client; an administrator can **Disconnect** a client (stops its tokens for everyone).
- **Tokens** are short-lived; a deactivated or locked account loses access immediately.
- **Limits** per user per minute (all tools, and per tool if set) → `rate_limited`.
- **Audit**: every call, refused ones too, with person, client (OAuth client id or the token prefix), tool, shortened arguments and outcome;
  client registrations, consents and disconnections too.
- **AI does not decide** — the server never approves anything by itself; an approve tool only records a decision the person asked for,
  under their own account, and only when an administrator enabled it.

## 8. Troubleshooting

| Symptom | Cause and fix |
|---|---|
| `404` on `/mcp` | the MCP server is off: Settings → MCP server → **Turn on the MCP server** |
| `401` and the client does not start a sign-in | the client does not support OAuth discovery: use a personal access token with the MCP scope |
| `403 auth.tokenScope` | the token lacks the **AI assistants (MCP)** access: create a new one |
| `403` for a supplier account | vendor (Wasel) accounts cannot use MCP |
| sign-in page says the request is not valid | the redirect host is not allowed (Allowed redirect hosts) or the client registration was removed: reconnect |
| OAuth error about HTTPS, or `http://` addresses in the metadata | the application does not see the original HTTPS address: enable forwarded headers behind the proxy |
| a tool answers `disabled` | an administrator turned it off, or its system is switched off |
| a tool answers `not_found` | the record does not exist or is outside your access |
