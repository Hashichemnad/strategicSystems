# 0132 — Audit-Office-only write model and read-only department users

- **Status:** Proposed (Phase 16)
- **Context:** SPEC §10.1 / §10.6 / §10.8: the system is operated only by the Audit Office; department users are read-only for their own
  section / department; audit data confidential by default; working papers Audit-Office-only.

## Decision
- One write permission `auditmgmt.office.manage` (Audit Office, MFA) on **every** command endpoint, MCP write tool, AI confirm and file
  upload; department users hold only `auditmgmt.finding.read` → 403 on any write, 404 for records outside their scope.
- Roles: `audit-office`, `chief-audit-executive` (follow-up review approver), `department-user` (everyone, unit and below + ADR 0130),
  `audit-committee` (All read, dashboards, exports, plan approver), `viewer` (All read; narrowed per installation in Settings → Roles).
- Reports are readable through any readable finding; follow-ups and Audit Office comments with the recommendation; Layer 2 universe,
  plan, engagements and working papers need `auditmgmt.lifecycle.manage` / `auditmgmt.workpaper.manage` (office only).

## Consequences
The separate Salesforce Audit Portal disappears; department users use the normal login, read-only.
