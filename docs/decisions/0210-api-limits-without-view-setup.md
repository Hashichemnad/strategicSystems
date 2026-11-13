# 0210 — API limits without "View Setup and Configuration"

- **Status:** Proposed (release 1.0.1) — awaiting owner acceptance
- **Context:** Testing the Salesforce standby sync against a real sandbox: the recommended integration user has the *Salesforce Integration*
  user licence (API only, with the *Salesforce API Integration* permission set licence). That licence cannot be given *View Setup and
  Configuration*, which the REST resource `/limits` requires (Salesforce REST API Developer Guide, "Limits"). In 1.0.0 the connection test
  and every run called `/limits` first and failed on its refusal, so the sync could not run at all with the recommended user. ADR 0206's
  rule "stop below 10 % of the daily API requests remaining" depended on `/limits`.

## Decision
- `SalesforceSession.TryLimitsAsync` returns the limits, or the refusal when Salesforce answers `/limits` with HTTP 401 / 403 or the error
  codes `INSUFFICIENT_ACCESS`, `FUNCTIONALITY_NOT_ENABLED` (or `API_DISABLED_FOR_ORG`). Any other failure is thrown as before.
- On a refusal the **connection test succeeds with a warning** (`integration.sync.warnings.limitsRefused`, with the Salesforce error details
  of ADR 0211) and a **run continues**, logging the warning `integration.sync.log.limitsUnavailable`.
- The session reads `Sforce-Limit-Info: api-usage=used/max` from **every** REST answer (not the `per-app-api-usage` part). When `/limits` was
  refused, the run makes one cheap call (`GET /services/data/{version}`, the resource list) to get a first value.
- The reserve rule is now checked **before every object** (not only at the start) against the latest known usage: `/limits` when it answered
  and no newer header was seen, else the header. `ApiLimits` gains an optional `Source` (`limits` | `header`; null on values stored by 1.0.0);
  header-based limits have no Bulk job counts. The connection's `last_limits` is written from the latest value at the end of the run / test.
- The Status tab notes "from Salesforce's response headers" when the usage came from the header.

## Consequences
- The sync works with the least-privileged user Salesforce recommends; *View Setup and Configuration* stays out of the permission set.
- Usage from headers can lag by the calls of other integrations made between two of ours; checking before every object keeps the reserve
  meaningful (the 10 % reserve is a safety margin, not an exact budget).
- The Bulk API daily job count is not known without `/limits`; Bulk jobs count towards the same daily request usage.
- Tests: unit (header parsing, refusal codes, reserve table), integration with the fake org refusing `/limits` (403 and 400) and reporting
  95 % usage in its headers (the run stops).
