# 0062 — Strategy hierarchy, cascading and weighted scorecards

- **Status:** Accepted by the owner (Phase 12, before the pull request)
- **Context:** SPEC §2.7: Vision → Pillars / Perspectives → Objectives → KPIs, strategy map, cascading organisation → department → section
  KPIs with roll-up, weighted scorecards using capped performance.

## Decision
- One table `strategy_element` with fixed kinds `vision`, `perspective`, `objective` (labels translatable, so "Pillar" is a label change).
  The card's balanced-scorecard perspective is the parent of its objective — no separate perspective field that could disagree.
- Weights are optional numbers per element / card within the parent; null weights mean equal. Nodes without a score are left out and the
  remaining weights re-normalised. Inputs are **capped** performance (SPEC K5), so no node exceeds 100 %.
- Cascading: `parent_card_id` on the card; the parent shows a cascaded score (weighted average of children) and uses it in scorecards when it
  has no approved results of its own.
- Department scorecard: KPIs whose responsible unit is the department or below.
- Scores use approved results only (year-to-date performance of the annual plan, design note §5.3).
