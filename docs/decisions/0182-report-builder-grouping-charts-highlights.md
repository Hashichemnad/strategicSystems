# 0182 — Report builder: grouping, summaries, charts, highlighting, relative dates, CSV

- **Status:** Accepted (Phase 21)
- **Context:** E2: columns across the report type, filters incl. relative dates, grouping up to 3 levels with subtotals, summary formulas,
  charts (bar, line, pie / donut, stacked), conditional highlighting, exports Excel / PDF / CSV bilingual and RTL.

## Decision
- The report definition gains (additively) `reportType`, qualified columns, `summaries`, `chart`, `highlights`, `showDetails`; grouping
  ≤ 3. Phase 17 definitions keep running on the Phase 17 path.
- Subtotals, grand total, chart data and tones are computed **on the server** over all composed rows (pure `ReportAggregation`, unit-tested),
  so the screen, exports and scheduled e-mails show the same figures; the client renders the chart with ECharts (ADR 0148), the PDF uses
  a server SVG.
- Relative dates (`relative` operator: today … last / next N days, this / last / next week, month, quarter, year) resolve to a range in the
  installation time zone at every run; quarters are calendar quarters shifted by the fiscal start month.
- CSV: UTF-8 with BOM, header labels in the chosen language, comma separated, formula-injection guard (`=`, `+`, `-`, `@` prefixed with `'`).

## Consequences
Charts and subtotals never disagree with the table; highlights are conditions on raw values, not on formatted text.
