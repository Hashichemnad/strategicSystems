# 0124 — Budget routing: draft Program Request from a CAP action

- **Status:** Proposed (Phase 15)
- **Context:** SPEC §9.4 C2.4: an action that needs budget not available this year creates a draft Program Request in Portfolio
  (pre-filled, Source of Suggestion = "Action Plan"), linked to the action, whose progress can mirror the project's (configurable).

## Decision
- Additive `Portfolio.Contracts.IProgramRequestDrafts`: `CreateDraftAsync(ProgramRequestDraft, transactionContext)` creates a **draft**
  request (number `PR-…`, requester = given employee with the requester record role, suggestion source `action-plan`, origin
  `cap.action`) inside the caller's transaction; `GetProgressAsync(requestId)` returns the request status and, once approved, its
  project and progress. `IPortfolioDirectory.SearchProjectsAsync` feeds the PMO's *link project* picker.
- Portfolio seeds the suggestion-source value `action-plan` ("خطة العمل" / "Action Plan").
- Routing runs in the `cap.action-validation` hook on approval when `budget_required && budget_available == false` and no project is
  linked; the request is linked back (Links registry, kind `remark-source`) and its badge shows on the action. The requester (remark
  owner) completes and submits the request in Portfolio.
- `mirrorProjectProgress` (off): the monitor copies the progress of the request's project into the action's achievement (an audited
  update with channel `job`).

## Consequences
The action stays in implementation; without mirroring the assignee keeps reporting progress.
