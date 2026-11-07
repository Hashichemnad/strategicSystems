# Rich text (Phase 19, stream R — SPEC-ADDENDUM A2)

ADR: [0161](../decisions/0161-rich-text.md). Closes the Phase 14 known gap "Rich text shown as plain text (no DOMPurify)"
(`docs/release-notes/known-gaps-review.md`; the lead updates the review).

## 1. Requirement

Fields that are rich text in the SPEC use a Tiptap editor (bold, italic, underline, strike, headings, bullet / numbered lists, quote, code,
links, tables, RTL / LTR / auto per paragraph). They are stored as **sanitised HTML** (server allow-list), rendered through **DOMPurify** with
the same allow-list, and leave the system as **plain text** for exports, e-mail plain parts, search columns, AI prompts and MCP read output.
Existing plain values are **not rewritten**: a value without HTML markup is plain text, renders as escaped paragraphs / line breaks, loads
into the editor as paragraphs, and becomes HTML only when someone saves it from the editor.

## 2. Inventory (SPEC → fields switched to rich text)

Only these fields switch; every other text field stays a plain `textarea` / input.

| Module | SPEC | Entity . column | DTO / input | Screen (editor → read view) | Limits |
|---|---|---|---|---|---|
| Portfolio | §1 data model "Team Meeting (MOM) — Notes (rich text)" | `portfolio.meeting.notes` (`Meeting.Notes`) | `ProjectMeetingDto.notes`, `MeetingValues.Notes`, vendor `VendorMeetingDto.notes` | Project → Meetings tab dialog → Meetings table cell; Wasel portal meetings | 20 000 stored |
| Teams | §4.4 C "Meetings & minutes: agenda, minutes, decisions" (A2 "minutes") | `teams.meeting.agenda`, `.minutes`, `.decisions` | `CreateMeeting` / `UpdateMeeting.Agenda`, `SaveMinutes.Minutes` / `.Decisions`, `MeetingDto` | Team → Meetings → meeting dialog (agenda) and minutes dialog → meeting detail | 20 000 stored |
| Request Framework | §5 form builder "field types (text, **rich text**, …)" | `requests.request.data` (jsonb) fields of type `richtext` | form values | Request form (`FormRenderer`) → request read view | field `maxLength` (default 20 000) visible characters, 40 000 stored |
| Portfolio | §1 request "Program Description (5000); Program Justifications (5000)" (A2 "descriptions, justifications") | `portfolio.program_request.description`, `.justifications`; `portfolio.project.description` (copied from the request) | `RequestValues.Description` / `.Justifications`, request and project DTOs | Request wizard step 1 → request page; project details | 5 000 visible characters, 20 000 stored (columns widened) |

Stay plain (not rich text in the SPEC, short, or structured): approval / return comments and reasons (short, audited, shown in timelines and
e-mails), lessons, challenges, risk texts, KPI notes, CAP / audit findings and recommendations, document-registry and IP descriptions
(the SPEC's "policy text" lives in the registered *files*, previewed by pdf.js), benchmarking notes, task descriptions, change-request
texts. They can be switched later field by field with the same building blocks (`RichText.Sanitize` + `RichTextEditor` / `RichTextView`).

## 3. Server — `Strategy.Platform.Kernel.Text.RichText`

- Library: **HtmlSanitizer** (mganss, MIT) on **AngleSharp** / **AngleSharp.Css** (MIT) — maintained, DOM-based (no regex parsing).
- **Allow-list** tags: `p br strong b em i u s h2 h3 h4 ul ol li blockquote a table thead tbody tr th td colgroup col code pre hr`.
  Attributes: `dir` (only `ltr` / `rtl` / `auto`, only on block elements `p h2–h4 ul ol li blockquote table th td pre`), `href` (only
  `http` / `https` / `mailto`, absolute; entity-encoded / obfuscated schemes are decoded by the parser first), `target` (only `_blank`),
  `rel` (always rewritten to `noopener noreferrer`), `colspan` / `rowspan` (td / th, 1–100). No `style`, `class`, `id`, `data-*`, `on*`.
- Tags outside the list are removed **keeping their text** (`div`, `span`, `h1`, `font` …) except dangerous containers, removed **with their
  content**: `script style iframe frame object embed applet svg math template noscript textarea select button form input link meta base img
  video audio source canvas …`. Comments removed.
- `IsHtml(value)`: true when the value contains a tag of a known HTML element (`<p>`, `</b>`, `<script …>`) or a comment; `a < b`, `x <y>`,
  `&lt;b&gt;` are plain. Plain values are stored trimmed and otherwise **unchanged** — this is how legacy values survive.
- `Sanitize(value)` → null (blank or empty markup) / plain value / sanitised HTML. `ToPlainText(value)`: block elements and `br` → new lines,
  `• ` / `1. ` list prefixes, table rows on their own line with cells joined by ` | `, entities decoded, whitespace collapsed (kept in `pre`).
  `ToSafeHtml(value)`: sanitised HTML, or escaped plain text as `<p>` paragraphs with `<br>` — for PDF templates and HTML e-mails.
- Length: `PlainLength` (what the user sees; the SPEC limits such as "5000" apply to it) and `StoredLength` (column size).
  `RichTextRules.RichTextLength(plainMax, storedMax)` (FluentValidation) and `RichText.LengthError(field, …)` (hand-written validators) return
  `validation.richTextTooLong` with `maxLength` (ar + en in `validation.json`).
- Applied in: Portfolio `SaveMeetingAsync` (replaces the Phase 7 regex `RichText` class), request save / submit (`RequestRules` +
  `ProgramRequest.Apply`), Teams `CreateMeeting` / `UpdateMeeting` / `SaveMinutes` (domain methods sanitise, validators measure), Request
  Framework `FormValidation` for `richtext` fields (was stored unsanitised before).
- Plain-text projections: Request Framework PDF (`RequestPdf`), Portfolio AI prompts (request review / charter assists: description,
  justifications), Teams minutes-writer input, and — centrally — every `AiRecordField` value the AI platform sends to a model ("Ask the
  system" record tool, submission review), so MCP / AI read paths of any module only ever see plain text. None of the inventoried fields
  is part of an Excel export, a `*_search` column or an e-mail template today; when one is added, it must use `ToPlainText`
  (exports, search, e-mail plain part) or `ToSafeHtml` (HTML e-mail body, PDF).
- Every `<a>` gets `rel="noopener noreferrer"`, also one whose unsafe `href` was removed.
- No migration rewrites data. One schema migration widens `program_request.description` / `.justifications` and `project.description` from
  5 000 to 20 000 characters (stored HTML); the 5 000-character SPEC limit is enforced on the visible text.

## 4. Web — `/web/src/ui/rich-text*`

- `rich-text.ts`: the allow-list (same as the server), `isHtml(value)` (same rule), `sanitizeHtml(value)` with **DOMPurify**
  (`ALLOWED_TAGS` / `ALLOWED_ATTR`, `ALLOWED_URI_REGEXP` http / https / mailto, hook forcing `rel="noopener noreferrer"`, `dir` values
  checked), `toPlainText(value)` for previews.
- `RichTextView`: HTML → DOMPurify → `dangerouslySetInnerHTML` inside a `.rich-text` container (typography with logical properties); plain
  value → escaped paragraphs with line breaks (React text, never HTML). Empty → fallback (`—`).
- `RichTextEditor` (lazy chunk `rich-text-editor.tsx` via `React.lazy`, Suspense fallback = a skeleton): Tiptap `@tiptap/react` +
  `StarterKit` (link, underline included in v3), `@tiptap/extension-table` (`TableKit`), and the in-house `ParagraphDirection` extension
  (`dir` global attribute on paragraph / heading / blockquote / list item / table cell; commands `setDirection('rtl' | 'ltr' | 'auto')`;
  new paragraphs default to `auto`; the editor root gets `dir` from the UI language). Toolbar: `role="toolbar"`, roving buttons with
  `aria-label` + `aria-pressed` (ar + en, `common:richText.*`), Tab / Shift+Tab leaves the toolbar, arrow keys move inside it. Controlled
  value / `onChange(html | null)` for react-hook-form and plain state; legacy plain value loaded as paragraphs (escaped, split on blank
  lines / line breaks). Link insertion through a small inline form (no `window.prompt`), with scheme check.
- Light / dark via the design tokens; logical CSS only; toolbar wraps at 360 px.

## 5. Screens changed

Project → Meetings (dialog editor, table cell view) · Wasel meetings view · Team → meeting dialog (agenda) and minutes dialog, meeting detail ·
Request Framework form (`richtext` fields) and request read view · Portfolio request wizard step 1 (description, justifications), request
page and project details.

## 6. Tests

- Unit (`Kernel/RichTextTests`): XSS corpus (script / iframe / object / embed / svg / math / img / form / template / noscript, `on*`,
  `javascript:` / `data:` / `vbscript:` incl. entity-encoded and whitespace-obfuscated, protocol-relative, `style` with `expression()` / `url()`,
  `class` / `id` / `data-*`, nested and malformed markup, `<a target>` without rel), allow-list round trip, attribute normalisation,
  `ToPlainText` table, legacy detection, `ToSafeHtml`, lengths, validator rule.
- Integration: Portfolio meeting save (script removed, legacy plain value returned unchanged), Teams minutes save (sanitised, plain
  unchanged), Request Framework `richtext` field sanitised, AI / export path receives plain text.
- Vitest: `RichTextView` (DOMPurify strips script / handlers / `javascript:` links, plain text escaped with line breaks), `rich-text.ts`
  helpers, `RichTextEditor` (toolbar names in ar / en, bold toggles, direction toggle sets `dir`, arrow-key toolbar, plain value loads as
  paragraphs, lazy wrapper), `FormRenderer` rich-text read view.
- Playwright `web/e2e/rich-text.spec.ts` (ar + en): formats a programme request description in the wizard (bold, bulleted list, a
  paragraph in the other direction), saves the draft, sees it rendered on the request page, and checks that a script sent through the API is
  removed; screenshots `<lang>-rich-text-editor.png`, `<lang>-rich-text-view.png` in `docs/screenshots/$E2E_SCREENSHOTS/`.
- Existing E2E specs that `fill` the description / justifications by label keep working: the editor's `contenteditable` carries the field
  label as `aria-label`, and Playwright fills `contenteditable` elements.
