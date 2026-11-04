# 0085 — "Uncontrolled when printed" watermark on downloads

- **Status:** Accepted by the owner (Phase 14, before the pull request)
- **Context:** SPEC §6.4: download with an "uncontrolled when printed" watermark (configurable). Arabic text needs shaping; QuestPDF and
  commercial PDF libraries are not allowed (CLAUDE.md §4.3).

## Decision
- The stored signed file is never changed. Downloads through the document endpoint stamp every page of the PDF: the bilingual stamp text
  (Settings, AR / EN) is rendered once by Gotenberg (correct shaping, embedded fonts) and drawn on each page with **PdfPig** (Apache-2.0,
  already a dependency) by copying the pages into a new document. If the stamp cannot be produced (Gotenberg down), the download falls back
  to the original file with a response header `X-Watermark: unavailable` and a log warning — the business action never fails.
- Non-PDF files are served unchanged. The setting can switch the stamp off. Downloads are audited as before.

## Consequences
Stamped copies are produced per download (no cache); large PDFs cost a few hundred milliseconds.
