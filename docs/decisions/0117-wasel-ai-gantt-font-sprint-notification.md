# 0117 — Wasel AI (PM side), bundled Gantt export font, sprint review notification

- **Status:** Accepted by the owner (Phase 11)

## Decision
- Feature `portfolio-vendor-review` for the PM: server-computed inconsistency flags (100 % without evidence, progress jump > 30 % of the
  weight vs last approved, dates outside the assignment / contract or before the milestone's start, delayed without causality) + model summary
  + draft "return to vendor" comment (AR / EN). Drafts only; vendors never reach AI.
- Gantt PNG / PDF export embeds IBM Plex Sans Arabic / IBM Plex Sans (OFL, `@fontsource`, already bundled) as data-URL `@font-face` in the
  exported SVG, replacing the system-font fallback (Phase 10 gap).
- `portfolio.sprintReviewed` is sent to the PM and owner when a sprint review approval completes.

## Consequences
No new dependency.
