# 0142 — Month-grain periods, comparison, snapshots and refresh triggers

- **Status:** Proposed (Phase 17)
- **Context:** SPEC §0.4: period selectors (month / quarter / year / custom) and comparison with the previous period on every dashboard,
  "risk movement since last quarter", KPI trends; aggregates refreshed by the scheduler and on relevant changes. Module dashboards today
  filter by year only, each on a different date field.

## Decision
- Facts at **month grain**. Stock metrics read the last month of the period, flow metrics sum its months. Custom ranges are whole months.
  Comparison = the preceding period of equal length. Fiscal start month in Settings → Reporting (default January).
- Jobs: `reporting.refresh` (every 10 min, current month of dirty metrics), `reporting.snapshot` (monthly, freezes the previous month),
  `reporting.rebuild` (manual, 24 months). Disallow concurrent execution; idempotent replace.
- Dirty marking: an additive `IAuditObserver` in Platform.Audit reports the system codes of each committed change; integration events
  (`KpiResultApproved`, `ProjectClosureApproved`, `CapActionCompleted`, `AuditFindingClosed`, `RiskKriBreached`, `DocumentPublished`, …)
  mark cross-system metrics. A dirty mark triggers a debounced refresh within 60 s.
- Widgets show "as of"; authorised users may request a refresh (queued, never in the request).

## Consequences
No history tables in modules; monthly snapshots give trends and movement. Figures lag a change by about a minute.
