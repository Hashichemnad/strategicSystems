# 0031 — Tool registry shared by the MCP server and "Ask the system"

- **Status:** Accepted (Phase 5)
- **Context:** SPEC §0.5: each system registers its own MCP tools (plug-in pattern); read tools on by default, write tools opt-in, approve /
  reject never by default; every call acts as the signed-in user. "Ask the system" needs the same read capabilities for the model.

## Decision
- The contract lives in `Strategy.Platform.AI.Contracts` (namespace `Strategy.Platform.AI.Tools`), so modules reference contracts only:
  `ToolDefinition(Name, SystemCode, Kind Read|Write|Approve, TitleAr, TitleEn, Description, InputSchema, Permission?, DefaultEnabled)` and
  `IAppTool.InvokeAsync(arguments, ToolContext(User, Channel, Language))` returning `ToolResult` (data or a stable error code: `not_found`,
  `forbidden`, `validation`, `disabled`, `rate_limited`, `failed`). Registered with `services.AddAppTool<T>()` from each component's or
  module's `Mcp/` folder.
- `ToolCatalog` refuses at start-up an **approve tool marked default-enabled** and duplicate names. A tool is available to a user when it is
  internal, the tool's system is enabled and the user holds the tool's permission; the tool itself checks each record (out of scope →
  `not_found`, the 404 semantics).
- `ToolExecutor` runs a tool and writes the audit event `<channel>.tool.call` (`mcp` or `ai`): tool, client, arguments (truncated to 500
  characters), outcome, duration.
- **MCP** adds administrator switches and rate limits (ADR 0032); write and approve tools are listed only when an administrator enables them.
  **Ask the system** gets read tools only, as Microsoft.Extensions.AI functions (at most 5 rounds); write / approve tools are never offered to
  the model.
- Core tools: `whoami` (MCP), `my_notifications` (Notifications), `my_pending_approvals`, `record_status`, `approval_decide` — approve,
  off (Workflow), `search_records`, `get_record` (AI, over modules' search and record sources).

## Consequences
- Platform components Workflow and Notifications reference `AI.Contracts` (contracts only). Modules expose data to search and AI by
  implementing `IRecordSearchSource` / `IAiRecordSource` once.
