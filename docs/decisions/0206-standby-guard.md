# 0206 — Standby guard: synced records read-only, workflows / jobs / notifications suspended

- **Status:** Proposed (Phase 23) — awaiting owner acceptance
- **Context:** §G4: while Salesforce is primary, synced records are read-only everywhere (UI, API, MCP, imports), and workflows, approvals,
  jobs and notifications about synced records are suspended; local-only data stays editable. Enforcement must be on the server (CLAUDE.md §2.4).

## Decision
- `IStandbyGuard` in the **Kernel** (implemented by Integration) so Persistence, Workflow, Scheduling and Notifications use it without
  referencing Integration.
- **One write rule** in `StandbyWriteInterceptor` (added to every module DbContext): with the guard active and channel ≠ `sync`, updating or
  deleting an audited entity that has an origin reference from a standby connection — or inserting / updating / deleting an audited child row
  whose `[AuditParent]` is such a record — throws `RecordReadOnlyException` → 409 `integration.sync.readOnly`. This covers every entry point
  at once (UI, REST, MCP, imports, jobs, AI drafts accepted by a user). The guard reads references through the data source (not a DbContext),
  caches the mode for 5 seconds and looks up only the keys being saved.
- The approval engine refuses submit / decide on a read-only subject before writing.
- Quartz trigger listener vetoes jobs of the configured **suspended systems** (default the 11 business systems) while the guard is active;
  platform jobs keep running.
- `NotificationService` drops notifications whose template belongs to a suspended system while the guard is active (logged).
- The record page shows "Synced from Salesforce — read-only" / "Deleted in Salesforce" / "Resubmit required" from the references endpoint.

## Consequences
Records created locally during standby (no Salesforce reference) stay editable; the guard costs one indexed query per save that touches
audited entities, only while standby is active.
