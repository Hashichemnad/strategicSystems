# 0127 — Annual benchmarking plan and the pre-quarter search notification

- **Status:** Proposed (Phase 15)
- **Context:** SPEC §8.2: approved requests form the annual plan circulated to all units; 30 days before the target quarter (configurable)
  the responsible unit is notified to fill the Search form. ADR 0027: deadlines in working time.

## Decision
- Plan view by year / quarter / unit; *Circulate plan* (PM Office) notifies the heads of all units under the organisation and the
  coordinators, recorded per circulation.
- Notice moment = first day of the target quarter − `searchNoticeDays` (30) calendar days (working days when `countWorkingDays` is on),
  moved back to the previous working day of the responsible unit's business hours, from the start of that day in the unit's time zone.
  `bench.monitor` (hourly) sends `bench.searchDue` once to the coordinator and the unit head when the moment has passed, the study is in
  Search and the form is not submitted; late approvals are notified on the next run.

## Consequences
The notice is never sent on a weekend / holiday; a study approved inside the window is notified immediately.
