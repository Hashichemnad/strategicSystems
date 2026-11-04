# 0107 — Risk Office is a role; owning unit configurable

- **Status:** Accepted by the owner (Phase 13, before the pull request)
- **Context:** SPEC §3.2 / §3.10: the Risk Office is a role held by the Internal Audit Office now and by the Strategy Office later, moved by
  role reassignment (§0.2 system ownership).

## Decision
- Role `risk.risk-office` (All scope, MFA) is the default approver of every risk process (approval role rule) and owns the framework; it is
  assigned to a unit (its employees hold it) or to users in Settings → Roles. The Risk system's owning unit (Settings → Systems) is
  seeded from `Modules:Risk:OwningUnitCode` when that unit exists (empty by default) so the "owning unit of this system" approver rule
  works too.
- Moving the Risk Office to another department = reassigning the role (bulk role transfer moves pending approvals, SPEC §0.2) and changing
  the owning unit — no code or data change.

## Consequences
The Risk Committee is likewise a role (`risk.risk-committee`); leadership membership is configuration.
