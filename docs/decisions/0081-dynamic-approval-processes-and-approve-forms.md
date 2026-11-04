# 0081 — Approval processes for request types and approve mini-forms

- **Status:** Accepted by the owner (Phase 14, before the pull request)
- **Context:** Approval processes are registered in code (ADR 0024) and their chains seeded once. Custom request types are created at run
  time but each needs its own chain, "default single PMO step per type, editable in the Approval Settings centre" (SPEC §5–8 decision).
  The review of a document must capture classification (level, type, sensitivity) with the approval (SPEC §5.1 "each action can open a
  mini-form").

## Decision
- **Additive Workflow contract** `IApprovalProcessSource { IReadOnlyCollection<ApprovalProcessDefinition> GetProcesses(); }`. The process
  registry merges code definitions with every source (the Requests module returns one definition per active custom type, cached and
  refreshed when a type changes). Version 1 of a dynamic process is seeded the first time the engine or the Approval Settings centre needs it;
  from then on the centre owns the chain, as for every process. Hooks may declare process keys dynamically (`IApprovalHook.ProcessKeys` is
  read per event).
- Built-in types register ordinary code processes (`docreg.review`, `docreg.withdrawal`, `ip.review`).
- Default chain of every request process: one step, role `<system>.office` (labelled "PM Office / Strategy Office"), SLA 24 working hours,
  comment required on reject / return.
- **Approve forms**: the request page decides through `POST /requests/{id}/decide` (validates the mini-form, keeps it in a scoped
  `PendingDecision`, then `IApprovalEngine.DecideAsync`). The framework's hook stores the values and **refuses the completion** of an
  instance when a required approve-form field is still empty, so the generic task screens cannot bypass it (pattern of ADR 0064).
- Variables offered to entry conditions: `requestType` plus every number / currency / checkbox / picklist field as `f_<code>`.

## Consequences
Admins see custom request processes next to the others in the centre. Deactivating a type keeps its process (instances in flight finish).
