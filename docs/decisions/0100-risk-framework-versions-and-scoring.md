# 0100 — Risk framework: versions, shared scoring engine, band gaps, recalculation

- **Status:** Accepted by the owner (Phase 13, before the pull request)
- **Context:** SPEC §3.3: configurable scales with impact-area criteria, scoring method (multiplicative default, additive, custom formula
  with `L`, `I`, optional `C`), live matrix preview and the count of risks that would change rating, a new framework version on save, and
  the admin's choice to recalculate now or keep risks on the old version until their next review. Default bands Low 1–4, Medium 5–9,
  High 10–16, Critical 20–25 leave scores 17–19 (and fractions) unassigned.

## Decision
- Module table `risk.framework_version`: one immutable row per saved framework (scales, impact areas and criteria, method, formula, bands
  with residual-status mapping, control-effectiveness scale with a `C` factor 0–10, review frequency per band, project residual rule). The
  newest row is current; saving always creates version n + 1 with a reason. Version 1 is seeded on first start.
- One pure `RiskScoringEngine` scores every risk (enterprise and project): multiplicative `L × I`, additive `L + I`, custom formula
  compiled by the shared expression sandbox (ADR 0022) with number variables `L`, `I`, `C`; rounded to 2 dp. The formula is validated on
  save **and on every matrix cell** (no error, finite, ≥ 0) so a saved formula cannot fail at run time.
- **Band mapping:** a score in a gap between two bands maps to the **higher** band (safest; 17–19 → Critical as in Phase 9), below the
  first band → the first, above the last → the last.
- **Recalculation:** `now` re-rates every open risk in the save transaction through EF (one audit entry per changed risk, plus an
  assessment snapshot `recalculation`); `next-review` leaves risks on their version until their next approved review / reassessment, when
  they move to the current version. Both publish `RiskFrameworkChanged`.
- Categories (with appetite, empty by default — Q-R3) are a module table, not a lookup, because they are hierarchical and carry the appetite.

## Consequences
Old versions stay readable (assessment history shows the version used). A risk may show a rating computed on an older version until its
review when the admin chose `next-review`; the risk page shows the version number.
