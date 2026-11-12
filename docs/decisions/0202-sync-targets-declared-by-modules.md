# 0202 — Sync targets declared by modules, written by a generic entity writer

- **Status:** Proposed (Phase 23) — awaiting owner acceptance
- **Context:** The sync must write records of every system while CLAUDE.md §5 keeps module internals private and §2.5 requires EF Core change
  tracking (audit) for business writes. Writing through each module's workflow commands is not possible: synced records arrive in their final
  state (stage, status, approvals done in Salesforce) and must not start approvals, raise integration events or notify users (§G3, §G4).

## Decision
- A module **declares** in its own project, with `services.AddSyncTarget(...)` (Integration contracts, no EF types), each record type the sync
  may write: its DbContext type and entity type, and the **allow-listed properties** with a field code, kind (`text`, `date`, `datetime`,
  `decimal`, `ratio`, `int`, `bool`, `code` with the fixed code list, `lookup` with its lookup type, `codes`, `person`, `unit`, `reference` to
  another target), required flag and defaults.
- A generic **`EntitySyncWriter`** (Integration) resolves the module DbContext, loads or creates the entity (private parameterless constructor),
  sets only declared properties through `Entry(entity).Property(name).CurrentValue`, validates values (lookup codes active, fixed codes, max
  lengths from the EF model), saves, and links the external reference in the same transaction (ADR 0023). Change tracking and the audit
  interceptor apply (channel `sync`); no SQL is written by hand.
- Domain methods are not called; optional **hooks** (`ISyncTargetHook`, module code) derive module-owned data in the same transaction (record
  roles of a synced project or team). Types with structural invariants supply a **custom writer** (`ISyncTargetWriter`): organisation units
  (hierarchy, versions) and employees.
- No approval instance is created for synced records; the Salesforce approval history is kept as source history (ADR 0208).

## Consequences
Adding a field to the sync is one declaration line in the owning module plus a mapping. The declaration is the module's explicit consent and
its allow-list; a property not declared can never be written by the sync.
