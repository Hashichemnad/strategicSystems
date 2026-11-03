# 0064 — KPI verification checklist and bulk approval

- **Status:** Proposed (Phase 12) — awaiting owner acceptance
- **Context:** SPEC §2.4 K4: configurable verification checklist, bulk approval by year + department, chain configured in the approval
  engine (default single PMO step).

## Decision
- Checklist items live in Settings → KPI (code, AR / EN label, required). The reviewer decides through the KPI endpoint that stores the
  answers on the result; approving needs every required item. The approval hook refuses a final approval of a result without the required
  answers, so the generic approval screens (My Work) cannot bypass it — they show the message and link to the result.
- Bulk approval confirms the checklist once for the selection and decides each result in its own transaction (partial success reported).
- Workflow contract addition (additive): `IApprovalEngine.DecideAsync(processKey, resourceId, action, comment)` — the signed-in user's
  pending task on the record (own or delegated), with the same checks as the task endpoint.
