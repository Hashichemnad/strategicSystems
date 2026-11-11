# 0172 — Application workspaces: an app registry derived from the existing routes

- **Status:** Accepted by the owner (11 Nov 2026), Phase 20
- **Context:** SPEC-ADDENDUM §C2: in Enterprise and Modern each system feels like a separate application — launcher, workspace header,
  own tabs, own home page (system dashboard + my items + quick actions), accent colour; shared apps My Work, Dashboards, Settings, Tasks,
  Teams; per-system appearance settings; deep links keep working across themes.

## Decision
- One **app registry** in code (`web/src/layout/apps.ts`): app code (same list as the server's `AppearanceCatalogue.Apps`, checked by a
  unit test), group (systems / shared), access gate (system code), default icon and accent, URL prefixes, tabs (route + permissions), home
  dashboard, quick actions. No route moves: the **current app is derived from the URL** (longest prefix), so a deep link opens the same
  page in every theme and Classic is untouched. New routes only: `/apps/$app` (home pages of the system apps and Tasks / Teams) and
  `/my-work` (My Work without the landing redirect of `/`).
- **Visibility** = the Classic sidebar's rule: the user may open the system and holds a permission of at least one tab. The launcher,
  workspace tabs, command palette and home pages read the same function. The server still checks every call (CLAUDE.md §7).
- In the workspace themes the app tabs replace each module's secondary navigation (`data-module-nav`, hidden by CSS), so a page shows one
  navigation. Domain-specific filters of those navigations (e.g. Audit Office-only tabs) become permission checks on the tabs; a tab a user
  should not use leads to the page's own access check, as a typed URL does.
- **Per-app overrides** (Settings → Appearance → Applications): names AR / EN, icon (curated Lucide list, validated as a code), accent
  (`#rrggbb`, warning below 3 : 1 with white), tab order, hidden tabs, home dashboard. They never grant access; home is never hidden.
- Accent colours of the defaults all have ≥ 3 : 1 contrast with the white icon (unit test).

## Consequences
- Adding a page to a system = one tab entry (and its AR / EN label); the Classic sidebar entry is unchanged.
- My Work items on a home page are the user's My Work filtered by system code — no new API.
