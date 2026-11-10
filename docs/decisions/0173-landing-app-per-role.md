# 0173 — Landing app per role

- **Status:** Proposed (Phase 20) — awaiting owner acceptance
- **Context:** SPEC-ADDENDUM §C2: "Default landing app per role (admin sets; e.g. PMO → Portfolio, Audit Office → Audit); users with several
  apps can set their own default and pin favourites."

## Decision
- Ordered rules in Settings → Appearance (`landingRules`: role code `system.role` → app). On `/` in Enterprise / Modern: the user's own
  default app if they can open it; else the **first** rule whose role the user holds (`/api/v1/access/me` roles) and whose app they can
  open; else My Work. Pure function `resolveLandingApp` (table-tested); evaluated in the route's `beforeLoad` after the setup redirect.
- **Classic keeps My Work at `/`** — the Classic navigation has no app homes, and the addendum keeps Classic as it is.
- Favourites (profile, ≤ 20 app codes) are shown first in the launcher and in the Modern sidebar.
- No rules are seeded: role codes differ per installation (CLAUDE.md §2 rule 2).

## Consequences
- The landing app is a navigation convenience, not a security decision; saved codes are validated against the app list on the server.
