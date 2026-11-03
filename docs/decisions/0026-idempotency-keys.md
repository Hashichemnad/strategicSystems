# 0026 — Idempotency keys on submit and approval endpoints

- **Status:** Accepted (Phase 4)
- **Context:** CLAUDE.md §11: "Idempotency key header supported on submit / approve endpoints." A double click or a retried request must
  not approve twice or create two submissions.

## Decision
- Endpoints opt in with `.WithIdempotency()` (metadata in `Strategy.Platform.Http`). A middleware registered by `Platform.Workflow` handles
  the header `Idempotency-Key` (1–100 printable characters) for signed-in callers.
- Records in `workflow.idempotency_record`, unique per (user, key), with a fingerprint = SHA-256 of method, path and body. First request:
  insert "in progress", run, and keep a **successful** (2xx) response: status, content type and body (≤ 64 KB). Same key + same
  fingerprint → the stored response is replayed with header `Idempotent-Replayed: true` and the handler does not run. Same key, different
  request → 422 `idempotency.keyReused`. Still running → 409 `idempotency.inProgress`. A failed response (4xx / 5xx) is not kept, so the
  client may correct the request and retry with the same key. Records expire after 24 hours (`workflow.idempotency-cleanup` job).
- The table lives in the `workflow` schema because the approval endpoints are its main users; it is generic and any module endpoint can opt in.
- Without the header, row versions (409) and state checks still stop a second decision.

## Consequences
The SPA sends a fresh key per dialog opening, so a retried click replays and a new decision gets a new key.
