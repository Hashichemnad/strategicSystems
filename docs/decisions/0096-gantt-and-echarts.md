# 0096 — In-house SVG Gantt, its exports, and Apache ECharts for dashboards

- **Status:** Accepted by the owner (Phase 10)
- **Context:** CLAUDE.md §4.2: read-only in-house SVG Gantt (milestones, phases, quarters, planned vs actual, RTL mirrored), no commercial
  Gantt; Apache ECharts for charts. SPEC §1.11: Gantt export to PDF / image; §1.10: < 5 s for 30 milestones.

## Decision
- `web/src/ui/gantt/` renders SVG from a pure layout function (`ganttLayout`) that is unit-tested: date → x, RTL mirrors x and moves the
  label column to the right, quarter bands above month ticks, planned / actual / baseline bars, achievement fill, diamonds for zero-length
  milestones, dependency arrows, collapsible phases.
- Export to **PNG** via an off-screen canvas and to **PDF** by embedding that image (JPEG) in a one-page PDF written in the browser
  (~60 lines, no library). No server-side rendering of client SVG (avoids SSRF / injection through Gotenberg).
- **Apache ECharts 5 (Apache-2.0)** is added for the portfolio dashboard (bubble, heat-map, bars, funnel) and agile metrics (burndown,
  burnup, velocity, cumulative flow); recorded in THIRD_PARTY_LICENSES.md. The `dir` of the page drives axis inversion and legend side.

## Consequences
No new back-end dependency; Gantt performance and RTL geometry are covered by Vitest and E2E.
