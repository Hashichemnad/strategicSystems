# Design note — Phase 22: Integrations for analytics and other systems (`Strategy.Platform.Integration`)

**Scope.** SPEC-ADDENDUM §F (wins over SPEC.md): integration clients (service accounts for other systems), read-only **OData v4** analytics
feeds per system, **incremental extracts** (changed since + deleted feed, JSON / CSV, optional scheduled export), **webhooks** (signed,
retried, dead-letter, replay, delivery log, SSRF guard), Settings → Integrations pages and a **developer portal** page with a Power BI guide.
Builds on Phase 5 (OpenIddict, MCP gate), Phase 17 (module datasets read through Contracts, ADR 0140 / 0144), Phase 18 (outbound URL policy,
ADR 0155), Phase 19 (Settings → Integrations → Source systems, external references) and the integration events of ADR 0043.
ADRs **0190–0195**. Phase 21 (Reporting 2.0) is built in parallel; the shared metadata contract is ADR 0190.

## 1. Projects and files (all additive)

| Project | Phase 22 contents |
|---|---|
| `Strategy.Platform.Reporting.Contracts` | **new file `Records.cs`** — the minimal *reportable record* contract (ADR 0190): record types, fields, relationships, a neutral filter tree, `IReportableRecordSource`. No other file changes. |
| `Strategy.Platform.Reporting` | **new folder `Records/`** — `RecordTypeQuery<T>`, the helper modules use to run a `RecordQuery` on their own scoped `IQueryable` (like `DatasetQuery<T>`). |
| Modules (all 11 + Sample) | **new folder `Feeds/`** — one `IReportableRecordSource` per module (its own DbContext and the same scoped readers as its screens) and its **webhook events** (`AddWebhookEvent<TEvent>`). |
| `Strategy.Platform.Integration` | `Clients/` (clients, credentials, service accounts, authentication handler, gate middleware, rate limiter, usage log), `Feeds/` (catalogue, EDM model, OData endpoints, extracts, scheduled exports), `Webhooks/` (catalogue, fan-out handler, deliveries, dispatcher job, signing), new migration in schema **`integration`**. |
| `Strategy.Platform.Identity` | Service accounts (`ServiceAccounts`, principal type `integration`), the bearer classifier for `sik_` keys / Basic credentials, `IIntegrationClientAuthority` (contract the OAuth server calls), token-scope rules for `feeds`. |
| `Strategy.Platform.Mcp` | OpenIddict: **client-credentials flow** (scope `feeds`, `api.read`) delegated to `IIntegrationClientAuthority`; the OAuth handler builds the client principal for those tokens; Settings → MCP lists only MCP clients. |
| `Strategy.Platform.Access` | The access snapshot of an integration principal (ADR 0191): only its own role assignments, its *acting unit*, its allowed systems. One branch in the loader; no second access path. |

## 2. Integration clients (Settings → Integrations → Clients)

### 2.1 Data model (schema `integration`)

**`integration.client`** (`[Audited("integration.client")]`) — `id`, `client_id` (public identifier `ic_<16 base32>`, unique), `name_ar`,
`name_en`, `description`, `service_user_id` (the client's service account, §2.2), `acting_unit_id` (optional: the unit for Unit / Unit-and-below
scopes), `auth_kind` (`oauth` = OAuth 2.0 client credentials · `api_key`), `scopes` (`feeds` · `api.read`; `feeds` by default), `allowed_systems`
(codes; empty = none), `allowed_record_types` (codes; empty = every record type of the allowed systems), `ip_allow_list` (CIDR or single
addresses; empty = any), `rate_limit_per_minute` (default 120, 1–10 000), `expires_at` (required, ≤ Settings maximum, default 365 days),
`status` (`active` · `revoked`), `revoked_at`, `revoke_reason`, `last_used_at`, `last_used_ip`, stamps, `xmin`. Never deleted: revoked.

**`integration.client_credential`** — `id`, `client_id`, `kind` (`secret` for OAuth · `api_key`), `prefix` (8 chars, unique, shown in lists),
`secret_hash` (SHA-256, constant-time compare), `created_at`, `expires_at` (credential end: on rotation the old credential keeps working
until the chosen overlap ends — 0 to 30 days, default 7), `revoked_at`, `last_used_at`. The value is shown **once**.
For OAuth clients the secret is also registered as the OpenIddict confidential application's secret (OpenIddict keeps one secret, so OAuth
rotation has no overlap; documented in the UI) — ADR 0191.

**`integration.client_usage`** — one row per call: `id`, `client_id`, `at`, `method`, `path` (no query string values), `status_code`,
`duration_ms`, `rows` (feed rows returned), `ip`, `refused_reason` (`ip`, `rate`, `expired`, `revoked`, `scope`). Retained 90 days
(Settings → Integrations, `UsageRetentionDays`), purged by the job `integration.maintenance`. Not entity-audited (technical log, like
`ai.usage_log`); the audit trail gets one `integration.feed.query` entry per feed / extract request (channel **`api`**).

### 2.2 Service account and permissions — the existing access model

Each client owns a **service account**: an `identity.user` row with `principal_type = integration`, no password, no e-mail, no employee,
never offered in user pickers or interactive sign-in. Its claims at call time: `strategy:principal = internal` (so every module's checks apply
unchanged), `NameIdentifier = service user id`, `strategy:integration = <client id>`, `amr = client`, scopes, allowed systems, acting unit.

**Permissions and data scope = role assignments of the service account** (`access.role_assignment`, principal kind `user`), chosen on the
client's *Permissions* tab from the roles of each allowed system (the roles carry the data scopes: Own · Team · Unit · Unit-and-below · All).
The access snapshot of an integration principal (ADR 0191) differs from a person's in three ways only:
1. only assignments made **to the service account** count — "everyone" and unit-wide assignments and system grants are ignored;
2. **Unit / Unit-and-below** scopes use the client's *acting unit* (and its subtree); Own / Team match nothing (a client owns no records);
   record roles are never held by a client;
3. **system access** = systems of its roles ∩ the client's allowed systems ∩ enabled systems.

So `IAccessService.CanAsync / ApplyScopeAsync` filter feeds, extracts, REST reads and webhooks exactly as for screens. *Allowed record
types* further narrows the OData entity sets, extracts and webhook events.

### 2.3 Authentication

| Credential | How it is sent | Scheme |
|---|---|---|
| OAuth 2.0 **client credentials** | `POST /connect/token` `grant_type=client_credentials` with `client_id` + `client_secret` (Basic or form) and `scope=feeds` (or `api.read`) → access token (Data Protection, lifetime Settings → Integrations, default 60 min) → `Authorization: Bearer <token>` | `oauth` (existing) — the handler recognises client tokens and asks `IIntegrationClientAuthority` for the principal |
| **API key** | `Authorization: Bearer sik_<prefix>_<secret>` | `integration` (new) |
| API key as **HTTP Basic** (Power BI / Excel OData connector) | `Authorization: Basic base64(<client_id>:<api key>)` — HTTPS only outside Development | `integration` |

The default policy scheme picks `integration` for `Bearer sik_…` and `Basic …`; `pat` for `sst_…`; otherwise `oauth` (unchanged).
On every call: client `active` and not expired, credential not revoked / expired, service account active → else **401** (`integration.clientRefused`,
usage row with the reason, audit `integration.client.refused` at most once per minute per client and reason).

### 2.4 Gate (`IntegrationGateMiddleware`, after authentication)

For integration principals only: **IP allow-list** (remote address after forwarded headers; refused → 403 `integration.ipRefused`) →
**rate limit** per client per minute (fixed window in memory like ADR 0032; 429 `integration.rateLimited` with `Retry-After`) → **paths**:
`feeds` opens `/odata/v1/**` and `/api/v1/integration/extracts/**`; `api.read` opens GET / HEAD on `/api/v1/**` (except `/api/v1/me/**`,
`/api/v1/auth/**`, `/api/v1/integration/clients/**`); nothing else (MCP, AI, Wasel, SPA) → 403 `auth.tokenScope`. Channel `api`.
Usage row written after the response. People (sessions, PATs, OAuth MCP tokens) cannot call `/odata` or the extracts (403): a person's token
is not used for system integrations (SPEC-ADDENDUM §F).

### 2.5 Administration API (`/api/v1/integration/clients`, permission `integration.client.manage`, MFA role)

List · get · create (returns the first credential once) · update (name, description, systems, record types, IP list, rate limit, expiry,
scopes, acting unit) · `revoke` (reason; credentials revoked, OpenIddict application and its tokens removed) · `reactivate`? **no** — a revoked
client stays revoked (create a new one) · `credentials` list · `rotate` (new credential, overlap days for API keys) · `credentials/{id}/revoke`
· `roles` get / put (role keys of the allowed systems → assignments of the service account through `AccessAdministration`) · `usage` (paged
log, totals per day) . All changes audited (entity audit + `integration.client.*` events).

## 3. OData v4 analytics feeds (`/odata/v1/{system}`)

**Library** (ADR 0192): `Microsoft.OData.Core` / `Microsoft.OData.Edm` (MIT) for the **URI parser** and the **CSDL writer**; execution is
in-house through the record sources (§5) — no `Microsoft.AspNetCore.OData`, no EF `IQueryable` across modules.

| URL | Result |
|---|---|
| `GET /odata/v1/{system}` | service document (entity sets the client may read) |
| `GET /odata/v1/{system}/$metadata` | CSDL XML built **for the caller** (only readable record types, fields and relationships) |
| `GET /odata/v1/{system}/{EntitySet}` | collection |
| `GET /odata/v1/{system}/{EntitySet}({id})` | one entity (404 when outside scope) |
| `GET /odata/v1/{system}/{EntitySet}/$count` | plain-text count |

**Query options**: `$select`, `$filter` (`eq ne gt ge lt le and or not`, parentheses, `in`, `contains`, `startswith`, `endswith`, `null`,
literals of every field type), `$orderby` (several, asc / desc), `$top`, `$skip`, `$count=true`, `$expand` along the record type's
relationships with nested `$select`, `$filter`, `$orderby`, `$top`; `$format=json`; `lang=ar|en`. Others (`$apply`, `$search`, `$compute`,
filters on navigation properties or labels, lambda operators) → **400** OData error `notImplemented`.

**Model**: namespace `Strategy.<System>`; one entity type per record type (name from the type code: `portfolio.project` → `Project`), key `id`
(`Edm.Guid`), `modifiedAt` (`Edm.DateTimeOffset`) on every type, field types: Id / Unit / Employee → `Edm.Guid`, Text / Lookup → `Edm.String`,
Number / Money / Ratio → `Edm.Decimal`, Date → `Edm.Date`, DateTime → `Edm.DateTimeOffset`, Bool → `Edm.Boolean`. **Codes always included**;
for every Lookup / Unit / Employee field a read-only `<field>Label` string in the `lang` language (lookup labels from Settings, fixed
choices from the contract, unit and employee names from Organisation; fallback to the other language, then the code).
Navigation properties = relationships whose target type is in the same system and readable by the caller (cross-system `$expand` is
Phase 21's report-type layer; ADR 0190).

**Paging**: server page size = Settings → Integrations `ODataPageSize` (default 1 000, max 5 000); `@odata.nextLink` carries `$skip` (and the
caller's other options) until `$top` or the end; `$top` above the page size is honoured through next links. Expanded collections: at most
`ExpandLimit` (default 10 000) child rows per page, else 400 `expandTooLarge`.

**Permission filtering**: every row by the module's scoped reader for the client; fields with a field permission are left out of the
model and refused in `$select` / `$filter` / `$orderby` (400); each `$expand` level is read through its own source with the client's scope
(a child the client may not see is never returned even if the parent is). The response: `@odata.context`, optional `@odata.count`, `value`,
optional `@odata.nextLink`; header `OData-Version: 4.0`. Errors: `{"error": {"code", "message"}}`. 401 responses carry
`WWW-Authenticate: Basic realm="…", charset="UTF-8"` and `Bearer` so Excel / Power BI prompt for credentials.

## 4. Incremental extracts (`/api/v1/integration/extracts`)

| Endpoint | Result |
|---|---|
| `GET …/extracts/{system}/{EntitySet}?changedSince=&cursor=&pageSize=&format=json\|csv&lang=` | records **changed** (created or updated, `modifiedAt > changedSince`) ordered by (`modifiedAt`, `id`) |
| `GET …/extracts/{system}/{EntitySet}/deleted?changedSince=&cursor=&…` | **deleted feed**: records that are cancelled / withdrawn / inactive / closed-as-void (each record type declares its "deleted" condition, ADR 0193), with `id`, `modifiedAt` (when it became deleted, last change), status |
| `GET …/extracts` | catalogue: systems → record types the client may read |

Keyset paging: `nextCursor` (opaque `modifiedAt|id`), `hasMore`, `watermark` (the highest `modifiedAt` delivered — the next run's
`changedSince`). JSON: `{ items: [...], nextCursor, hasMore, watermark }`; CSV: UTF-8 with BOM, header row of field codes (+ label columns),
the cursor in `X-Next-Cursor` / `X-Watermark` headers. Page size ≤ 5 000. Same permission filtering as OData. Audit `integration.feed.query`.

**Scheduled exports** (optional, ADR 0193): `integration.export_schedule` per client: record types, format, **target folder** (a sub-folder of
the operator-configured `Integration:ExportRoot`, path traversal refused), frequency (every N hours / daily at a time), last watermark per
record type. Job `integration.export` (every 15 min) runs due schedules **as the client** (its scope), writes `<type>-<utc>.json|csv` and
records the run. SFTP and S3 targets are deferred (ADR 0193): operators sync the folder (rclone / SFTP) or pull the REST extract.

## 5. Reportable record contract (ADR 0190, shared with Phase 21)

`IReportableRecordSource` (per module): `SystemCode`, `RecordTypes` (code, system, entity set, labels, read permission, fields with type /
field permission / lookup type / choices, relationships `(name, target type, source field, target field, many)`), and
`QueryAsync(RecordQuery, ClaimsPrincipal, ct)` → `RecordPage` (raw values in field order + optional total). `RecordQuery`: fields, neutral
filter tree (`And / Or / Not / Compare / Text / In`), sort, skip / take, total, `ChangedSince`, keyset `After`, mode `Current | Deleted`.
Implicit fields `id` and `modifiedAt` (from `AuditableEntity.UpdatedAt`). Modules implement it with `RecordTypeQuery<T>` on **the same
scoped reader as their screens**, in their own DbContext; the engine validates every field against the caller's allowed fields before calling.

Seeded record types (entity sets): Portfolio `Projects` → `Milestones`, `ProgramRequests`; KPI `KpiCards` → `KpiResults`; Risk `Risks`;
Teams `Teams`; Requests `Requests`; Document Registry `Documents`; IP `IpRecords`; Benchmarking `Studies`; CAP `Remarks`, `Actions`;
Audit Management `Findings` → `Recommendations`; Tasks `Tasks`; Sample `Items` (worked example and tests).

## 6. Webhooks

**Catalogue** — modules register integration events: `services.AddWebhookEvent<TEvent>(new WebhookEventDefinition(code, system, labels,
resource type, permission, resource-id selector, sample))`. Seeded: `portfolio.project.closure-approved`, `portfolio.project.stage-changed`,
`kpi.result.approved`, `kpi.result.target-missed`, `risk.risk.rating-changed`, `risk.kri.breached`, `risk.event.logged`,
`risk.control.ineffective`, `cap.remark.status-changed` (raised or status changed), `cap.action.completed`, `auditmgmt.finding.closed`, `auditmgmt.finding.linked-to-risk`,
`docreg.document.published`, `teams.membership.changed`, `tasks.task.completed`, `requests.request.decided`.

**Subscriptions** (`integration.webhook_subscription`, audited): client, name, target URL (https; validated by the outbound URL policy on save,
ADR 0155), event codes, active, secret (generated, **encrypted** with Data Protection — it is needed to sign; shown once; *Regenerate*).
Event-to-delivery fan-out (`WebhookFanOut<TEvent>`, an integration-event handler → outbox, ADR 0043): for each active subscription of an
active client whose allowed systems / record types include the event, **the client must be able to read the record** (Links registry
visibility, or the event's permission for non-linkable resources) — else no delivery. One `integration.webhook_delivery` per (subscription,
event key) — unique, so outbox retries never duplicate.

**Payload** `{ "id", "type", "occurredAt", "system", "resource": { "type", "id" }, "data": { …event fields (ids, codes, changed values) } }`.
**Headers**: `X-Strategy-Event`, `X-Strategy-Delivery`, `X-Strategy-Timestamp` (Unix seconds), `X-Strategy-Signature: v1=<hex HMAC-SHA256(secret,
"<timestamp>.<body>")>`; receivers verify and reject timestamps older than 5 minutes (portal sample code).

**Delivery** — job `integration.webhooks` (every 30 s) sends due deliveries: `HttpClient` from `OutboundUrlPolicy.CreateHandler()` (connect-time
SSRF guard, no redirects), 10 s time-out, 2xx = delivered. Failure → attempt logged (`integration.webhook_attempt`: time, status code,
duration, error, no response body beyond 500 chars) and next attempt after `RetryBaseSeconds · 2^(n−1)` capped at 6 h (default base 60 s,
**8 attempts**), then status **dead** (dead-letter list). **Replay** (one or a selection, from the delivery log or the dead-letter list) puts a
delivery back to pending with a fresh attempt count; **Send test** queues a `ping`. Times use `IClock` (tests use the fake clock).
Retention: delivered 30 days, dead 90 days (job `integration.maintenance`).

## 7. Permissions (system `integration`, platform)

| Permission | Role (seeded) |
|---|---|
| `integration.client.manage` — clients, credentials, client roles, usage, export schedules | `integration.administrator` (MFA; existing role gains it) |
| `integration.webhook.manage` — subscriptions, delivery log, dead letters, replay | `integration.administrator` |
| `integration.portal.view` — developer portal | `integration.administrator`, new `integration.developer` |

## 8. Settings (`integration` area, Settings → Integrations → Settings section on the Clients page)

`MaxClientDays` 730 · `DefaultClientDays` 365 · `AccessTokenMinutes` 60 · `ODataPageSize` 1 000 · `ExpandLimit` 10 000 · `ExtractMaxPageSize`
5 000 · `UsageRetentionDays` 90 · `WebhookMaxAttempts` 8 · `WebhookRetryBaseSeconds` 60 · `WebhookTimeoutSeconds` 10 · `AllowHttpWebhooks`
false (Development / tests only via configuration `Integration:AllowHttpWebhooks`).

## 9. Screens (AR + EN, RTL / LTR)

- **Settings → Integrations → Clients**: list (name, kind, systems, status chip, expiry, last used), *New client* wizard (name, kind, systems,
  record types, acting unit, IP list, rate limit, expiry, scopes → shows client id + secret / key **once** with copy buttons).
- **Client page** tabs: *Details* · *Permissions* (roles per allowed system with their data scopes; effective scope summary) ·
  *Credentials* (list, rotate with overlap, revoke) · *Webhooks* (subscriptions: URL, events, active, regenerate secret, send test) ·
  *Deliveries* (log with status, attempts, next attempt, replay) · *Usage* (calls per day, refusals, log).
- **Settings → Integrations → Webhook deliveries**: dead-letter list across clients, replay selected.
- **Developer portal** (`/settings/integrations/developer`, `integration.portal.view`): API (link to `/openapi`), OData endpoints per system with
  the metadata document (rendered for the viewer), extract endpoints, webhook catalogue with **sample payloads** and signature verification
  samples, **"Connect Power BI"** guide (Arabic and English).

## 10. Events, jobs, audit

Jobs: `integration.webhooks` (30 s), `integration.export` (15 min), `integration.maintenance` (daily: usage / delivery retention).
Audit events: `integration.client.create|update|revoke|rotate|credential.revoke|roles`, `integration.client.refused`, `integration.feed.query`,
`integration.webhook.subscription.*`, `integration.webhook.replay`, `integration.export.run`. Entities `[Audited]`: client, credential (hash
masked), subscription (secret masked), export schedule.

## 11. Tests

Unit: OData filter translation (every operator, `in`, functions, nesting, refused constructs), CSDL for a model, keyset cursor, CIDR matching,
rate limiter, HMAC signature + verification sample, back-off schedule, `RecordTypeQuery` translation.
Integration (real PostgreSQL): client reads only allowed systems and its unit scope (OData and extracts; out-of-scope rows never returned;
other system 404 / absent from service document); `$filter` / `$select` / `$orderby` / `$expand` / paging (`nextLink`) / `$count` correct and
permission-filtered (field permission refused); expired, revoked client and revoked credential → 401; IP allow-list → 403; rate limit → 429;
OAuth client credentials token works on `/odata`, refused on `/mcp` and on writes; deleted feed lists a cancelled record; changed-since with
cursor; webhook delivered with a verifiable signature, failures retried with back-off on the fake clock and dead-lettered, replay delivers;
SSRF guard refuses internal targets on save and at connect time; delivery only for records the client can read; usage log and audit (`api`).
Power BI: manual check documented in the phase summary and the guide.
