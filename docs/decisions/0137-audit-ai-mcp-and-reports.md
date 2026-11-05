# 0137 — Audit AI features, MCP tools, reports and committee pack

- **Status:** Accepted by the owner (Phase 16)
- **Context:** SPEC §10.3 C (reports), §10.7 (AI, MCP), CLAUDE.md §12 / §13.

## Decision
- AI features (drafts only, gateway-controlled, audited channel `ai`): `auditmgmt-extract`, `auditmgmt-evidence` (draft follow-up
  comment), `auditmgmt-repeat` (trigram similarity without a model, optional rationale), `auditmgmt-summary` (committee narrative).
- MCP read tools on: `audit_findings_by_department`, `audit_finding_details`, `audit_followups_due`, `audit_overdue_findings`,
  `audit_my_finding_status`; write tool `audit_record_follow_up_draft` off by default and Audit-Office-only.
- Reports (bilingual PDF through Gotenberg, Excel through ClosedXML, exports audited): findings register per report, department follow-up
  report, revenue audit full report (all findings, recommendations and follow-ups of revenue audits), audit committee pack (figures,
  overdue high / critical findings, risk acceptances, by department, optional accepted AI summary).

## Consequences
The committee pack contains only data the requesting user may read.
