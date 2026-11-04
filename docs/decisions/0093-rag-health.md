# 0093 — Project health (RAG): calculation and override

- **Status:** Proposed (Phase 10)
- **Context:** SPEC §1.12: RAG on schedule, cost, scope, risk and overall, auto-calculated with manual override + justification; §1.16:
  overdue tasks feed the health RAG. No thresholds are given.

## Decision
- Thresholds in Settings `health` (defaults): schedule gap ≤ 5 points green, ≤ 15 amber (or a leaf ≤ 30 days past due, or overdue linked
  tasks), else red; cost variance (actual − budget × progress) ÷ budget ≤ 5 % green, ≤ 15 % amber, else red; scope: approved scope / details
  changes in the last 180 days 0 green, 1 (or one under review) amber, ≥ 2 red; risk: highest active residual band critical red, high
  amber, else green. Grey when there is no data. Overall = worst of the four.
- Calculated on read from facts loaded in bulk (no stored, stale value). Override per dimension (and overall) by the PM or PM Office with a
  justification (required); effective = override ?? auto; clearing the override returns to auto. Overrides are audited.

## Consequences
Dashboards and the record page show identical colours; the thresholds are configuration, not code.
