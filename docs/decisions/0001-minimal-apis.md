# 0001 — Endpoint style: Minimal APIs grouped per module

- **Status:** Accepted (Phase 0)
- **Context:** CLAUDE.md §4.1 asks to pick Minimal APIs or controllers in Phase 0 and use one style everywhere.

## Decision
Use **ASP.NET Core Minimal APIs**. Each module maps its endpoints in `Api/` under a route group created by
`MapModuleGroup(module)` → `/api/v1/<module>`. Handlers are small static methods that build a command or query,
send it through the dispatcher (ADR 0003) and return typed results
(`Results<Ok<T>, ProblemHttpResult>`, `Results<Created<T>, ProblemHttpResult>`).

Conventions every endpoint follows:
- `.RequirePermission("<system>.<object>.<action>")` or `.AsPublic()`. An integration test fails the build when an `/api` endpoint has neither.
- `.WithName("<OperationId>")`: the name becomes the operation id in OpenAPI and in the generated TS client.
- Errors are `Error` → RFC 9457 Problem Details (`code`, `messageKey`, `params`, `errors[]`) through `ProblemResults`.
- State changes are intent-named commands (`POST /items/{id}/deactivate`), not status PATCHes (CLAUDE.md §11).

## Why
- Fewer moving parts than MVC (no model binding conventions, filters or controller discovery to learn); faster startup.
- Typed results give an exact OpenAPI document, which drives the generated client (ADR 0004).
- Endpoint filters and metadata cover the cross-cutting needs (permission, module switch) in one place.

## Consequences
- No `[ApiController]` automatic 400s: validation runs in the dispatcher pipeline instead, which also covers MCP and jobs.
- Query parameters are declared as handler parameters (camelCase names), not `[AsParameters]` records with PascalCase names, so the public contract stays camelCase.
