# 0030 — Document text extraction (PDF, Word, PowerPoint) and similar records without embeddings

- **Status:** Accepted (Phase 5)
- **Context:** Document-to-form extraction needs the text of PDF, Word and PowerPoint files in Arabic and English, with free libraries only.
  "Similar records" should work without embeddings (BUILD_PLAN Phase 5).

## Decision
- **Recognition by content**: `%PDF-` → PDF; a ZIP with `word/document.xml` → Word; with `ppt/presentation.xml` → PowerPoint; `.txt` that is
  valid UTF-8 → text. Anything else is refused (`ai.extract.unsupported`); a parser failure is `ai.extract.unreadable`. 10 MB maximum.
  The uploaded file is **not stored**; the audit event records its name, size and SHA-256.
- **PDF**: PdfPig (Apache-2.0, already used by the tests). Letters are grouped into lines by baseline and ordered by position; a line with
  more Arabic than Latin letters is read **right to left**, keeping runs of Latin letters and digits left to right, because PDF glyph order
  is visual. `[page n]` markers.
- **Word / PowerPoint**: DocumentFormat.OpenXml (MIT): body paragraphs and table rows (`cell | cell`); slides in presentation order plus
  speaker notes, `[slide n]` markers. Legacy `.doc` / `.ppt` are not supported.
- **Normalisation**: NFKC (Arabic presentation forms and ligatures become letters), control characters removed, blank lines collapsed,
  60 000 characters kept (truncation reported to the user).
- **No OCR**: a scanned PDF without a text layer answers `ai.extract.noText`. OCR (e.g. Tesseract, Apache-2.0, as a sidecar) is a later
  option.
- **Similar records**: each module's `IRecordSearchSource.SimilarAsync` uses PostgreSQL `pg_trgm` similarity on its titles within the
  user's data scope (threshold 0.3, top 5). No model call; embeddings can be added behind the same contract later.

## Consequences
- Complex PDF layouts (columns, rotated text) may come out in a different reading order; the source-snippet check and human review catch
  wrong values.
