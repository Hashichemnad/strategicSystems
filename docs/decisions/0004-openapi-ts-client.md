# 0004 — OpenAPI → TypeScript client pipeline

- **Status:** Accepted (Phase 0)
- **Context:** CLAUDE.md §11: the OpenAPI document is published at `/openapi`, and the TS client is generated, never hand-written.

## Decision
1. `Microsoft.AspNetCore.OpenApi` builds the document. At runtime it is served at `/openapi/v1.json` (`/openapi` redirects).
2. **At build time**, `Microsoft.Extensions.ApiDescription.Server` writes the same document to `web/openapi/strategy.json` on every Host build. Docker builds turn this off (`-p:OpenApiGenerateDocuments=false`).
3. `openapi-typescript` (MIT) turns it into `web/src/api/schema.d.ts` (`npm run api:generate`).
4. `openapi-fetch` (MIT, ~6 kB) is the runtime client: `api.GET('/api/v1/sample/items', { params: { query } })` is fully typed.
   `web/src/api/client.ts` adds `X-Client: spa`, `X-Correlation-Id`, `Accept-Language` and the antiforgery header.
   `unwrap()` turns failures into `ApiError` carrying the typed `Problem`.
5. Problem Details are one named schema, `Problem` (code, messageKey, params, errors[], traceId, correlationId), on every `/api` operation as the `default` response.
6. Both generated files are committed. CI runs `tools/check-api-client.sh`, which regenerates them and fails on any difference.

## Why
- Types only, plus a tiny fetch wrapper: no runtime generator lock-in, and nothing to hand-write.
- The build-time document comes from the same code as the runtime one, so they cannot drift.

## Consequences
- After changing an endpoint: `dotnet build src/Host/Strategy.Host && (cd web && npm run api:generate)`, then commit both files.
- ESLint forbids raw `fetch(` in `web/` so all calls go through the client.
