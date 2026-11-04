# 0121 — CAP lifecycle: owner response, per-action PMO validation, completion and extension approvals

- **Status:** Proposed (Phase 15)
- **Context:** SPEC §9.4 / §9.9: accept / reject is the owner's response, not an approval; rejection is final by default with an optional
  PMO confirmation (off); the PM Office validates **each** action (approve / reject / return, link project) and approves completion and
  extensions; it is the only approver by default, editable in the Approval Settings centre.

## Decision
- Remark statuses `assigned · rejection_pending · rejected · planning · validation · correction · implementation · verification · completed
  · cancelled`, mapped to the SPEC stages Analysis › Corrective Action › Implementation › Verification › Closed. Explicit transition table.
- Accept / reject / reassign are module commands (not approvals). With `rejectionNeedsPmoConfirmation` on, a rejection waits in
  `rejection_pending` for the PM Office (`cap.office`) to confirm (final) or send back (owner responds again).
- *Submit for validation* starts **one approval instance per action** (`cap.action-validation`, resource `cap.action`). The Validate
  Actions screen decides per action through `IApprovalEngine.DecideAsync` (same checks as the task screen, delegation included). The remark
  status is derived from its actions after every decision (any pending → validation; draft / returned → correction; all rejected →
  planning; else implementation).
- `cap.completion` (at 100 % with ≥ 1 evidence file) and `cap.extension` (new end date + justification; approved → `revised_end`, the
  original planned end kept) on the action. Default step: PM Office (role `cap.cap-office`). One pending process per action.
- Effectiveness verification (off by default): `verification` after all actions complete, due after N months; *not effective* reopens the
  remark to `planning`.

## Consequences
Every approval, delegation and SLA comes from the engine; linking an already approved action needs no new validation.
