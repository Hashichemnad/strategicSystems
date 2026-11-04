# 0048 — Request approval chain, status per step and the Approved Budget

- **Status:** Accepted by the owner (Phase 8, before the pull request)
- **Context:** SPEC §1.5 W1 / Appendix A Q-P4: Section Manager (when applicable) → Department Director → PM Office → DG Office (budget >
  threshold) → Budget Section (budget required). The Salesforce status lists one value per step (Manager / PMO / DGO / Budget Approval);
  steps are configuration in the Approval Settings centre (CLAUDE.md §9), so codes may be renamed, added or removed. The Budget step "sets
  the Approved Budget" but the approval engine's decision carries no business values.

## Decision
- Process `portfolio.request` is seeded with five steps (`section-manager`, `department-director`, `pm-office`, `dg-office`,
  `budget-section`) and conditions written in the expression language with the variables `requiresBudget`, `estimatedBudget`,
  `threshold` (from Settings → Portfolio at submission), `classification`, `evaluationScore`. Roles `portfolio.pm-office`,
  `portfolio.dg-office`, `portfolio.budget-section` are the default approvers.
- The request keeps one status **`in-approval`** plus the **current step code and AR / EN name** (updated by the `StepEntered` hook). Lists
  and the stage stepper show the step's name, so renamed or added steps need no code. The well-known codes only choose an icon.
- **Approved Budget:** the step named by `budgetStepCode` (default `budget-section`) lets its current assignee (or delegate) set the
  approved budget on the request (`POST …/approved-budget`) before deciding; audited like any change. On final approval an unset approved
  budget defaults to the estimated budget (no budget → none).
- Return for correction puts the request back to Draft with approval status `returned`; resubmission starts a new instance linked to the
  returned one (engine behaviour). Reject is final (`rejected`, W1.6 "Stop").

## Consequences
The chain can be reshaped in Settings without code. Approvers who are not at the budget step cannot change the budget.
