# Build Plan — Integrated Strategy System

19 phases, built in order. Each phase has: goal · SPEC sections · scope · a **paste-ready prompt** for Claude Code · **acceptance checks**. Tick the progress checklist at the bottom as phases are accepted.

## How to run each phase

1. Start a fresh Claude Code session in the repository root (or `/clear` between phases) so context stays focused.
2. Paste the phase prompt. Claude Code enters plan mode, reads `CLAUDE.md`, this phase and the SPEC sections, and proposes a plan.
3. Review the plan (entities, endpoints, screens, permissions, tests). Approve or correct it.
4. Let it build in vertical slices. It runs the quality gates (CLAUDE.md §16) as it goes.
5. Demo the phase yourself in **Arabic and English**, walk through the acceptance checks, and only then tick the phase and move on.
6. If a requirement changes, **update `docs/SPEC.md` first**, then ask Claude Code to apply the change.

Every prompt below starts with the same header; keep it — it makes Claude Code follow the rules even in a fresh session.

---

## Phase 0 — Foundations and developer environment

**Goal:** a running, empty product shell with every cross-cutting decision fixed.
**SPEC:** §0.3, §0.6. **CLAUDE.md:** all.

**Scope**
- Git repo structure per CLAUDE.md §3; `Directory.Build.props` (nullable, warnings as errors, analyzers), `Directory.Packages.props`, `.editorconfig`.
- `Strategy.Host` (ASP.NET Core, .NET 10): Serilog, OpenTelemetry, health checks (`/health/live`, `/health/ready`), Problem Details, OpenAPI at `/openapi`, security headers (CSP, HSTS, X-Content-Type-Options, frame-ancestors), rate limiting, antiforgery, request correlation id and **channel** detection.
- `Platform.Kernel`: base entity, `Result`/error types, `IClock`, Guid v7 IDs, domain events, in-house command / query dispatcher (no MediatR), paging, Problem Details mapping with `code` + `messageKey`.
- Module template: one empty sample module wired end-to-end (schema, DbContext, migration, endpoint, TS client, page) to prove the pattern; then delete or keep as `_Template` documented.
- `web/`: Vite + React + TS strict, router choice, TanStack Query, i18next with `ar`/`en`, `dir` switching, Tailwind with logical properties + design tokens (light / dark), Radix + `DirectionProvider`, base UI kit (`Button`, `Input`, `Select`, `DatePicker`, `Dialog`, `Tabs`, `Table`, `Toast`, `EmptyState`, `Skeleton`, `PageHeader`, `StageStepper`, `StatusBanner`, `Wizard`), app shell (sidebar, top bar with language toggle, profile menu placeholder).
- OpenAPI → generated TS client pipeline.
- Tests: xUnit + Shouldly + Testcontainers base fixtures; architecture tests (module boundaries, banned packages); Vitest; Playwright with AR and EN projects.
- `deploy/docker-compose.yml` (postgres, gotenberg, mailpit; optional profiles: meilisearch, clamav, ollama), Dockerfile (multi-stage, non-root), `.env.example`.
- CI pipeline file (GitHub Actions or Azure DevOps — produce GitHub Actions; keep steps portable): build, all quality gates, vulnerability scan, E2E.
- `README.md` (dev setup), `docs/THIRD_PARTY_LICENSES.md`, `docs/decisions/0001-*.md` for the choices made (router, endpoint style, etc.).

**Prompt**
```
Phase 0 — Foundations and developer environment.
Follow CLAUDE.md strictly (rules §2, working method §17). Read CLAUDE.md fully, docs/BUILD_PLAN.md "Phase 0", and docs/SPEC.md §0.3 and §0.6.
Enter plan mode and propose the plan for the Phase 0 scope listed in BUILD_PLAN.md. Include: solution and project list, package list with licences (must satisfy CLAUDE.md §4.3), folder structure for web/, the UI kit components, the docker-compose services, CI steps, and the ADRs you will write (router choice, minimal APIs vs controllers, dispatcher design).
After I approve, implement it, prove the module pattern with one sample module end-to-end (DB → API → generated TS client → page in AR and EN), and run every quality gate in CLAUDE.md §16. Finish with the phase summary (CLAUDE.md §17.5) and update CLAUDE.md §18 commands to the real ones.
```

**Acceptance checks** (verified by Claude Code in the build session; owner acceptance pending — see `docs/phase-summaries/phase-0.md`)
- [x] `docker compose up` + `dotnet run` + `npm run dev` give a working shell; language toggle flips the whole layout RTL ↔ LTR instantly.
- [x] Sample module round-trip works; migration applies from empty DB.
- [x] All quality gates green (locally, with the exact CI commands; the GitHub Actions run starts with the pull request); architecture tests fail if a module references another module's internals or a banned package.
- [x] Security headers present; OpenAPI published; health endpoints respond.
- [x] ADRs and `THIRD_PARTY_LICENSES.md` written.

---

## Phase 1 — Identity, Organisation and Employees

**Goal:** people, structure and login.
**SPEC:** §0.1 (Organisation, Employee / User), §0.0 global decisions (authentication), §0.2 (delegation basics), §0.3.

**Scope**
- Organisation units: unlimited-depth hierarchy, configurable level names, AR/EN names, unit code (used by numbering), head / deputy, cost centre, active flag, effective dates, **history preserved on re-org**; org chart view; Excel import.
- Employees: all SPEC fields, photo, manager, unit, status; user account linked to employee; Excel import; optional AD sync job (stub with interface).
- Authentication: ASP.NET Core Identity local login, lockout, password policy, **MFA** (TOTP + email OTP), password reset, session management (cookie, HttpOnly, Secure, SameSite), logout everywhere.
- External identity providers, **configured in Settings, enabled per installation**: OpenID Connect (Entra ID / ADFS), **UAE Pass** (OIDC profile), SAML 2.0 (Sustainsys.Saml2), LDAP / AD bind. Account linking by employee number / email; just-in-time linking rules configurable.
- Personal access tokens (create, scope, expiry, revoke) — used later by API / MCP.
- Profile page (language preference, theme, MFA setup, tokens), user admin screens.
- Delegation records (from, to, date range, scope) — consumed by the approval engine in Phase 4.

**Prompt**
```
Phase 1 — Identity, Organisation and Employees.
Follow CLAUDE.md strictly (rules §2, working method §17). Read CLAUDE.md, docs/BUILD_PLAN.md "Phase 1", and docs/SPEC.md §0.0 (global decisions table: authentication, single workspace, external users), §0.1 Organisation and Employee/User modules, §0.3.
Plan first: data model (units with history, employees, users, provider configs, tokens, delegations), auth schemes and their configuration screens, import templates, screens, tests (including negative auth tests and MFA). Use Platform projects Identity and Organisation.
External providers must be fully configurable from the UI at runtime (no appsettings edits needed by a customer) and disabled by default. Secrets encrypted with Data Protection.
Implement, test (unit, integration with Testcontainers, E2E login in AR and EN), and finish with the phase summary.
```

**Acceptance checks**
- [ ] Admin builds a 4-level org structure, re-parents a unit, and historical records still show the old structure.
- [ ] Import employees and units from the Excel templates (with validation report).
- [ ] Local login with MFA works; lockout after failed attempts; password reset by email (Mailpit).
- [ ] OIDC and SAML providers can be configured and tested against a test IdP (document how); LDAP bind tested against a container.
- [ ] Personal access token created, used on an API call, revoked.
- [ ] All auth events visible in logs (audit UI comes in Phase 3).

---

## Phase 2 — Access control and Settings centre

**Goal:** the three-layer permission model and the configuration backbone every module uses.
**SPEC:** §0.2 (access management, system ownership, bulk role transfer, configurable lists, rules & thresholds, localisation, branding, settings centre).

**Scope**
- `Platform.Access`: permission catalogue (seeded, AR/EN labels), systems registry, **system access**, **system roles** with data scope, **record roles** table and API for modules, `IAccessService.CanAsync` / `ApplyScope`, 404-vs-403 rule, policy helpers for endpoints, **system ownership** (owning unit per system), **bulk role transfer** (with re-routing hook for pending approvals / tasks — wired in Phase 4), **access review report** (export).
- Settings centre UI: typed settings per area, **lookups / picklists** manager (AR/EN, order, active, parent), **numbering formats** service + UI, **feature flags / module enable-disable**, **branding** (name, logos, colours, email header/footer), **translations** override editor, regional settings (time zone, Hijri toggle off by default, Arabic-Indic digits toggle).
- App shell shows only systems the user can access.
- Permission matrix screen per system (roles × permissions × scope).

**Prompt**
```
Phase 2 — Access control and Settings centre.
Follow CLAUDE.md strictly (rules §2, §7 access control, working method §17). Read CLAUDE.md, docs/BUILD_PLAN.md "Phase 2", docs/SPEC.md §0.2 (all bullets about access management, configurable lists, rules & thresholds, localisation, branding, settings centre).
Plan first: access data model, the IAccessService contract and how modules will use it (with an example endpoint and list query), permission catalogue format, the settings / lookups / numbering / feature-flag services, and the Settings UI navigation.
Implement with exhaustive authorization tests: each data scope (Own, Team, Unit, Unit-and-below, All) proven by tests, record-role grants proven, out-of-scope record returns 404.
Finish with the phase summary.
```

**Acceptance checks**
- [ ] A user with "Unit-and-below" scope sees only records of their unit tree; tests cover all 5 scopes.
- [ ] Granting a record role gives access to exactly that record.
- [ ] Disabling a system in Settings hides it from the sidebar and blocks its API.
- [ ] Lookup values and numbering formats edited in the UI take effect without redeploy.
- [ ] Translation override changes a label live in both languages.
- [ ] Access review report exports to Excel.

---

## Phase 3 — Audit trail, Files, Notifications, Jobs

**Goal:** the platform services that make everything traceable and communicative.
**SPEC:** §0.2 (audit trail, notifications, scheduler, documents, auto-numbering), CLAUDE.md §8, §14.

**Scope**
- Audit interceptor (all fields, all audited entities), explicit security / export / download / settings events, **hash chain** + verification job + admin check screen, DB role with INSERT/SELECT only on `audit.entry`, **History tab component** for any record, **global Audit Log screen** (search, filter, export), **view record as of date**.
- Files: `IFileStore` (local volume, S3-compatible option), typed attachments per record, versions, allow-list + magic-number checks, size limits, optional ClamAV scan, authorised download endpoint, preview component (pdf.js / images).
- Notifications: template registry (event key → AR/EN subject/body, editable in Settings), email via MailKit with bilingual side-by-side layout and branding, in-app notification centre (SignalR live), user preferences, daily digest option, outbox with retry.
- Jobs: Quartz.NET with PostgreSQL store, outbox dispatcher, job dashboard for admins (last run, next run, failures).
- PDF rendering service via Gotenberg (HTML templates, Arabic fonts) + Excel export helpers.

**Prompt**
```
Phase 3 — Audit trail, Files, Notifications, Jobs.
Follow CLAUDE.md strictly (rules §2, §8 audit, §14 notifications/files/jobs, working method §17). Read CLAUDE.md, docs/BUILD_PLAN.md "Phase 3", docs/SPEC.md §0.2 bullets on audit trail, notifications, scheduler, documents.
Plan first: audit entry schema and canonical hashing, interceptor design (incl. channel and on-behalf-of), the DB permission setup for append-only, file storage abstraction and security checks, notification template model and email layout, outbox and Quartz setup, Gotenberg PDF service.
Implement with tests: an update produces correct field-level entries; tampering with a row is detected by the verification job; a download by an unauthorised user is refused and audited; a notification is delivered in both languages to Mailpit; a bilingual PDF renders Arabic correctly (add a snapshot test).
Finish with the phase summary.
```

**Acceptance checks**
- [ ] Editing a record shows old → new values in its History tab with user, time and channel.
- [ ] Manually altering an audit row in the DB is flagged by the verification screen.
- [ ] Audit Log screen searches across systems and exports.
- [ ] Upload of a disallowed type is rejected; allowed file downloads only for authorised users.
- [ ] Bilingual email and bilingual PDF render correctly (Arabic shaping, RTL).
- [ ] Job dashboard shows scheduled jobs and their runs.

---

## Phase 4 — Approval engine, Approval Settings centre, My Work

**Goal:** one configurable approval engine for all systems, and the user's home page.
**SPEC:** §0.2 (configurable workflow & approval engine, global Approval Settings centre, delegation, "Current Status" engine), §0.3 (My Work), §0.4 level 1 (My Work basics). CLAUDE.md §9.

**Scope**
- Process definitions (versioned), steps, approver rules (role, org relations, specific employee, record role, team role, submitter's manager, owning unit of system), entry conditions (sandboxed expressions), allowed actions, comments rules, SLA + escalation, notification templates per step.
- Instances, tasks, actions (Approve / Reject / Return / custom), resubmission linking, history timeline, hooks / events for modules, delegation ("on behalf of"), bulk role transfer re-routing of pending tasks (completes Phase 2 feature).
- **Global Approval Settings centre** UI listing every process key registered by modules; editor for steps with live preview ("who would approve this sample record").
- `ICurrentStatusProvider` contract + header banner component + **My Work** page: pending approvals (all systems), items needing my action, notifications; extensible cards for later modules (tasks, KPIs, etc.).
- Shared **approval panel** component for record pages.
- Sandbox demo process for testing (dev-only seed).

**Prompt**
```
Phase 4 — Approval engine, Approval Settings centre, My Work.
Follow CLAUDE.md strictly (rules §2, §9 workflow, working method §17). Read CLAUDE.md, docs/BUILD_PLAN.md "Phase 4", docs/SPEC.md §0.2 (approval engine, approval settings centre, delegation), §0.3 (My Work), §0.4 level 1.
Plan first: definitions/instances/tasks schema, approver-rule resolution against the Organisation module, entry-condition expression sandbox, SLA/escalation jobs, module registration API (process keys, hooks, current-status providers), the Approval Settings UI, My Work page layout.
Implement with tests: each approver rule type resolves correctly; skipped steps by condition; return-for-correction and resubmission; delegation recorded as "on behalf of"; SLA escalation fires; an approver outside the chain cannot act (403); every action audited.
Finish with the phase summary.
```

**Acceptance checks**
- [ ] Admin creates a 3-step process with a conditional step in the UI; a demo record follows it, including return for correction and resubmission.
- [ ] Delegated approver approves "on behalf of"; history shows both names.
- [ ] Overdue step escalates per SLA (time-travel test with fake clock).
- [ ] My Work lists pending approvals across modules with deep links; current-status banner shows next actions.

---

## Phase 5 — AI platform and MCP server

**Goal:** provider-neutral AI and the MCP server, ready for modules to plug into.
**SPEC:** §0.5 (all), CLAUDE.md §12, §13.

**Scope**
- Settings → AI: providers (Anthropic, OpenAI, Azure OpenAI, OpenAI-compatible self-hosted), encrypted keys, endpoint, model list / free text, per-feature model routing, limits, **Test connection**, master switch, per-feature switches, per-role access, data-sharing controls and PII masking, usage log + monthly limits.
- Shared AI features as reusable services + UI components: **document-to-form extraction** (upload PDF / Word / PowerPoint in AR or EN → structured fields with confidence and source snippets, user review with highlighted fields), **approver brief**, **auto-translation AR ⇄ EN** for text fields, **similar records** (embedding-free baseline using full-text + trigram; optional embeddings later), "**Ask the system**" (natural-language Q&A over data the user may see — via tools that call module queries with the user's permissions).
- MCP server at `/mcp` (official C# SDK, Streamable HTTP), **OpenIddict** OAuth 2.1 (authorization code + PKCE) + PAT auth, tool registry with per-tool enable / disable, rate limits, audit of every call; core tools: who am I, my notifications, my pending approvals (read), search records (permission-filtered).
- Admin docs page: how to connect Claude Desktop / Claude Code to the installation's MCP server.

**Prompt**
```
Phase 5 — AI platform and MCP server.
Follow CLAUDE.md strictly (rules §2 incl. rule 6 "AI drafts, people decide", §12 AI, §13 MCP, working method §17). Read CLAUDE.md, docs/BUILD_PLAN.md "Phase 5", docs/SPEC.md §0.5.
Plan first: provider registry over Microsoft.Extensions.AI IChatClient, feature-key routing, prompt-template structure and JSON-schema validation, prompt-injection defences for uploaded documents, document text extraction approach for PDF/Word/PowerPoint (free libraries only), the review UI for AI-filled forms, MCP server setup with OpenIddict, tool registration contract for modules, and audit/usage logging.
Implement with tests using a fake IChatClient (no real API calls in CI): extraction maps to fields and is never saved without user confirmation; disabled AI hides UI and APIs return a clear "feature disabled"; MCP calls run with the token owner's permissions (out-of-scope record not returned); vendor principals refused.
Finish with the phase summary and a short guide for connecting Claude to /mcp.
```

**Acceptance checks**
- [ ] Admin configures a provider and passes **Test connection**; switching provider needs no code change.
- [ ] Uploading a sample business case fills a demo form as highlighted drafts with sources; nothing saved until confirmed.
- [ ] With AI switched off, no AI buttons appear and the app works normally.
- [ ] Claude (desktop or Claude Code) connects to `/mcp` via OAuth, lists tools, and only sees the user's permitted data; calls appear in the audit log with channel `mcp`.

---

## Phase 6 — Links registry and Task Management

**Goal:** the generic cross-system link mechanism and the shared task engine.
**SPEC:** §11 (all), §0.0 cross-system links, §0.4 level 1 (My Tasks card).

**Scope**
- `Platform.Links`: registry where modules declare linkable record types ("task-able", "team-able", "remark-source"), link storage, link display component (shows target record title, status badge via Contracts), permission-aware.
- Task Management module per SPEC §11: Full and Simple workflow templates, task types / sources / categories, **multi-assignment → one task per assignee + Assignment Group**, sub-tasks rule, timesheets on pause / start, follow-up employees, department secretary rights, comments with @mentions and attachments, checklist, recurrence, dependencies (for project tasks), confidential tasks, reminders and escalations, **creator approves** with feedback point.
- Screens: My Tasks (tabs, list, Kanban, calendar), New Task (multi-assignee), task page with timeline, assignment group page, task dashboard.
- My Work task card; task AI features (tasks from text, assignee suggestion, summaries, workload insights); MCP tools (§11.10).
- Employee performance input metrics stored (§11.11).

**Prompt**
```
Phase 6 — Links registry and Task Management.
Follow CLAUDE.md strictly (rules §2, working method §17). Read CLAUDE.md, docs/BUILD_PLAN.md "Phase 6", docs/SPEC.md §11 (all), §0.0 cross-system links table, §0.4.
Plan first: Links registry contract (how a module declares a linkable type and renders a link), Task domain model and the Full/Simple state machines as transition tables, multi-assign and Assignment Group behaviour, timesheet logic, access rules (§11.9) as record roles, reminders/escalation jobs, screens, AI features and MCP tools.
Implement with tests: every allowed and forbidden transition; multi-assign to 3 people creates 3 independent tasks + 1 group; sub-task only while parent is In Progress/On Hold; only the creator can approve; follow-up employee can comment/upload but not submit; confidential task hidden from others; timesheet durations correct across pauses.
Finish with the phase summary.
```

**Acceptance checks**
- [ ] Manager assigns one task to 3 employees; each accepts / rejects independently; group view shows "1 of 3 approved".
- [ ] Assignee pauses and restarts; timesheet shows periods and hold reasons; creator approves with feedback point.
- [ ] Kanban and calendar views work in AR and EN.
- [ ] AI turns pasted meeting notes into draft tasks that the creator confirms.

---

## Phase 7 — Teams Management

**Goal:** teams, committees, formation approval, operations, evaluation and rewards — shared by all systems.
**SPEC:** §4 (all).

**Scope** — everything in SPEC §4: configurable team types, member roles, evaluator setting, evaluation triggers (project closure event, end date, annual), versioned evaluation templates (team criteria + member questions + grade bands + evidence threshold), reward matrix (grade × workload × role), formation request + approval (engine) + **system-generated memo PDF or uploaded memo**, members with history and external members, meetings & minutes (with minutes-delay tracking), recommendations register, team tasks (via Task Management), periodic reports, evaluation wizard with **data-suggested criteria levels**, HR approval, reward calculation, certificates (bilingual PDF), visibility rules (only leader roles + HR see results), employee profile Teams tab, dashboard, reports, AI features, MCP tools. "Team-able" link type registration for other modules.

**Prompt**
```
Phase 7 — Teams Management.
Follow CLAUDE.md strictly (rules §2, working method §17). Read CLAUDE.md, docs/BUILD_PLAN.md "Phase 7", docs/SPEC.md §4 (all, including decisions in §4.9), and §0.0 cross-system links.
Plan first: data model (team, member history, meetings, recommendations, evaluation cycles as separate records, member evaluations, reward matrix versions), state machine, approval process keys (teams.formation, teams.evaluation, teams.reward), evaluation trigger jobs/events, memo and certificate templates, visibility rules, screens, AI features, MCP tools.
Implement with tests: evaluation history kept per cycle; grade bands validated without gaps; evidence required at team score ≥ threshold; reward amounts computed from the matrix; ordinary members cannot see results; formation approval generates a numbered bilingual memo; a project-closure integration event (fake publisher) opens a project-team evaluation.
Finish with the phase summary.
```

**Acceptance checks**
- [ ] Create a committee through formation approval; memo PDF generated (AR/EN) or uploaded instead.
- [ ] Meetings with minutes delay and a recommendations register feed the suggested evaluation levels.
- [ ] Evaluation submitted by the configured evaluator, approved by HR, rewards calculated, certificates generated.
- [ ] A member logs in and cannot see evaluation results; the President can.

---

## Phase 8 — Portfolio Management A: Program Requests

**Goal:** the request stage up to automatic project creation.
**SPEC:** §1.1–1.4 A, §1.5 W1, §1.6 (request messages), §1.7, §1.8 (requests), §1.9, §1.15 A (smart request + Proposal Assessment), §1.19 + Appendix A (Q-P4, Q-P5, Q-P6).

**Scope** — Program Request with all fields and validation rules; screening → classification (Appendix A rule, PMO override); priority scoring (configurable weights / bands); cost estimation vs project-scope path; Administration Dept quotations hand-off; approval process `portfolio.request` (Section Manager → Department Director → PM Office → DG Office by threshold → Budget Section; conditions per Appendix A); return for correction; cancel with reason; **auto-create Project / Initiative** on final approval (`PROJ-...` numbering); current-status messages; notifications; request list and record page; **"Start from a document" smart request** with AI extraction + **Proposal Assessment** (versioned); MCP tools for requests.

**Prompt**
```
Phase 8 — Portfolio Management A: Program Requests.
Follow CLAUDE.md strictly (rules §2, working method §17). Read CLAUDE.md, docs/BUILD_PLAN.md "Phase 8", docs/SPEC.md §1.1–§1.4 A, §1.5 W1, §1.6, §1.7, §1.8, §1.9, §1.15 A, §1.19 and Appendix A (Q-P4, Q-P5, Q-P6).
Write docs/design/portfolio.md first (covering the whole Portfolio module, with Phase 8 detailed): request data model and validations, classification and priority calculators, the request state machine, approval process configuration seeded as default, Admin Dept quotation flow, project auto-creation contract, screens (wizard), smart-request AI flow and Proposal Assessment.
Implement with tests: every validation rule from §1.4 A; classification and evaluation score table tests; approval routes for budget not required / ≤ threshold / > threshold; return-for-correction loop; project created with correct number and data on final approval; AI-filled request never saved without confirmation.
Finish with the phase summary.
```

**Acceptance checks**
- [ ] Three demo requests (no budget / small / large) follow the three approval routes; final approval creates the Project or Initiative.
- [ ] Year 1–4 costs not equal to budget blocks submission with a clear AR/EN message.
- [ ] Upload a sample case study → form pre-filled, gaps listed, Proposal Assessment visible to approvers.

---

## Phase 9 — Portfolio Management B: Project lifecycle (waterfall) and change control

**Goal:** charter to completion for waterfall projects and initiatives.
**SPEC:** §1.3, §1.4 B–D, §1.5 W2–W8, §1.6, §1.7, §1.8, §1.9, §1.16, §1.17, Appendix A (Q-P1, Q-P2, Q-P3, Q-P9).

**Scope** — Take Ownership; charter with minimums gate (≥1 KPI, ≥1 risk, ≥1 stakeholder, ≥3 team members); charter approval; LPO / contract upload by Administration; **team formation through Teams Management**; planning items, milestones with hierarchy, weights (=100%) and high-level plan; planning approval; monthly cycle (window, reminders, milestone updates with evidence / delay rules, approval chain per Appendix A, achievement lock); **project tasks via Task Management**; challenges, meetings / MOM, lessons learned; closure form + approval; Impact stage (switchable) + approval; change requests (types, conditional approvers, **semi-automatic apply**); cancel / hold; Initiative stage template (Appendix A Q-P2); project KPIs (new or linked to KPI Card — create the Contracts interface now, wire the KPI module in Phase 12) and project risks (shared scoring — interface now, Risk module Phase 13); current-status messages; record page with stage stepper and all tabs; MCP tools.

**Prompt**
```
Phase 9 — Portfolio Management B: Project lifecycle (waterfall) and change control.
Follow CLAUDE.md strictly (rules §2, working method §17). Read CLAUDE.md, docs/BUILD_PLAN.md "Phase 9", docs/design/portfolio.md, docs/SPEC.md §1.3, §1.4 B–D, §1.5 W2–W8, §1.6–§1.9, §1.16, §1.17 and Appendix A (Q-P1, Q-P2, Q-P3, Q-P9).
Update docs/design/portfolio.md for this phase first: project and initiative state machines (transition tables), gates, approval process keys and seeded chains, monthly-cycle job design, calculators (performance, planned-to-date, progress status), integrations with Teams (formation, closure → evaluation event) and Tasks, contracts for KPI and Risk modules, change-request apply flow.
Implement with tests: gate blocks submission until minimums met; weights must total 100%; 100% milestone requires evidence and actual end date; delayed milestone requires causality and corrective actions; approved achievement locked; closure → Impact → Completed; initiative follows its lighter template; change request applies changes only after PM confirms; closure approval publishes the event that opens the team evaluation.
Finish with the phase summary.
```

**Acceptance checks**
- [ ] A full demo project runs from Take Ownership to Completed, in Arabic, with every approval and notification.
- [ ] Monthly update cycle opens on the 1st (fake clock), reminders sent, update approved and locked.
- [ ] Ownership change request, once approved and applied, transfers PM record roles.
- [ ] Project closure automatically opens the project team's evaluation in Teams Management.

---

## Phase 10 — Portfolio Management C: Agile / Hybrid, PM standards, Gantt, portfolio views

**Goal:** complete Portfolio Management.
**SPEC:** §1.12, §1.13, §1.15 B, §1.16, §0.4 (portfolio system dashboard).

**Scope** — methodology selection (Waterfall / Agile / Hybrid), releases, epics (weights), user stories, sprints, boards (configurable columns, WIP limits, swimlanes), sprint review / retrospective (→ lessons learned), impediments (→ issues), agile metrics (velocity, burndown, burnup, cumulative flow), agile progress formula, sprint vs monthly roll-up approval (per installation / project); standards enhancements (each switchable): portfolios & programmes, prioritisation model, RAG health (auto + override), baselines & variance (re-baseline via change request), EVM, RAID (issues, assumptions, dependencies, decisions), 5×5 risk matrix view, RACI, communication plan, benefits realisation, deliverable quality sign-off, contracts with payment milestones and supplier rating, auto-generated monthly status report (PDF), stage-gate checklists, project templates; in-house SVG **Gantt** (project and portfolio-wide, quarter grouping, RTL); portfolio dashboards; remaining AI assists (charter assistant, plan generator, update assistant, status report writer, change impact, lessons search, portfolio insights).

**Prompt**
```
Phase 10 — Portfolio Management C: Agile/Hybrid, PM standards, Gantt, portfolio views.
Follow CLAUDE.md strictly (rules §2, working method §17). Read CLAUDE.md, docs/BUILD_PLAN.md "Phase 10", docs/design/portfolio.md, docs/SPEC.md §1.12, §1.13, §1.15 B, §1.16, §0.4.
Update the design note first: agile data model and state machines, how agile/hybrid progress feeds the common project progress and portfolio dashboards, feature flags for each standards enhancement, Gantt component design (RTL mirroring, quarters, planned vs actual), status report template, AI assists.
Implement with tests: agile progress formula; sprint and monthly roll-up approval modes; baseline variance; EVM values on a worked example; RAG calculation; each enhancement switch hides/shows its UI and APIs.
Finish with the phase summary.
```

**Acceptance checks**
- [ ] An agile project runs two sprints with board, burndown and a monthly roll-up approval; it appears beside waterfall projects on the portfolio dashboard.
- [ ] Gantt renders correctly in RTL and LTR for a 30-milestone project in under 5 s.
- [ ] Monthly status report PDF generated in both languages.

---

## Phase 11 — Wasel vendor portal

**Goal:** vendors take part in planning and execution.
**SPEC:** §1.18, §1.5 W3–W4, §1.9, §0.0 (external users).

**Scope** — vendor register (shared supplier master), vendor users (invite, MFA mandatory, optional UAE Pass, expiry at contract end / closure, dormant disable, roles Lead / Contributor), separate auth scheme + `/wasel` SPA area + `/api/wasel` with **vendor-visible DTOs** and field flags, plan proposal flow, milestone update submissions (**Vendor then PM** mode per project), PM review (accept / edit-then-accept with diff / return), deliverables, draft issues, meetings view, comment threads, submission status, vendor notifications, vendor performance rating at closure, Wasel audit channel, internal AI summary of vendor submissions.

**Prompt**
```
Phase 11 — Wasel vendor portal.
Follow CLAUDE.md strictly (rules §2, §7 access control — vendor principals, working method §17). Read CLAUDE.md, docs/BUILD_PLAN.md "Phase 11", docs/design/portfolio.md, docs/SPEC.md §1.18, §1.5 W3–W4, §1.9 and §0.0 (external users row).
Plan first: vendor principal and auth scheme, separation from internal sessions, vendor-visible field model, plan-proposal and update-submission state machines, PM review UI with diff, how accepted vendor values enter the normal monthly approval, notifications, rating model.
Security tests are mandatory: a vendor user cannot reach any internal endpoint, other vendors' projects, non-assigned milestones, budget fields, MCP or AI; access expires automatically; every vendor action audited with channel wasel.
Finish with the phase summary.
```

**Acceptance checks**
- [ ] PM invites a vendor; vendor logs in with MFA at `/wasel`, sees only assigned project data, proposes a plan, submits a monthly update.
- [ ] PM edits one value before accepting; vendor sees the change; approvers see which values came from the vendor.
- [ ] Penetration-style tests (manual checklist + automated) find no internal data exposure.

---

## Phase 12 — Performance Management (KPI)

**SPEC:** §2 (all), Appendix A (Q-K2, Q-K6).

**Scope** — KPI Cards, Annual Plans, Results; new measuring-year wizard; period windows with reminders, late flag, reopen; submit-result wizard with evidence and N/A; approval queue with verification checklist and **bulk approval**; configurable approval chain; **PerformanceCalculator** (actual + capped, all 14 test rows) and Measuring-Method aggregation; RAG bands; strategy hierarchy and strategy map; cascading; weighted scorecards; baselines and multi-year targets; composite KPIs from data elements; qualitative KPIs; definition change control with versions; data-quality score; trends; excellence (GEM / Global Star) reporting; integrations (PMS project KPI link, KRI hook for Risk, CAP remark hook — interfaces now, wired when those modules exist); dashboards; AI (evidence checker, anomaly flags, causality assistant, KPI card builder, target suggestion, performance narrative); MCP tools.

**Prompt**
```
Phase 12 — Performance Management (KPI).
Follow CLAUDE.md strictly (rules §2, §15 calculations, working method §17). Read CLAUDE.md, docs/BUILD_PLAN.md "Phase 12", docs/SPEC.md §2 (all) and Appendix A (Q-K2, Q-K6).
Write docs/design/kpi.md first: data model, new-year generation rules, period window jobs, result state machine, approval process kpi.result (default single PMO step), PerformanceCalculator and aggregation, scorecard roll-up using capped performance, strategy hierarchy, change control, integrations (Portfolio project KPI contract, KRI events to Risk, CAP hook), screens, AI, MCP.
Implement with tests: the 14-row performance table in SPEC §2 K5 (actual and capped), aggregation by Measuring Method, period generation per measuring period, late flag, bulk approval, N/A excluded from roll-ups, scope rules for KPI owners.
Finish with the phase summary.
```

**Acceptance checks**
- [ ] New measuring year generates plans and results; Q1 window opens per Appendix A; owners notified.
- [ ] Owner submits with evidence; PMO uses the checklist and bulk-approves a department.
- [ ] Scorecard and strategy map show capped performance; trend charts show actual.
- [ ] Portfolio project KPIs can now link to KPI Cards; the KPI card shows linked projects.

---

## Phase 13 — Risk Management

**SPEC:** §3 (all), Appendix A (Q-R3).

**Scope** — risk framework settings (categories, scales with impact criteria, scoring method incl. **custom formula editor with preview and impact count, versioning**, 4 bands, residual-status mapping, control-effectiveness scale, appetite, response strategies, review frequency, numbering); risks with cause → event → consequence, inherent / residual / target, controls separate from actions, convert completed action to control, KRIs (link to KPI Cards with thresholds, react to approved KPI results), assessment history snapshots, risk events / incidents, **Project Risks tab** (read-only from Portfolio, shared scoring), workflows R1–R5 with configurable approvals, escalation and Top-10, dashboards and heat-maps, generated Risk Assessment Mechanism page, reports, AI features, MCP tools. Re-point Portfolio project risks to the shared scoring framework.

**Prompt**
```
Phase 13 — Risk Management.
Follow CLAUDE.md strictly (rules §2, §15 calculations, working method §17). Read CLAUDE.md, docs/BUILD_PLAN.md "Phase 13", docs/SPEC.md §3 (all incl. decisions in §3.10) and Appendix A (Q-R3).
Write docs/design/risk.md first: framework settings model and versioning, sandboxed formula evaluation, risk/control/action/KRI/event/history model, state machine, approval process keys, review scheduling, KRI integration with KPI result events, Project Risks read model from Portfolio contracts, dashboards, AI, MCP.
Implement with tests: scoring for multiplicative, additive and a custom formula; band mapping; formula change preview counts affected risks; KRI breach on an approved KPI result flags the risk; review due dates by rating; numbering unique for units sharing a code.
Finish with the phase summary.
```

**Acceptance checks**
- [ ] Admin changes the formula; preview shows impact; chosen recalculation option applied and audited.
- [ ] Heat-maps (inherent vs residual) and Top-10 work; project risks appear as a filterable category.
- [ ] A KPI result breaching a KRI threshold notifies the risk owner.

---

## Phase 14 — Request Framework, Document Registry, IP Registry

**SPEC:** §5, §6, §7, Appendix A (Q-RQ2).

**Scope** — Request Framework (request types, form builder, stages and actions, approvals via engine, request catalogue, My Requests, processor queues, PDF templates, intranet deep links / embeddable submit pages); Document Registry (submission, new versions, review with classification, Document ID + version, publish with share-to-all, supersede, **validity by document type** with reminders / confirm / expire, withdraw, library with sensitivity clearance and watermark, version compare, read acknowledgement, dashboard, AI); IP Registry (request with contributors and encrypted Emirates ID, review, MOET form PDF, filed / registered / rejected-by-MOET, expiry default + renewal reminders, internal-only protection, certificates emailed, **auto-creation from Document Registry publish when the type is flagged**, dashboard, AI).

**Prompt**
```
Phase 14 — Request Framework, Document Registry, IP Registry.
Follow CLAUDE.md strictly (rules §2, working method §17). Read CLAUDE.md, docs/BUILD_PLAN.md "Phase 14", docs/SPEC.md §5, §6, §7 and Appendix A (Q-RQ2).
Write docs/design/requests.md first: the Request Framework (type definition, form schema in jsonb with server-side validation, stage/action model, approval integration, catalogue and queues) and how Document Registry and IP Registry are built on it with their own registers and logic; document validity rules and jobs; sensitivity enforcement; document-publish → IP integration event; MOET form template; Emirates ID encryption and masking.
Implement with tests: form schema validation server-side; document versioning and supersede; expiry reminders/confirm/expire with fake clock; sensitivity hides documents from unauthorised users (search included); publishing a flagged type creates an IP record; Emirates ID never appears unmasked in API responses, logs or audit values.
Finish with the phase summary.
```

**Acceptance checks**
- [ ] Employee submits a new document from the catalogue; Strategy Office classifies, approves, publishes; library shows it to the right audience only.
- [ ] Publishing a flagged document type creates the IP record; MOET form PDF prints in Arabic.
- [ ] An admin creates a new simple request type with the form builder without code.

---

## Phase 15 — Corrective Action Plan and Benchmarking

**SPEC:** §9, §8. (CAP first; Benchmarking depends on it.)

**Scope** — CAP: sources (incl. auto-created from other systems), remarks with priority defaults and root cause, accept / reject (final by default; optional PMO confirmation), reassign, actions (new or link existing — many-to-many), budget routing (draft Program Request in Portfolio when budget needed and unavailable), PMO validation per action, monthly reminders, achievement history, late escalation, extension requests, completion approval with evidence, optional effectiveness verification (off), reporting back to source records, dashboards, AI, MCP; hooks wired from KPI (missed target), Risk (events / ineffective controls). Benchmarking: four stages with PMO gates, annual plan, 30-day pre-quarter notification, search / outreach, visit info, analysis rows, **development plan → CAP remarks + actions**, closure when linked CAP actions complete, comparator register, best-practice library, KPI benchmark update, impact follow-up, team link, dashboards, AI, MCP.

**Prompt**
```
Phase 15 — Corrective Action Plan and Benchmarking.
Follow CLAUDE.md strictly (rules §2, working method §17). Read CLAUDE.md, docs/BUILD_PLAN.md "Phase 15", docs/SPEC.md §9 (with decisions) and §8.
Write docs/design/cap.md and docs/design/benchmarking.md first: CAP data model incl. remark–action many-to-many, state machines, approval keys (cap.action-validation, cap.completion, cap.extension), budget routing contract with Portfolio, source-system integration contract (any module can create a remarks source + remarks and receive completion events); Benchmarking stages, gates, notification job, and the analysis-approved → CAP integration.
Implement with tests: rejection final by default and PMO confirmation when switched on; linking one action to two remarks; budget-needed action creates a draft Program Request with source "Action Plan"; benchmark analysis approval creates CAP remarks/actions; benchmark closes only when all linked actions are completed with evidence.
Finish with the phase summary.
```

**Acceptance checks**
- [ ] Workshop remarks → section manager accepts → actions validated → monthly updates → completion approved.
- [ ] A missed KPI and a risk event each raise a CAP remark linked to their source.
- [ ] A benchmark study runs Plan → Search → Acquire → Analysis; its actions appear in CAP; the study closes when they complete.

---

## Phase 16 — Audit Management

**SPEC:** §10 (with decisions §10.8).

**Scope** — Layer 1: audit reports (types), findings (condition / criteria / cause / effect, priority, repeat link, risk / control links), recommendations (no default due date), **follow-ups recorded only by the Audit Office**, late status, revised dates and risk acceptance logs, cancel, closure rules, Excel import, **read-only department view** scoped to section / department, follow-up calendar, dashboards, reports (incl. revenue audit report, audit committee pack), notifications to departments (optional), AI (report-to-findings extraction, evidence review, repeat detection, committee summary), MCP tools. Layer 2 (feature flag **off by default**): audit universe, risk-based plan, engagements, programmes and working papers (Audit-Office-only), reporting, QAIP KPIs (publish to KPI module). Integrations: Risk links, Document Registry criteria links, Teams for engagement teams.

**Prompt**
```
Phase 16 — Audit Management.
Follow CLAUDE.md strictly (rules §2, working method §17). Read CLAUDE.md, docs/BUILD_PLAN.md "Phase 16", docs/SPEC.md §10 (all, especially the operating model and decisions in §10.8).
Write docs/design/audit.md first: Layer 1 model and state machine, Audit-Office-only write model, read-only department scope (section vs department-and-below setting), follow-up scheduling, import format, Layer 2 behind a feature flag, integrations, AI, MCP.
Implement with tests: department users can read only their own unit's findings and cannot call any write endpoint; finding closes when all recommendations are Completed/Risk Accepted/Cancelled; late status by due date; Layer 2 APIs unavailable while the flag is off; working papers invisible to non-audit users.
Finish with the phase summary.
```

**Acceptance checks**
- [ ] Audit Office imports a report's findings (Excel and AI extraction), records follow-ups, closes findings.
- [ ] A department manager sees only their unit's findings, read-only, with Audit Office comments.
- [ ] Turning on Layer 2 exposes the plan / engagement screens; turning it off hides them.

---

## Phase 17 — Dashboards and reporting (cross-system)

**SPEC:** §0.4 (all levels), each module's Screens section.

**Scope** — complete **My Work** with cards from all systems; review every system dashboard for consistency; **Executive dashboard** (strategy performance, portfolio, risk, governance & compliance, organisation health, **department comparison scorecard**, drill-down, AI executive brief); dashboard builder (widgets, role default layouts, pin / save filters) and report builder (object, columns, filters, grouping, export, schedule); scheduled email of dashboards / reports; cached aggregates refreshed by jobs (< 3 s executive load); period selectors and comparisons.

**Prompt**
```
Phase 17 — Dashboards and reporting (cross-system).
Follow CLAUDE.md strictly (rules §2, working method §17). Read CLAUDE.md, docs/BUILD_PLAN.md "Phase 17", docs/SPEC.md §0.4 and the Screens sections of every module.
Plan first: reporting read-model (aggregates per module via Contracts, refresh jobs), widget catalogue, executive dashboard layout and drill-downs, department scorecard definition, dashboard/report builder data model and security (users can only build on data they may see), scheduled delivery, AI executive brief inputs.
Implement with tests: every widget respects access scope; executive dashboard loads under 3 s on seeded volume (500 projects, 1,000 KPIs, 1,000 risks); scheduled report email delivered in both languages.
Finish with the phase summary.
```

**Acceptance checks**
- [ ] A Director General user sees the executive dashboard and drills from a red department into its records.
- [ ] Two users with different scopes see different numbers on the same dashboard.
- [ ] Admin builds a new dashboard for a role without code.

---

## Phase 18 — Installer, setup wizard, operations, hardening, documentation, release

**SPEC:** §0.5 (installation & operations), §0.0 (product model), all NFRs.

**Scope** — production Docker images and compose file, non-Docker install guide; **first-run setup wizard** (organisation profile, branding, languages, admin account, email server, authentication provider, AI provider (skippable), org-structure import, modules to enable); upgrade procedure with migrations and pre-upgrade backup; backup / restore scripts (DB + files + Data Protection keys); admin health page (DB, jobs, email, AI, storage, disk); seed packs (neutral defaults; optional ADoF demo pack); security hardening review against OWASP ASVS L2 with a written checklist and fixes; performance test (k6 or NBomber) against the NFRs; accessibility audit; full E2E regression in AR and EN; documentation: administrator guide, installation guide, user guides per system in AR and EN, MCP connection guide, release notes; version 1.0 tag.

**Prompt**
```
Phase 18 — Installer, setup wizard, operations, hardening, documentation, release.
Follow CLAUDE.md strictly (rules §2, working method §17). Read CLAUDE.md, docs/BUILD_PLAN.md "Phase 18", docs/SPEC.md §0.0 and §0.5 (installation & operations) and all non-functional requirements.
Plan first: production packaging, setup wizard steps, upgrade/backup/restore design, health page, seed packs, the ASVS L2 checklist you will verify, performance and accessibility test approach, documentation set and where it lives.
Implement, then run the full hardening review and fix findings, run performance and accessibility tests, run the full E2E suite in Arabic and English, and produce the documentation. Finish with a release summary listing any residual risks.
```

**Acceptance checks**
- [ ] Fresh Linux server: one command installs; setup wizard completes; first admin logs in.
- [ ] Upgrade from the previous build with data preserved; restore from backup verified.
- [ ] ASVS L2 checklist completed with no open high issues; performance NFRs met; WCAG 2.1 AA audit passed.
- [ ] Admin, installation and user guides delivered in Arabic and English.

---

## Progress checklist

| Phase | Name | Status | Accepted on |
|---|---|---|---|
| 0 | Foundations | ☑ built — awaiting owner acceptance | |
| 1 | Identity, Organisation, Employees | ☐ | |
| 2 | Access control & Settings | ☐ | |
| 3 | Audit, Files, Notifications, Jobs | ☐ | |
| 4 | Approval engine & My Work | ☐ | |
| 5 | AI platform & MCP | ☐ | |
| 6 | Links & Task Management | ☐ | |
| 7 | Teams Management | ☐ | |
| 8 | Portfolio A — Requests | ☐ | |
| 9 | Portfolio B — Lifecycle & change | ☐ | |
| 10 | Portfolio C — Agile, standards, Gantt | ☐ | |
| 11 | Wasel vendor portal | ☐ | |
| 12 | Performance Management | ☐ | |
| 13 | Risk Management | ☐ | |
| 14 | Requests, Document Registry, IP | ☐ | |
| 15 | CAP & Benchmarking | ☐ | |
| 16 | Audit Management | ☐ | |
| 17 | Dashboards & reporting | ☐ | |
| 18 | Installer, hardening, docs, release | ☐ | |
