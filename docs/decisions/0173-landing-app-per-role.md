# 0173 — Landing app per role

- **Status:** Accepted by the owner (11 Nov 2026), Phase 20
- **Context:** SPEC-ADDENDUM §C2: "Default landing app per role (admin sets; e.g. PMO → Portfolio, Audit Office → Audit); users with several
  apps can set their own default and pin favourites."

## Decision
- Ordered rules in Settings → Appearance (`landingRules`: role code `system.role` → app). On `/` in Enterprise / Modern: the user's own
  default app if they can open it; else the **first** rule whose role the user holds (`/api/v1/access/me` roles) and whose app they can
  open; else My Work. Pure function `resolveLandingApp` (table-tested); evaluated in the route's `beforeLoad` after the setup redirect.
- **Classic keeps My Work at `/`** — the Classic navigation has no app homes, and the addendum keeps Classic as it is.
- Favourites (profile, ≤ 20 app codes) are shown first in the launcher and in the Modern sidebar.
- No rules are seeded: role codes differ per installation (CLAUDE.md §2 rule 2).

- **Fallback confirmed at acceptance:** a user none of whose roles matches a rule (no rules configured, or rules only for roles they do
  not hold, or rules whose app they cannot open) lands on **My Work**. Tests: Vitest `resolveLandingApp` table (no rules; roles matching
  no rule; inaccessible app) and E2E *landing app: My Work when no rule matches…* (`themes.spec.ts`, ar + en).

## Consequences
- The landing app is a navigation convenience, not a security decision; saved codes are validated against the app list on the server.
