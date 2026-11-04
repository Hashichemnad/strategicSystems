# 0095 — RAID, RACI, communication plan, benefits, quality, payments, checklists, templates, portfolios and programmes

- **Status:** Accepted by the owner (Phase 10)
- **Context:** SPEC §1.12 lists the items without data details.

## Decision
- **Portfolios & programmes:** one table `portfolio_group` (kind portfolio / programme; a programme may belong to a portfolio); a project
  references one programme and / or one portfolio. Managed by `portfolio.portfolio.manage` (PM Office).
- **RAID:** one `raid_item` table for issues, assumptions, dependencies (project / milestone → project / milestone, finish-to-start) and
  decisions; risks are listed alongside from the risks table. Entries by team members start *proposed* and the PM accepts them (SPEC §1.9).
  Existing *challenges* stay as they were (Phase 9 data).
- **RACI:** `raci_entry` per area (free text or milestone) × employee; one Accountable per area.
- **Communication plan:** items generated from stakeholders (method, frequency) with an owner and next due date; a daily job reminds the
  owner once per due date; *Mark done* advances the date.
- **Benefits:** defined on the request, carried to the project (same rows, `project_id` set at creation), measured in the Impact stage; the
  impact gate requires every active benefit measured when the switch is on.
- **Quality:** acceptance criteria already exist on deliverable milestones; sign-off (accepted / rejected, comment) by the project owner or
  PM Office; the closure gate requires every deliverable accepted when the switch is on.
- **Procurement:** payment milestones per contract and a supplier rating (quality, timeliness, communication 1–5).
- **Gate checklists:** configured per gate (charter, planning, closure) in settings; required items join the hard minimums.
- **Templates:** stored in settings tables; applying a template creates milestones (dates offset from the project start), planning items,
  risks and epics through the same validation; *Save as template* by the PM Office.

## Consequences
All items are ordinary audited project children with no hard delete (deactivate / status).
