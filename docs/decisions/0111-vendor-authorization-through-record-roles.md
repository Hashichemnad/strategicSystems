# 0111 — Vendor authorization through `IAccessService` and record roles

- **Status:** Proposed (Phase 11) — awaiting owner acceptance
- **Context:** CLAUDE.md §7: one access service for every caller; vendors see only assigned projects; access ends at contract end / closure.

## Decision
- New system `wasel` (switchable). Permissions `wasel.project.read`, `wasel.plan.prepare`, `wasel.plan.submit`, `wasel.update.prepare`,
  `wasel.update.submit`, `wasel.deliverable.upload`, `wasel.item.propose`, `wasel.comment.write`, `wasel.meeting.confirm`.
- They are granted **only** by the record roles `vendor-lead` and `vendor-contributor` on `portfolio.project` (contributor lacks the two submit
  permissions). Grants are to the vendor **user id** with `validTo = access_until + 1 day`, so expiry needs no job to take effect.
- `AccessService`: a vendor principal may only use `wasel.*` permissions, only through active record roles, only while the `wasel` system is on;
  internal principals are refused `wasel.*`. Endpoints use `RequireVendorPermission` (metadata `VendorEndpoint`); handlers check the project
  record (404 when not assigned).
- A per-request vendor context also requires the vendor user and the vendor organisation to be active (suspension takes effect at once) and
  restricts milestones to those assigned to the vendor.

## Consequences
Vendor rights appear in the record-role editor like other record roles and are audited the same way.
