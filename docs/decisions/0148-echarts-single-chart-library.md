# 0148 — Apache ECharts as the single chart library; shared dashboard UI kit

- **Status:** Accepted by the owner (Phase 17)
- **Context:** ADR 0096 added ECharts for Portfolio / agile; Phases 6, 7, 12, 13 used interim CSS / SVG charts "until Phase 17"; tiles,
  sections and bars are duplicated in five feature folders; the wrapper imports the whole library.

## Decision
- `web/src/ui/charts/`: one `Chart` wrapper (lazy `echarts/core` with only the used charts, own chunk; theme from tokens; RTL axis
  inversion and legend side handled centrally; `aria` description, `role="img"` and a "show as table" toggle) and typed option builders.
- Replace the CSS bar charts (Tasks, Teams, KPI, IP, DocReg), KPI trend / yearly charts and the Risk movement / history SVGs; move existing
  ECharts users to the wrapper. Keep the risk heat-map as an accessible button grid (moved to `ui/dashboard/HeatGrid`), in-table progress
  bars and the in-house Gantt.
- `web/src/ui/dashboard/`: DashboardFrame, PeriodSelector, Tile, Section, ExportMenu, AsOf, DrillLink, WidgetRenderer, SavedFilters,
  PinButton; feature-local duplicates removed.

## Consequences
No new dependency; smaller main chunk; consistent RTL and accessibility for every chart.
