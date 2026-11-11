# Phase 22 — Integrations for analytics and other systems: summary

Status: **built — awaiting owner acceptance** · version stays **1.0.0** (tag at the end of Phase 23).
Requirements: [`docs/SPEC-ADDENDUM.md`](../SPEC-ADDENDUM.md) §F (and §B for the existing source systems) · plan / design note:
[`docs/design/integrations.md`](../design/integrations.md), written and committed before the code. This session ran without an interactive
approval step, so the design note was written first and then implemented as it stands; its open choices are ADRs **0190–0195** (proposed).
Guide for administrators and developers: [`docs/guides/en/integrations.md`](../guides/en/integrations.md) ·
[`docs/guides/ar/integrations.md`](../guides/ar/integrations.md).

**Phase 21 coordination.** Phase 21 (Reporting 2.0) is built in a parallel session and was not merged when this phase started, so this phase
defines the minimal shared **reportable record contract** (ADR 0190) in one *new* file, `Strategy.Platform.Reporting.Contracts/Records.cs`,
plus a helper in a new folder `Strategy.Platform.Reporting/Records/`. No existing Reporting file was changed; the names do not collide with
Phase 17's dataset types. When the branches meet, Phase 21's report types can adopt the contract (fields, relationships, neutral filters,
joins in the consumer) or be mapped onto it. Cross-system `$expand` is left to Phase 21's report-type layer.

## What was built

| Area | Result | ADR |
|---|---|---|
| **Reportable record contract** | `IReportableRecordSource` per module: record types (code, entity set, AR/EN labels, read permission), fields (type, field permission, lookup type, fixed choices), relationships (name, target, source / target field, many), implicit `id` + `modifiedAt`, a "deleted" condition per type; `RecordQuery` with a neutral filter tree (and / or / not / compare / text / in), sort, offset or keyset paging, total, changed-since, mode current / deleted. `RecordTypeQuery<T>` runs it on the module's **own scoped reader** in its own DbContext (parameterised SQL, no cross-schema SQL) | 0190 |
| **Module sources** (all 11 systems + Sample) | Portfolio `Projects` ↔ `Milestones`, `ProgramRequests` ↔ project · KPI `KpiCards` ↔ `KpiResults` · Risk `Risks` · Teams `Teams` ↔ `TeamMembers` · Requests · Document Registry · IP · Benchmarking `Studies` ↔ `StudyTopics` · CAP `Sources` → `Remarks` → `Actions` · Audit `Reports` → `Findings` → `Recommendations` · Tasks `Tasks` (parent / subtasks) · Sample `Items`. Each on the same reader and permission as the module's screens and Phase 17 dataset; Portfolio cost fields need `portfolio.project.costs` and are masked per row | 0190 |
| **Integration clients** | Settings → Integrations → **Clients**: named service accounts (an `identity.user` of principal type `integration`, no password, never listed with people), **API key** (`sik_…`, Bearer or HTTP Basic `client_id:key` for BI tools) or **OAuth 2.0 client credentials** (OpenIddict confidential application, scopes `feeds` / `api.read`), allowed systems and record types, **acting unit**, **IP allow-list** (CIDR), **rate limit** per minute, **expiry**, **rotation** (API keys overlap 0–30 days), revoke for good, **usage log** (every call, refusals with reason, rows read; 90-day retention). **Permissions and data scope = role assignments of the service account** through the existing access model; the access snapshot of a client counts only its own assignments, resolves Unit scopes against the acting unit and intersects systems with its allowed systems — one branch in the loader, no second access path. Every call re-checks client, credential and account; audit channel `api` | 0191 |
| **OData v4 feeds** | `/odata/v1/{system}`: service document, `$metadata` built **for the caller**, collections, single entity, `/$count`; `$select`, `$filter` (eq ne gt ge lt le and or not in contains startswith endswith null), `$orderby`, `$top`, `$skip`, `$count`, `$expand` along relationships with nested `$select / $filter / $orderby / $top`; server paging with `@odata.nextLink` (page size in Settings); codes always included plus `…Label` in `lang=ar|en`; every row **and every expanded row** filtered through its own source with the client's scope; field permissions remove fields from the model and refuse them in queries. Parser and CSDL from **Microsoft.OData.Core 8.2.3 (MIT)**, execution in-house; unsupported options → OData error 400. 401 carries `WWW-Authenticate: Basic` + `Bearer` so Excel / Power BI prompt | 0192 |
| **Incremental extracts** | `/api/v1/integration/extracts/{system}/{set}` (changed since) and `…/deleted` (cancelled / withdrawn / inactive), keyset cursor over (modifiedAt, id), watermark, JSON or CSV (BOM, formula-injection guard, cursor headers). **Scheduled exports** to a sub-folder of the operator's `Integration:ExportRoot` (traversal refused), run as the client with a watermark per record type (job `integration.export`). SFTP / S3 targets deferred (sync the folder or pull the API) | 0193 |
| **Webhooks** | Modules register integration events as webhook events (`AddWebhookEvent<TEvent>`): `portfolio.project.closure-approved`, `portfolio.project.stage-changed` *(new event)*, `kpi.result.approved`, `kpi.result.target-missed`, `risk.risk.rating-changed` *(new)*, `risk.kri.breached`, `risk.control.ineffective`, `cap.remark.status-changed`, `cap.action.completed`, `auditmgmt.finding.closed`, `auditmgmt.finding.linked-to-risk`, `docreg.document.published`, `teams.membership.changed`, `tasks.task.completed` *(new)*, `requests.request.decided` *(new)*. Subscriptions per client (HTTPS URL checked by the **outbound URL policy** on save and at connect time, events, encrypted signing secret shown once). Fan-out only to active clients that **may read the record**; one delivery per (subscription, event) even when the outbox retries. JSON payload `{id, type, occurredAt, system, resource, data}`, **HMAC-SHA256 signature with timestamp**, retries with exponential back-off (base 60 s, ×2, ≤ 6 h, 8 attempts) on `IClock`, **dead-letter list**, **replay**, delivery log with every attempt, *Send test* | 0194 |
| **Screens** (AR + EN) | Settings → Integrations → **Clients** (list, create with one-time secret dialog), client page tabs **Details · Permissions · Credentials · Webhooks · Delivery log · Usage · Scheduled exports**; **Webhook deliveries** (all clients, dead letters, replay, attempt detail); **Developer portal** (OData feeds and the viewer's metadata, extracts, webhook catalogue with sample payloads and signature verification sample, **Connect Power BI** guide, API and authentication) | 0195 |
| **Shared-file changes** (minimal, additive) | `StrategyClaims` (integration claims + `IntegrationScopeOf`), access loader branch, `TokenScopeRules` (client rules; personal tokens refused on feeds), default scheme selector (`sik_` / Basic → `integration`), OpenIddict client-credentials flow and token handler branch, MCP client list hides integration clients, `ServiceAccountRoles` façade over the internal assignment administration, outbox message names of generic handlers shortened (`IntegrationEventSubscription.MessageType`), `/odata` excluded from the SPA fallback, `RequireIntegrationClient()` endpoint metadata | — |

## Requested tests

| Requirement | Evidence |
|---|---|
| A client reads only its allowed systems and scope (OData and extracts) | Integration `IntegrationClientTests.A_client_reads_only_its_allowed_systems_and_unit_scope_through_odata_and_extracts`: unit A's items only (B never returned, not even by name), other system 404, field needing `sample.item.update` absent from metadata and refused in `$select`, person refused on `/odata` (403), anonymous 401 with the Basic challenge, no REST / write access without the scope, audit `integration.feed.query` with channel `api`, usage row with the row count |
| Expired / revoked clients refused; rotation | `Expired_revoked_clients_and_ended_credentials_are_refused_and_rotation_overlaps`: wrong key 401, Bearer and Basic accepted, rotation overlap (both keys, then only the new one after the overlap on the fake clock), expiry after 365 days, revoked → 401 at once and logged with reason, no rotation after revoke (422) |
| Rate limit, IP allow-list | `Rate_limit_and_ip_allow_list_are_enforced`: 3rd call in the minute → 429 with `Retry-After`, next minute OK; allow-list without the caller → 403; bad CIDR refused on save. Unit: fixed windows, CIDR v4 / v6 / mapped |
| OData `$filter` / `$expand` / paging correct and permission-filtered | `OData_filter_select_orderby_expand_paging_and_count_are_correct_and_permission_filtered` (comparisons, `in`, `not`, `or`, `$orderby desc`, `$select` with label keeping its code, `$count`, two pages through `@odata.nextLink`, `$expand` with nested options, an expansion the client may not read returns an empty list, single entity, `/$count`, `$apply` / label filter refused, unknown key 404). Unit `IntegrationFeedRulesTests`: translation of every operator to the neutral tree, literal flipping, refused constructs with codes, select / expand / orderby / paging parse, CSDL (namespace, key, labels, Edm.Date, navigation) |
| OAuth client credentials | `OAuth_client_credentials_tokens_read_the_feeds_and_nothing_else`: token from `/connect/token`, feeds read, `/mcp` 403, writes 403, wrong secret / unheld scope → no token, the client never appears in Settings → MCP clients, revoke invalidates the token at once |
| Deleted feed lists cancelled / inactive records; changed since | `Changed_since_extracts_page_with_a_cursor_and_the_deleted_feed_lists_deactivated_records` (one row per page with the cursor, no gaps or repeats; deactivated item in `/deleted`; CSV with Arabic labels and the watermark header; bad cursor 400) |
| Webhook signature, retries and dead-letter with the fake clock, replay, SSRF guard | `Webhooks_are_signed_retried_with_backoff_dead_lettered_replayed_and_only_for_readable_records`: 169.254.169.254 and fe80:: refused on save, unknown event refused; only the record the client may read becomes a delivery, published twice → one delivery; 503 then 200 after 60 s; signature verifies with the shown secret (and not with another); 8 failures with delays 60, 120 … 3 840 s then dead; attempt log of 8; replay delivers. Unit: signature, timestamp tolerance, back-off cap. (Connect-time refusal is the Phase 18 `OutboundUrlPolicy` handler, used by the webhook transport) |
| Scheduled exports | `Scheduled_exports_write_the_changes_since_the_last_run_into_the_export_folder_as_the_client` (traversal refused; file contains only in-scope rows) |
| Every module's record types run | `RecordSourceSmokeTests`: every record type of every module with all fields, a filter on every field type, sorting with total, keyset changed-since and the deleted mode, on PostgreSQL |
| **Power BI connects to the OData feed** | **Documented manual check** (no Power BI in CI): guide §3 (`docs/guides/{en,ar}/integrations.md`) and the portal's *Connect Power BI* tab. The E2E spec reads the feed with **HTTP Basic credentials exactly as Power BI's OData connector sends them** and captures the service metadata and a feed page (`*-odata-metadata-basic.png`, `*-odata-feed-basic.png`). Owner check: Power BI Desktop → OData feed → `https://<server>/odata/v1/portfolio` → Basic (client ID / API key) → Projects + Milestones load, relationship on `id` = `projectId` |

## Quality gates (local runs in the build sandbox: SDK 10.0.114, PostgreSQL 16, Chromium 1243)

| Gate | Result |
|---|---|
| `dotnet build` (Host, Release) · `dotnet format --verify-no-changes` | 0 warnings, 0 errors · clean |
| Unit tests | **2 186 / 2 193** — 36 new (`IntegrationFeedRulesTests`); the 7 failures are the OCR tests, which need Tesseract (not installed in this sandbox; CI installs it, CLAUDE.md §18) |
| Architecture tests | **32 / 32** (Microsoft.OData.Core / Edm / Spatial MIT, listed in `THIRD_PARTY_LICENSES.md`; module → Platform.Integration references allowed) |
| Integration tests (real PostgreSQL) | Full suite **2 867 / 2 869** on the first run; the 2 failures were expected updates (job dashboard list gains the 3 integration jobs; extract endpoints needed endpoint metadata `RequireIntegrationClient`) — fixed and both classes re-run green with the 8 Phase 22 tests (**14 / 14**); `RecordSourceSmokeTests` green |
| OpenAPI document · TS client | regenerated (`npm run api:generate`), no drift |
| `npm run lint` · `format:check` · `typecheck` | clean (incl. the RTL logical-class check) |
| `npm test` (Vitest) | **464 / 464** (the server message-key coverage test caught two invented keys; replaced by the existing `validation.inclusiveBetween` / `validation.invalid`) |
| `npm run build` | OK |
| E2E `e2e/integrations.spec.ts` (ar + en) | **2 / 2**; screenshots in [`docs/screenshots/phase-22/`](../screenshots/phase-22/) |

Not run in this sandbox: the full E2E / accessibility suite, visual regression, `npm audit` / `check-vulnerable.sh` (no new npm package;
one NuGet family added, MIT) — CI runs them on the pull request.

## Screenshots (Arabic and English, `docs/screenshots/phase-22/`)
`integration-clients`, `integration-client-editor`, `integration-client-secret`, `integration-client-permissions`, `integration-webhook-editor`,
`integration-webhooks`, `integration-delivery-log`, `integration-deliveries`, `developer-portal`, `developer-portal-webhooks`,
`developer-portal-power-bi`, `odata-metadata-basic` and `odata-feed-basic` (the feed as Power BI's OData connector reads it with Basic
credentials — the documented substitute for a live Power BI screenshot).

## How to demo
1. Settings → Integrations → **Integration clients → New client**: API key, systems Portfolio + Sample, save, copy ID and key.
2. **Permissions** tab: tick the viewer roles → Save. **Webhooks** tab: new subscription (`https://…`), *Send test*; **Delivery log** shows it.
3. `curl -u ic_…:sik_… https://<server>/odata/v1/portfolio/Projects?$top=5&$expand=milestones&lang=ar`, or Power BI → OData feed (guide §3).
4. `curl -u ic_…:sik_… "https://<server>/api/v1/integration/extracts/portfolio/Projects?changedSince=2026-01-01T00:00:00Z"`, then `/deleted`.
5. Settings → Integrations → **Developer portal**: metadata, webhook catalogue with sample payloads, Power BI guide.

## Decisions (ADRs, proposed)
0190 reportable record contract · 0191 integration clients and service accounts · 0192 OData with Microsoft.OData.Core · 0193 extracts and
scheduled exports · 0194 signed webhooks · 0195 developer portal.

## Deviations and known gaps
- **SFTP and S3 export targets deferred** (ADR 0193): the folder export plus the REST extract cover the need without storing more remote
  credentials; operators sync the folder.
- **OAuth secret rotation has no overlap** (OpenIddict keeps one secret per application); API keys overlap up to 30 days.
- **Cross-system `$expand`** is not offered (relationships are within a system); Phase 21's report types cover cross-system joins.
- `risk.event.logged` is not offered as a webhook: risk events are not a linkable record type, so the per-record visibility check cannot be
  made. `cap.remark.status-changed` fires on creation and on every status change of a remark (there is no separate "raised" event).
- Rate-limit counters are in memory (single-instance host, as for MCP).
- Usage-log and delivery retention use `ExecuteDelete` on technical log tables (not business records, not entity-audited).
- A live Power BI connection is a manual owner check (above).
