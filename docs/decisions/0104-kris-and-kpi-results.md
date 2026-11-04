# 0104 — Key risk indicators react to approved KPI results

- **Status:** Proposed (Phase 13) — awaiting owner acceptance
- **Context:** SPEC §3.4 D: risk ↔ KPI card links with warning / breach thresholds and direction; an **approved** KPI result crossing the
  threshold notifies the risk owner and Risk Office and flags the risk for reassessment. ADR 0065 publishes `KpiResultApproved`.

## Decision
- `risk.kri` per link: `basis` (`value` = achieved value in the KPI's unit, default · `performance` = capped performance ratio),
  `direction` (`above` / `below`), `warning_threshold` (optional), `breach_threshold`. Level: breach when the value reaches the breach
  threshold in the KRI's direction (inclusive), warning likewise, else normal. Not-applicable results are ignored.
- Handler of `KpiResultApproved` (outbox, idempotent through `last_result_id`): stores the reading; a breach sets
  `reassessment_required` (`kri-breach`), notifies the owner, coordinator and Risk Office (`risk.kriBreach`) and publishes
  `RiskKriBreached`. Warnings notify only when Settings → Risk *notify warnings* is on.
- Risk implements `IKpiLinkedRecordSource` so the KPI card lists its linked risks (badge only for risks the viewer may read).

## Consequences
KRIs on KPIs measured as text / scale use the numeric value the KPI module approved.
