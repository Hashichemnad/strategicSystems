# 0021 — PDF rendering through Gotenberg with embedded Arabic fonts

- **Status:** Accepted (Phase 3)
- **Context:** CLAUDE.md §4.1 prescribes Gotenberg (headless Chromium) for correct Arabic shaping; QuestPDF is excluded (§4.3).

## Decision
- `IPdfRenderer` posts `index.html` plus the font files to `/forms/chromium/convert/html` (`Gotenberg:Url`; empty disables PDF output and the
  API answers `reporting.errors.pdfNotConfigured`). A health check tagged `optional` reports Gotenberg without failing readiness.
- **Fonts:** IBM Plex Sans Arabic (OFL; already the SPA's font) — Arabic and Latin subsets, regular and bold — are embedded resources of
  `Strategy.Platform.Reporting`, referenced with `@font-face … unicode-range` so the PDF never depends on fonts in the container.
- `ReportDocument` builds the bilingual HTML (titles in both languages, body and table direction per language, everything encoded).
- **Test:** the PDF from a real Gotenberg container is read with PdfPig (Apache-2.0, tests only): the embedded font subsets, Arabic glyphs
  mapped to contextual presentation forms (proof of shaping) including the lam-alef ligature, right-to-left word order and right-margin
  alignment, plus a **layout snapshot** (words, fonts, positions rounded to 2 pt) in `tests/Strategy.IntegrationTests/Reporting/Snapshots/`
  (`UPDATE_SNAPSHOTS=1` rewrites it). The E2E suite also screenshots the PDF rendered by pdf.js in the browser.

## Consequences
The snapshot pins the Gotenberg 8 rendering; a Gotenberg / Chromium upgrade that moves glyphs by more than the rounding needs a reviewed
snapshot update.
