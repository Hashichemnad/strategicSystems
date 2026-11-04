# 0105 — Risk screens: CSS / SVG heat-maps and charts; reports through the platform renderers

- **Status:** Proposed (Phase 13) — awaiting owner acceptance
- **Context:** The prompt allows introducing Apache ECharts for heat-maps if Phase 10 has not; Phase 10 is built in parallel and may add
  charting to the shared web folder. Heat-maps are 5 × 5 clickable grids that must mirror correctly in RTL and be keyboard operable.

## Decision
- No new dependency: heat-maps are a CSS grid of buttons (`web/src/features/risk/HeatMap.tsx`, logical properties, `aria-label` per cell,
  band colours from the framework), the assessment history and movement charts are small SVG components like the KPI charts (Phase 12).
  ECharts remains the Phase 17 choice for cross-system dashboards; nothing in `web/src/ui` is changed.
- Reports: Excel through `ExcelExport` (ClosedXML), PDF through the Gotenberg renderer with the bilingual HTML templates, exports audited.

## Consequences
No `THIRD_PARTY_LICENSES.md` change; Phase 10 / 17 may later replace the SVG charts without touching the API.
