# 0084 — Document validity by type, reminders, confirmation and expiry

- **Status:** Accepted by the owner (Phase 14, before the pull request)
- **Context:** SPEC §6.2.5 replaces a fixed review cycle with a validity rule per document type: until superseded, or N months from the
  effective date; reminders 60 / 30 days; owner confirms (extended by the type's period, logged), submits a new version or requests
  withdrawal; at expiry without action → Expired (still visible, flagged).

## Decision
- Rule per type in Settings → Document Registry (seed: policies and procedures 24 months, manuals / guides and strategies 36 months, legal /
  legislative documents and forms until superseded). Expiry = effective date + N months (end-of-month clamped), editable by the office.
- Daily job `docreg.validity` (idempotent through `validity_event`): reminders at each configured day count (default 60, 30) once per expiry
  date; at expiry → status `expired`, owner and office notified once. Confirming extends from the **current expiry** by N months (from today
  when already expired) and returns an expired document to `published`.
- "Current version stays valid until the new one is published": a version in review does not change the document's status; the document
  shows "under revision". Publishing any new version supersedes the previous one and recomputes the expiry from the new effective date.
- Days are counted in the installation's time zone.

## Consequences
Changing a type's rule affects documents published afterwards and confirmations; existing expiry dates stay until edited.
