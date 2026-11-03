# Phase 7 — Teams Management: summary

**Status:** built and verified in the build session · awaiting owner acceptance
**Branch:** `claude/inspiring-fermat-lovf4b`
**Planning:** the plan is the design note [`docs/design/teams.md`](../design/teams.md), written and committed before the code (commit
`docs(teams): Phase 7 design note…`). This session ran without an interactive approval step (as Phases 5–6), so the plan was written
first and then implemented as it stands; its open choices are ADRs 0041–0046.

## What was built

| Area | Result |
|---|---|
| **Module** (`Strategy.Modules.Teams` + `.Contracts`, schema `teams`) | `team` (`S-NNNN`), `team_member` (employee **or external** member; never deleted), `meeting` (`MTG-…`) with `attendance`, `recommendation` (`REC-…`, recommendations and decisions), `team_report`, `team_evaluation` (`EVAL-YYYY-NNNN`, **one record per cycle**), `member_evaluation`; configuration tables `team_type`, `member_role`, `evaluation_template`, `reward_matrix` (ADR 0041). Core system, always on (like Tasks) |
| Configuration (Settings → Teams, ADR 0041) | Team types (permanence, formation approval, memo, linkable types, cadence, **evaluation trigger**, annual date, **evaluator** and leader's evaluator); member roles (leader, **can view evaluation results** — default only President / Project Manager, quorum, writes minutes); **versioned evaluation templates** (criteria with weights and levels, data-backed ranges, member questions, grade bands, evidence threshold — the ADoF 2025 defaults of SPEC §4.3); **reward matrix versions** grade × workload × role with certificate grades and annual budget (seeded empty — Q-T5); general values. Published versions are immutable; new versions are drafts |
| Team lifecycle (ADR 0046) | `TeamLifecycle` transition table: Draft → Pending Approval → Active ⇄ Inactive → Pending Evaluation → Evaluation Waiting Approval → Active / Closed; Replaced; **Cancelled** for requests that never became a team; returned formation → Draft |
| Formation (T1) | Request with type, names, purpose, owning unit, sponsor, dates, cadence, workload, members with roles / responsibilities / skills, linked record, memo mode. Approval **`teams.formation`** through the Phase 4 engine with the Q-T5 default chain **Department Director of the owning unit → General Manager** (new approver subject `organisation` = head of the top unit, ADR 0044). On approval: numbered memo (`MEMO-YYYY-NNNN`), **bilingual memo PDF** rendered by the Phase 3 Gotenberg service via the outbox and stored through the Files service — **or an uploaded external memo** (number, date, PDF); members notified; record roles granted |
| Operations (T2) | Membership changes by the Teams administrator: add, end (leave) with date and reason, **replace** (successor row, `replaced_by`), change role — history kept, record roles follow in the same transaction, people notified. Meetings: schedule (leader), attendance, minutes and decisions (leader / secretary), **submit minutes stamps the minutes delay** (hours after the meeting's end, ADR 0045). Recommendations / decisions register with owners, due dates, status, closure; the owner updates their own. **Team tasks** through Task Management (team page Tasks tab, the leader follows every linked task); **tasks from a recommendation** (New Task pre-filled, linked to `teams.recommendation`); **New Task → "Team's members"** (one task per active internal member). Periodic reports per cadence (job), due at the end of working time (business-hours calculator), reminders and overdue notices |
| Evaluation (T3, ADR 0045) | Cycles opened by **annual date** (permanent), **end date** (temporary), **`LinkedRecordClosed` integration event** (project teams) or manually; evaluator per type / team (default leader) and optional leader's evaluator; template snapshot. **Suggested levels** from data: recommendations closure % ("12 of 13 closed = 92.31 %") and average minutes delay; another level needs a **justification**; **evidence mandatory at score ≥ threshold**; member answers 1–5, points, grade from the bands, attendance rate; inconsistency flags. **HR approval** `teams.evaluation` (single step, role Teams administrator) — approve or return for re-evaluation |
| Rewards (T4) | Amounts from the matrix published for the cycle's year (grade × team workload × role at the time), certificate per grade; HR adjusts with **justification**, submits **`teams.reward`** (head of the Teams system's owning unit, configurable); on approval **certificates** numbered (`CERT-YYYY-NNNN`) and rendered as bilingual PDFs; budget used / remaining per year |
| Who sees results (ADR 0042) | One permission `teams.evaluation.results` (HR / Teams administrator; record roles `results_viewer` from the member role flag, `evaluator`, approvers). Ordinary members get **403** on the evaluation, status only, no score on the team, register, employee tab, search, Ask (record source) or MCP; evidence and certificates are owned by the evaluation and downloadable only with results access |
| Integration events (ADR 0043) | Kernel `IIntegrationEvent` / `IIntegrationEventPublisher` / `IIntegrationEventHandler<T>`; Scheduling writes **one outbox message per subscriber**; `LinkedRecordClosed` in `Links.Contracts` (Portfolio will publish it on closure approval in Phase 9) |
| Links | `teams.team`, `teams.recommendation`, `teams.meeting` declared **task-able**; teams link to **team-able** records; **Sample items declare team-able** and show a **Teams** tab (linked teams with members, *Request a team* with the item linked); `GET /teams/linked` counts restricted teams without showing them; `ITeamRoleResolver` resolves `teamRole` approver rules |
| Current status, My Work, notifications | Status provider for `teams.team` (draft / returned / waiting step / pending evaluation with the evaluator's action / report due / rewards pending…); My Work items (evaluations to fill or returned, reports due, overdue recommendations, returned requests); 14 AR / EN templates |
| AI (Phase 5 registry, drafts only) | `teams-formation` (objective, mandate, memo text), `teams-members` (no model: skill overlap with past memberships, workload, over-assigned flag; grades only for results holders), `teams-evaluation` (activity summary explaining suggestions and flags), `teams-minutes` (notes → minutes, decisions, recommendations with owners and due dates). Prompts `Ai/Prompts/*.v1.md`; the offline demo provider answers all three |
| MCP (ADR 0031) | `my_teams`, `team_details`, `my_pending_evaluations`, `evaluation_status` (read, on; scores only for results viewers), `add_meeting_minutes` (write, **off**; saves a draft) |
| Dashboard, reports | Teams by type / status, active committees, employees on many teams, evaluation progress, score / grade distribution and rewards by year (results scope only); exports register / membership / evaluations / rewards in Excel or bilingual PDF (audited) |
| Web (AR + EN, RTL / LTR) | **Teams register** (cards, tabs, filters), **formation wizard** (AI draft, member suggestions), **team page** (header, stepper, current-status banner, tabs Overview with memo card and stats, Members with history, Meetings, Recommendations, Tasks, Reports, Documents, Evaluations, Approvals, Activity), **meeting page** (attendance, minutes, AI minutes writer, submit), **evaluation page** (wizard with suggested levels and their data, flags, AI summary, HR review with approval panel and previous cycles, Rewards with budget and certificates), **dashboard**, **Settings → Teams**, **employee Teams tab**, **Sample Teams tab**, New Task team option |

## Acceptance checks (BUILD_PLAN Phase 7)

| Check | Evidence |
|---|---|
| Committee formed through formation approval; memo PDF (AR / EN) or uploaded instead | Integration `Formation_approval_by_director_then_general_manager_generates_a_numbered_bilingual_memo` (draft → pending → director → GM → Active, `MEMO-YYYY-…`, PDF stored and downloadable by a member, HTML contains Arabic and English titles, names, external member, approval steps) and `An_uploaded_external_memo_replaces_the_generated_one` (no rendering). E2E (ar + en) runs the chain and renders the real PDF: `teams-memo-pdf` |
| Meetings with minutes delay and a recommendations register feed the suggested levels | Integration `Meetings_and_recommendations_feed_suggested_levels…` (minutes submitted 30 h late → *late*; 12 of 13 closed, one rejected not counted → 92.31 % → *medium*); unit `EvaluationSuggestionsTests`. E2E screenshots `team-meeting-minutes`, `team-recommendations`, `team-evaluation-wizard` |
| Evaluation by the configured evaluator, HR approval, rewards, certificates | Same integration test: leader evaluates (justification and evidence enforced), HR approves, rewards from the matrix (5 000 / 3 000 / 0), adjustment needs a reason, reward approval, `CERT-YYYY-…` PDFs; budget used. E2E `team-hr-review`, `team-rewards`, `team-rewards-certificates`, `team-certificate-pdf` |
| A member cannot see results; the President can | Integration: member 403 on the evaluation, null score on the team and the list, no results on the employee tab, no score in Ask record source or MCP `evaluation_status` / `team_details`, certificate and evidence downloads 404; President and HR see them. E2E logs a member in: 403 / null score; President 200 |

**Also asked for in the prompt:**

| Request | Evidence |
|---|---|
| Evaluation history kept per cycle | Integration `Annual_and_end_date_triggers…` (cancelled 2026 and open 2027 cycles both listed); unique index per (team, cycle) |
| Grade bands validated without gaps | Unit `TemplateRulesTests` (gap, overlap, coverage); integration: Settings refuses a template with a gap (`teams.errors.bandsGap`) |
| Evidence required at score ≥ threshold | Unit `EvaluationCalculatorTests`; integration submission refused without evidence at 95 |
| Suggested levels from closure % and minutes delay | Unit (boundaries, no data) + integration (above) |
| Reward amounts from the matrix | Unit `RewardCalculatorTests` + integration |
| Ordinary members cannot see results (API, search, Ask, MCP) | Integration (above) |
| Formation approval generates a numbered bilingual memo, or accepts an uploaded memo | Integration (above) |
| Fake project-closure event opens a project-team evaluation | Integration `A_project_closure_integration_event_from_a_fake_publisher…` (fake `portfolio.project` linkable type; event → Pending Evaluation, trigger `linked-record-closed`, cycle `completion`, evaluator = leader; duplicate delivery changes nothing) |
| Annual and end-date triggers with the fake clock | Integration with the travelling clock: +10 days opens the temporary committee's completion cycle; 1 January opens the standing committee's previous-year cycle; reruns are idempotent |
| Member history kept on replace / leave | Unit + integration `Member_history_is_kept_on_replace_and_leave…` (5 rows kept, statuses, reasons, record roles move, notifications) |
| Team-able links proven on Sample | Integration `Sample_items_are_team_able…` (editor requests a team for the item; outsider 404; reader without team access sees `restricted = 1`) |
| Tasks: assign to a team's members, tasks from recommendations | Integration (3 tasks for 3 internal members, external skipped; outsider refused; leader follows linked tasks); web New Task |
| Every allowed / forbidden transition | Unit `TeamLifecycleTests` (9 statuses × 17 actions), `EvaluationLifecycleTests` |
| AI and MCP | Integration `TeamsAiTests` (fake model: minutes draft not saved, untrusted-data wrapping, formation draft, member suggestions without grades); MCP tools registered, write off, no approve tool |

## Quality gates run

| Gate | Result |
|---|---|
| `dotnet build Strategy.slnx -c Release` · `dotnet format --verify-no-changes` | 0 warnings, 0 errors · clean |
| `dotnet test --solution Strategy.slnx` | **2 008** tests (unit, architecture and integration on real PostgreSQL): the full run had 3 failures, all expected Phase 7 facts, fixed below and re-run green (with the classes touched by the last changes: Teams, migrations, job dashboard, authorization matrix). New: **62** Teams unit tests, **10** Teams integration tests |
| `tools/check-api-client.sh` · `tools/check-vulnerable.sh` | OpenAPI document and TS client up to date · no High / Critical |
| `npm run lint` · `format:check` · `typecheck` · `npm test` | clean · **95 / 95** Vitest (6 new Teams message-key tests) |
| `npm run build` · `npm audit --audit-level=high` · `npm run lint:licenses` | OK · no High / Critical (the same 14 moderate dev-tool advisories as Phases 3–6) · 613 packages allowed |
| `npx playwright test` | **78 / 78**, the whole suite in Arabic and English on a fresh database (Phase 7: 1 scenario × ar + en; screenshots with `E2E_SCREENSHOTS=phase-7`). The job dashboard spec now expects 12 jobs |

Existing tests updated for Phase 7 facts: the authorization matrix lists the Teams permissions everyone holds; the job dashboard lists
`teams.evaluation-triggers` and `teams.reports` (API test and E2E: 12 jobs); effective roles include `teams.employee`; the `teamRole`
approver rule now resolves through Teams instead of reporting "unavailable"; the first administrator holds 11 platform administrator roles.
The architecture rule "modules do not use other modules' internals" now compares full type names (an assembly-name prefix made
`Strategy.Modules.Teams` match `Strategy.Modules.Teams.Contracts`).

**Dependencies added:** none (NuGet or npm). The E2E converts generated PDFs to PNG with `pdftoppm` (poppler-utils) on the test machine
only.

## Decisions (ADRs)
- [0041](../decisions/0041-teams-configuration-and-versioning.md) Module-owned configuration tables; immutable published template /
  matrix versions; validation rules; Q-T5 empty matrix.
- [0042](../decisions/0042-evaluation-results-visibility.md) One results permission and its record roles; every read path; the department
  manager sees memberships, not results.
- [0043](../decisions/0043-integration-events-outbox.md) Integration events through the outbox (one message per subscriber);
  `LinkedRecordClosed`.
- [0044](../decisions/0044-formation-memo-certificates-org-head.md) Memo and certificates rendered after the transaction via the outbox;
  external memo; approver subject `organisation`.
- [0045](../decisions/0045-suggested-levels-and-minutes-delay.md) Data sources, ranges (24–48 h gap closed upwards), calendar hours for the
  minutes delay, cycle periods.
- [0046](../decisions/0046-team-lifecycle.md) Transition table, Cancelled status, closing after the completion evaluation, Replaced.

## Deviations
- **From the SPEC:** none known. Interpretations are in the ADRs; the added *Cancelled* status follows CLAUDE.md §6.
- **From the plan / CLAUDE.md:**
  - Dashboard charts are accessible CSS bars (as in Phase 6); ECharts arrives with the reporting phase (17).
  - The certificate is not downloadable by the member themselves by default (setting `allowMemberCertificateDownload` reserved, off): HR
    hands it over. Turning it on is a follow-up.
  - The member-suggestion skill match uses past memberships' skills and responsibilities (the employee record has no skills field yet).
- **Platform changes:** Kernel integration-event contracts and their Scheduling implementation; `LinkedRecordClosed` in Links.Contracts;
  approver subject `organisation` (Workflow + Approval Settings editor); Tasks accepts `assigneeTeamId` (references Teams.Contracts);
  Sample items are team-able; the Teams administrator joins the platform administrator roles; the development seed makes Mariam head of
  the demo organisation too (demo General Manager); the demo AI provider answers the three Teams prompts; OpenAPI DTO names made unique
  (`TeamPersonDto`, `EvaluationTemplateDto`, …); shared validation keys `validation.duplicate` / `validation.greaterThan`.

## Known gaps (by design: later phases or follow-ups)
- Portfolio publishes `LinkedRecordClosed` and uses formation for project teams in Phase 9; until then the event is proven with a fake
  publisher.
- Closing a linked record can also *close* the team per type configuration after evaluation (done for project teams); an explicit "close the
  team when the record closes without evaluation" option is not offered.
- Reward budget is informative (used / remaining); it does not block approvals.

## How to demo
```bash
REGISTRY_PREFIX=mirror.gcr.io/ docker compose -f deploy/docker-compose.yml --profile dev up -d postgres gotenberg mailpit
dotnet run --project src/Host/Strategy.Host
cd web && npm ci && npm run dev        # http://localhost:5173
```
1. **Mariam** (`mariam@strategy.local` / `Strategy#Demo2026`, MFA — Teams administrator, department director and demo General Manager):
   Settings → Teams → Reward matrix → *New version*, fill amounts, publish. Settings → Systems → Teams owning unit (e.g. the PMO section)
   and give that unit a head (approves rewards).
2. **Ahmed** (`ahmed@strategy.local`): Teams → *Request a team* → standing committee, members (himself as President, colleagues, an external
   expert), *Draft with AI* (with the demo AI provider), *Suggest members* → *Submit for approval*.
3. Mariam approves twice (director, General Manager) from My Work or the team's Approvals tab; the team becomes Active, the **memo PDF**
   appears on Overview.
4. Ahmed schedules a meeting, records attendance and minutes (or *Write minutes with AI*), submits them; adds recommendations and closes
   some; *Create task* from a recommendation; New Task → *Team's members*.
5. Mariam: Evaluations → *Open evaluation*. Ahmed: My Work → the evaluation wizard shows the **suggested levels** with "x of y closed" and
   the minutes delay; answer the members, attach evidence if the score is ≥ 90, submit.
6. Mariam: HR review tab → approve; Rewards tab → adjust (with a reason), submit; the owning unit's head approves; **certificates** appear.
7. Sign in as a plain member: the Evaluations tab says results are visible only to leaders and HR; the employee Teams tab shows
   memberships without results.

## Cloud environment notes
- Docker started manually (`dockerd`); Gotenberg and Mailpit through `mirror.gcr.io/`. Integration tests reuse the local PostgreSQL 16 with
  `STRATEGY_TEST_POSTGRES` (with `max_connections = 600`; the default 100 makes the parallel suite fail with "too many clients"). Tesseract
  (`tesseract-ocr`, `-ara`, `-eng`) installed for the OCR tests. E2E ran against database `strategy` with
  `E2E_CHROMIUM_PATH=/opt/pw-browsers/chromium`.
- Screenshots (`E2E_SCREENSHOTS=phase-7`) are in [`docs/screenshots/phase-7/`](../screenshots/phase-7/) as `ar-*` / `en-*` pairs:
  `teams-register`, `teams-formation-request`, `teams-formation-approval`, `teams-memo-pdf`, `team-page-overview`, `team-page-members`,
  `team-meeting-minutes`, `team-recommendations`, `team-evaluation-wizard`, `team-hr-review`, `team-rewards`,
  `team-rewards-certificates`, `team-certificate-pdf`, `employee-teams-tab`, `teams-dashboard`.
