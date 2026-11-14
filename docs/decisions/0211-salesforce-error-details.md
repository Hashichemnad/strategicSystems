# 0211 — What Salesforce answered: error details on the Connection tab, in the run log and in the server log

- **Status:** Proposed (release 1.0.1) — awaiting owner acceptance
- **Context:** In 1.0.0 a failed connection test stored only a message key ("Salesforce answered with an error"); runs stored the first 300
  characters of the raw response body as an untranslated `detail`; the server log had only the message key. Administrators could not tell
  which call failed or why (e.g. `INVALID_FIELD` on a query, `INSUFFICIENT_ACCESS` on `/limits`, `invalid_grant` on sign-in).

## Decision
- `SourceConnectorException` carries `SalesforceError(Status, Call, ErrorCode, Message)`:
  - `Status`: the HTTP status (null for network failures, which carry the .NET request error instead of an error code);
  - `Call`: a **sanitised description** built by the session, never the URL: `POST /services/oauth2/token`, `GET /limits`, `GET /sobjects`,
    `GET /sobjects/{Object}/describe`, `GET /queryAll: SELECT <fields> FROM <Object> WHERE …` (the WHERE part — filter literals, Id lists,
    timestamps — is dropped), `POST /jobs/query: …`, `GET /jobs/query/{id}`, `GET /sobjects/{Object}/{Id}/{field}` (Ids only);
  - `ErrorCode` / `Message`: read from Salesforce's body in its three shapes — REST `[{"errorCode","message"}]`, OAuth
    `{"error","error_description"}`, Bulk `{"errorMessage"}`; anything else is kept as text. One line, at most 500 characters.
- Stored as message params `status`, `call`, `errorCode`, `message` on the run and run-log lines, and as `sync_connection.last_test_detail`
  (jsonb `{ error, warnings[] }`, migration `SalesforceErrorDetails`) shown by `SyncTestDto.Error` / `Warnings`. Older rows keep `detail`.
- Written to the server log with structured fields (`LoggerMessage`: status, call, errorCode, Salesforce message) for test failures, run and
  object failures and `/limits` refusals.
- The SPA shows the translated sentence and, under it, a labelled block (AR / EN labels; the values exactly as Salesforce sent them, isolated
  left-to-right with `<bdi dir="ltr">`) on the Connection tab, the Status tab, the Runs tab and its log, and mapping validation issues.

## Consequences
- Tokens, the JWT assertion, the private key and record values never reach the stored text: only the call shape and Salesforce's own message
  (which can name fields and objects). An integration test scans every stored run, log line and test detail for tokens.
- Translations stay ours (message keys); Salesforce's message is technical and shown untranslated, as the error code is.
