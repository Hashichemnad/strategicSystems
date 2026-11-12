# 0184 — Record dossier templates and per-section permissions

- **Status:** Accepted (Phase 21)
- **Context:** E3: formatted PDF / Word dossier of one record from admin-designed templates (cover, branding, sections bound to the record
  or related lists / charts / Gantt / KPI trend / risk matrix / approval history / attachments, order, conditions, header / footer);
  permissions applied to every section.

## Decision
- `reporting.dossier_template` (record type = a dataset; jsonb definition). Section kinds: `fields`, `text`, `related`, `chart`, `trend`,
  `gantt`, `matrix`, `approvals`, `attachments`; condition on a record field; page break. Seven seeded templates (E3), re-seeded only while
  unedited. Designer and preview in Settings → Reporting (`reporting.dossier-template.manage`); running needs `reporting.dossier.run`.
- Data only through the report engine as the runner: record not visible → 404; related sections through the scoped module queries; fields
  without permission are left out; a section bound to something the runner may not read (object, all its fields, file owner) is **omitted**.
  Approval history needs the record to be readable (it is part of the record page); attachments need `IFileOwnerAccess.CanReadAsync`.
- Project RAG health is computed by Portfolio's health service and not stored; the stage / health section shows the stored progress status,
  performance and planned progress (the colours shown on the record page are derived from these and the overrides).

## Consequences
Templates are configuration; no template can show more than the runner could open on screen.
