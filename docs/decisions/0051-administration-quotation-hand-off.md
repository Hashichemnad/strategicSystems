# 0051 — Administration Dept quotation hand-off

- **Status:** Accepted by the owner (Phase 8, before the pull request)
- **Context:** SPEC W1.3: without a cost file, the creator uploads the project scope, the Administration Dept is emailed, uploads
  quotations, and the request goes back to the creator. §1.9: Administration Dept (Procurement) scope = "Assigned".

## Decision
- *Send to Administration* (requester, draft, project scope uploaded) sets `procurement_status = waiting-quotations` and grants the record
  role **`procurement`** (read + `portfolio.request.quotations`) on that request to the users who hold role `portfolio.administration`
  at that moment (users and unit assignments resolved by `IRoleHolderQuery`); they receive `portfolio.scopeSubmitted`. No holder → the
  hand-off is refused with `portfolio.errors.noAdministration` (nothing is lost silently).
- Administration uploads quotations (purpose `portfolio.quotation`) and confirms *Quotations uploaded* (at least one quotation required) →
  `quotations-uploaded`, requester notified (`portfolio.quotationsUploaded`). The procurement record role stays (history visibility) but
  uploading is only allowed while waiting.
- When budget is required, submission requires the cost-estimation file (cost path) or uploaded quotations (scope path).

## Consequences
Administration sees only the requests handed to them. Re-assigning the role later does not move past hand-offs (they can be re-sent).
