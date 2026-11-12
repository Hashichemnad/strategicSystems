# 0205 — Change detection, idempotent runs and the exceptions queue

- **Status:** Proposed (Phase 23) — awaiting owner acceptance
- **Context:** Re-runs must not create duplicates; unmapped values / unknown users / missing parents go to an exceptions queue with the
  reason; admins fix and re-process (§G2).

## Decision
- Identity of a synced record = its external reference (source, target type, external ID from the template). Change detection =
  SHA-256 of the canonical mapped source values (`source_hash`) plus `SystemModstamp` (`source_modified_at`): equal hash → `unchanged`, no write.
- A record with any mapping issue is **not written**; one open exception per (connection, object, external ID) stores reason, field, value and
  the source payload. Re-processing uses the stored payload with the current mapping (no Salesforce call); a payload older than what is already
  synced is closed as superseded. Missing parents are deferred within a run (multi-pass for self-references) and re-tried at the end.
- Per-record errors never stop a run (status `partial`); connector errors stop it (`failed`) and keep finished objects' watermarks.

## Consequences
Runs can be repeated at any time (Sync now, full load) with the same result. The exceptions queue is the admin's to-do list.
