# 0161 — Rich text: Tiptap editor, server allow-list (HtmlSanitizer), DOMPurify, no data migration

- **Status:** Proposed (Phase 19, stream R) — awaiting owner acceptance
- **Context:** SPEC-ADDENDUM A2 asks for rich text in the SPEC's rich-text fields, stored as sanitised HTML, rendered with DOMPurify, with
  plain-text fallbacks, while existing plain values migrate unchanged. Until now the only rich-text fields (project meeting notes, Request
  Framework `richtext` fields) were edited in textareas and shown as text; the project-meeting notes were filtered by a regex (Phase 7) and
  the Request Framework stored `richtext` values unsanitised (rendered inert in the SPA).

## Decision
1. **Which fields**: the fields the SPEC marks as rich text plus the A2 categories the SPEC describes as narrative text of a record: project
   meeting notes (MOM), team meeting agenda / minutes / decisions, Request Framework `richtext` fields, programme request description and
   justifications (and the project description copied from the request). The inventory is in `docs/design/rich-text.md` §2; every other
   field stays plain.
2. **Server sanitiser in the kernel** (`Strategy.Platform.Kernel.Text.RichText`) on **HtmlSanitizer** (mganss, MIT; AngleSharp MIT): fixed
   allow-list (tags, `dir`, `href` http / https / mailto, `target=_blank`, `colspan` / `rowspan`), no `style` / `class`, `rel` forced to
   `noopener noreferrer`; dangerous containers removed with their content. Applied on every write path (UI, API, MCP, AI accept) inside the
   module's handler / domain method — never only in the browser.
3. **Legacy values stay as they are**: there is no data migration. `IsHtml` decides per value: without markup it is plain text (escaped
   paragraphs on render, paragraphs in the editor); it becomes HTML only when saved from the editor. The programme-request / project
   description columns are widened (5 000 → 20 000) so the formatted HTML fits; the SPEC's 5 000 limit applies to the visible text
   (`RichText.PlainLength`), error `validation.richTextTooLong`.
4. **Plain-text projection** (`ToPlainText`) for exports, e-mail plain parts, search columns, AI prompts (as data) and MCP read output;
   `ToSafeHtml` (sanitised HTML or escaped plain text) for HTML templates (PDF, HTML e-mail body).
5. **Web**: owned `RichTextEditor` (lazy-loaded chunk; Tiptap 3 `@tiptap/react`, `@tiptap/pm`, `@tiptap/starter-kit`,
   `@tiptap/extension-link`, `@tiptap/extension-table` — MIT; in-house `ParagraphDirection` extension for per-paragraph `dir`) and
   `RichTextView` (DOMPurify — MPL-2.0 OR Apache-2.0, used under Apache-2.0 — with the same allow-list). Tiptap Pro / Cloud extensions are not
   used (commercial).

## Consequences
- One allow-list in two places (C# and TS) — both carry a comment pointing at each other and both are covered by tests with the same corpus.
- Stored HTML is larger than its text: limits are measured on the visible text, the column on the stored size.
- Older values, AI drafts (plain text) and API clients that send plain text keep working without conversion.
- Other text fields can switch later one by one without schema changes (if their column is large enough).
