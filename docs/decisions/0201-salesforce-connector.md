# 0201 — Salesforce connector: JWT bearer, REST and Bulk API 2.0 reads, SystemModstamp watermarks

- **Status:** Proposed (Phase 23) — awaiting owner acceptance
- **Context:** SPEC-ADDENDUM §G2 / G1: OAuth 2.0 JWT bearer with a Connected App and an integration user, no stored password, encrypted
  private key, configurable API version, Bulk API 2.0 for large loads and REST for deltas, incremental by `SystemModstamp`, deleted records,
  API limits, **never write to Salesforce**.

## Decision
- **Auth**: RS256 JWT assertion (`iss` consumer key, `sub` username, `aud` login URL, 3-minute expiry) built in-house with
  `System.Security.Cryptography` (no new package); private key stored as PEM protected by ASP.NET Core Data Protection
  (`integration.salesforce.key.v1`), write-only through the API. Login and instance URLs must be HTTPS and pass the outbound URL policy (ADR 0155).
- **Reads only**: describe global / object, `queryAll` (REST, `nextRecordsUrl` paging), Bulk API 2.0 `queryAll` jobs (`POST /jobs/query`
  creates a *query* job — the only POST, it reads data), `/limits`, `ContentVersion/VersionData`, `Attachment/Body`. The HTTP client refuses
  any other verb / path at the connector boundary (test asserts it). No DML, Apex, metadata or tooling calls exist in the code.
- **Bulk vs REST**: full / initial loads and deltas above `bulk_threshold` (`SELECT COUNT()`) use Bulk 2.0; smaller deltas and objects Bulk
  cannot query (`ProcessInstance*`, some history objects) use REST.
- **Watermark**: per object mapping, `SystemModstamp` (configurable, `CreatedDate` for history); condition `>= watermark − 5 minutes` to cover
  commit lag; overlap is harmless because unchanged hashes are skipped (ADR 0205). Watermarks advance only when an object completed.
- **Deletes**: `queryAll` returns `IsDeleted = true` rows → reference status `deleted_in_source`; never a hard delete here.
- **Limits and retries**: `/limits` per run shown on the status page; stop below 10 % of DailyApiRequests; 3 retries with exponential back-off on
  network errors and 5xx; one token renewal on 401; `REQUEST_LIMIT_EXCEEDED` stops the run.
- **API version**: setting (default `v62.0`), validated `^v\d{2,3}\.0$`.

## Consequences
Bulk results arrive as CSV (RFC 4180, parsed in-house); types are converted with the describe. A Salesforce outage simply fails runs, which
the monitor reports (late / failed alerts) — the administrator then decides on failover.
