# 0027 — Business hours, holidays and working-time SLAs

- **Status:** Accepted (Phase 4, before the pull request — owner request)
- **Context:** Approval SLAs were calendar hours. The owner asked for Salesforce-style Business Hours and Holidays, unit-specific business hours,
  and one shared calculator for all later deadlines (KPI windows, CAP due dates, task due dates).

## Decision
- A new platform component **`Strategy.Platform.Calendar`** (schema `calendar`, added to the platform schemas in CLAUDE.md §5) with its contract
  `IBusinessHoursCalculator` in `Strategy.Platform.Calendar.Contracts`. It references Organisation (unit inheritance) and Settings (installation
  time zone); the approval engine references only the contract.
- **Business hours:** several records, exactly one default (partial unique index; making another default switches it in one transaction); the
  default cannot be deactivated; records are never deleted (inactive). Each weekday is *closed*, *24 hours* or **one** start–end interval on that
  day. Overnight shifts (end after midnight) are not supported; a "24 hours" day followed by another covers them. Time zone per record (IANA).
- **Holidays:** a date or a range (≤ 366 days); all day or specific hours (non-working hours on each day of the range); one-time or recurring
  every year on the same month/day (29 February only in leap years; a recurring range cannot cross 31 December). Hijri-based holidays are entered
  per year as one-time dates. A holiday applies to all records or to chosen ones. **Overlap rule:** two active holidays sharing a record may not
  overlap (dates, and hours when both are partial) — chosen so the calendar is never ambiguous and the message can name the conflict.
- **Units:** a unit may use a record; otherwise it inherits from the nearest ancestor with one, then the default (current structure). Removing a
  unit's record sets it to "inherit" (no delete).
- **Working-time arithmetic** is a pure, tested core (`WorkingCalendar`); the service caches one calendar per record and drops the cache on every
  change. `AddWorkingTime` returns the end of the working interval when the time runs out exactly at its end (as Salesforce does).
- **Approval steps** choose `working` (default) or `calendar` hours and which business hours: the default, the record's unit, or a specific
  record. The due time is computed when the step opens and snapshotted on the task with the record's name; later edits of business hours do not
  move open tasks (same rule as approvers, ADR 0025). Steps saved before this change become *working hours, default business hours*; open tasks
  keep their due time.
- **Sample defaults** change from 72 / 72 / 120 calendar hours to 16 / 16 / 40 working hours (two, two and five working days).

## Consequences
KPI windows, CAP and task due dates use the same calculator. A business calendar per user (personal leave) is not modelled.
