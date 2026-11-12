# 0200 — Sync engine in Platform.Integration and the `sync` channel

- **Status:** Proposed (Phase 23) — awaiting owner acceptance
- **Context:** SPEC-ADDENDUM §G2: a general, reusable connector framework (connectors, connection settings, mapping packs, sync runs, run
  log, errors) on top of the Phase 19 external references; first connector Salesforce, one-way.

## Decision
- The framework lives in `Strategy.Platform.Integration/Sync/` (schema `integration`, one migration), next to the external-reference store
  it reuses. Connectors implement a small source interface (describe objects / fields, query changed rows including deleted ones, download
  file content, read limits); the engine (mapping, change detection, exceptions, writing, runs) is connector-neutral.
- Synced writes run as the **system principal** with a new audit channel **`sync`** (added to `Channels`; CLAUDE.md §8 list extended). Every
  synced change is audited by the normal interceptor and is distinguishable from local changes — the standby guard (ADR 0206) lets exactly
  this channel write synced records, and the outage report (ADR 0207) excludes it.
- One run at a time per connection (PostgreSQL advisory lock); runs are idempotent (ADR 0205).

## Consequences
The audit-log channel filter and labels gain `sync`. Another connector (SAP, Oracle…) adds a source implementation and a mapping pack; the
standby / failover behaviour applies to any connection whose mode is standby.
