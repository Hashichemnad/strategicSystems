# 0094 — Baselines, re-baselining through a change request, variance and earned value

- **Status:** Proposed (Phase 10)
- **Context:** SPEC §1.12: plan baselined at planning approval, re-baselined only through an approved change request, variance on dates,
  cost, weights; EVM (PV, EV, AC, SPI, CPI, EAC). Q-P9: the apply dialog is pre-filled incl. re-baseline. Phase 9 kept the milestone plan
  frozen after planning approval.

## Decision
- `baseline` versions with a jsonb snapshot of milestones / epics (dates, weight, cost) plus budget and project dates. v1 at planning approval.
- A new version is created **only** by *Apply changes* of an approved change request with **Re-baseline** ticked; no other endpoint exists.
- A change request with the new area **plan** re-opens planning of milestones / epics while it is approved and not yet applied; applying with
  re-baseline requires weights = 100 %.
- EVM uses the common progress: BAC = baseline budget (else budget), PV = BAC × baseline planned-to-date, EV = BAC × progress, AC = Σ actual
  yearly costs entered on the charter (payment milestones are not added, to avoid double counting); SPI, CPI, SV, CV, EAC = BAC ÷ CPI, ETC,
  VAC, TCPI; ratios are null on a zero divisor. EVM is behind its own switch (default off) and needs cost visibility (ADR 0098).

## Consequences
Variance is always against an approved plan; re-planning is governed exactly like other changes.
