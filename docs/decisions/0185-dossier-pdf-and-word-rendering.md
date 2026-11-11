# 0185 — Dossier rendering: PDF via Gotenberg, Word via DocumentFormat.OpenXml

- **Status:** Accepted (Phase 21)
- **Context:** E3 asks for PDF and Word in Arabic, English or bilingual. CLAUDE.md §4.3 allows MIT / Apache libraries only.

## Decision
- One `DossierDocument` model per record → **HTML** (encoded, no script, embedded Arabic fonts, `dir` per language, the template's header
  line under the cover and footer line at the end of each record — Chromium's fixed-position elements overlapped page content, so they are
  not repeated per page in PDF; Word repeats them as real header / footer parts —, SVG charts, Gantt and matrix) rendered by **Gotenberg** (as all PDFs, ADR 0096); merged PDFs are one HTML with a page break per
  record (one call).
- **Word** with **DocumentFormat.OpenXml 3.x** (MIT; already in central package management): paragraphs and tables with `bidi` /
  `bidiVisual` and `rtl` runs for Arabic, heading styles, header / footer, cover page. Charts, Gantt and the matrix are written as native
  shaded tables (values per category; month cells filled for Gantt bars; coloured matrix cells with counts) — editable in Word and
  readable by screen readers, no image rasteriser needed.
- Bulk: merged PDF or ZIP (PDF or DOCX per record, file names from the record number).

## Consequences
No new dependency; Word output is structurally identical in both directions; charts in Word are tables, not images.
