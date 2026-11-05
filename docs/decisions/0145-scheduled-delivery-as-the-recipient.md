# 0145 — Scheduled dashboards and reports: built as the recipient, in their language; email attachments

- **Status:** Proposed (Phase 17)
- **Context:** SPEC §0.4: dashboards and reports can be emailed on a schedule. The notification pipeline has no attachments. Gotenberg must
  not run client JavaScript from our pages (ADR 0096).

## Decision
- `reporting.schedule` (daily / weekly / monthly, period rule, format PDF / Excel / link, recipients users / roles / units expanded at
  send time). Job `reporting.deliver` builds the content **per recipient** with a principal from their user (roles, scope, system access
  at send time) and their profile language; nothing visible → no email (logged). Idempotent per schedule, recipient and period.
- PDF: bilingual HTML template with tiles, tables and server-generated SVG bars / lines. Excel: one sheet per widget or the report.
- Additive Notifications change: `NotificationRequest.Attachments` (file ids in `IFileStore`, purpose `reporting.delivery`, kept 30 days,
  ≤ 10 MB, otherwise a link to the authorised download). Setting `deliveryMode = attachment | link`.

## Consequences
A recipient never receives more than they could open in the UI; changes of role take effect at the next send.
