# 0073 — Change requests: conditional approvers, one under review per project, semi-automatic apply

- **Status:** Proposed (Phase 9)
- **Context:** SPEC W7 and Q-P4 / Q-P9: types Details, Cancellation, Postponement, Ownership, Merge; approval Department Director → PM Office →
  Finance Service Section (when cost / budget is affected) → GM Office (Cancellation or Merge, or a cost change above a configurable amount);
  after approval the PM opens "Apply changes" pre-filled, confirms, the system applies and logs each change → Changes Implemented.

## Decision
- Process `portfolio.change-request` with the project as subject (ADR 0070): **one change request under review per project**. Variables
  `changeType`, `costAffected`, `costChange` (|proposed budget − budget|), `gmThreshold` (Settings → Portfolio, default 100 000). Conditions
  `costAffected` (Finance) and `changeType == 'cancellation' or changeType == 'merge' or costChange > gmThreshold` (GM), editable.
  New approver roles `portfolio.finance-section`, `portfolio.gm-office`.
- `cost_affected` is true when the request proposes a budget different from the current one (or the requester ticks it).
- **Apply** is a separate command by the PM (or PM Office) on an *approved* request: the body carries the values to apply (pre-filled by the
  UI from the request; the server validates them against the type). The server applies them in one transaction, writes `applied` (field, old,
  new) and the audit trail, sets `completed`. **Nothing is applied by the approval itself.**
- Ownership: the old PM / owner record roles are revoked and granted to the new people (`IRecordRoleService` in the same transaction).
  Cancellation / Merge set the project to Cancelled (merge stores the target, which must be an active project the PM may read).
  Postponement moves the project's planned dates only; milestones are re-planned by the PM (logged).

## Consequences
Change requests never alter a project silently; the log is the change history. Parallel change requests wait (drafts are allowed).
