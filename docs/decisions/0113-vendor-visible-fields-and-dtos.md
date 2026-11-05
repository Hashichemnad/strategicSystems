# 0113 — Vendor-visible fields and separate vendor DTOs

- **Status:** Accepted by the owner (Phase 11)
- **Context:** SPEC §1.18 "field-level vendor-visible flags per object"; CLAUDE.md §7 "never reuse internal DTOs".

## Decision
- Fixed vendor-visible fields: project number, names, scope, planned dates, stage / status, PM name and e-mail, the vendor's assigned
  milestones (code, titles, dates, status, approved achievement).
- Optional flags in Settings → Portfolio → Wasel `visibleFields`: project `description` (on), `progress` (on), `owner` (off), `budget` (off),
  `approvalStatus` (off); milestone `weight` (on), `cost` (off).
- All Wasel responses are dedicated `Vendor*Dto` records built by explicit mapping. Internal comments, approval history and comments,
  approvers, other vendors, team, stakeholders, internal risks / issues are never mapped. A unit test checks the DTO shapes.

## Consequences
Showing more to vendors is a settings change, never a code change; hiding is the default.
