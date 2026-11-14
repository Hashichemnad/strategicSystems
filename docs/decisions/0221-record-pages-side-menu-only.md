# 0221 — Record pages keep only the side menu

- **Status:** Proposed (release 1.0.2) — awaiting owner acceptance
- **Context:** Owner requirement (release 1.0.2, fix 8): "record pages: remove the header tab menu, keep only the side menu with every item
  (same order, counts, deep links `?tab=` unchanged, drawer at 360 px), all themes, update visual baselines." Since Phase 20 (ADR 0170,
  docs/design/themes.md §4) a record page showed its sections twice in Enterprise and Modern: a header tab strip (Radix tabs) and a side column
  of related lists (count cards that opened some of the same tabs); Classic showed only the tab strip. Long tab strips wrapped to several rows
  (the project page has up to 31 sections) and the two navigations disagreed (the related column listed a subset, in another order).

## Decision
- `RecordLayout` (`web/src/ui/record-layout.tsx`) takes the record's sections instead of tab children:
  `sections: { key, label, count?, hidden?, testId?, content }[]`, `activeSection`, `onSectionChange(key)`. The other slots (back, header,
  highlights, path, status, summary, testId) are unchanged; `children` is optional and rendered after the layout (dialogs).
- One side menu in every theme (`RecordSideMenu`, `web/src/ui/record-side-menu.tsx`, owned code): every visible section in the page's order,
  with its count (`formatNumber`, plus a screen-reader "N items"), at the inline start of the content on `md` and wider. The header tab strip
  and the related-lists aside are gone; their counts moved onto the menu items, so there is no duplicate list.
- Pattern: **navigation landmark + region**, not ARIA tabs. The menu is a `<nav>` named "Record sections" of buttons; the active one has
  `aria-current="page"`; every item `aria-controls` the content, a `role="region"` named after the active section. Only the active section's
  content is rendered (as Radix `TabsContent` did). Keyboard: Up / Down move between items (wrapping), Home / End go to the first / last, Enter
  or Space opens; a roving tab index makes the menu one Tab stop (the active item), so a 31-section project menu does not cost 31 Tab presses.
  Rationale: the menu changes the address (`?tab=`) and reads as page navigation in a vertical list; a `tablist` would promise horizontal
  tab semantics and arrow-activation the owner did not ask for.
- Deep links: a click calls `onSectionChange`, and the pages navigate exactly as their tab strips did (`navigate({ search: { tab } })`, or
  local state on the two pages without a `?tab=`: CAP source, team evaluation). The router's search schemas (`web/src/app/router.tsx`) are
  unchanged, so every existing link and bookmark keeps working; an unknown or hidden key shows the first visible section.
- Phones: below `md` (`(max-width: 767.98px)`, read with `matchMedia` through `useSyncExternalStore`, so only one of the two is mounted) the
  menu is a drawer — a Radix Dialog sheet from the inline-start edge — opened by a full-width **Sections** button in the record header that
  names the current section. Choosing a section closes the drawer; Escape closes it; Radix returns focus to the button. Widening the
  window closes it and shows the inline menu.
- Themes: only design tokens (`card`, `muted`, `accent`, `app-accent`, `--radius`, `--shadow-card`) and logical properties. Classic: bordered
  card, active item on the accent fill (as the Classic sidebar). Enterprise: card with the top accent bar, active item with the inline-start
  accent bar (as the app tab bar). Modern: rounded card, active item with the rounded accent marker (as the app sidebar). Light and dark come
  from the tokens. Enterprise keeps its highlights panel and path bar; Modern keeps its key-facts card, now under the menu (above the
  content on phones).
- Migration: all 17 record pages pass their sections in their former tab order with the counts they showed before (in the tab strip or the
  related column). Menu items keep the former trigger test ids (`risk-tab-*`, `cap-action-tab-*`, `document-tab-*`, `ip-tab-*`,
  `audit-tab-*`, `bench-tab-*`, `cap-tab-*`, `request-tab-benefits`); others are `record-section-<key>`. E2E specs reach a section with
  `recordSection(page, language, label)` (`web/e2e/fixtures.ts`); the axe audit walks the menu instead of the tabs.
- `Tabs` / `TabsList` stay in the UI kit for tab sets inside a section or on pages that are not records (settings, profile, org unit details,
  vendor and audit plan pages).
- Visual baselines (ADR 0175): the UI kit gains a static record page (no live data, so pixels do not depend on the database); `visual.spec.ts`
  adds a record shot and a 360 px drawer shot per theme × mode × language. Regenerated in the pinned container.

## Consequences
- Every record page reads the same in Classic, Enterprise and Modern: one list of sections, counts beside them, no wrapping tab rows.
- The content column is narrower on `md`–`lg` (12–14 rem menu); wide tables inside sections keep their own horizontal scroll regions.
- New record pages (and the two being added in 1.0.2) use the same `sections` API; there is no tab-strip variant to choose.
- `RelatedList` and the `appearance:record.related` string are removed; `appearance:record.sections` / `record.sectionsButton` are added (AR + EN).
- Supersedes the "tabs" and "related lists" parts of the record-page row in docs/design/themes.md §4 (ADR 0170); the rest of ADR 0170 stands.
