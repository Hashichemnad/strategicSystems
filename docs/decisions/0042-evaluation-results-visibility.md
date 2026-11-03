# 0042 — Who sees team evaluation results

- **Status:** Accepted by the owner (Phase 7, before the pull request)
- **Context:** SPEC §4.2 / §4.9: "Members do **not** see evaluation results"; "evaluation results visible only to leader roles and HR".
  §4.7 also lists the Department Manager as viewing "teams and memberships / evaluations of their staff".

## Decision
- One permission, **`teams.evaluation.results`**, guards every result value (team score, criterion levels, member points, grades, rewards,
  certificates, evidence). It is held by the *Teams administrator* role (HR, All) and by record roles on the team: `results_viewer`
  (granted automatically to active members whose **member role** has *can view evaluation results* — by default only leader roles:
  President, Project Manager), `evaluator` / `leader_evaluator` (during their cycle) and `evaluation_approver`.
- Every read path asks `IAccessService.CanAsync(teams.evaluation.results, teamRef)`: evaluation endpoints (others get the cycle's status
  only), team page, employee Teams tab, register (last score column), search and AI record sources (Ask / `get_record`), MCP tools,
  dashboards (score, grade and reward charts are computed only over teams where the caller holds results in scope), file downloads of
  certificates and evidence.
- The **Department manager** default role reads teams and memberships of its unit and below but **not** results: the §4.9 decision is the
  later and more specific one. An installation may grant `teams.evaluation.results` with scope Unit-and-below to that role in Settings.
- Members' own certificates are not downloadable by them by default (setting `allowMemberCertificateDownload`, off); HR hands them over.

## Consequences
Changing a member's role re-evaluates `results_viewer` at once (granted / revoked in the same transaction). Tests prove a plain member
gets no result values from the API, search, Ask and MCP while the President does.
