# 0183 — My reports for everyone, folders, publishing needs Report author

- **Status:** Accepted (Phase 21)
- **Context:** E2: "My reports" for every user (private, no role needed); folders private / shared with people, roles, units; sharing /
  publishing needs Report author. Phase 17 required `reporting.report.build` to save any report.

## Decision
- Saving a **private** report or a **private folder** needs only `reporting.report.run` (everyone). Sharing a report, creating or sharing a
  shared folder, and placing a report in a shared folder need `reporting.report.share` (Report author). The Phase 17 integration test that
  expected 403 for an editor saving a private report is changed accordingly (addendum E2 wins over Phase 17).
- `reporting.report_folder` (owner, name AR / EN, kind private / shared, status); shares reuse `reporting.share` (`target_kind = folder`).
  A report is visible to its owner, to direct shares and to users matching a shared folder it is in. Every run is still the runner's rights.

## Consequences
Any user keeps personal reports; what others see is published only by Report authors.
