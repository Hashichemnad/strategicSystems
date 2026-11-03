# Design note — Phase 7: Teams Management (`Strategy.Modules.Teams`)

**Scope.** BUILD_PLAN Phase 7; SPEC §4 (all, incl. the decisions in §4.9), §0.0 cross-system links, §0.1 (Team / Committee module),
Appendix A **Q-T5**; CLAUDE.md §5–§16. Reuses Phase 3 (files, notifications, PDF via Gotenberg, numbering, audit), Phase 4 (approval
engine, current status, My Work, business-hours calculator — ADR 0027), Phase 5 (AI and tool registries), Phase 6 (Task Management, links
registry). Decisions: ADRs **0041** (configuration storage and versioning), **0042** (who sees evaluation results), **0043** (integration
events through the outbox; linked-record closure), **0044** (formation memo, certificates and the "head of the organisation" approver),
**0045** (suggested criterion levels and minutes delay), **0046** (team lifecycle).

This note is the Phase 7 plan. The session runs without an interactive approval step (as Phases 5–6): the plan is committed first, then
implemented as written; open choices are the ADRs above.

## 1. Projects

| Project | Contents |
|---|---|
| `Strategy.Modules.Teams.Contracts` | `ITeamsDirectory` (a team's badge and its active members for a caller who may read it — used by Tasks for "assign to a team's members"), DTOs. No EF Core |
| `Strategy.Modules.Teams` | `Domain/` (Team, TeamMember, Meeting, Attendance, Recommendation, TeamReport, TeamEvaluation, MemberEvaluation, TeamType, MemberRole, EvaluationTemplate, RewardMatrix; `TeamLifecycle` transition table; calculators), `Application/`, `Infrastructure/` (`TeamsDbContext`, schema **`teams`**, migrations, seeder), `Api/`, `Jobs/`, `Ai/` (features + prompts), `Mcp/` |
| Platform additions | `IIntegrationEventPublisher` / `IIntegrationEventHandler<T>` (Kernel contracts, outbox implementation in Scheduling, ADR 0043); `LinkedRecordClosed` event (Links.Contracts); approver subject `organisation` (head of the top unit, ADR 0044) |

System code **`teams`**. Teams is a **core** system (SPEC §0.0 "Core (shared): … Teams"; §0.1): `SystemKind.Platform`, always on, no
system-access grant needed — the same choice as Tasks (ADR 0036). Code lives under `src/Modules` with its own schema and contracts.

## 2. Configuration (Settings → Teams; ADR 0041) — module-owned tables, seeded once, edited in the UI, audited

| Table | Columns | Seed (SPEC §4.3) |
|---|---|---|
| `team_type` | `code`, `name_ar/en`, `permanence` (`permanent` / `temporary` / `either` — chosen per team), `requires_approval`, `requires_memo`, `linkable_types` (text[], empty = any team-able type), `default_cadence`, `evaluation_trigger` (`none` / `annual` / `end-date` / `linked-record-closed`), `annual_month`, `annual_day`, `evaluator_rule`, `evaluator_employee_id`, `evaluator_member_role`, `leader_evaluator_rule` (+ employee / member role), `sort`, `active` | Work Team (either, end-date for temporary / annual for permanent → trigger `auto`), Project Team (temporary, `linked-record-closed`), Standing Committee (permanent, `annual` 1 Jan), Temporary Committee (temporary, `end-date`). All: approval on, memo on, evaluator `leader`, leader evaluator `same` |
| `member_role` | `code`, `name_ar/en`, `sort`, `is_leader`, `can_view_results`, `counts_toward_quorum`, `is_secretary`, `active` | President (leader, results), Deputy, Project Manager (leader, results), Member, Rapporteur, Member & Rapporteur, Secretary (secretary, not quorum). **Only leader roles view results** (§4.9) |
| `evaluation_template` | `year`, `version_no`, `status` (`draft` / `published` / `retired`), `criteria` (jsonb), `questions` (jsonb), `grade_bands` (jsonb), `evidence_threshold`, `notes` | Year = installation year, v1 published: the five ADoF 2025 criteria with levels (§4.3), three member questions, bands Very Active 12–15 · Active 9–11 · Not Active 3–8, evidence ≥ 90 |
| `reward_matrix` | `year`, `version_no`, `status`, `currency`, `annual_budget`, `cells` (jsonb: grade × workload × member role → amount), `certificate_grades` (text[]) | Q-T5: **seeded empty** (administrator fills amounts); certificate for Very Active |

`team_type.evaluation_trigger = auto` (Work Team) means: permanent team → annual, temporary team → end date.

**Template rules (validated server-side, unit-tested):** criteria weights total 100; each criterion's level scores ≤ its weight and include
0; a data-backed criterion (`data_source` = `recommendations-closure` / `minutes-delay`) has contiguous numeric ranges per level covering the
whole domain (0–100 % / 0–∞ h); questions 1–10 on a 1–5 scale; **grade bands are integer ranges covering exactly
[questions × 1, questions × 5] with no gap and no overlap** (the Salesforce "8 vs 9" gap is impossible); evidence threshold 0–100.
A published template / matrix is immutable: changes create a new version (`version_no + 1`, draft → publish). An evaluation snapshots the
template version it was opened with; rewards use the matrix published for the evaluation's year at calculation time.

**Settings area `teams`** (`ISettingsArea`): over-assignment threshold (active memberships per employee, 4), report reminder days before
due (3), evaluation reminder after N working days (5), recommendation default due days (30), member suggestion limit (10),
`allowMemberCertificateDownload` = false.

**Lookups:** `teams.approved-by` (General Manager, President — the "approved by" list of §4.4 A). Fixed logic enums with labels in the
translation files: cadence (`monthly` / `quarterly` / `biannually` / `annually` / `other`), workload (`high` / `medium` / `low`),
statuses.

## 3. Data model (schema `teams`; business tables audited, `xmin`, audit columns, no hard deletes)

| Table | Columns (main) | Notes |
|---|---|---|
| `team` | `number` (`S-{SEQ:4}`), `name_ar/en`, `type_code`, `permanent`, `subject`, `objectives`, `mandate`, `owning_unit_id`, `sponsor_employee_id`, `requested_by_employee_id`, `leader_employee_id` (derived from the active member with a leader role), `approved_by_code`, `memo_mode` (`generate` / `external`), `memo_number`, `memo_date`, `memo_file_id`, `memo_draft` (AI / user text), `start_date`, `end_date`, `cadence`, `report_to`, `workload`, `status`, `status_reason`, `replaces_team_id`, `replaced_by_team_id`, `evaluator_rule` / `evaluator_employee_id` (per-team override), `closed_at`, `activated_at` | Linked records in `links.record_link` (kind `team-able`, from `teams.team`). Computed on read: active members, meetings held, recommendations open / closed %, last evaluation score (results viewers only) |
| `team_member` | `team_id`, `employee_id` **or** external (`external_name`, `external_org`, `external_email`, `external_phone`), `role_code`, `responsibilities`, `required_skills`, `allocation_pct`, `position`, `from_date`, `to_date`, `status` (`active` / `left` / `replaced`), `replaced_by_member_id`, `leave_reason` | Never deleted. Replace = old row `replaced` + new row; leave = `left` with reason and date. Record roles follow the active rows |
| `meeting` | `number` (`MTG-{SEQ:5}`), `team_id`, `title`, `scheduled_at`, `ends_at`, `location`, `agenda`, `minutes`, `decisions`, `status` (`scheduled` / `held` / `cancelled`), `minutes_submitted_at`, `minutes_delay_hours` (elapsed hours from `ends_at`), `minutes_by_employee_id`, `ai_drafted` | Parent = team in History |
| `attendance` | `meeting_id`, `member_id`, `status` (`present` / `absent` / `excused`), `note` | One per member per meeting |
| `recommendation` | `number` (`REC-{SEQ:5}`), `team_id`, `meeting_id`, `kind` (`recommendation` / `decision`), `text`, `owner_employee_id`, `due_date`, `status` (`open` / `in_progress` / `closed` / `rejected`), `closed_on`, `closure_note`, `evidence` (team files by id) | Task-able (tasks from decisions / recommendations) |
| `team_report` | `team_id`, `period_start`, `period_end`, `due_date`, `due_at`, `status` (`due` / `submitted`), `summary`, `submitted_at`, `submitted_by`, `reminded_at`, `overdue_notified_at` | Created per cadence period by the job; files purpose `teams.report` |
| `team_evaluation` | `number` (`EVAL-{YYYY}-{SEQ:4}`), `team_id`, `cycle` (`2026` or `completion`), `period_start`, `period_end`, `template_id`, `trigger` (`manual` / `annual` / `end-date` / `linked-record-closed`), `opened_by`, `opened_at`, `evaluator_employee_id`, `leader_evaluator_employee_id`, `criteria` (jsonb: code, selected level, score, comment, suggested level, basis, override justification), `score`, `workload`, `evidence_file_ids`, `status` (`open` / `submitted` / `returned` / `approved`), `submitted_at`, `approved_at`, `return_comment`, `reward_status` (`none` / `draft` / `pending` / `approved`), `reward_total`, `reward_matrix_id`, `flags` (jsonb) | Unique (team, cycle) among non-cancelled; history kept per cycle |
| `member_evaluation` | `evaluation_id`, `member_id`, `employee_id`, `external_name`, `role_code` (at the time), `answers` (jsonb), `comment`, `points`, `grade`, `attendance_rate`, `evaluated_by_employee_id`, `evaluated_at`, `calculated_reward`, `approved_reward`, `adjustment_reason`, `certificate`, `certificate_number`, `certificate_file_id`, `financial_reward` | One per member active in the cycle |

Files purposes (Files service): `teams.document` (team documents, reports, outputs, evidence, external memo — 20 MB, versioned) and
`teams.generated` (memos and certificates written by the system). Downloads go through `IFileOwnerAccess` for `teams.team`; certificates
and evidence also require the results permission (below).

## 4. Team lifecycle (`TeamLifecycle`, explicit transition table, ADR 0046)

| From | Action | By | To | Side effects |
|---|---|---|---|---|
| (new) | create request | requester (`teams.team.request`) | Draft | record role `requester` |
| Draft | submit | requester / administrator | Pending Approval (type requires approval) or **Active** | approval `teams.formation` started; without approval: activate |
| Pending Approval | approval approved (engine hook) | — | Active | memo number + date, memo PDF rendered (outbox) unless external; members notified; record roles granted |
| Pending Approval | returned (hook) | — | Draft | requester corrects and resubmits (new instance) |
| Pending Approval | rejected (hook) / withdraw | — / requester | Cancelled | reason |
| Draft | cancel | requester / administrator | Cancelled | reason |
| Active | deactivate (reason) | administrator | Inactive | |
| Inactive | reactivate | administrator | Active | |
| Active, Inactive | open evaluation (trigger / manual) | job · event · administrator | Pending Evaluation | evaluation record, evaluator record roles, notifications |
| Pending Evaluation | evaluation submitted | evaluator | Evaluation Waiting Approval | approval `teams.evaluation` |
| Evaluation Waiting Approval | returned (hook) | HR | Pending Evaluation | evaluation `returned` |
| Evaluation Waiting Approval | approved (hook) | HR | **Active** (permanent, or temporary before its end) or **Closed** (temporary team whose end date passed / closure requested / linked record closed) | scores locked, grades final, rewards calculated (draft) |
| Active, Inactive | close (reason) | administrator | Closed — or Pending Evaluation first when the type's trigger evaluates on closure (temporary) | |
| Active, Inactive | replaced (successor formation approved with `replaces_team_id`) | — | Replaced | links both ways |

Statuses are the SPEC's eight plus **Cancelled** (a request that never became a team: CLAUDE.md §6 "use status Cancelled with reason";
ADR 0046). Evaluation statuses: Open · Submitted · Returned · Approved (+ Cancelled when an administrator cancels an opened cycle with a
reason). Unit tests cover every allowed and forbidden (status, action) pair.

## 5. Approval processes (Phase 4 engine; Approval Settings centre)

| Key | Resource | Default chain (seeded) | Hook |
|---|---|---|---|
| `teams.formation` | `teams.team` | **Q-T5**: Department Director of the owning unit (`departmentDirector`, of record) → General Manager (`headOfUnit`, of `organisation` = head of the top unit, ADR 0044). Variables: `typeCode`, `permanent`, `members` | Active / Draft / Cancelled |
| `teams.evaluation` | `teams.team` | One step: role `teams.administrator` (HR) — SPEC §4.3 "default single step: HR" | Approved → lock, grades, rewards draft; Returned → re-evaluate |
| `teams.reward` | `teams.team` | One step: head of the Teams system's owning unit (`owningUnit`, e.g. HR director), configurable | Approved → rewards final, certificates rendered |

Approvers get record role `approver` on the team (read) while their task is open; the evaluation approver also sees results
(`evaluation_approver`). Formation from other systems (e.g. Portfolio's team formation step, Phase 9) uses the same process.

## 6. Access (SPEC §4.7, CLAUDE.md §7; ADR 0042)

| Permission | Default role / record role |
|---|---|
| `teams.team.read` (scopable: own = requester / leader, unit = owning unit) | *Teams administrator* (All), *Viewer / Leadership* (All), *Department manager* (Unit and below), *Employee* (everyone, Own); record roles `requester`, `leader`, `member`, `secretary`, `evaluator`, `approver` |
| `teams.team.request` | *Employee* (everyone — any authorised user may request; SPEC §4.2), administrator |
| `teams.team.manage` (edit, membership changes, close, deactivate, link records, memo) | administrator (All); record role `requester` while Draft (edit request) |
| `teams.team.operate` (meetings, recommendations, reports, team tasks) | record role `leader`; administrator |
| `teams.meeting.minutes` (write minutes and attendance) | record roles `leader`, `secretary` |
| `teams.evaluation.open` · `teams.evaluation.cancel` | administrator |
| `teams.evaluation.evaluate` | record roles `evaluator` / `leader_evaluator` (granted when a cycle opens, revoked when submitted/approved) |
| **`teams.evaluation.results`** (scores, grades, rewards, certificates, evidence) | administrator (HR, All); record role **`results_viewer`** granted to active members whose role has *can view evaluation results*; `evaluator` (own cycle); `evaluation_approver` |
| `teams.reward.manage` (adjust, submit rewards, certificates) | administrator |
| `teams.dashboard.read` · `teams.report.export` | administrator, viewer / leadership, department manager (scoped) |
| `teams.settings.manage` | administrator (MFA) |
| `teams.ai.formation` · `teams.ai.members` · `teams.ai.evaluation` · `teams.ai.minutes` | employee (formation, members), leader / evaluator via record role (evaluation, minutes) |

**Ordinary members never see results** — every read path asks `CanAsync(teams.evaluation.results, teamRef)`: evaluation detail (summary
without scores otherwise), team page (last score), employee Teams tab, search source (team hits carry no score), AI record source (Ask /
`get_record`: score fields only for results viewers), MCP `evaluation_status` (status only), dashboards (score / grade / reward charts need
results in scope), certificate and evidence downloads. The department manager sees teams and memberships of their unit, not results
(the §4.9 decision; an installation may grant `teams.evaluation.results` in scope). 404 outside read scope, 403 for visible + forbidden.

## 7. Evaluation (SPEC §4.5 T3, T4; ADR 0045)

**Triggers** (`teams.evaluation-triggers` job daily 01:10 + events + manual):
- *annual* — permanent active teams on the type's month/day (default 1 Jan) open cycle `<previous year>` (period = that year ∩ team life),
  once per team and year (unique index).
- *end-date* — temporary teams whose `end_date` < today and no completion cycle → open cycle `completion`.
- *linked-record-closed* — integration event `LinkedRecordClosed(type, id)` (ADR 0043): every active team of a type with this trigger
  linked to that record opens its completion cycle (Portfolio publishes it when project closure is approved, Phase 9; tested now with a
  fake publisher and a fake `portfolio.project` linkable type).
- manual — administrator, any active team.

The fake clock (`IClock`) drives all dates.

**Evaluator resolution** (team override, else type): `leader` (default) · `sponsor` · `administrator` (any holder of `teams.administrator`
may fill it: no single evaluator, the record role is not needed — the permission is) · `employee` · `member-role` (the active member
holding that role). Leader evaluator: `same` (default) or one of the above.

**Suggested levels** (pure `EvaluationSuggestions`, unit-tested): for criteria with a data source, computed over the cycle period:
- `recommendations-closure` — closed ÷ (all − rejected) of recommendations created in the period (decisions excluded) → level whose range
  holds the percentage. Basis: "12 of 13 closed = 92 %".
- `minutes-delay` — average `minutes_delay_hours` of held meetings in the period (meetings without minutes count as the delay up to now) →
  level by range (≤ 24 h → 10, ≤ 72 h → 5, else 0; the 24–48 h hole of the policy text is closed upwards — ADR 0045). Basis: "average
  30 h over 6 meetings".
- No data → no suggestion. Choosing a level different from the suggestion **requires an override justification**.

**Wizard** (evaluator): (1) team criteria (level per criterion, comment, suggestion and basis shown), workload; (2) each member — answers
1–5 to every question + comment; the leader's own evaluation by the leader evaluator when different; (3) evidence (team files) —
**mandatory when score ≥ threshold**; (4) review → submit. Draft saved at each step (`PUT`).
Calculations (pure, unit-tested): team score = Σ selected level scores; member points = Σ answers; grade = the band holding the points;
attendance rate = present ÷ (meetings held in period where the member was active).

**Inconsistency flags** (deterministic, shown in the wizard and to HR; the AI assistant explains them): every member rated 5 on every
question; all members equal points (≥ 3 members); grade top band with attendance < 50 %; lowest band with attendance ≥ 90 %; suggested level
overridden by more than one level.

**HR review**: approve / return (comment) through the engine, with the previous cycles side by side. On approval → scores locked, team
status as §4.

**Rewards** (pure `RewardCalculator`): amount = matrix cell (grade, team workload, role at the time); missing cell → 0; certificate when
the grade is in `certificate_grades`; financial reward when amount > 0. HR adjusts `approved_reward` (justification required when it
differs), toggles certificate / financial reward, then **submits rewards** (`teams.reward` approval). Approved → certificates rendered
(numbered `CERT-{YYYY}-{SEQ:4}`, bilingual PDF), totals counted against the matrix's annual budget (budget used / remaining per year).

## 8. Formation memo and certificates (ADR 0044)

On formation approval the hook (in the engine transaction) assigns the memo number (`MEMO-{YYYY}-{SEQ:4}`) and date and enqueues outbox
message `teams.render-memo`; the handler renders the bilingual HTML template (organisation names and colour from branding, team name,
type, objective, mandate, members and roles table, dates, approver chain from the approval history, memo number and date; Arabic right /
English left) through `IPdfRenderer` (Gotenberg) and stores it via the Files service (`teams.generated`, owner the team), then sets
`memo_file_id`. Rendering failures retry through the outbox (the approval is never rolled back by a PDF failure). **External memo:**
`memo_mode = external` — the requester / administrator uploads the signed memo (number and date entered); generation is skipped.
Administrators can regenerate a memo. Certificates use the same mechanism (`teams.render-certificate`).

## 9. Operations (T2)

- **Members**: add / leave (date, reason) / replace (new person or external, same role by default) / change role — administrator; each
  change audited, history visible (Members tab "Show history"); notifications to the person. Record roles granted / revoked in the same
  transaction (`member`, `leader`, `secretary`, `results_viewer`).
- **Meetings**: leader schedules; leader / secretary records attendance, minutes, decisions and "Submit minutes" (stamps
  `minutes_submitted_at`, computes the delay); recommendations / decisions added from the meeting. *Minutes writer* (AI) drafts minutes,
  decisions and recommendations from notes — a draft the secretary accepts.
- **Recommendations register**: leader manages; owner (employee) may update status / closure with evidence; **Create task** opens New
  Task with the recommendation linked (task-able type `teams.recommendation`) and the owner as assignee.
- **Team tasks**: Task Management with link `teams.team` (task-able) — the team page's Tasks tab is `LinkedTasksPanel`; the leader follows
  every linked task (`FollowLinkedPermission = teams.team.operate`). **Assign to a team's members**: New Task accepts `teamId` → one task per
  active internal member (multi-assignment group), via `ITeamsDirectory` (caller must read the team).
- **Periodic reports**: `teams.reports` job (daily 02:10) creates the current period's report row for active teams with a cadence
  (monthly / quarterly / biannually / annually), due at the end of working time on the period's last day (business hours of the owning
  unit); reminder N working days before (leader), overdue notice to leader and sponsor. Leader submits summary + files.
- **Documents**: generic attachments on the team (Files).

## 10. Links (Phase 6 registry)

- `teams.team` declared linkable (capabilities `task-able`; badge = number, name, status). `teams.recommendation` and `teams.meeting`
  task-able.
- Teams **link to team-able records**: a team stores its links in `links.record_link` (kind `team-able`, from `teams.team`). Linking requires
  `teams.team.manage` on the team and `CanLinkAsync(type, id, team-able)`; the type must be allowed by the team type (`linkable_types`).
- `GET /teams/linked?type=&id=` lists the teams linked to a record (the caller must read the record — `CanLinkAsync`/source — and each team
  is filtered by team read scope, others counted as restricted). **Sample items declare `team-able`**: the item page has a **Teams** tab
  (`LinkedTeamsPanel`: linked teams with leader and members, *Request a team* → formation wizard with the item linked, *Link existing
  team*).

## 11. Integration events (ADR 0043)

Kernel contracts `IIntegrationEvent`, `IIntegrationEventPublisher.PublishAsync(evt, transactionContext)`, `IIntegrationEventHandler<T>`;
Scheduling implements them on the outbox: one message **per subscriber** (`integration:<handler>`), so a failing subscriber retries alone,
handlers idempotent. `LinkedRecordClosed(ResourceType, ResourceId, Reason, ClosedAt)` lives in `Strategy.Platform.Links.Contracts`.
Teams subscribes (`OpenEvaluationOnLinkedRecordClosed`).

## 12. Current status, My Work, notifications, search

- `ICurrentStatusProvider` for `teams.team`: draft (requester: submit), waiting formation step (approvers), active (leader: next meeting /
  report due), pending evaluation (evaluator: open the wizard), waiting HR approval, rewards pending, closed, replaced.
- `IMyWorkSource`: evaluations I must fill, reports due for teams I lead, recommendations I own that are overdue, formation requests
  returned to me.
- Notifications (AR / EN, Settings → Notifications): `teams.activated` (members), `teams.memberAdded`, `teams.memberLeft`,
  `teams.formationReturned`, `teams.formationRejected`, `teams.evaluationOpened` (evaluator / leader evaluator), `teams.evaluationReturned`,
  `teams.evaluationApproved` (evaluator, HR), `teams.rewardsApproved` (HR), `teams.reportDue`, `teams.reportOverdue`,
  `teams.recommendationAssigned`, `teams.meetingScheduled`, `teams.evaluationReminder`.
- `IRecordSearchSource` / `IAiRecordSource` for `teams.team` (scope in the database query, results fields gated).

## 13. AI (Platform.AI registries — drafts only, CLAUDE.md rule 6)

| Feature key | Permission | What |
|---|---|---|
| `teams-formation` | `teams.ai.formation` | Draft objective, mandate and the bilingual memo body from a short description and the linked record (prompt `teams-formation.v1.md`) |
| `teams-members` | `teams.ai.members` | Member suggestions **without a model**: candidates in the owning unit and below and past members; score = skill overlap (required skills vs job title, past responsibilities / skills) − workload (active memberships); over-assigned flag (≥ threshold); past grade used only when the caller has results in scope |
| `teams-evaluation` | `teams.ai.evaluation` | Activity summary of the cycle (meetings, minutes timeliness, recommendations closed, outputs) explaining the suggested levels; inconsistency flags listed (deterministic, §7) (prompt `teams-evaluation.v1.md`) |
| `teams-minutes` | `teams.ai.minutes` | Notes → structured minutes, decisions and recommendations (owner names matched to members, due dates) (prompt `teams-minutes.v1.md`) |

The offline demo provider (development) answers the three prompts so E2E runs without a model.

## 14. MCP tools (tool registry, ADR 0031; §4.8)

`my_teams` (read, on), `team_details` (read, on; no scores unless results viewer), `my_pending_evaluations` (read, on),
`evaluation_status` (read, on; status and dates — scores only for results viewers), `add_meeting_minutes` (write, **off**; saves minutes as
a draft on a meeting the caller may minute, never submits).

## 15. API (`/api/v1/teams`, minimal APIs, Problem Details, idempotency on submit / approve-like commands)

| Endpoint | Notes |
|---|---|
| `GET /` (`view=all\|mine\|leading\|pending-evaluation`, type, status, unit, search, linkedType/linkedId, paging) · `GET /counts` | Register |
| `POST /` · `GET /{id}` · `PUT /{id}` · `POST /{id}/{submit\|cancel\|deactivate\|reactivate\|close}` | Formation request, team, lifecycle |
| `POST /{id}/members` · `POST /{id}/members/{mid}/leave` · `/replace` · `/role` · `GET /{id}/members?history=true` | Membership |
| `POST /{id}/memo` (external memo: number, date, file) · `POST /{id}/memo/regenerate` | Memo |
| `POST /{id}/links` · `POST /{id}/links/remove` · `GET /linked?type=&id=` | Team-able links |
| `GET/POST /{id}/meetings` · `GET/PUT /meetings/{mid}` · `POST /meetings/{mid}/{submit-minutes\|cancel}` · `PUT /meetings/{mid}/attendance` | Meetings |
| `GET/POST /{id}/recommendations` · `PUT /recommendations/{rid}` · `POST /recommendations/{rid}/status` | Register |
| `GET /{id}/reports` · `POST /reports/{rid}/submit` | Periodic reports |
| `GET /{id}/evaluations` · `POST /{id}/evaluations` (open) · `GET /evaluations/{eid}` · `PUT /evaluations/{eid}` (draft) · `POST /evaluations/{eid}/submit` · `/cancel` | Evaluation |
| `PUT /evaluations/{eid}/rewards` · `POST /evaluations/{eid}/rewards/submit` | Rewards |
| `GET /employees/{employeeId}/memberships` | Employee Teams tab |
| `GET /dashboard` · `GET /reports/export?kind=register\|membership\|evaluations\|rewards&format=xlsx\|pdf` | Dashboard, reports |
| `GET/PUT /settings`, `GET/POST/PUT /settings/types`, `/settings/roles`, `/settings/templates` (+ `/publish`), `/settings/reward-matrices` (+ `/publish`) | Settings → Teams |
| `POST /ai/formation` · `POST /ai/members` · `POST /evaluations/{eid}/ai/summary` · `POST /meetings/{mid}/ai/minutes` | AI drafts |

## 16. Screens (AR + EN, RTL / LTR)

**Teams register** (`/teams`: cards / list, tabs All · My teams · I lead · Pending evaluation, filters type / status / unit); **formation
wizard** (`/teams/new`: type, names, subject, objectives / mandate with *Draft with AI*, dates, owning unit, members with roles,
responsibilities and *Suggest members*, linked record, memo mode; save draft / submit); **team page** (`/teams/$id`: header with number,
type, status chips, leader, dates, linked records; stage stepper; current-status banner; tabs Overview, Members (history), Meetings,
Recommendations, Tasks, Reports, Documents, Evaluations, Activity (History)); **meeting page** (`/teams/meetings/$id`: attendance, minutes,
decisions, *Write minutes with AI*, submit); **evaluation wizard** (`/teams/evaluations/$id`, steps criteria → members → evidence →
review, suggested levels with their data, flags); **HR review** (same page for the approver: approval panel, previous cycles side by
side); **rewards** (evaluation page Rewards tab: calculated vs approved, justification, certificate, budget; submit); **dashboard**
(`/teams/dashboard`); **Settings → Teams** (general, types, member roles, templates with versions, reward matrices); **employee page →
Teams tab**; **Sample item → Teams tab**; **New Task → "Team's members"**; My Work Teams card.

## 17. Tests

Unit: lifecycle table (every allowed / forbidden pair), evaluation status table, template validation (weights, levels, **grade bands
without gaps / overlaps**), suggestions (closure %, delay, boundaries, no data), team score, points / grade, **evidence required at score ≥
threshold**, reward calculator (cells, missing cell, certificate rule, budget), inconsistency flags, cadence periods, annual / end-date
trigger dates. Integration (real PostgreSQL, fake clock, fake PDF renderer capturing HTML): formation → approval chain (director → GM) →
**numbered bilingual memo PDF** stored; external memo instead; member history on replace / leave (rows kept, roles moved); meetings with
minutes delay and recommendations feeding the **suggested levels**; evaluation by the configured evaluator → HR approval → **reward amounts
from the matrix** → rewards approval → certificates; **evaluation history per cycle** (two annual cycles kept); **ordinary member cannot see
results** (API 403/limited, search, Ask record source, MCP); President can; annual and end-date triggers with the fake clock; **fake
`LinkedRecordClosed` (project closure) opens the project-team evaluation**; Sample item team-able link (outsider restricted); Tasks
"assign to team's members"; AI with the fake model; MCP tools registered (write off). E2E (ar + en) screenshots in
`docs/screenshots/phase-7/`: teams register, formation request and approval, memo PDF, team page tabs, meeting minutes, recommendations,
evaluation wizard with suggested levels, HR review, rewards and certificate, employee Teams tab.
