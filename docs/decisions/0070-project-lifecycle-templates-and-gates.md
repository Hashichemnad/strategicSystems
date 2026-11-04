# 0070 — Project lifecycle: stage templates, gates, one approval at a time, high-level vs detailed plan

- **Status:** Proposed (Phase 9)
- **Context:** SPEC §1.3 / §1.5 W2–W6 and Appendix A Q-P2 / Q-P3: a project runs Initiation › Planning › Execution › Closure › Impact ›
  Completed; an initiative skips charter approval, LPO, team formation, closure approval and Impact ("stage templates per classification are
  configurable"); Impact can be switched off. Take Ownership is open to "any employee from the same section". The charter needs a "high-level
  plan" and the planning needs milestones with weights totalling 100 % ("high-level plan validated separately"; SPEC §1.4 B keeps two sums:
  Total Weight and Total High-Level Weight).

## Decision
- **Stage templates** in Settings → Portfolio, one per classification, five switches (`charter`, `lpo`, `teamFormation`, `closure`,
  `impact`); defaults project = all on, initiative = all off. `lpo` / `teamFormation` require `charter`, `impact` requires `closure`. The
  template is **snapshotted on the project** so running projects never move when the settings change. The stage stepper is derived from it.
- The lifecycle is explicit code (`ProjectLifecycle`): stage + sub-state + status, one transition table, unit-tested. Approvals come only
  through the engine's hooks.
- **One approval at a time per project:** all project processes (`portfolio.charter`, `.planning`, `.monthly-update`, `.closure`, `.impact`,
  `.change-request`) use the project as the approval subject. The engine refuses a second instance in progress; the project header shows the
  process + step ("Department Director – Charter").
- **Take Ownership:** a project with no PM is readable by, and can be taken by, an employee whose unit is the project's section (or a unit below
  it); with no section, the requesting unit. The PM Office may assign a PM instead. Ownership is final except through an approved Ownership
  change request (ADR 0073).
- **Rejected** charter / planning / closure / impact returns the project to its draft sub-state with approval result `rejected` (the PM may
  correct and resubmit; the PM Office may cancel the project with a reason). A rejected *request* stays final (ADR 0048) — a running project is
  not thrown away by one rejection.
- **Initiative completion:** at 100 % approved achievement an initiative waits for the PM Office's *Confirm completion* (a command with
  `portfolio.project.manage`, audited) — no approval process, matching "PM Office confirms".
- **Plans:** milestones carry a `high_level` flag. The **high-level plan** (charter) is the set of high-level milestones and must total 100 %
  at charter submission. The **detailed plan** (planning) is every other milestone; its **leaf** weights must total exactly 100 % at planning
  submission. Phases (headers) carry no own weight: weight and achievement are the sums of their children (one level). Progress uses the
  detailed plan only.
- Charter "team members ≥ 3" counts the **proposed team members** listed on the charter (employee, role, responsibilities); the formal
  project team is formed later through Teams (ADR 0072).

## Consequences
Running projects keep their path; the PM Office changes defaults for new projects only. The gate rules live in one place (`ProjectGates`),
reused by the record page (live checklist) and the submit commands.
