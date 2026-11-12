# Integrations: clients, analytics feeds, extracts, webhooks and Power BI

For administrators and developers of other systems (Phase 22, SPEC-ADDENDUM §F, ADRs 0190–0195).

## 1. Create an integration client (administrator)

1. **Settings → Integrations → Integration clients → New client.**
2. Name it after the other system (e.g. "Power BI — Strategy Office").
3. **Authentication**: *API key* for Power BI, Excel, Tableau, Qlik and scripts; *OAuth 2.0 client credentials* for systems that support it.
4. **Allowed systems** (and, optionally, record types), **acting unit** (used by the Unit / Unit-and-below data scopes of its roles),
   **IP allow-list** (one address or CIDR block per line), **requests per minute**, **expiry**.
5. Save and **copy the client ID and the key / secret now** — they are shown once.
6. **Permissions tab**: tick the roles the client holds. Their permissions and data scopes decide what it reads, exactly as for a person.
   Create a dedicated role in Settings → Roles when no existing role fits (e.g. "Portfolio read — whole organisation").
7. **Credentials tab**: *Issue new credential* rotates the key; API keys may overlap up to 30 days. *Revoke client* stops it for good.
8. **Usage tab**: every call, refusals (address, rate limit, expired, revoked) and rows read; every feed call is also in the audit trail (channel `api`).

## 2. OData analytics feeds

`https://<server>/odata/v1/<system>` — one read-only OData v4 service per system (`portfolio`, `kpi`, `risk`, `teams`, `requests`, `docreg`,
`ip`, `bench`, `cap`, `auditmgmt`, `tasks`). `…/$metadata` describes what *this client* may read.

- `$select`, `$filter` (`eq ne gt ge lt le and or not in contains startswith endswith`), `$orderby`, `$top`, `$skip`, `$count=true`,
  `$expand` along a record type's relationships (with `$select`, `$filter`, `$orderby`, `$top` inside).
- Codes are always included; `<field>Label` gives the label in `?lang=ar` or `?lang=en`.
- Large results are paged: follow `@odata.nextLink`.
- Every row and every expanded row is filtered by the client's permissions and data scope.

## 3. Connect Power BI (manual check)

1. Power BI Desktop → **Get data → OData feed**.
2. URL: `https://<server>/odata/v1/portfolio` (add `?lang=ar` for Arabic labels) → OK.
3. Credentials: **Basic** — user name = client ID (`ic_…`), password = API key (`sik_…`). Apply them at the server level.
4. Navigator: tick e.g. *Projects* and *Milestones* → **Load**. The relationship project → milestones is on `id` = `projectId`.
5. Power BI service: publish, then set the same Basic credentials in the dataset's data-source settings (an on-premises data gateway is
   needed when the server is not reachable from the internet). Schedule the refresh.
6. 401 after a while: the key was rotated / expired or the client revoked. 429: lower the refresh frequency or raise the rate limit.

Power Query (advanced editor):

```
let
    Source = OData.Feed("https://<server>/odata/v1/portfolio", null, [Implementation = "2.0", Query = [lang = "en"]]),
    Projects = Source{[Name = "Projects"]}[Data]
in
    Projects
```

Excel (Data → Get data → From OData feed), Tableau (OData connector) and Qlik (OData / REST connector) use the same URL and Basic credentials.

## 4. Incremental extracts (ETL)

- Changed: `GET /api/v1/integration/extracts/<system>/<EntitySet>?changedSince=2026-11-01T00:00:00Z&format=json|csv&lang=en`
- Deleted (cancelled / withdrawn / inactive): `…/<EntitySet>/deleted?changedSince=…`
- Keyset pages: pass `nextCursor` back as `cursor` while `hasMore` is true; store `watermark` as the next `changedSince`. CSV returns the
  cursor and watermark in `X-Next-Cursor` / `X-Watermark`.
- Scheduled exports (Clients → *Scheduled exports* tab) write the changes to a sub-folder of the operator's `Integration__ExportRoot`
  (JSON or CSV). Sync that folder to SFTP / S3 with the operator's tools.

## 5. Webhooks

Client → **Webhooks tab → New subscription**: HTTPS target URL (addresses of the server itself, link-local and cloud-metadata addresses are
refused), events, then copy the **signing secret** (shown once). The catalogue with sample payloads is in the developer portal.

Each delivery is a `POST` with JSON `{ id, type, occurredAt, system, resource: { type, id }, data }` and the headers `X-Strategy-Event`,
`X-Strategy-Delivery` (deduplicate on it), `X-Strategy-Timestamp`, `X-Strategy-Signature: v1=<hex HMAC-SHA256(secret, "<timestamp>.<body>")>`.
Answer 2xx within 10 s; failures are retried after 1, 2, 4, 8 … minutes (8 attempts, at most 6 h apart), then go to the **dead-letter list**
(Settings → Integrations → Webhook deliveries) where they can be **replayed**. A client only receives events about records it may read.

## 6. Operator settings

| Variable | Meaning |
|---|---|
| `Integration__ExportRoot` | Folder for scheduled exports (mount a volume) |
| `Integration__AllowHttpWebhooks` | `true` only in development: allows `http://` webhook targets |
| `Outbound__AllowPrivateNetworks` | `false` refuses webhook targets on private networks (ADR 0155) |
