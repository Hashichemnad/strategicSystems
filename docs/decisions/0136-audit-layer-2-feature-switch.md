# 0136 — Layer 2 (audit lifecycle) behind a feature switch

- **Status:** Accepted by the owner (Phase 16)
- **Context:** SPEC §10.4 / §10.8: universe, risk-based plan, engagements, programmes and working papers, reporting, QAIP — switchable,
  off by default.

## Decision
- Platform feature flag `auditmgmt.lifecycle` (Settings → Features, default **off**). Every Layer 2 endpoint has a filter that answers
  **404** problem `auditmgmt.lifecycleOff` while off (no information about the data); the SPA hides the navigation and routes.
- Universe entries are created by hand or pulled from the organisation (units at the department level) and from Risk (risks the auditor
  may read, inherent rating from the residual band). Plan items are engagements ordered by risk score (rating weight × years since the
  last audit). Plan approval through process `auditmgmt.plan` (default step: role `auditmgmt.audit-committee`).
- Engagement stages Planning → Fieldwork → Reporting → Follow-up are an explicit state machine; Reporting creates a draft audit report;
  issuing it moves the engagement to Follow-up. Working papers (Audit Office only) with reviewer sign-off.
- QAIP measures (plan completion, report timeliness, closure rate, satisfaction) are shown on the plan and can be published as KPIs.

## Consequences
Organisations that only track findings never see Layer 2; switching it off hides but keeps the data.
