# 0002 — Front-end router: TanStack Router

- **Status:** Accepted (Phase 0)
- **Context:** CLAUDE.md §4.2 offers TanStack Router or React Router, to be fixed in Phase 0.

## Decision
Use **TanStack Router** (MIT), with a **code-based** route tree in `web/src/app/router.tsx`. Feature pages load
lazily (`lazyRouteComponent`).

## Why
- **Typed search params** validated with zod (`validateSearch`). List state (page, sort, filters, search) lives in
  the URL, so views are shareable links and become saved views later. Wrong params fall back to defaults instead of crashing.
- Fully typed links and params: a renamed route is a compile error.
- It fits with TanStack Query, which we already use for server state.

## Consequences
- Code-based routes (no file-based generator plugin) keep the build simple; each module adds its routes in one place.
- The route tree is registered once (`declare module '@tanstack/react-router'`) for type inference.
