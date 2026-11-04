# 0116 — Capacity view and quarterly portfolio review

- **Status:** Proposed (Phase 11) — awaiting owner acceptance
- **Context:** SPEC §1.12 items left by Phase 10 (ADR 0092).

## Decision
- **Capacity:** allocation % per employee = Σ active memberships of active Teams teams (`allocation %`, read through the new
  Teams.Contracts `ITeamAllocationDirectory`) + Σ proposed charter members (new `allocation_percent`) of active projects that have no formed
  team yet (no double count). Warning when the total exceeds `capacityThreshold` (default 100 %). Filter by unit (and below). Permission
  `portfolio.capacity.read`. Memberships without an allocation count as 0 and are listed as "not set".
- **Quarterly review:** review per quarter with one decision per project (continue / re-scope / hold / cancel + comment). Completing the review
  never changes a project: hold → change request of the new type `hold` (applied by *Apply changes* as on-hold), cancel → `cancellation`,
  both submitted into `portfolio.change-request` (left as draft when another change request is under review); re-scope → a draft `details`
  change request for the PM. Permissions `portfolio.review.read` / `portfolio.review.manage`.

## Consequences
Governance decisions go through the same approval chain as every other change (W7).
