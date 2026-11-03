# 0041 — Teams configuration: module-owned tables, versioned templates and reward matrices

- **Status:** Accepted by the owner (Phase 7, before the pull request)
- **Context:** SPEC §4.3 asks for configurable team types (with permanence, approval, memo, linkable types, cadence, evaluation trigger,
  evaluator), member roles (leader, can view results, quorum), evaluation templates **versioned per year** (criteria with weights and levels,
  member questions, grade bands, evidence threshold) and a reward matrix (grade × workload × role) versioned per year with a budget. Picklists
  (`settings.lookup_value`) hold code + labels + metadata, which is too weak for validated, versioned structures.

## Decision
- Teams owns its configuration in schema `teams`: `team_type`, `member_role`, `evaluation_template`, `reward_matrix`; seeded once by a
  module `IDatabaseSeeder` (idempotent: inserts missing codes, never overwrites edits), edited in Settings → Teams (`teams.settings.manage`),
  audited like business data. Codes are stable; labels AR / EN.
- Templates and matrices have `year`, `version_no` and `draft` / `published` / `retired`. **A published version is immutable**; editing
  creates the next draft. Publishing retires the previous published version of that year. Evaluations snapshot the template version they
  were opened with (the version id is stored; the rows are never changed afterwards). Rewards use the matrix published for the evaluation's
  year when calculated; the matrix id is stored with the result.
- Validation (pure, unit-tested): weights total 100; level scores within the weight; data-backed criteria have contiguous numeric ranges;
  **grade bands are integer ranges covering exactly [questions × 1, questions × 5] with no gap and no overlap**; evidence threshold 0–100.
- Q-T5: the reward matrix is seeded **empty** (amounts 0) for the current year with *Very Active* earning a certificate; the administrator
  fills amounts and the annual budget.
- Teams is a core system (`SystemKind.Platform`, always on) like Tasks (ADR 0036).

## Consequences
Year roll-over needs no code: an administrator copies last year's template into a new draft for the new year (button "New version"). If no
template is published for a cycle's year, the newest published one is used and the evaluation shows its year.
