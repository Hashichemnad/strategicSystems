# Phase 9 — Portfolio Management B: Project lifecycle and change control: summary

**Status:** built and verified · ADRs 0070–0076 accepted by the owner · rebased on Phase 14 (`main` after PR #11)
**Branch:** `claude/dreamy-keller-t7ikki`
**Planning:** the plan is the Phase 9 part of [`docs/design/portfolio.md`](../design/portfolio.md) (§15–§28), committed before the code
(`docs(portfolio): Phase 9 design note…`). As in earlier phases the session ran without an interactive approval step; open choices are ADRs
0070–0076 (status *Proposed*, for the owner).

## What was built

| Area | Result |
|---|---|
| **Lifecycle** (`ProjectLifecycle`, ADR 0070) | Stage + sub-state + status, one transition table. Project: Initiation (Take Ownership → charter → charter approval → LPO → team formation) › Planning › Execution › Closure › Impact › Completed. Initiative (Q-P2): Take Ownership › Planning › Execution › PM Office *Confirm completion* at 100 %. **Stage templates** per classification in Settings → Portfolio (charter, LPO, team formation, closure, **Impact switch** — Q-P3), snapshotted on the project. Hold / resume / cancel with a reason (PM Office); cancel stops the running approval |
| **Take Ownership** | Any employee whose unit is the project's section (or below) sees an unclaimed project and takes it; the PM Office may assign. PM record role `pm`; notification |
| **Charter + gate** | Charter details (scope, objective, themes, structural changes, resources, contracted / actual / operating costs), KPIs, risks, stakeholders, **proposed team members**, the **high-level plan**. Submission blocked until ≥ 1 KPI, ≥ 1 risk, ≥ 1 stakeholder, ≥ 3 members, high-level weights = 100 % (all failures together, live checklist on the page) |
| **Approvals** (Approval Settings centre, seeded) | `portfolio.charter`, `.planning`, `.closure` (section manager when applicable → director → PM Office), `.monthly-update` (director → PM Office, Q-P1), `.impact` (PM Office), `.change-request` (director → PM Office → Finance Service Section when cost affected → GM Office for cancellation / merge or a cost change above `gmThreshold`, Q-P4). One approval at a time per project; approval status shown phase-qualified |
| **LPO** | On charter approval Administration role holders get record role `procurement` and are notified; they upload the LPO and contracts / POs and confirm → PM Office notified |
| **Team formation** (ADR 0072) | `portfolio.project` is task-able and team-able. The PM Office requests a team from the Team tab (Teams formation chain + memo). Teams now publishes **`TeamMembershipChanged`**; Portfolio moves the project to Planning when its team is Active and **syncs record role `team-member`** to the active members (project-team members get project access and may contribute) |
| **Planning** | Planning items (scope in / out, assumptions, constraints, deliverables, added values, resources); milestones with one level of phases (`<Number>-n.m`), detailed leaf weights **exactly 100 %** at submission (≤ 100 % on every save), deliverable fields, high-level plan validated separately |
| **Monthly cycle** (ADR 0071) | Hourly job `portfolio.monthly-cycle`: window opens at the **first working moment on or after the 1st** in the project unit's business hours, due at the end of the 10th working day, reminder 3 working days before, overdue notice; idempotent; PM Office *open now* and additional windows in the same month (sequence). Milestone updates: achieve ≤ weight and **≥ approved (locked)**, actual start, **actual end + evidence at 100 %**, **causality + corrective actions when delayed**, open-tasks warning. Approval locks achievement, recalculates performance / planned-to-date / On Track–Delayed (Q-P6, 5-point tolerance), 100 % → Closure / completion |
| **Closure / Impact** | Summary, main deliverables, lessons learned; closure approval publishes `LinkedRecordClosed(closure-approved)` (Teams opens the project-team evaluation) and `ProjectClosureApproved`; Impact form (cost performance, three satisfactions, previous operational cost) → PM Office → Completed |
| **Change requests** (ADR 0073) | Details, cancellation, postponement, ownership, merge; draft → under review → approved / rejected (return → draft); **Apply changes** is a separate PM confirmation pre-filled from the request, applied in one transaction with a change log; ownership **moves the PM / owner record roles**; cancellation / merge cancel the project |
| **Other children** | Challenges / issues, meetings (MOM), lessons, contracts / POs, documents; tasks for the project and per milestone through Task Management (`LinkedTasksPanel`) |
| **Integrations** | Strategic objectives now come from the **KPI strategy hierarchy** (`IKpiStrategyCatalog`, ADR 0075; Phase 8 lookup values migrated by a seeder, codes kept). Project KPIs: new (goal status via `IKpiPerformance`) or existing card (`IKpiCatalog`); Portfolio implements `IKpiLinkedRecordSource`. Project risks: 5×5 scoring behind `IProjectRiskScoring`, scores stored, `IProjectRiskDirectory` for Phase 13 (ADR 0074) |
| **Current status, notifications, My Work** | Project banner messages of SPEC §1.6 with actions; 11 new AR / EN templates; My Work items (take ownership, charter / planning / closure / impact to submit, monthly update due / returned, LPO to upload, team to form, initiative to confirm, approved change to apply) |
| **MCP** (ADR 0076) | `portfolio_get_project` (extended), `portfolio_my_projects`, `portfolio_project_milestones` (read, on), `portfolio_update_milestone_draft` (write, **off**, never submits). No AI features in this phase (charter assistant, plan generator, update assistant → Phase 10) |
| **Web** (AR + EN, RTL / LTR) | Project record page: header chips, **stage stepper** from the template, current-status banner, progress strip, tabs Overview · KPIs · Risks · Stakeholders · Team · Scope · Assumptions & constraints · Added values · Milestones · Monthly updates · Tasks · Challenges · Meetings · Lessons · Contracts · Change requests · Closure · Impact · Approvals · Documents · History, with their dialogs (charter, milestone, monthly update with evidence, apply changes, hold / cancel). Projects list (PM, progress, status, views). Settings → Portfolio (stage templates, monthly values, tolerance, GM threshold) |

## Acceptance checks (BUILD_PLAN Phase 9)

| Check | Evidence |
|---|---|
| A full demo project runs from Take Ownership to Completed, in Arabic, with every approval and notification | Integration `A_project_runs_from_take_ownership_to_completed_with_every_approval_and_notification` (6 approval instances, notifications at every step). E2E `portfolio-projects.spec.ts` in **ar** and en: request → Take Ownership → charter gate → approvals → LPO → Teams formation → planning → monthly → change request → closure → impact → Completed |
| Monthly update cycle opens on the 1st (fake clock), reminders sent, update approved and locked | Integration `Monthly_window_opens_on_the_first_working_moment…` (Saturday 1 May 2027 → Monday 07:30 Asia/Dubai, due 14 May 15:30, reminder 11 May, overdue, idempotent, no pile-up); lock asserted in the full-lifecycle test (`achieveBelowApproved`) |
| Ownership change request, once approved and applied, transfers PM record roles | Integration `Change_requests_apply_only_after_the_pm_confirms…` (nothing changes when approved; after apply the new PM holds `pm`, the old one lost it) |
| Project closure automatically opens the project team's evaluation in Teams | Full-lifecycle integration test (team → `pending_evaluation`) and the E2E |

Also asked for: gate blocks submission (unit `ProjectGateTests`, integration); weights = 100 % (unit 99.99 / 100 / 100.01, integration); 100 % needs evidence and actual end, delayed needs causality and corrective actions (unit `MilestoneRulesTests`, integration); closure → Impact → Completed and → Completed with Impact off (integration `Impact_switched_off…`); initiative template (unit + integration `Initiative_follows…`); team members get project access (integration); objectives from the KPI catalogue incl. the lookup migration (integration `Strategic_objectives_come_from…`).

## Quality gates run

| Gate | Result |
|---|---|
| `dotnet build Strategy.slnx -c Release` · `dotnet format --verify-no-changes` | 0 warnings, 0 errors · clean |
| `dotnet test --solution Strategy.slnx` | **After the rebase on Phase 14 and the recall addition: 2 944 / 2 944.** Before the rebase, full run: **2 667** tests, 2 658 green; the 9 failures were environmental or expected facts — 8 OCR tests (Tesseract not installed in the container; after installing it they pass) and the job dashboard list (updated for `portfolio.monthly-cycle`; passes). Re-run after the UI merge: Portfolio integration **21 / 21**. New: **92** Portfolio unit test cases (147 in the namespace) (lifecycle, gates, progress, milestone rules, risk scoring, change lifecycle), **9** Portfolio integration tests |
| `tools/check-api-client.sh` · `tools/check-vulnerable.sh` | up to date · no High / Critical |
| `npm run lint` · `format:check` · `typecheck` · `npm test` | clean · **185 / 185** Vitest after the rebase (new `projectLogic.test.ts`; `messageKeys.test.ts` covers every Phase 9 server key in AR and EN) |
| `npm run build` · `npm audit --audit-level=high` · `npm run lint:licenses` | OK · no High / Critical (same 14 moderate dev-tool advisories) · 613 packages allowed |
| `npx playwright test` | **Whole suite after the rebase on Phase 14: 88 / 88** in Arabic and English on a fresh database (Phases 0–9, 12 and 14; the job dashboard now lists 17 jobs) |

Existing tests updated for Phase 9 facts: Portfolio MCP tool list; project response type (`ProjectFullDto`); Teams / KPI test doubles use their own link type `test.project` (the real `portfolio.project` now exists); authorization matrix sends a file to `/evidence`; job dashboard (API and E2E) lists 14 jobs; Phase 8 E2E reads the Phase 9 banner key.

**Dependencies added:** none.

## Owner addition: recall a pending approval
The submitter (the PM for stage and monthly approvals; the PM or the person who raised a change request) and the PM Office may **recall** a
pending charter, planning, monthly update, closure, impact or change-request approval with a reason (`POST /projects/{id}/recall`): the engine
instance is cancelled, the stage returns to its draft (monthly window and milestones returned, change request back to draft) and the project
shows the result *recalled*. Hold stays refused while an approval runs; the refusal carries `action = recall` and the hold dialog offers
**Recall**; the Approvals tab and the current-status banner show **Recall** too. Tests: integration `A_pending_approval_can_be_recalled…`
(nothing to recall 422, hold refusal with the hint, viewer 403, reason required, planning / monthly / change-request recalls, hold afterwards),
Vitest `offersRecall`, E2E step and screenshot `approval-recall`. Recorded in ADR 0070 and the design note.

## Decisions (ADRs — accepted by the owner)
- [0070](../decisions/0070-project-lifecycle-templates-and-gates.md) Stage templates, gates, one approval at a time, high-level vs detailed plan, rejection returns to draft, initiative completion by PM Office.
- [0071](../decisions/0071-monthly-cycle-and-progress.md) Window opening / due / reminders in working time, pending values and lock, additional windows, linear planned-to-date.
- [0072](../decisions/0072-team-formation-and-project-access.md) `TeamMembershipChanged` and the `team-member` record role; closure events.
- [0073](../decisions/0073-change-requests.md) Conditional approvers, one under review per project, semi-automatic apply.
- [0074](../decisions/0074-project-risks-and-kpis.md) Risk scoring interface (5×5, Low / Medium / High / Critical) and the risk directory; project KPIs.
- [0075](../decisions/0075-strategic-objectives-from-kpi.md) Objectives from the KPI hierarchy and the lookup migration.
- [0076](../decisions/0076-phase-9-ai-and-mcp.md) No new AI in Phase 9; MCP tools.

## Deviations
- **From the SPEC:** none known; interpretations are in the ADRs (notably rejected stage approvals return to draft; hold is refused while an approval runs).
- **From the plan / CLAUDE.md:**
  - Project commands are services called by the endpoints (as Phase 8's `AssessmentService`), not dispatcher handlers.
  - Meeting minutes are plain text shown escaped (DOMPurify is not yet a dependency; the server strips scripts). Rich-text minutes when DOMPurify is added with its licence entry.
  - The Existing-KPI picker lists the first 20 cards of a search (no search-as-you-type).
  - Milestone structure changes after planning approval go through a change request (re-baseline proper is Phase 10).
- **Platform / other modules (additive):** Teams publishes `TeamMembershipChanged` (Teams.Contracts); Kpi.Contracts `IKpiStrategyCatalog` + implementation; Portfolio.Contracts `ProjectClosureApproved`, `IProjectRiskDirectory`; dev seed: demo objectives created in the KPI hierarchy, Mariam also holds Finance Service Section and GM Office; `.gitignore` ignores `.claude/worktrees/`.

## Known gaps
- AI assists (charter assistant, plan generator, update assistant), Gantt, agile, baselines / EVM and portfolio dashboards: Phase 10. Vendor-then-PM updates: Phase 11. Shared risk framework scoring: Phase 13.
- "View costs / budget" for team members is not separately configurable (team members see the project's costs).

## How to demo
```bash
REGISTRY_PREFIX=mirror.gcr.io/ docker compose -f deploy/docker-compose.yml up -d postgres gotenberg mailpit
dotnet run --project src/Host/Strategy.Host
cd web && npm ci && npm run dev        # http://localhost:5173
```
1. **Ahmed** creates and submits a program request with a budget above AED 50 000 and all screening answers Yes; **Mariam** (MFA) approves every step → Project.
2. Ahmed opens the project → *Take ownership*; fills KPIs, risks, stakeholders, three members, a high-level milestone (100 %) — the gate checklist turns green → *Submit charter*; Mariam approves.
3. Mariam (Administration) uploads the LPO → *LPO uploaded*; (PM Office) Team tab → *Request a team*, approves the formation → the project moves to Planning.
4. Ahmed adds scope items and milestones to 100 % → *Submit planning*; Mariam approves → Execution. Mariam *Open window now*; Ahmed updates milestones (evidence at 100 %, causality when late) → *Submit monthly update*; Mariam approves.
5. At 100 %: closure form + lesson → approval (the project team's evaluation opens in Teams) → Impact → Completed. Try a change request (ownership or budget) and *Apply changes*.

## Cloud environment notes
- PostgreSQL 16 local (`max_connections = 600`, `STRATEGY_TEST_POSTGRES`); it had to be restarted once after the session paused. Docker started manually for Gotenberg / Mailpit; Tesseract installed for the OCR tests; E2E on a fresh database with the bundled Chromium.
- The web UI and E2E were built in a parallel worktree and merged (`feat(web)…`, `test(e2e)…` commits; one backend fix there: unique OpenAPI schema names for project meetings, proposed members and the KPI card picker).
- Screenshots (`E2E_SCREENSHOTS=phase-9`) in [`docs/screenshots/phase-9/`](../screenshots/phase-9/) as `ar-*` / `en-*` pairs: `take-ownership`, `charter-risks`, `charter-gate-blocked`, `team-formation`, `planning-items`, `milestones-weights`, `approval-recall`, `monthly-update-evidence`, `monthly-update-delay`, `closure-form`, `impact`, `change-request-apply`, `change-request-applied`, `project-record-execution`, `project-record-completed`.
