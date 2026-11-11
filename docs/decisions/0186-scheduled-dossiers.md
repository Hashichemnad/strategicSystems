# 0186 — Scheduled dossiers (e.g. monthly dossier of each active project to its owner)

- **Status:** Accepted (Phase 21)
- **Context:** E3: dossiers on a schedule; ADR 0145 builds every delivery as its recipient.

## Decision
- Reuse `reporting.schedule` with `target_kind = dossier`, `target_id` = template id and a new `options` jsonb (record filters, format
  `pdf` / `zip-pdf` / `zip-docx`, language mode). Recipients: users / roles / units, or `field:<employee field>` of the record type
  (each record's person; the candidate records are found as the schedule owner at send time).
- Per recipient: their principal, their language (or the template's bilingual mode), only records they may see, sections filtered for
  them; nothing → no e-mail (delivery logged `nothing-visible`); one e-mail per (schedule, recipient, period) with the merged PDF / ZIP
  (attachment limit, else link) — idempotent like ADR 0145.

## Consequences
"Each project owner gets their projects' dossiers monthly" is one schedule; access changes apply at the next send.
