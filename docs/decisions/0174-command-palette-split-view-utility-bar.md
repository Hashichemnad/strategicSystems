# 0174 — Modern command palette, split-view lists and the Enterprise utility bar

- **Status:** Proposed (Phase 20) — awaiting owner acceptance
- **Context:** SPEC-ADDENDUM §C3–C4 and A1: Ctrl+K command palette (records, pages, actions) in Modern; split-view lists; Enterprise utility
  bar with quick notes / recent items.

## Decision
- **Palette:** the Phase 19 `CommandSearch` combobox gains an `inline` mode and runs inside a dialog. Records come from the same hook as the
  top-bar search (`useGlobalSearchGroups`, so results are identical and permission-filtered on the server); *Pages* are the visible apps and
  tabs (matched in both languages, Arabic-normalised); *Actions* are the visible apps' quick actions the user holds a permission for, plus
  language, mode (when allowed) and theme (when allowed). In Modern Ctrl / ⌘ + K opens the palette; in Classic and Enterprise it keeps
  focusing the top-bar search.
- **Split view:** `useListView(key)` + `ListViewToggle` + `SplitView`: Enterprise defaults to the table with a toggle, Modern to list +
  preview; the choice is remembered per list in the browser; Classic shows the table only. Below `lg` the preview column is not shown and
  rows open the record. The preview uses list data only (no extra request).
- **Utility bar (Enterprise, `md` and wider):** *Recent items* (Phase 19 server-side recent items, re-checked on read) and *Quick note*
  (kept in the browser's localStorage only, never sent to the server — no new personal-data store).

## Consequences
- Inline edit and mass actions remain where Phases 1–18 built them; no new endpoints for them in Phase 20.
