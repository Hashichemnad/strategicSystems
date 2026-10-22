# Integrated Strategy System — Requirements Specification v1.0

Product: A standalone, integrated strategy management suite, packaged as a product to be deployed at multiple government / enterprise organisations.
Reference implementation: Ajman Department of Finance (ADoF) Salesforce systems — their workflows are the baseline, but nothing ADoF-specific may be hard-coded.
Status: **Requirements complete (v1.0, 22 Oct 2026).** This is the source of truth for the build. Read together with `CLAUDE.md` (how to build, repository root) and `docs/BUILD_PLAN.md` (build order and phase prompts).

---

## 0. Global decisions

| Topic | Decision | Status |
|---|---|---|
| Platform | Standalone web application (not Salesforce) | Agreed |
| Product model | Re-usable product deployed for multiple organisations; every org-specific value is configuration, not code | Agreed |
| Deployment / tenancy | Sold per organisation; **single-tenant, self-hosted** on the customer's own servers (on-prem or their private cloud). Must also run in restricted / offline networks | Agreed |
| Licensing | None. One-time sale and setup; no licence keys, no subscription checks, no expiry. Admin can enable/disable modules from Settings | Agreed |
| Dates | Gregorian by default; Hijri display is an optional Settings toggle (off by default) | Agreed |
| AI | AI assistance across all systems + MCP server exposing the whole suite to Claude or any MCP client | Agreed — §0.5 |
| Stack | **.NET 10 (LTS) + ASP.NET Core** back end, **React + TypeScript** front end, **PostgreSQL** database — chosen on a weighted rating (security, performance, maintainability, long-term support). Details: §0.6 and `CLAUDE.md` | Agreed |
| Behaviour | Salesforce systems are the **functional reference, not a blueprint**: keep the business process, but apply industry standards and best practice wherever the current implementation is weaker (data types, calculations, workflow design, UX). Where the spec deviates from Salesforce, it says so | Agreed |
| Language | Fully bilingual Arabic / English across all systems (UI, emails, reports, PDFs), full RTL; language switchable per user in one click; more languages addable from settings | Agreed |
| Design | Modern, best-in-class UI/UX — explicitly *not* Salesforce-looking | Agreed |
| Single workspace | One application and one login for everyone. No separate portals (the Salesforce Risk Portal / KPI portal are replaced). Each user sees only the systems, menus, records and actions their roles allow (§0.2) | Agreed |
| External users | One exception by design: **vendors** use the **Wasel** vendor portal (§1.18) — same application and database, but a separate external user type, entry URL and restricted, vendor-branded experience | Agreed |
| Authentication | Local user table always; SSO (SAML 2.0 / OpenID Connect, e.g. Azure AD / Entra ID, ADFS), LDAP / Active Directory and UAE Pass switchable from Settings | Agreed |
| Data migration | None. Clean install + import tools (Excel/CSV) for master data | Agreed |
| Project methodologies | Waterfall (current), **Agile (new)**, and Hybrid | Agreed — design in §1.13 |
| KPI catalog | Provided by the Performance Management system (§2) | Agreed |

### 0.0 Suite map (from client's draft diagram)

```
Employees ──► Departments ──► (every system below is owned / filtered by department)

Core (shared):   Employees · Departments · Teams (DOF Teams — §4) · Tasks (§11)

Systems:
  1. Portfolio Management   ── specified (§1)
  2. Performance Management ── KPI catalog, results & approval    ── specified (§2)
  3. Risk Management        ── enterprise risk register, controls, treatment ── specified (§3)
  4. Audit Management       ── findings follow-up by Audit Office (+ optional full audit lifecycle) ── specified (§10)
  5. Benchmark Request      ── benchmarking studies, best practices ── specified (§8)
  6. Intellectual Property  ── IP registry & MOET registration   ── specified (§7)
  7. Document Registry      ── controlled corporate documents      ── specified (§6)
  (Systems 5–7 are built on the shared Request Framework, §5)
  8. Corrective Action Plan ── remarks, root cause, corrective actions ── specified (§9)
```

**Cross-system links (known so far)**
| From | To | Link |
|---|---|---|
| Portfolio Management → Project | Teams | Project team is a Team record (§4); project closure approval opens the team evaluation |
| Any system record | Teams | Any system can request / link a team for its records (audit, corrective action, benchmark…) (§4.4 F) |
| Any system record | Tasks | Any system can create tasks (single or multi-assign) linked to its records; standalone tasks too (§11) |
| Portfolio Management → Project KPI | Performance Management | "Existing KPI" links to a corporate KPI; project KPI results can feed the KPI |
| Portfolio Management → Project Risk | Risk Management | Project risks stay owned by PMS and are **not** copied into the corporate risk register. Risk Management shows them in a separate **"Project Risks"** tab (live, read-only, filtered by the user's access; each row opens the risk in its project). Project risks are included in Risk Management dashboards / heat-maps as a separate, filterable category |
| Document Registry | Intellectual Property | When a document is **published**, if its document type is flagged in Settings as "Creates IP register entry", an IP record is created automatically and linked to the document. Other types never create IP records |

Integration rule for all systems: links go through each system's internal service/API, never by one system writing into another system's tables. Every linked record shows a live badge of the other system's record (e.g. a project risk shows its corporate risk status). Disabling a system in Settings hides its links without breaking the others.

### 0.1 Shared core modules (centralised, used by every system)
Built once and consumed by PMS and every later system. No system keeps its own copy.

**Organisation / Department module**
- Unlimited-depth hierarchy (Organisation → Sector → Department → Section → Unit…); level names configurable per installation.
- Each unit: code, name (AR/EN), parent, head (manager) employee, deputy, cost centre, active flag, effective dates.
- Approver resolution uses this hierarchy ("Section Manager of the requester", "Department Director of the project's department", "head of unit X").
- Org chart view; history kept when the structure changes (re-orgs must not break historical records).

**Employee / User module**
- One record per person: employee no., name AR/EN, email, mobile, job title AR/EN, grade, unit, direct manager, photo, status (active / on leave / left), join date.
- Login account linked to employee (local password or external identity: SSO / LDAP / UAE Pass ID).
- Delegation: an employee can delegate approvals to another for a date range (leave cover) — every approval logs "approved by X on behalf of Y".
- Import / sync: Excel/CSV import; optional sync from AD / HR system.
- Roles & permissions assigned here; one user can hold several roles.
- **Employee performance (future section):** individual employee KPIs / objectives and an employee performance dashboard will live here, separate from the organisational KPI system — to be specified.

**Team / Committee module**
- Fully specified as Module 4 (§4): teams, committees, project teams and any other approved group; members and roles; meetings, recommendations, tasks; evaluation of teams and members; rewards. Linkable to records in every system.

### 0.2 Platform services (shared engine)
- **Access management (per system, three layers)** — all configurable from Settings, with a permission matrix screen per system:
  1. **System access:** which users / roles can open each system at all (e.g. someone may use Portfolio Management but not Audit Management).
  2. **System roles:** each system defines its own roles and permissions (e.g. PMS: PM Office Admin, Portfolio Viewer, Requester; Audit: Auditor, Auditee…). A user can hold different roles in different systems. Permissions are granular per object and action (view / create / edit / delete / submit / approve / export).
  3. **Record roles:** a person's role *on a specific record* (e.g. Project Manager, Project Owner, Team Member on one project), each with its own permission set. Record roles grant access to that record only.
  - **Data scope** per role: own records / my team / my unit / my unit and below / all.
  - Effective access = system access AND (system-role permission within scope OR record-role permission). Checked on every page, API call and MCP call.
  - Access review report: who can see / do what in each system; exportable for audits.
  - **System ownership:** each system has an *owning unit* setting (e.g. Risk Management → Internal Audit Office). Approval steps can target "owning unit of this system", so moving a system to another department (e.g. Risk Management → Strategy Office) is one settings change plus role reassignment.
  - **Bulk role transfer:** move a role from one set of users / unit to another in one action, including re-routing their **pending approvals and open tasks**, with an audit entry.
- **Configurable workflow & approval engine** — one **global Approval Settings centre** lists every approval process of every system (request approvals, stage gates, evaluations, document / IP / benchmark gates, closures…) in one place; admins add, remove, reorder or change steps at any time without code. Approval chains defined in Settings, not code: ordered steps, approver type (role, org-hierarchy relation, specific person, team role), entry conditions per step (e.g. budget > threshold), actions Approve / Reject / Return for Correction, mandatory comments on reject, SLA per step with escalation reminders, delegation, full approval history, hooks on approve / reject. ADoF chains ship as the default configuration.
- **Configurable lists (picklists)** — every dropdown (strategic objectives, risk categories, stakeholder classifications…) managed in Settings, AR/EN labels, sort order, active flag.
- **Configurable rules & thresholds** — e.g. Project/Initiative budget threshold, numbering formats, mandatory charter minimums.
- **Localisation** — translation management screen (edit any label AR/EN), RTL / LTR layouts, Hijri/Gregorian date display option, Arabic/Latin numerals option, currency format.
- **Branding** — organisation name, logos, colours, email header/footer, configurable per installation.
- **Audit trail (all systems, all data)** — every create / update / delete on every record in every system and in the core modules is logged automatically (no per-field opt-in needed):
  - who (user, and "on behalf of" for delegation), when, IP / device, **channel** (UI, API, MCP, AI-assisted, scheduled job, import), record, field, **old value → new value**, reason / comment where given.
  - Also logged: logins / logouts / failed logins, approvals and workflow transitions, file uploads / downloads / deletions, exports, permission and role changes, settings changes (including formula and framework changes), AI actions.
  - **History tab on every record** (timeline of changes, filterable by field / user) + a **global Audit Log screen** for administrators and auditors: search across all systems by user, system, object, record, date range, action type; export to Excel / PDF.
  - Append-only and tamper-evident (hash-chained entries); nobody, including administrators, can edit or delete audit entries. Retention period configurable (default: keep forever).
  - "View record as of date" for any record.
- **Notifications** — bilingual email templates (editable in Settings) + in-app notification centre; links open the record; email queue with retry; delivered < 5 min.
- **Scheduler** — cron jobs (monthly reminders, SLA escalation, digest emails).
- **Documents** — typed attachments per record, version history, size / type limits, virus-scan hook.
- **Auto-numbering** — format per object configurable (e.g. `PROJ-{UNIT}-{YYYY}-{NNN}`).
- **"Current Status" engine** — each record shows "next action required + who" (my-tasks inbox aggregates these).
- **Reporting** — saved list views with filters, dashboards, Excel / PDF export (bilingual).
- **Settings centre** — one admin area for all of the above, including authentication providers.

### 0.3 UX principles (apply to every system)
- Clean, modern, card-based design; consistent design system; light and dark mode.
- **My Work home page** per user: pending approvals, tasks due, overdue items, my projects, notifications.
- Guided actions instead of hidden buttons: each record shows its stage stepper, the current status, and only the actions valid for this user right now.
- Wizards for multi-step forms (request form, take ownership, closure) with save-as-draft and inline validation.
- Inline editing in tables; drag-and-drop where natural (Kanban, Gantt, backlog ordering).
- Global search; keyboard-friendly; responsive down to mobile browsers.
- Accessibility (WCAG 2.1 AA) and identical quality in RTL and LTR.

### 0.4 Dashboards & reporting (all systems)
Every dashboard respects the viewer's access (§0.2): the same dashboard shows different data to different people. All are bilingual, RTL-ready, drill-down to records, filterable, exportable (PDF / Excel / image) and can be emailed on a schedule.

**Four levels**
1. **My Work (every user — home page):** my pending approvals across all systems; my tasks (assigned / created / overdue); my KPI results to submit; my risks and actions; my CAP remarks and actions; my projects and milestones due; my teams and pending evaluations; my requests (documents, IP, benchmarks) and their status; notifications. Only cards for systems the user can access are shown.
2. **Role dashboards per system (operational):** e.g. PM's project dashboard, PMO portfolio dashboard, KPI owner's pending results, Risk Office heat-maps, Audit Office follow-up calendar, HR team-evaluation progress, CAP validation queue. Specified in each module's Screens section.
3. **System dashboards (management view per system):** the full picture of one system for its owners and leadership — e.g. portfolio health, KPI scorecard, risk profile, audit closure, CAP closure, document library status, IP register, benchmarking plan, team evaluations, task performance.
4. **Executive dashboard (high management — cross-system, overall):** one page for the Director General / leadership:
   - **Strategy performance:** organisation and objective scores from KPIs (capped performance), trend vs last period, strategy map coloured by performance.
   - **Portfolio:** projects by stage and health (RAG), budget vs actual, delayed projects, upcoming milestones.
   - **Risk:** Top-10 institutional risks, critical risks, risks outside appetite, movement since last quarter.
   - **Governance & compliance:** open audit findings by priority and age, overdue recommendations; CAP remarks open / overdue by source.
   - **Organisation health:** departments' on-time rates (KPI submissions, CAP actions, tasks), active committees, documents expiring.
   - **Department comparison:** one row per department across all systems (KPI score, project health, risks, audit findings, CAP closure, overdue tasks) — a cross-system department scorecard.
   - Click-through from any figure to the system dashboard, then to records.
   - **AI executive brief** (if AI enabled): a bilingual narrative of the period — what improved, what needs attention, recommended decisions — with links to the evidence.

**Dashboard builder (Settings)**
- Widgets (KPI tiles, charts, tables, heat-maps, maps of the strategy, lists) built on a shared reporting layer per system; admins can arrange the default dashboards per role and publish new ones without code; users can pin favourites and save filters.
- Report builder for list reports (choose object, columns, filters, grouping) with export and scheduling.
- Period selectors (month / quarter / year / custom) and comparison with previous period consistently across all dashboards.
- Performance: dashboards use cached aggregates refreshed by the scheduler (and on relevant changes), so the executive dashboard loads in under 3 seconds.

### 0.5 AI & integration layer (applies to every system)

**Installation & operations (self-hosted product)**
- Guided installer + first-run setup wizard: organisation profile, branding, languages, admin account, email server, authentication provider, AI provider (optional, can be skipped), org structure import, modules to enable.
- Docker-based deployment option plus plain server install; documented hardware/software requirements.
- Versioned database migrations and an in-place upgrade path between releases; backup / restore tooling.
- Health page for admins (queue, scheduler, email, AI provider, storage).

**REST API**
- Every module exposes a documented, versioned REST API (OpenAPI spec). API tokens per user or per integration, same permission rules as the UI. The MCP server and any future integrations sit on top of this.

**MCP server (whole suite)**
- Built-in Model Context Protocol server so Claude (or any MCP-capable client) can connect to the installation.
- Transport: Streamable HTTP; authentication via OAuth 2.1 (per MCP spec) or personal access tokens issued from the user's profile.
- **Acts as the signed-in user:** every MCP call obeys that user's roles and record access — an AI client can never see or do more than the person could in the UI.
- Each system registers its own tools and resources (plug-in pattern), so new systems are exposed automatically. PMS examples: search / get requests and projects, portfolio summary, my pending approvals, project status report, create draft request, update milestone draft, log risk / issue, add meeting minutes.
- Read tools on by default; write tools opt-in. Approve / reject actions **never** exposed by default (admin may enable, still requires the approver's own token).
- Admin settings: enable/disable MCP, enable/disable individual tools, rate limits, token expiry.
- Every MCP call recorded in the audit log (user, client, tool, parameters, result).

**AI provider configuration (Settings → AI)**
- Provider-agnostic, no hard-coded default — the admin chooses and configures the provider in Settings: Anthropic Claude, OpenAI, Azure OpenAI, or a self-hosted model via an OpenAI-compatible endpoint (e.g. vLLM / Ollama) for customers who cannot send data outside their network.
- Per provider: API key (stored encrypted), endpoint URL, model selection (list fetched from the provider, or typed), temperature / max tokens, timeout; **Test connection** button.
- Optional: different model per feature (e.g. a stronger model for document extraction, a faster one for translation).
- Implemented behind one internal AI interface (adapter per provider) so new providers can be added without touching the features.
- Master switch to disable AI completely; per-feature switches; per-role access.
- Data controls: choose which modules / fields may be sent to the AI; optional masking of personal data.
- Usage log and monthly usage limits.

**AI principles (all features)**
- Human in the loop: AI drafts and suggests, people decide. AI never approves, rejects or submits on its own.
- AI-generated content is clearly marked until a user accepts or edits it.
- Works in Arabic and English (input documents in either language; output in the user's language, with one-click translation of free-text fields AR ⇄ EN).
- Every AI action is logged (who, feature, input reference, output).

**Shared AI features**
- **Ask the system:** natural-language questions over data the user is allowed to see ("which projects in Finance are delayed?", "summarise risks for Q3").
- **Document-to-form extraction:** upload a document, AI fills a form; reusable by every module (first use: Program Request, §1.15).
- **Approver brief:** one-paragraph summary of any record awaiting approval, with what changed since the last submission.
- **Auto-translation** of AR/EN text fields.
- **Similar records:** find similar or duplicate items across the system.

### 0.6 Technology stack (decided)
| Layer | Choice |
|---|---|
| Runtime / back end | .NET 10 (LTS, supported to Nov 2028), ASP.NET Core Web API, C# — modular monolith (one deployable, one module per system) |
| Front end | React + TypeScript (Vite), Tailwind CSS with logical (RTL-safe) properties, accessible component primitives (Radix-based), TanStack Query, react-hook-form + zod, i18next |
| Database | PostgreSQL 16+ (EF Core with Npgsql); full-text search built in; optional Meilisearch for Arabic search inside documents |
| Background work | Quartz.NET (persistent store in PostgreSQL) + transactional outbox for reliable jobs and emails |
| Real-time | SignalR (in-app notifications, live dashboards) |
| Auth | ASP.NET Core Identity (local users, TOTP / email OTP MFA) + OpenID Connect (Entra ID / ADFS / UAE Pass) + SAML 2.0 (Sustainsys.Saml2) + LDAP / Active Directory |
| AI | Microsoft.Extensions.AI `IChatClient` abstraction; providers: Anthropic (official C# SDK), OpenAI / Azure OpenAI, OpenAI-compatible self-hosted (Ollama / vLLM) |
| MCP | Official Model Context Protocol C# SDK (`ModelContextProtocol.AspNetCore`), Streamable HTTP |
| Documents | PDF via headless Chromium HTML-to-PDF (correct Arabic shaping); Excel via ClosedXML |
| Deployment | Docker / Docker Compose for customer installs (Linux); documented non-Docker install as an alternative |
| Licensing rule | Only free / OSI-licensed dependencies (MIT, Apache-2.0, BSD, MPL…) so the product can be sold without third-party licence fees — see `CLAUDE.md` |

---

## 1. Module: Portfolio Management (PMS)

Source documents: PMS BRD V1.1 (Mar 2026), PMS Technical Specification V1.1, Project Request User Guide V1.0, Project Process User Guide V1.0, PMS Process Flow diagram, object relationship diagram.

### 1.1 Purpose
Manage the full lifecycle of organisational projects and initiatives: request → screening & prioritisation → multi-level approval → auto-creation of project → charter → planning → monthly execution tracking → closure with lessons learned → impact assessment. Plus change control on live projects.

### 1.2 Actors
| Actor | Responsibilities |
|---|---|
| Employee / Request Creator | Creates request, uploads cost estimation or project scope, submits, corrects when returned |
| Project Manager (owner) | Takes ownership; charter, planning, milestones, monthly updates, closure, impact; raises change requests |
| Section Manager | Approver: request, charter, planning, closure (where applicable) |
| Department Director (Project Owner) | Approver: request, charter, planning, closure; first approver for monthly updates and change requests |
| PM Office (Strategy Office) | Approver at every stage; uploads Team Formation; maintains threshold amount |
| DG Office | Request approver where required (budget > threshold) |
| Budget Department | Final request approver where budget is required; sets Approved Budget |
| Finance Service Section / GM Office | Change Request approvers where required |
| Administration Department | Uploads quotations (request stage) and LPO + contract/supplier details (charter stage) |
| Leadership | Views dashboards |
| System Administrator | Users, roles, config, scheduled jobs |

### 1.3 Two lifecycles (by classification)
- **Project:** Request → Charter (Initiation) → Planning → Execution → Closure → Impact → Completed
- **Initiative:** Request → Planning → Execution → Completed *(lighter workflow — default steps in Appendix A, Q-P2)*

Project statuses: Draft | Active | On Hold | Cancelled | Completed.

### 1.4 Data model

#### A. Program Request (`PMS_Project_Request__c`) — ID format `PR-XXXX`
**Core (required):** Program Name; Program Description (5000); Program Justifications (5000); Strategic Objective (picklist — 2026 objectives); Source of Suggestion (Idea | Benchmarking | Reports | Global Practices | Questionnaire | Meeting Outputs | Leadership Guidance | Others + free text); Program Type (Digital | Non-Digital); Requires Budget? ; Estimated Budget; Year 1–4 Costs; Expected Start / End Date; Department (requesting).
**Classification & nature:** Program Nature (New | Continuous of Previous) + Previous Program Name (required if Continuous); Program Scope of Work; Section to Implement.
**Screening questions (Yes/No):** work core to unit; done once without repetition; specific time period; unique results/outputs; requires at least [Threshold] AED; operational costs. → **Classification = Project | Initiative** (computed). Threshold Amount configurable by PM Office (currently AED 50,000).
**Priority assessment (1–5 each):** link to Ajman Vision 2030 & department; expected impact & added value; complexity level; driving radical change. → **Evaluation Score (%)** and **Priority band** (computed).
**Delivery options:** Main Option and Alternative Option (Contracting with Company | Internal Implementation | Outsourcing Employee | Public-Private Partnership | Other + text), each with advantages / disadvantages.
**Cost sharing:** others bear costs?; others cover implementation?; shared cost amounts; partner entity name; partner cost-sharing %.
**Operating costs after implementation:** annual operation / licence / maintenance / other cost; expected-from date.
**Financial return:** financial return expected?; expected return value + year (4-digit); operational cost reduction?; reduction value + year (4-digit).
**Link with internal practices:** Operation, Service, Project, Initiative (long text).
**Procurement:** cost proposals requested?; Transfer to Administrative (Waiting For Quotations | Quotations Uploaded); Cost Estimation Uploaded flag.
**Workflow:** Status (Draft → Waiting Approval → Manager Approval → PMO Approval → DGO Approval → Budget Approval → Approved | Rejected | Cancelled); Approval Status (last result: Approved | Rejected | Rejected for Correction); Approved Budget (set at Budget step); resolved Section Manager / Department Manager users; Last Action On / Status; Cancel Reason; Current Status (computed); Total Expected Time Frame (computed).

**Validation rules:**
- Year 1 + 2 + 3 + 4 costs = Estimated Budget exactly.
- Expected Start Date within current year or next three years.
- Expected End Date ≥ Start Date and ≤ 3 years from current year.
- Return / reduction years are 4-digit numbers.
- Either Operation or Service must be filled.
- Previous Program Name required when nature = Continuous.
- "Other" option / source requires free text.
- Currency: 2 decimal places.

#### B. Project / Initiative (`PMS_Project__c`) — ID format `PROJ-SPMO-YYYY-NNN`
Record types: Project, Initiative. Auto-created on final request approval (copies request data).
**Identity:** Name; Request (link); Classification; Stage; Status; Project Manager; Ownership Taken; Department Concerned; Section to Implement; Project Team (link to Team module); Strategic Objective; Operational Themes (multi-select, incl. Innovative, Ajman Vision).
**Budget & cost:** Project Budget (from approved request); Contracted costs Year 1–4 + total; Actual costs Year 1–4 + total; Contracted Initial Cost; recurring annual operational / maintenance / licence-support / other costs + start dates.
**Charter resources:** Human, Financial, Technical, Contract, Other (long text).
**Gates:** Charter / Planning / Closure Mandatory Done flags; LPO Uploaded; Team Formation Uploaded.
**Roll-ups (computed from children):** planned start (MIN), planned end (MAX), actual start (MIN), actual end (MAX), Total Weight (SUM), Total High-Level Weight (SUM), Total Milestone Cost (SUM), No. of KPIs / Risks / Stakeholders (COUNT).
**Progress:** Performance % = Σ(Achieve) across milestones (Achieve is already weighted); Planned Progress to date (Till Weight); Actual approved progress (Total Achieve); Progress Status (On Track | Delayed).
**Approval:** Approval Status (phase-qualified, e.g. "Section Manager - Charter", "PM Office - Planning", "Waiting Monthly Approval", "Department Director - Execution", "Closure Approved").
**Closure:** Summary of Achievement; Main Deliverables.
**Impact:** Cost Performance; Community / Employee / Vendor Satisfaction; Previous Operational Cost.
**Monthly cycle:** Update Opened Date; Monthly Approved Date.
**Other:** Cancel Reason; Current Status (computed).

**Validation rules:** total milestone weight ≤ 100%; total high-level milestone weight ≤ 100%.

#### C. Milestone / Work Plan (`PMS_Work_Plan__c`) — child of Project; ID `<ProjectID>-1.1`
Stage Title; Header Phase (self-reference for phase → sub-milestone hierarchy); Is Header Task; High-Level Plan flag; Planned Start / End; Weight %; Achieve % (0 – Weight); Completion Rate (= Achieve / Weight × 100); Actual Start / End; Cost; Status (Not Started | Active | Deactivate | Cancelled | Completed); Progress Status (On Track | Delayed); Corrective Actions; Causality (delay reason); Evidence (+ attachment); Approved? (Waiting for Update | Waiting for Approval | Pending Approval | Approved | Rejected); Updated Achievement (pending) vs Max Achieved (highest approved).
**Deliverable fields** (shown when "Is this deliverable?"): Main Deliverable; Acceptance Criteria; Output met quality? (Yes/No); Is Innovative? + reason.
**Rules:** Achieve ≤ Weight; at 100% → Actual End Date + evidence attachment required; delayed (past planned end, < 100%) → causality + corrective actions required; header milestone Achieve auto-aggregates from children; achievement locked once approved.

#### D. Other children of Project
| Object | Key fields |
|---|---|
| Project KPI | KPI Nature (New \| Existing → link to corporate KPI catalog); Project Goal; KPI Name; Measuring Equation; Measuring Direction; Index Classification; Target; Year of Achieving Target; When Measured (During \| After project); Current Result; Performance; Goal Status (computed); Created Stage; Status |
| Project Risk | Risk (picklist / specific text); Probability (1–5); Impact (1–5); Risk Factor; Inherent Risk Score (computed); Existing Control Evaluation (1–10); Residual Risk Status (computed); Response Strategy (Accept \| Avoid \| Mitigate \| Transfer); Mitigation Actions; Implementation % + dates; RMS Status; Created Stage |
| Stakeholder | Name; Classification; Interest Level; Impact Level; Communication Method; Communication Frequency; Email; Phone; Status |
| Challenge / Constraint | Challenge; Impact; Achieved %; Recommendation; Report Date; Milestone (optional link) |
| Change Request | Change Type (Details \| Cancellation \| Postponement \| Ownership \| Merge); Change Areas; Reason; Impact of Non-Response; Proposed Project Name; Requested / Implemented Date; Status (Draft → Under Review → Approved \| Rejected \| Cancelled → Completed) |
| Contract / PO | Name; Number; Supplier; Issue Date; Status |
| Lesson Learned | Project Stage; Lesson; Type; Recommendation to Improve |
| Team Meeting (MOM) | Meeting Date/Time; Notes (rich text) |
| Planning Item | Category (In Scope \| Out of Scope \| Assumption \| Constraint \| Deliverable \| Human / Technical / Financial / Contract / Other Resource); Description; Created Stage; Status |

#### E. Shared objects used by PMS (owned by core modules / other systems)
- Department / Section → **Organisation module** (§0.1)
- Project Manager, approvers, team members → **Employee module** (§0.1)
- Project team (min. 3 members for charter), team formation, member evaluation → **Teams Management** (§4)
- "Existing KPI" link → **KPI Card in Performance Management** (§2)

### 1.5 Workflows

#### W1 — Program Request (pre-initiation)
1. Creator fills the request form → saved as Draft (`PR-XXXX`).
2. Screening: if all assessment answers are true and estimated budget > threshold → **Project**; otherwise → **Initiative**.
3. Cost file available?
   - **Yes:** creator uploads Cost Estimation File and fills multi-year budget details.
   - **No:** creator downloads Project Scope template, fills and uploads it → Administration Dept emailed → Admin uploads quotations → transferred back to creator → creator fills budget details.
4. Creator submits for approval (with comments).
5. Approval chain (each: Approve / Reject / Return for Correction):
   Section Manager → Department Director → PM Office → then by budget:
   - Budget not required → approved
   - Budget ≤ threshold → Budget Section
   - Budget > threshold → DG Office → Budget Section
6. Return for Correction → back to creator, resubmit after correction. Reject → Stop / Cancelled.
7. Final approval → Project/Initiative auto-created (Stage = Initiation for Projects), creator notified.
8. Cancel Program action (with reason) available on the request.

#### W2 — Charter (Initiation) — Projects only
1. Any employee from the same section can **Take Ownership** → fills Project Manager, scope, structural changes, resources.
2. PM adds ≥ 1 KPI, ≥ 1 Risk, ≥ 1 Stakeholder, ≥ 3 Team Members, high-level plan; uploads project scope / quotations. Submit is blocked until minimums are met.
3. Submit → Section Manager (where applicable) → Department Director → PM Office (Approve / Reject / Return for Correction).
4. On approval → Administration Dept emailed → uploads **LPO** + contract/supplier details.
5. PM Office emailed → forms the project team through **Teams Management** (§4): formation request → formation approval → system-generated or uploaded memo → team Active → **Stage = Planning**.

#### W3 — Planning
1. PM updates planning details (risks, stakeholders, planning items).
2. PM creates all milestones & phases (weights; total must be 100%; high-level plan validated separately). **If a vendor is attached (Wasel, §1.18)**, the vendor can propose the detailed plan (milestones, dates, weights, tasks / user stories); the PM reviews, edits and accepts it into the project plan before submitting.
3. Submit → Section Manager → Department Director → PM Office.
4. On approval → **Stage = Execution**; monthly reminders activated.

#### W4 — Execution (monthly cycle)
1. On the 1st of every month, the update window opens and the PM is emailed.
2. Milestone updates — **two modes, set per project** (default: PM only):
   - **PM only (current):** PM updates each milestone: actual start (first update), Achieve %, actual end + evidence at 100%, causality + corrective actions if delayed.
   - **Vendor then PM (Wasel, §1.18):** the vendor updates the milestones they are responsible for and submits to the PM; the PM reviews each update — accept, edit (logged), or return to the vendor with comments. Milestones not assigned to the vendor are still updated by the PM.
   PM may add MOM, challenges, KPI results.
3. PM submits the monthly update → approval *(default: Department Director → PM Office — Appendix A, Q-P1)*.
4. Approved achievement is locked; project Performance recalculates.
5. When total achievement = 100% → **Stage = Closure**.

#### W5 — Closure
1. PM fills closure form: Summary of Achievement, Main Deliverables, Lessons Learned rows (stage, lesson, type, recommendation).
2. Submit → (Section Manager where applicable) → Department Director → PM Office.
3. On approval → **Stage = Impact** *(default; Impact stage can be switched off per installation → Completed — Appendix A, Q-P3)*.

#### W6 — Impact
1. PM records cost performance, community / employee / vendor satisfaction and after-project KPI results.
2. PM Office approves → **Stage = Completed, Status = Completed**.

#### W7 — Change Request (any active stage)
1. PM / owner creates a change request (details, cancellation, postponement, ownership, merge).
2. Approval: Department Director → PM Office → Finance Service Section (where required) → GM Office (where required).
3. On approval → Status = Approved → PM applies changes and clicks **Changes Implemented** → Completed.

#### W8 — Cancel / On Hold
Requests and projects can be cancelled or put on hold with a mandatory reason.

### 1.6 "Current Status" messages (next action + who)
**Request:** Waiting user to upload cost estimation file (Creator) · Waiting Administration Dept to upload quotations (Admin) · Waiting user to upload project scope (Creator) · Waiting user to submit for approval (Creator) · Waiting user to submit after corrections (Creator) · Waiting Manager approval (Section Manager / Dept Director) · Waiting PM Office approval · Waiting DG Office approval · Waiting Budget Dept approval · Approved — project created · Rejected & cancelled.
**Project:** Waiting user to take ownership (any employee in section) · Fill at least 1 KPI, 1 Risk, 1 Stakeholder, 3 Team Members (PM) · Waiting user to submit for approval (PM) · Waiting Admin Dept to upload LPO · Waiting PM Office to upload team formation · Waiting approval for Section Manager / Department Director / PM Office – <Stage> · Waiting user to update planning details · Waiting user to add all milestones (total weight 100%) · Waiting monthly update / approval · Waiting user to submit project closure · Waiting impact entry / approval.

### 1.7 Notifications (bilingual EN/AR side-by-side)
- Each approval step → approver (project details, stage, submitter, Approve / Reject link).
- Approval outcome (approved / rejected / returned) → submitter.
- Project scope submitted → Administration Dept (upload quotations).
- Quotations uploaded → creator.
- Request final approval → creator (project created).
- Charter approved → Administration Dept (upload LPO).
- LPO uploaded → PM Office (upload team formation).
- Stage transitions → PM.
- Monthly reminder (1st of month) → PM of every project in Execution, and to the vendor users of projects in *Vendor then PM* mode.
- Vendor submission received / returned / accepted → PM / vendor (Wasel).
- Change request submitted / decided → relevant parties.

### 1.8 Screens & UI
- **Program Requests:** list view (filters by status, department, classification, priority); record page with Details, Approvals (status + history + Approve/Reject/Return buttons for the current approver), Attachments, History; actions: Cost Estimation File, Upload Quotations, Submit for Approval, Cancel Program.
- **Projects:** list view; record page with stage path bar (Initiation › Planning › Execution › Closure › Impact), Current Status banner, Details, Related tabs (KPIs, Risks, Stakeholders, Team, Milestones, Contracts, Change Requests, Challenges, Lessons, Meetings, Planning Items), Gantt chart; actions: Take Ownership, Edit Details, Update Charter, Create Milestone, Update Planning Details, Submit for Approval, Upload LPO, Team Formation, Project Closure, Minutes of Meeting. Simplified layout for Initiatives.
- **Milestone page:** information, achievement, approval history; actions: Update Milestone, Edit, Cancel; printable view.
- **Gantt / quarter plan view:** per project (milestones by quarter, planned vs actual, achievement %) and **portfolio-wide** across all projects. Read-only; edits only through Update Milestone.
- **Closure form:** summary, deliverables, lessons learned with Add Row.
- KPI / Risk / Stakeholder / Change Request pages with their quick actions (Update Actual Result, Update Status, Changes Implemented).

### 1.9 Access model — Portfolio Management
Uses the three-layer access model (§0.2). Defaults below are the shipped configuration; every cell is editable in Settings.

**System roles (PMS)**
| Role | Scope | Typical permissions |
|---|---|---|
| PMS Administrator / PM Office | All | Everything incl. settings, thresholds, templates, reassign PM, cancel/hold, reports |
| Portfolio Viewer (Leadership) | All | View all requests, projects, dashboards, reports; export |
| Department Viewer | My unit and below | View requests and projects of their department |
| Requester | Own | Create and edit own requests, submit, cancel own drafts |
| Administration Dept (Procurement) | Assigned | Upload quotations and LPO / contracts when asked |
| Approver | Assigned | See and act on items routed to them (granted automatically by the approval engine for that step) |
| Vendor Administrator (PM Office) | All | Manage vendor register, invite / deactivate vendor users, assign vendors to projects |

**Record roles (per project)**
| Permission | Project Owner | Project Manager | Team Member | Stakeholder (optional) |
|---|---|---|---|---|
| View project, plan, dashboards | ✔ | ✔ | ✔ | Summary only |
| Edit project details, charter, planning items | — | ✔ | — | — |
| Create / edit milestones (waterfall) or epics / sprints (agile) | — | ✔ | — | — |
| Create tasks / user stories and assign them | — | ✔ | Configurable | — |
| Update own assigned tasks (status, %, comments, files) | — | ✔ | ✔ | — |
| Submit monthly / sprint update, closure, impact | — | ✔ | — | — |
| Log risks, issues, challenges | ✔ | ✔ | ✔ (draft for PM to accept) | — |
| Add meeting minutes | ✔ | ✔ | ✔ | — |
| Raise change request | ✔ | ✔ | — | — |
| Approve stage gates | ✔ (as Department Director / Owner step) | — | — | — |
| View costs / budget | ✔ | ✔ | Configurable | — |
| Manage team members | — | ✔ (PM Office approves formation) | — | — |

- **Project Owner** = sponsor, by default the Department Director of the owning department (can be changed by PM Office / change request).
- **Project Manager** = the employee who took ownership (changeable only by approved Ownership change request).
- **Team Members** = members of the project's Team record; added/removed through the Team module, so access follows team membership automatically.
- Users outside these roles see a project only if their system role and data scope allow it.
- Approval history and comments visible to the PM, Owner, approvers and PM Office.
- **Vendor users** (external): access only to projects they are assigned to, only through Wasel, only to vendor-visible data — see §1.18.

### 1.10 Non-functional
Record pages < 3 s; Gantt < 5 s for 30 milestones; 500 concurrent active projects; Chrome / Edge latest; mobile browser access; full audit trail on approvals and record changes; notifications sent within 5 minutes; currency to 2 decimals.

### 1.11 Out of scope (carried over)
ERP / Oracle / IBM budget integration (future JS-0036); automated PO generation; public / citizen portal; vendor invoicing and payments; native mobile app. *(Vendor portal is now **in scope** — Wasel, §1.18. Gantt export to PDF / image is now in scope as a normal export. No data migration, as decided.)*

### 1.12 Enhancements from project / portfolio management standards
Sources: PMI *Standard for Portfolio Management* & PMBOK 7, ISO 21502 (project management), ISO 21504 (portfolio management), PRINCE2 / AXELOS P3O, PMI Agile Practice Guide. Each item below is an addition to the Salesforce baseline; each can be switched on/off per installation.

**Portfolio level**
- **Portfolio & programme hierarchy:** Portfolio → Programme → Project / Initiative (a programme groups related projects; portfolios by strategic pillar, department or year).
- **Strategic alignment:** each project linked to strategic objectives (and later to the KPI catalog); alignment score feeds prioritisation.
- **Configurable prioritisation model:** the four ADoF criteria become the default of a configurable weighted scoring model (criteria, weights, 1–5 scales, priority bands editable in Settings).
- **Portfolio dashboard:** health heat-map, bubble chart (value vs complexity vs budget), budget vs actual by department, projects by stage, delayed projects, upcoming milestones, pipeline funnel (requests → approved → active → completed).
- **Portfolio review / periodic governance:** quarterly portfolio review records (decision per project: continue / re-scope / hold / cancel).
- **Capacity view:** employee allocation % across all projects and teams (over-allocation warning).

**Project level**
- **Project health (RAG)** on Schedule, Cost, Scope, Risk, plus overall — auto-calculated with manual override + justification.
- **Baselines:** plan baselined at planning approval; re-baselined only through an approved change request; variance (baseline vs current vs actual) on dates, cost, weights.
- **Earned value (optional, waterfall):** PV, EV, AC, SPI, CPI, EAC from milestone weights, costs and actuals.
- **RAID log:** existing Risks + new **Issues** (realised problems with owner, priority, due date, resolution), **Assumptions** & **Dependencies** (between projects / milestones), **Decisions** log.
- **Risk matrix:** 5×5 probability × impact heat-map; risk owner; review date.
- **RACI** on the project team (Responsible / Accountable / Consulted / Informed per deliverable or area).
- **Communication plan** derived from stakeholders (method, frequency, owner) with reminders.
- **Benefits realisation:** expected benefits defined at request, owner and measurement date; measured at Impact stage (extends existing impact fields).
- **Quality:** acceptance criteria and sign-off per deliverable (extends existing deliverable fields).
- **Procurement:** contracts / POs with payment milestones and supplier performance rating.
- **Monthly status report:** auto-generated bilingual project status report (PDF) from the approved monthly update.
- **Stage-gate checklists:** configurable checklist per gate (charter, planning, closure) in addition to the hard minimums.
- **Templates:** project templates (milestones, planning items, risks, checklist) to start a new project quickly.

### 1.13 Delivery methodologies — Waterfall, Agile, Hybrid
Methodology is chosen at charter (Take Ownership), suggested by the request type, and can change only through an approved change request. **Governance is identical for all methods** (request approval, charter approval, closure, impact, change control); only *planning and execution* differ.

| Area | Waterfall (current) | Agile (new) | Hybrid |
|---|---|---|---|
| Plan structure | Phases → milestones with weights (= 100%) | Releases → Epics → User Stories / Tasks | Waterfall phases; selected phases delivered in sprints |
| Planning approval approves | Milestone plan & weights | Product roadmap, release plan, epics with weights (= 100%), sprint length, Definition of Done | Both |
| Execution unit | Monthly milestone update | Sprint (1–4 weeks, configurable) | Both |
| Progress % | Σ approved milestone achievement | Σ (epic weight × % story points done) | Weighted mix |
| Progress reporting / approval | Monthly update → approval chain | Sprint review summary → approval chain (or monthly roll-up of sprints — configurable) | Monthly roll-up |
| Visuals | Gantt / quarter plan | Scrum / Kanban board, burndown, burnup, velocity, cumulative flow | Gantt + board |

**Agile objects:** Product Backlog; Release (name, target date, goal); Epic (title AR/EN, weight %, acceptance criteria, status); User Story (as-a / I-want / so-that, acceptance criteria, story points, priority / MoSCoW, assignee, status); Task (estimate, assignee, status); Sprint (goal, start / end, capacity, committed vs completed points); Sprint Review (demo outcome, accepted stories, stakeholder feedback); Sprint Retrospective (went well / to improve / actions → feed Lessons Learned); Impediments (→ Issues log).
**Agile boards:** configurable columns (To Do › In Progress › Review › Done), WIP limits, swimlanes by epic or assignee, drag-and-drop.
**Agile metrics:** velocity trend, sprint burndown, release burnup, cumulative flow, cycle time.
Agile and waterfall projects appear together in the same portfolio dashboards, using the common progress %, health RAG, budget and dates.

**Agile progress approval (decided):** configurable per installation, overridable per project — either (a) approval after every sprint, or (b) one monthly roll-up of all sprints in that month, following the same approval chain and rhythm as waterfall monthly updates.

### 1.15 AI features — Portfolio Management
**A. Smart request from a case study (headline feature)**
1. On "New Program Request" the user chooses **Fill manually** or **Start from a document**.
2. User uploads a case study / business case / proposal (PDF, Word, PowerPoint; Arabic or English; multiple files allowed).
3. AI extracts and pre-fills the request form: name, description, justifications, scope, strategic objective (matched to the configured list), program type, source, expected dates, budget and Year 1–4 split, delivery options with advantages / disadvantages, cost sharing, operating costs, financial return, link with internal practices, and suggested answers to the screening and priority questions.
4. The user reviews in the normal wizard: AI-filled fields are highlighted, each with a confidence level and a link to the source passage in the document. Missing fields are flagged. Nothing is saved until the user confirms.
5. AI produces a **Proposal Assessment** saved on the request (visible to the creator and all approvers):
   - Executive summary of the proposed project
   - **Gaps:** missing or weak information (e.g. no cost breakdown, no measurable benefits, unclear scope, no risks)
   - Strengths
   - Risks and concerns
   - Strategic alignment: which objectives it supports and how strongly
   - Suggested classification (Project / Initiative) and suggested priority scores, with reasoning — suggestions only
   - Similar or overlapping existing requests / projects (possible duplication)
   - Recommendations and best practices to strengthen the proposal
6. The assessment can be regenerated after edits; previous versions are kept.

**B. Other PMS AI assists**
- **Charter assistant:** suggests KPIs, risks (with probability / impact), stakeholders and planning items from the request and assessment; user picks which to add.
- **Plan generator:** drafts a milestone plan with weights (waterfall) or epics / user stories (agile) from the scope; user edits before saving.
- **Monthly / sprint update assistant:** drafts the progress narrative and corrective actions from the latest data; flags milestones at risk of delay.
- **Approver brief** at every approval step (shared feature).
- **Status report writer:** drafts the bilingual monthly status report.
- **Change impact analysis:** for a change request, summarises the effect on dates, cost, weights and dependent projects.
- **Lessons learned search:** at charter and planning, surfaces relevant lessons from past projects.
- **Portfolio insights:** narrative summary for leadership dashboards (top risks, delays, budget variance, recommendations).

### 1.16 Tasks under milestones (new)
Milestones (waterfall) can be broken down into **Tasks**; in agile the equivalent is User Story → Task (§1.13). Projects can also have **project-level tasks not tied to a milestone**. All of these use the shared **Task Management** module (§11) — same Task object, lifecycle, multi-assign, timesheets, comments and My Tasks list; the fields below are the project-specific use of it.

**Task fields:** title AR/EN; description; parent milestone (or user story); optional parent task (sub-tasks, one level); assignee (project team member; picking several people creates one task per person — §11.4); planned start / due date; actual start / end; priority (High / Medium / Low, as in §11.5); status per workflow template (**Full** by default for project tasks — §11.3); % complete; estimated / actual effort (hours); checklist items; dependencies (finish-to-start on other tasks); attachments; comments with @mentions; activity history.

**Behaviour**
- Created by the PM (and by team members if the project allows it); assigned to team members only.
- Assignee is notified; tasks appear in the assignee's **My Work** page and calendar; reminders before due date and when overdue.
- Views per milestone and per project: list, Kanban board (drag between statuses), Gantt (tasks nested under milestones), calendar, "my tasks".
- **Progress link (configurable per installation):**
  - *Manual* (default, matches current process): tasks are for execution tracking only; the PM still enters milestone Achieve % in the monthly update. The system shows the task-based % beside it as a hint.
  - *Task-driven:* milestone Achieve % is suggested from task completion (weighted by effort); the PM confirms it in the monthly update. Approvals are unchanged either way.
- A milestone cannot be submitted as 100% while it has open tasks (warning, PM may close or cancel them).
- Overdue tasks feed the milestone's delay indicator and the project health RAG.
- Blocked tasks can be converted into an Issue in one click.
- MCP / AI: "my tasks", "update task status", and the plan generator (§1.15) can also draft tasks under each milestone.

### 1.17 Project record contents (consolidated, matches client diagram)
KPIs (→ Performance Management) · Risks (→ Risk Management) · Stakeholders · Scope In & Out · Assumptions · Constraints · Added Values (benefits) · Milestones → Tasks · Challenges / Issues · Meetings · Change Requests · Lessons Learned · Team (→ Teams) · Contracts · Documents.
Scope, Assumptions, Constraints and Added Values are presented as their own tabs in the UI (stored via the Planning Item / Benefits structures).

### 1.18 Wasel — Vendor Portal (new)
Brings the vendor executing a project into planning and execution, so the vendor reports progress directly and the PM reviews it instead of re-typing it. Replaces future requirement JS-0157.

**Concept**
- Same application and database; vendors are an **external user type** with their own entry URL (e.g. `/wasel`), vendor-branded bilingual layout, and no access to internal menus or other systems.
- Per project the PM / PM Office chooses the **milestone update mode**: *PM only* (current, default) or *Vendor then PM*. Both remain available; can be switched during the project (logged).

**Vendor register (shared master data)**
Vendor organisation: name AR/EN, trade licence no., licence expiry, country, address, contact person, email, phone, status (Active | Suspended | Blacklisted), linked contracts / LPOs (PMS Contracts), vendor performance history. Also used by Contracts (§1.4 D) as the supplier list.

**Vendor users**
- Invited by email by the PM / PM Office for a specific vendor and project(s); first login sets password.
- **Mandatory MFA** (email OTP or authenticator app); optional UAE Pass login if enabled in Settings. Internal SSO is not used for vendors.
- Access auto-expires at the contract end date / project closure (configurable) and can be revoked at any time; dormant accounts disabled after N days.
- Vendor roles: **Vendor Project Lead** (submits updates) and **Vendor Contributor** (prepares updates, uploads files; cannot submit) — configurable.

**What vendors can do (only on assigned projects)**
| Area | Vendor capability |
|---|---|
| Project overview | View vendor-visible fields: name, scope, milestones assigned to them, dates, status, PM contact. Budget / internal costs / approvals hidden unless configured |
| Planning | Propose the detailed plan (milestones / phases with dates and weights, tasks; or epics / user stories / sprints for agile) → submit to PM → PM accepts into the plan (with edits) or returns |
| Execution | Update assigned milestones: actual start / end, Achieve %, progress note, delay causality + corrective actions, evidence files (required at 100%); update their tasks / user stories → **submit to PM** |
| Deliverables | Upload deliverables against milestones; see acceptance result (accepted / returned with comments) |
| Issues & challenges | Raise issues / challenges / risks as drafts for the PM to accept |
| Meetings | View meetings they attend and their minutes; confirm actions assigned to them |
| Communication | Comment thread per milestone / submission with the PM (@mentions, attachments) — all kept on the record |
| Status | See the status of each submission (Draft · Submitted · Returned · Accepted by PM · Included in approved update) |

**Review flow (Vendor then PM mode)**
1. Monthly window opens → vendor users notified (bilingual email + in-portal).
2. Vendor fills the update for their milestones → Vendor Project Lead submits.
3. PM reviews each milestone update side by side with the previous approved values: **Accept**, **Edit then accept** (difference logged and visible to the vendor), or **Return to vendor** with comments.
4. PM completes any non-vendor milestones, then submits the monthly update into the normal approval chain (W4). Approvers see which values came from the vendor.
5. Approval outcome is shown to the vendor (without internal comments, unless marked "share with vendor").

**Vendor performance**
At project closure (and optionally per milestone) the PM rates the vendor — timeliness, quality of deliverables, responsiveness, compliance with reporting — with comments. Ratings build the vendor's performance history in the register and feed the Impact stage "vendor satisfaction".

**Security & audit**
- Field-level "vendor-visible" flags per object; server-side checks on every request (UI, API) — vendors never get MCP or AI chat access by default.
- All vendor actions in the audit trail with channel = *Wasel*; uploaded files virus-scanned; rate limiting and lockout on failed logins.
- Vendor user activity visible to the PM Office (who is active, last login).

**AI (internal side only)**
Summarise the vendor's monthly submission for the PM, flag inconsistencies (e.g. 100% without evidence, progress jump vs previous month, dates outside the contract), and draft the "return to vendor" comment.

### 1.19 Default configuration items (all configurable; build defaults are in Appendix A)
- **Q-P1** Monthly update approval: Department Director → PM Office (BRD) or PM Office only (user guide, diagram)?
- **Q-P2** Initiative lifecycle: which steps does an Initiative skip (charter approval, LPO, team formation, closure, impact)?
- **Q-P3** After closure approval: → Impact stage (BRD) or → Completed (diagram)?
- **Q-P4** Approval entry criteria: when is the Section Manager step skipped; when are Finance Service Section and GM Office needed for change requests?
- **Q-P5** Classification rule: Project = all screening answers Yes AND budget > threshold?
- **Q-P6** Formulas: Evaluation Score and priority bands; Risk Factor / Inherent / Residual; KPI Goal Status; Progress Status (On Track / Delayed).
- **Q-P7** Default picklist values (strategic objectives, operational themes, risk list, stakeholder classification, lesson types…) — can be pulled from the Salesforce org.
- **Q-P8** Reports and dashboards currently used.
- **Q-P9** Approved change requests: applied automatically by the system (new owner, dates, re-baseline) or manually by the PM?

---

## 2. Module: Performance Management (KPI)

Source documents: KPI Management Portal Technical Specification V1.0, KPI Management Portal User Guide V2.0, KPI Admin User Guide V1.0, KPI Process Flow, KPI Data Model ERD.

### 2.1 Purpose
One governed register of all organisational KPIs. The Strategy Office defines KPIs and yearly targets; responsible departments submit periodic results with evidence when a period opens; the Strategy Office / PMO verifies the proof and approves before results are published to dashboards. Also serves as the **KPI catalog** used by Portfolio Management ("Existing KPI") and by other systems.

### 2.2 Actors
| Actor | Responsibilities |
|---|---|
| KPI Owner (responsible person / department) | Enters periodic results, causality, corrective actions, notes, evidence; submits for approval; marks a period Not Applicable |
| Department Manager | Views and reviews own department's results (optional review step, if added in the approval settings) |
| Strategy Office / PMO Reviewer | Defines KPI cards, sets targets, opens periods, verifies evidence, approves / rejects results (single or bulk) |
| System Administrator | Runs new measuring year, configuration |
| Executive Viewer | Dashboards and reports, read-only |

### 2.3 Data model (three tiers + supporting objects)

#### A. KPI Card (`Org_Kpi_Goal__c`) — the KPI definition
**Identity:** KPI Code (configurable auto-number or manual); KPI Name AR/EN; Description AR/EN (long); Measuring Equation; Process Name; Status (Active | Inactive); Initiated Date; Closed Date.
**Classification:** KPI Type (Strategic | Regulatory | Operational | Sectors | Competitive); Index Classification; Effectiveness / Efficiency; Quantitative / Descriptive; Perspective (balanced scorecard); KPI Source (multi); Measurement Method; Competitiveness Indicator; Is Global Star Rating?
**Measurement:** Measuring Unit (number, %, AED, days…); Measuring Period (Quarterly | Half-yearly | Annual | Two-year); Measuring Direction (Increase Better | Decrease Better | Matching Better); Target Setting Mechanism.
**Alignment:** Strategic Objective; Sector Goal; Emirates / national goal; Link to Ajman Vision (configurable "vision" list per installation); GEM criteria (multi), Criteria / Sub-criteria; Link with excellence system.
**Ownership:** Department; Responsible Department / Unit; Responsible Person (employee); Responsible Team.
**Benchmark:** Benchmark Description; Benchmark Result %.
**Computed:** Max Achieved % (best year).
**Related:** KPI description documents tab; yearly performance chart; target vs achieved chart.

#### B. KPI Annual Plan (`Org_Kpi_Measure__c`) — one per KPI per measuring year
Measuring Year; Measuring Period, Unit, Direction (copied from card, overridable for that year); Is Accumulated?; Target Type (Single value | Range); annual Total Target (numeric) or Range Min / Max (numeric); Responsible Person.
**Computed:** Q1–Q4 achieved values; Total Achieve Value; Last Achieved / Target value; Calculated Total Target / Achieve; Performance % (annual); KPI Status; count of results.

#### C. KPI Result (`Org_Kpi_Process__c`) — one per period of the year
Value Period (Q1–Q4 | First Half | Second Half | Full Year | Two Year); Target or Range Min / Max (per period, numeric); Achieve (numeric); Actual Performance % and Capped Performance % (see K5); Achievement Status (achieved / not achieved); Causality; Corrective Actions; Notes; Evidence attachments; Not Applicable? (+ reason); Open for Update? (computed from period window); Result Submitted?; Approval Submitted?; Approved?; Submitted by; Last modified by; Status (computed).
**Validation:** value period must match the KPI's measuring period; if unit is Percentage the target must be a % (and vice versa); evidence required on submit (configurable); causality + corrective actions required when target not met.

#### D. Links to other systems
- **Portfolio Management:** Project KPI "Existing KPI" → KPI Card. KPI Card shows a "Linked projects" tab.
- **Risk Management:** Risk ↔ KPI relation (`Risk_Kpi_Relation__c`) — enterprise risks linked to KPIs (key risk indicators). Fields to be confirmed when Risk Management is specified.
- **Corrective Action / Benchmark Request:** see §2.8 (proposed).

### 2.4 Workflows

#### K1 — New measuring year (admin)
1. Create any new KPI Cards; activate / deactivate cards for the coming year.
2. Open "New Year" screen → select year → all active KPIs listed → untick those not needed → adjust period, unit, direction, targets if needed → Submit.
3. System creates one Annual Plan per KPI and the Result records automatically from the measuring period (Quarterly → Q1–Q4; Half-yearly → First / Second Half; Annual → Full Year; Two-year → Two Year).
4. Strategy Office reviews and confirms targets and ranges (baseline for the year).

#### K2 — Period opens
1. Each period has a configurable submission window (open date, due date). **Late submission is allowed** after the due date: the result is flagged *Late* (days late recorded) and counts against the department's on-time rate; the PMO can still lock a period at year end.
2. When a window opens, results become "Open for Update", appear in owners' **Pending Updates**, and owners are emailed. Reminders before the due date; overdue escalation to the department manager.
3. Closed periods are locked; Strategy Office can reopen a single result or a whole period.

#### K3 — Submit result (KPI owner)
1. Open pending result → **Submit Result** wizard: (1) enter achieved value (target and unit shown); (2) causality, corrective actions (mandatory if target missed), notes; (3) upload evidence.
2. Submit for approval → record locked. Owner can recall while pending.
3. Alternative: mark **Not Applicable** for this period with reason (excluded from calculations).

#### K4 — Verify & approve (Strategy Office / PMO)
1. Reviewer works from an **Approval queue** filtered by year, period, department.
2. For each result: sees value, target, trend vs previous periods, evidence (in-page viewer), causality and corrective actions, measuring equation.
3. **Verification checklist** (configurable): evidence attached and readable; evidence supports the value; calculation follows the equation; correct period; causality / corrective actions adequate.
4. Approve → final, roll-ups recalculate, owner notified. Reject / Return → unlocked, comments mandatory, owner notified to correct and resubmit.
5. **Bulk approval** (as in Salesforce): select year + department, tick results to approve, untick to leave, Update.
6. Approval chain is **configured in Settings** through the shared approval engine (§0.2): the admin can add, remove or reorder steps at any time (e.g. add a Department Manager review before PMO). Shipped default: single step — PMO.

#### K5 — Calculation & reporting
- **Performance calculation** — ONE central, unit-tested service (`PerformanceCalculator`) used everywhere (results, annual plan, scorecards, dashboards, reports, API, MCP). Based on the Salesforce formula, corrected to best practice:
  - **Targets are numeric fields**, never text. Each annual plan / period has a **Target Type**: *Single value* (Target) or *Range* (Range Min + Range Max, either may be 0). Range is chosen explicitly, not detected from a "-" in the text as today.
  - **Direction** (single-value targets): Increase Better · Decrease Better · Matching Better (the old "Proximity Better" is the same rule and is merged into Matching Better).
  - Ratio KPIs require non-negative values (validated on entry); KPIs that can be negative (e.g. growth %) use Matching/Range or a scale-based KPI.

```
function performance(targetType, direction, target, rangeMin, rangeMax, achieve):
    if achieve is null: return NULL

    if targetType == Range:                       // rangeMin <= rangeMax, 0 allowed
        if rangeMin <= achieve <= rangeMax: actual = 1.0
        elif achieve < rangeMin:            actual = achieve / rangeMin
        else:                               actual = (achieve == 0) ? 1.0 : rangeMax / achieve
    else:                                         // Single value
        if target is null: return NULL
        if target == 0:
            actual = (achieve == 0) ? 1.0 : NULL  // NULL = cannot calculate
        else switch direction:
            IncreaseBetter: actual = achieve / target
            DecreaseBetter: actual = (achieve == 0) ? 1.0 : target / achieve
            MatchingBetter: actual = (achieve == target) ? 1.0
                                   : (achieve > target) ? target / achieve : achieve / target

    capped = (actual is NULL) ? NULL : min(actual, 1.0)
    return { actual, capped }
```
  - **Both values are stored and shown:** *Actual performance* (uncapped, e.g. 125%) and *Capped performance* (max 100%). The UI shows capped as the main figure with actual beside it (e.g. "100% (actual 125%)").
  - **Capped** is used for all roll-ups — objective, perspective, department and organisation scorecards, RAG bands and averages — so one over-achieving KPI cannot hide under-performing ones. **Actual** is used for trend charts, over-achievement reports and target-setting.
  - NULL shows as "—" with tooltip "cannot be calculated" and is excluded from roll-ups.
  - Required unit tests (minimum):

| # | Type | Direction | Target | Min | Max | Achieve | Actual | Capped |
|---|---|---|---|---|---|---|---|---|
| 1 | Single | any | 0 | – | – | 0 | 100% | 100% |
| 2 | Single | any | 0 | – | – | 5 | NULL | NULL |
| 3 | Single | Increase | 80 | – | – | 100 | 125% | 100% |
| 4 | Single | Increase | 80 | – | – | 0 | 0% | 0% |
| 5 | Single | Decrease | 10 | – | – | 5 | 200% | 100% |
| 6 | Single | Decrease | 10 | – | – | 0 | 100% | 100% |
| 7 | Single | Decrease | 10 | – | – | 20 | 50% | 50% |
| 8 | Single | Matching | 50 | – | – | 40 | 80% | 80% |
| 9 | Single | Matching | 50 | – | – | 62.5 | 80% | 80% |
| 10 | Range | – | – | 85 | 95 | 92 | 100% | 100% |
| 11 | Range | – | – | 85 | 95 | 68 | 80% | 80% |
| 12 | Range | – | – | 85 | 95 | 100 | 95% | 95% |
| 13 | Range | – | – | 0 | 5 | 3 | 100% | 100% |
| 14 | Range | – | – | 0 | 5 | 10 | 50% | 50% |
- Annual aggregation is driven by the KPI Card's **Measuring Method** field: *Cumulative* → sum of period values; *Average* → average of reported periods (Not Applicable periods excluded). Values are configurable picklist entries mapped to an aggregation rule.
- Only approved results count in dashboards (pending shown separately).

### 2.5 Screens
- **Home dashboard:** total KPIs, by type, by department, by strategic objective, achievement distribution (on target / below / not reported), pending updates, approval backlog, top / bottom performers.
- **KPI Cards:** register with filters (code, department, type, objective, status); card page with Details, Measuring info, Alignment, Yearly performance (chart), Documents, Linked projects, Linked risks, History.
- **My Pending Updates:** owner's open results with due dates.
- **KPI Approval:** reviewer queue + bulk approve screen.
- **New Year:** wizard described in K1.
- **Reports / Export:** bilingual Excel / PDF; year-wise, period-wise, department scorecards.

### 2.6 Access (defaults, configurable — §0.2)
| Role | Scope | Permissions |
|---|---|---|
| KPI Owner | KPIs where they are responsible person / their unit | View cards; submit results, evidence, N/A |
| Department Manager | My unit and below | View all department KPIs and results; optional review step |
| Strategy Office / PMO | All | Manage cards, targets, years, periods; approve / reject; reopen |
| Executive Viewer | All | Dashboards and reports |
| Administrator | All | Configuration |

### 2.7 Enhancements from performance management standards (switchable)
Sources: Balanced Scorecard (Kaplan & Norton), UAE Government Excellence Model (GEM 2.0) & Global Star Rating, ISO 9001 §9.1 (monitoring & measurement).
- **Strategy hierarchy:** Vision → Pillars / Perspectives → Strategic Objectives → KPIs, configurable per installation, with a **strategy map** view (objectives by perspective, coloured by performance).
- **KPI cascading:** organisation KPI → department KPIs → section KPIs, with roll-up of performance.
- **Scope note:** this system holds only organisation-approved KPIs (organisation / department / section). **Employee-level KPIs are out of scope here**; they will be a separate section in the Employee module / an employee performance dashboard (to be specified later).
- **Weighted scorecards:** weights on KPIs within an objective and objectives within a perspective → objective, perspective, department and organisation scores.
- **Performance bands (RAG):** configurable thresholds (e.g. ≥ 95% green, 80–95% amber, < 80% red), per installation or per KPI.
- **Baseline & multi-year targets:** baseline value and year; targets for the full strategy cycle (e.g. 2026–2030), not only the current year.
- **Composite / formula KPIs:** KPI value calculated from data elements (e.g. numerator / denominator entered separately), so the equation is applied by the system, not by hand.
- **Qualitative KPIs:** descriptive KPIs scored on a defined scale or milestones.
- **KPI definition change control:** changes to target, equation, unit or direction after the year starts require Strategy Office approval; full version history kept.
- **Data quality score** per department: on-time submission rate, rejection rate, evidence completeness.
- **Trend & comparison:** multi-year trend, period-on-period, vs benchmark, vs target path.
- **Excellence reporting:** filter and export KPIs by GEM criteria / Global Star Rating for external submissions.

### 2.8 Integration hooks (proposed, confirm when those systems are specified)
- **KPI missed → Corrective Action Plan (§9):** when a result is approved below threshold (or red for N consecutive periods), the PMO can raise a CAP remark in one click (or automatically — configurable).
- **Benchmark Request:** benchmark results can be linked to the KPI card's benchmark fields.
- **Portfolio Management:** project KPI results can be proposed as contributions to the corporate KPI (PMO decides).

### 2.9 AI features — Performance Management
- **Evidence checker:** AI reads the uploaded proof (PDF, Excel, image) and tells the reviewer whether the value appears in the evidence, whether the period matches, and what is missing. Advisory only.
- **Anomaly flags:** highlights results that are unusual versus history (big jumps, identical values copied, value outside range).
- **Causality & corrective action assistant:** drafts causality and corrective actions for missed targets from the owner's notes; flags weak or generic justifications to the reviewer.
- **KPI card builder:** create a KPI card from an uploaded document or a short description; checks SMART quality (specific, measurable, equation present, unit consistent, direction clear) and suggests improvements.
- **Target suggestion:** proposes next-year targets from trend, benchmark and strategy-cycle target, with reasoning.
- **Performance narrative:** drafts the bilingual quarterly performance report (highlights, under-performers, reasons, actions).
- **MCP tools:** my pending KPI results, submit a draft result, KPI card lookup, department scorecard, approval queue summary.

### 2.10 Default configuration questions
- **Q-K2** Default period window dates (e.g. Q1 opens 1 April, due 15 April) — confirm defaults; editable in Settings.
- **Q-K6** Default picklist values (KPI sources, perspectives, GEM criteria, units, strategic objectives…) — can be pulled from the Salesforce org.

---

## 3. Module: Risk Management

Source documents: Risk Management Technical Specification V1.0 (draft), Risk Management Admin User Guide V2.0, Risk Portal User Guide V2.0, Risk Process Flow, Risk Data Model ERD.
Standards applied: ISO 31000:2018 (risk management), COSO ERM 2017, ISO 31010 (assessment techniques), Three Lines model (IIA).

### 3.1 Purpose
Maintain the organisation's enterprise risk register: every organisational unit identifies its risks, assesses them, records existing controls and treatment actions, and reports progress until the risk is within appetite. The Risk Office reviews, monitors and reports (dashboards, Top-10 institutional risks).

**Changes from Salesforce (best practice):**
| Salesforce today | New system |
|---|---|
| Separate Risk Portal with its own Employee ID / password | Same single application and login as every other system; access by role |
| Inherent score = Probability **+** Impact (e.g. 5 + 4 = 9) | Score = Probability **×** Impact (1–25) on a configurable 5×5 matrix (standard practice; additive scoring under-ranks high-impact risks). Method is configurable if an organisation insists on additive |
| Residual status set by hand | Residual risk re-rated (likelihood × impact *after* controls); status derived from residual score bands, with manual override + justification |
| No approval, review is manual | Configurable workflow: risk submission review, periodic reassessment, and closure approval (all steps editable in Settings) |
| Controls and actions are one object; action at 100% "moves" to existing controls | Controls and treatment actions are separate; a completed action prompts "convert to control" with an effectiveness rating |
| Affected strategic goal is free text | Linked to Strategic Objectives (shared list / strategy hierarchy, §2.7) |
| Risk number may collide (three units share code GRD) | Numbering from the unit code in the Organisation module, sequence unique per code + year |
| No review history | Every assessment is snapshotted, so risk movement over time is visible |

### 3.2 Actors
| Actor | Responsibilities |
|---|---|
| Risk Owner (employee) | Accountable for a risk; keeps assessment current; owns treatment |
| Department Risk Coordinator (risk champion) | Registers and updates the unit's risks and actions; coordinates periodic reviews |
| Action Owner | Executes treatment actions; updates progress and evidence |
| Control Owner | Maintains an existing control; confirms it operates |
| Risk Office (a role, not a fixed department — ADoF today: Internal Audit Office; later: Strategy Office) | Framework & settings; reviews and approves risks; sets Top-10; monitors; reports |
| Leadership / Risk Committee | Reviews Top-10 and high risks; approves risk appetite; approves closure of high risks (configurable) |
| Internal Audit / Viewer | Read-only access for assurance |

### 3.3 Risk framework (Settings → Risk framework)
- **Risk categories** (multi-level, AR/EN): Strategic, Operational, Financial, Legal & Compliance, Technical, IT & Information Security, Reputation, … (default from Salesforce).
- **Likelihood scale** (default 1–5) and **impact scale** (default 1–5) with AR/EN descriptors and criteria per impact area (financial, reputation, service, compliance, safety) — replaces the static "Risk Assessment Mechanism" page; the page is generated from these settings.
- **Scoring method:** multiplicative Likelihood × Impact (default), additive Likelihood + Impact, or a **custom formula** written in a safe expression editor using variables (`L`, `I`, and optionally control effectiveness `C`), e.g. `L * I`, `L + I`, `(L * I) * (1 - C/10)`. The editor validates the expression, shows a live preview of the resulting matrix, and lists how many existing risks would change rating. Saving a new formula creates a new framework version; existing risks are recalculated (with an audit entry per risk) or kept on the old version until their next review — admin chooses.
- **Rating bands** on the matrix (number of bands, names AR/EN, colours, score ranges all editable). **Default: Low 1–4 (green), Medium 5–9 (yellow), High 10–16 (orange), Critical 20–25 (red)** on the 5×5 multiplicative matrix. Default mapping to residual status: Low → *No Issue*, Medium → *Continuous Review*, High / Critical → *Active Management*.
- **Control effectiveness scale:** Effective / Partially effective / Ineffective (default; Salesforce 1–10 evaluation can be configured instead).
- **Risk appetite & tolerance** per category (max acceptable residual score); appetite breaches flagged automatically.
- **Response strategies:** Accept · Avoid · Reduce (Mitigate) · Transfer (and Exploit / Enhance for opportunities, if enabled).
- **Review frequency** by residual rating (default: Critical monthly, High monthly, Medium quarterly, Low semi-annually).
- **Numbering format:** `{UNITCODE}{YY}-R{NNN}` (default); unit code taken from the Organisation module.
- **Shared scoring service:** the same framework (scales, matrix, bands) is used by **Project Risks** in Portfolio Management, so a project risk and an enterprise risk with the same ratings always score the same.

### 3.4 Data model

#### A. Risk
**Identity:** Risk Number (auto); Register year; Title AR/EN; Risk statement in *cause → event → consequence* form (Cause(s), Risk event, Consequence(s) — structured, with guidance); Risk Determined By; Date identified; Exposure start date; Risk type (Internal | External); Categories (multi); Status (Draft | Under Review | Active | Closed | Rejected); Emerging risk? ; Opportunity? (positive risk, optional).
**Ownership:** Owning unit (register = the unit's register; Organisation module); Risk Owner; Department Risk Coordinator; Responsible for periodic follow-up.
**Alignment:** Affected strategic objective(s) (linked); affected processes / services (text or link); Key Risk Indicators (links to KPI cards, §3.4 D).
**Inherent assessment:** Likelihood; Impact (with impact area); Inherent score & rating (computed).
**Controls:** existing controls (child records); overall control effectiveness (computed from controls or set by Risk Office).
**Residual assessment:** Residual likelihood; Residual impact; Residual score & rating (computed); Residual status (No Issue | Continuous Review | Active Management, derived; override with justification).
**Target assessment:** Target likelihood / impact / score after planned treatment; target date.
**Response:** Response strategy; Within appetite? (computed); Needs periodic follow-up?; Top-10 institutional risk? (set by Risk Office / committee); Escalated? (to leadership).
**Review:** Last review date; Next review due (computed from frequency); Review status (on time / overdue).
**Closure:** Closure reason; Closed date; Closed by.

#### B. Control (existing control)
Control name / description AR/EN; Control type (Preventive | Detective | Corrective | Directive); Nature (Manual | Automated | Hybrid); Control owner; Frequency (continuous, daily, monthly…); Effectiveness rating; Last tested / assessed date; Evidence; Source action (if created from a completed treatment action); Status (Active | Retired).

#### C. Treatment Action
Action number (`ORA-NNNN` default format); Proposed action AR/EN; Action owner (employee) + responsible units (multi); Planned start / end; Actual start / end; Completion % ; Implementation narrative (progress updates, each update kept as history); Status (Not Started | In Progress | Completed | Overdue | Cancelled); Evidence attachments; Cost (optional); On completion: "Convert to existing control?" (Yes → creates Control linked back).

#### D. Key Risk Indicator (Risk ↔ KPI link)
Many-to-many link between Risks and KPI Cards (Performance Management) — replaces `Risk_Kpi_Relation__c`. Per link: threshold values (warning / breach) and direction. When an **approved** KPI result crosses the threshold, the risk owner and Risk Office are notified and the risk is flagged for reassessment.

#### E. Risk Assessment History
Snapshot on every reassessment (who, when, inherent / residual / target ratings, status, comments) → risk movement chart and "risks that worsened this quarter" reports.

#### F. Risk Event / Incident (loss event log)
Date; description; linked risk(s); actual impact (financial / other); near miss?; root cause; lessons; linked CAP remark (→ Corrective Action Plan, §9). Realised events trigger reassessment of the linked risk.

#### G. Project Risks tab (from Portfolio Management)
Read-only, live view of PMS project risks (decided in §0.0): filters by project, department, rating; each row opens the risk inside its project. Included in heat-maps and dashboards as a separate, filterable category. Not copied into the enterprise register.

### 3.5 Workflows

#### R1 — Identify & register
1. Department Risk Coordinator (or any user with permission) creates a risk with the guided wizard: statement (cause / event / consequence) → categories, type, owner, objectives → inherent rating on the clickable matrix → existing controls → treatment actions → KRIs.
2. Saved as Draft; risk number assigned on save.
3. Submit → **review workflow** (configurable; default: Risk Office review). Risk Office can approve (→ Active), return for correction, or reject (duplicate / not a risk). Risk Office may adjust ratings, response strategy and Top-10 flag, with comments.

#### R2 — Treat
1. Action owners update progress: implementation note, completion %, dates, evidence (each update kept as history).
2. Overdue actions → reminders to action owner, escalation to risk owner / coordinator.
3. At 100% → action Completed → prompt to convert into an existing control (owner chooses effectiveness rating) → residual reassessment suggested.

#### R3 — Monitor & review (periodic)
1. Each risk has a next-review date from its residual rating; reminders to the risk owner / coordinator before due date.
2. Review wizard: confirm or update controls' effectiveness, residual likelihood / impact, status of actions, add commentary → snapshot saved.
3. Review is submitted to the Risk Office (configurable approval). Overdue reviews flagged on dashboards.
4. Triggers for an unscheduled review: KRI breach, risk event logged, audit finding linked, appetite breach.

#### R4 — Escalate
- Risks rated above appetite or Critical → automatically flagged for leadership / Risk Committee; Top-10 list maintained by the Risk Office (ranked by residual score, adjustable).
- Committee decisions recorded on the risk (accept, additional treatment, transfer).

#### R5 — Close
Risk owner requests closure with reason (no longer relevant, fully treated, transferred) → approval workflow (default: Risk Office; Critical risks also Risk Committee) → Closed, kept in history; can be reopened.

### 3.6 Screens
- **Risk dashboard:** total risks; by category, type, register (unit), rating; **heat-maps** inherent vs residual (click a cell to list risks); risks outside appetite; Top-10 institutional risks; overdue actions; overdue reviews; risk movement (improved / worsened); KRI breaches. Filters: unit, category, residual status, year, include project risks.
- **Risk register:** list with saved views (My risks, My unit, Top-10, Outside appetite, Overdue review); inline rating badges; export.
- **Risk page:** header (number, title, owner, ratings as coloured chips inherent → residual → target, status); tabs: Overview (statement, alignment), Assessment (matrix, history chart), Controls, Treatment actions, KRIs (with latest KPI results), Events, Reviews, Documents, Activity / audit.
- **My actions:** action owners' list with quick progress update (replaces portal update popup).
- **Project Risks** tab.
- **Risk Assessment Mechanism:** generated from the framework settings (bilingual), printable.
- **Reports:** risk register report, Top-10 report, unit register report, risk profile per unit, action tracker — bilingual PDF / Excel.

### 3.7 Access (defaults, configurable — §0.2)
| Role | Scope | Permissions |
|---|---|---|
| Risk Office | All | Framework settings, all risks, approve / return / reject, Top-10, close, reports |
| Department Risk Coordinator | My unit and below | Create / edit unit risks, controls, actions; submit; run reviews |
| Risk Owner (record role) | Own risks | Edit assessment, controls, actions; request closure |
| Action / Control Owner (record role) | Assigned items | Update progress / effectiveness and evidence |
| Department Viewer | My unit and below | Read |
| Leadership / Risk Committee | All | Read; record committee decisions; approve high-risk closure |
| Internal Audit (viewer) | All | Read incl. history and audit trail |

### 3.8 Integrations
- **Performance Management:** KRIs (§3.4 D); KPI card shows "Linked risks".
- **Portfolio Management:** Project Risks tab (read-only) + shared scoring framework.
- **Audit Management (§10):** audit findings can be linked to risks and controls and trigger reassessment; the risk register feeds the risk-based audit plan; shared Risk & Audit report page.
- **Corrective Action Plan (§9):** risk events / ineffective controls can raise CAP remarks; CAP actions can be linked as risk treatment.
- **Strategy hierarchy:** affected objectives link to the shared strategic objective list.

### 3.9 AI features — Risk Management
- **Risk identification assistant:** from a description, process document, project charter or audit report, suggests risks written in cause → event → consequence form, with suggested category, likelihood / impact and reasoning.
- **Risk statement quality check:** flags vague statements ("lack of resources") and proposes a clear rewrite.
- **Control & treatment suggestions:** proposes controls and actions for a risk, using similar risks already in the register.
- **Duplicate detection:** warns when a new risk resembles an existing one in any unit.
- **Review assistant:** summarises what changed since the last review (actions progress, KRI movement, events) and drafts the review commentary.
- **Risk report writer:** drafts the bilingual quarterly risk report and Top-10 briefing for leadership.
- **MCP tools:** risk register search, risk details, my actions, update action progress (draft), heat-map summary, Top-10, risks outside appetite.

### 3.10 Default configuration questions
- **Q-R3** Default risk appetite per category (can be left empty and set by each organisation).

**Decided:** multiplicative scoring as default with configurable / custom formula; Risk Office role held by Internal Audit Office now, moved to Strategy Office later through role reassignment (§0.2 system ownership); four bands Low / Medium / High / Critical.

---

## 4. Module: Teams Management (Teams, Committees & Evaluation)

Source documents: DOF Teams & Evaluation Technical Specification V1.0, Team Evaluation User Guide V1.0, DOF Teams Process Flow, DOF Teams Data Model ERD, HR policy "Development of the team and committee evaluation system" (2025).
This is the shared **Team** core module (§0.1): every system uses it whenever work is done by a team or committee.

### 4.1 Purpose
One register of every approved group in the organisation — work teams, project teams, standing committees, temporary committees and any other type the organisation defines — with members, roles, formation decision, operations (meetings, recommendations, tasks) and the evaluation of the team and each member, leading to rewards and certificates.

**Changes from Salesforce (best practice):**
| Salesforce today | New system |
|---|---|
| Evaluation scores stored as fields on the team and member rows (each new year overwrites the last; "Evaluation Year" picklist must be extended each year) | Each evaluation cycle is its own record (Team Evaluation + Member Evaluations), full history kept per year / per cycle |
| Only HR can create teams from a memo | Configurable formation request + approval; other systems can request a team for their record (e.g. PMS team formation) |
| Teams link only to PMS projects | Teams link to records in **any** system (project, audit, risk, corrective action, benchmark request…) |
| Evaluation opened manually | Opened automatically by rule per team type (project closed, end date reached, annual date), plus manual open |
| Team criteria scored purely by judgement | Scores for "recommendations closure" and "governance / minutes timeliness" are suggested from actual data (recommendation register, meeting minutes) — leader confirms or overrides with justification |
| Evaluator fixed to the team leader | Evaluator configurable per team type (and overridable per team); default remains the team leader |
| Reward rule kept in Salesforce custom settings | Reward matrix maintained in Settings (grade × workload × role → amount); amounts calculated automatically; HR can adjust with justification; reward budget tracking |
| "Not Active ≤ 8" vs "Active 9–11" gap at 8 | Grade bands are configurable ranges validated to cover the whole scale without gaps |

### 4.2 Actors
| Actor | Responsibilities |
|---|---|
| Teams Administrator (HR by default — role, not hard-coded) | Team types, templates, formation approval, opens / approves evaluations, rewards, certificates |
| Requester (any authorised user or system) | Requests a new team / committee for a purpose or a record |
| Formation approver(s) | Approve team formation (configurable chain, e.g. Department Director → General Manager) |
| Team Leader (President / Chair / Project Manager) | Runs the team; evaluates the team and each member; submits evidence |
| Team members (incl. Deputy, Rapporteur, Secretary) | Participate; secretary records minutes; view their own team. Members do **not** see evaluation results |
| Department Manager | Sees team memberships and evaluations of their staff |

### 4.3 Configuration (Settings → Teams)
- **Team types** (AR/EN), each with: Permanent or Temporary; requires formation approval?; requires decision memo?; can be linked to which systems / record types; default reporting cadence; **evaluation trigger** and evaluation template.
  Defaults: Work Team (permanent or temporary — chosen per team), Project Team (temporary), Standing Committee (permanent), Temporary Committee (temporary).
- **Member roles** (AR/EN, order, is leader?, can view evaluation results?, counts toward quorum?): President, Deputy, Project Manager, Member, Rapporteur, Member & Rapporteur, Secretary. Default: only leader roles (team leader, President, Project Manager) can view evaluation results.
- **Evaluator** per team type, overridable per team: Team leader (default) · Team sponsor · Teams Administrator (HR) · a specific employee · holder of a specific role. Optionally a different evaluator for the leader's own member evaluation (default: same evaluator).
- **Evaluation triggers** per type:
  - *Project Team* → opens automatically when the linked project's closure is approved (or the linked record in another system is closed).
  - *Temporary team / committee* → opens on end date or when the team is closed.
  - *Permanent team / committee* → opens every year on a configurable date (default 1 January, for the previous year).
  - Manual "Open for evaluation" always available to the Teams Administrator.
- **Evaluation templates** (versioned per year):
  - *Team criteria* with weights and levels. Default (ADoF policy 2025): Recommendations closure 25 (≥ 95% = 25 · 80–94% = 12.5 · < 80% = 0); Implementation effectiveness & impact 30 (radical solution = 30 · steering = 15 · unenforceable decisions = 0); Quality of output 20 (approved without feedback = 20 · with feedback = 10 · not delivered = 0); Governance, discipline & documentation 10 (minutes within 24 h = 10 · 48–72 h = 5 · poor = 0); Cooperation & harmony 15 (shared professionally = 15 · depends on few = 7.5 · disputes = 0). Total 100.
  - *Member questions* on a 1–5 agreement scale (AR/EN labels). Default: contribution to achieving goals; commitment to attend meetings and submit on time; communication with the team. Total 3–15.
  - *Member grade bands*: default Very Active 12–15 · Active 9–11 · Not Active 3–8.
  - *Evidence rule*: evidence mandatory when team score ≥ 90 (threshold configurable).
  - *Workload classification*: High / Medium / Low.
- **Reward matrix:** amount (AED) by **member grade × team workload × member role** (e.g. Very Active + High workload + President → AED X), maintained in Settings and versioned per year; optional certificate-of-appreciation rule per grade; annual reward budget.
- **Approval chains** (via the shared engine): team formation, evaluation approval (default single step: HR), reward approval.

### 4.4 Data model
#### A. Team
Team code (default `S-NNNN`); Name AR/EN; Type; Subject; Objective(s); Scope / mandate; Permanent / Temporary; Owning unit; Sponsor (who formed it); Team leader (from members with leader role); Approved by (General Manager | President | configurable list); Decision / memo number + date + memo document; Start date; End date (blank for permanent); Reporting cadence (Monthly | Quarterly | Biannually | Annually | Other) + report-to; Workload classification; Status (Draft | Pending Approval | Active | Inactive | Pending Evaluation | Evaluation Waiting Approval | Closed | Replaced) ; Replaced by / replaces (link to successor formation); Linked records (0..n, any system); computed: number of active members, meetings held, recommendations open / closed %, last evaluation score.

#### B. Team Member
Team; Employee (or **external member**: name, organisation, email, phone); Role; Responsibilities; Required skills; Allocation % (optional); Position order; From date / To date; Status (Active | Left | Replaced) + replaced by; Reason for leaving. Members are never deleted — history kept.

#### C. Team operations
- **Meetings & minutes:** date/time, attendees (from members, with attendance), agenda, minutes, decisions; *minutes submitted at* timestamp → minutes delay (hours) feeds the governance criterion.
- **Recommendations / decisions register:** recommendation, owner, due date, status (Open | In Progress | Closed | Rejected), closure date, evidence → closure % feeds the recommendations criterion.
- **Team tasks:** shared Task Management module (§11), linked to the team / meeting decision; assigned to members.
- **Periodic reports:** submitted per reporting cadence; reminders when due.
- **Documents:** formation memo, reports, outputs.

#### D. Team Evaluation (one per team per cycle)
Team; Cycle (year or "on completion"); Template version; Opened by / date; Evaluator (per configuration, default team leader); per-criterion selected level + score + comment (+ system-suggested level with the data behind it); Overall score (0–100); Workload classification; Evidence attachments; Status (Open | Submitted | Returned | Approved); Submitted / approved dates; approval history.

#### E. Member Evaluation (one per member per cycle)
Team Evaluation; Member; Role at the time; answers to each question (1–5) + comment; Points; Grade (computed from bands); Participation level; Evaluated by; Calculated reward (from reward matrix); Approved reward amount (AED) + adjustment justification if different; Certificate of appreciation (Yes / No + generated certificate); Financial reward (Yes / No).

#### F. Links to other systems (generic)
Any system can register "team-able" record types (e.g. PMS Project, Audit Engagement, Corrective Action, Benchmark Request, Risk treatment). From such a record the user can **request a new team** or **link an existing team**; the record shows the team, members and their roles, and the team shows the linked records. Closing the linked record can trigger team closure and evaluation (per type configuration).
- **Portfolio Management:** the PMS "Team Formation" step (§1.5 W2) creates the Project Team here (or links an existing one); project team members get the PMS record roles (§1.9); project closure approval opens the project team evaluation automatically.

### 4.5 Workflows
#### T1 — Formation
1. Requester creates a team request (type, name, subject, objectives, leader, members with roles and responsibilities, dates, linked record, draft memo).
2. **Formation approval inside the system** (configurable chain per team type via the shared approval engine). Applies equally to project teams requested from Portfolio Management.
3. **Formation memo:** on approval the system generates the bilingual formation decision (memo) as a numbered PDF from a template (team name, objective, mandate, members and roles, dates, approver, memo number) — **or** the user uploads an externally generated memo instead. Memo number, date and file are stored on the team.
4. Team → **Active**; members notified (bilingual email + in-app).

#### T2 — Operate
Meetings and minutes, recommendations register, tasks, periodic reports with reminders; membership changes (add / leave / replace) with dates and reason — each change logged.

#### T3 — Evaluate
1. Evaluation opens (automatic trigger or manual) → Status *Pending Evaluation* → leader (and leader evaluator) notified.
2. Evaluator's wizard (default: team leader): (1) team criteria — each shows the system's suggested level and the data behind it (e.g. "12 of 13 recommendations closed = 92%", "average minutes delay 30 h"); (2) each member — three questions, comment; (3) evidence (mandatory if score ≥ threshold); (4) review → Submit.
3. If configured, a separate evaluator completes the leader's own member evaluation.
4. Approval (default: HR) — approve, or return with comments for re-evaluation.
5. Approved → scores locked, grades final, reward suggestions calculated.

#### T4 — Recognise
Reward amounts calculated from the reward matrix; HR reviews, adjusts with justification, approves (configurable chain); certificates of appreciation generated as bilingual PDFs; reward totals tracked against the annual budget.

#### T5 — Close / replace
Temporary teams close at end date or when the linked record closes (after evaluation). A committee re-formed under a new decision → old team *Replaced*, linked to the new one.

### 4.6 Screens
- **Teams register:** cards / list by type, status, unit, leader; filters (my teams, teams I lead, pending evaluation).
- **Team page:** header (code, type, status, leader, dates, linked records); tabs: Overview, Members (with roles and history), Meetings, Recommendations, Tasks, Reports, Documents, Evaluations (by cycle), Activity.
- **Evaluation wizard** (leader) and **evaluation review** (HR) with side-by-side history of previous cycles.
- **Employee profile → Teams tab:** all memberships, roles, evaluations, rewards (feeds the future employee performance section).
- **Dashboard:** teams by type / status; active committees; employees on many teams (workload); evaluation progress (opened / submitted / approved); score and grade distribution; rewards by year and unit.
- **Reports:** team register, membership report, evaluation results, rewards — bilingual PDF / Excel.

### 4.7 Access (defaults, configurable — §0.2)
| Role | Scope | Permissions |
|---|---|---|
| Teams Administrator (HR) | All | Configure types / templates / rules; create teams; open, approve, return evaluations; rewards; certificates |
| Formation requester | Own requests | Request teams; system roles in other modules (e.g. PM Office for project teams) |
| Team Leader (record role) | Own teams | Manage meetings, recommendations, tasks, reports; evaluate team and members |
| Team Member (record role) | Own teams | View team, meetings, tasks; secretary edits minutes; no access to evaluation results |
| Department Manager | My unit and below | View teams and memberships / evaluations of their staff |
| Viewer / Leadership | All | Read and dashboards |

### 4.8 AI features — Teams
- **Formation assistant:** drafts the team objective, mandate and bilingual formation memo from a short description or linked record.
- **Member suggestions:** recommends members by skills, past team evaluations and current team workload (flags over-assigned employees).
- **Evaluation assistant:** summarises the team's activity in the cycle (meetings, minutes timeliness, recommendations closed, outputs) and explains the suggested criterion levels; flags inconsistent evaluations (e.g. every member rated 5, or grade contradicts attendance).
- **Minutes writer:** turns meeting notes into structured minutes with decisions and recommendations.
- **MCP tools:** my teams, team details, add meeting minutes (draft), my pending evaluations, evaluation status by team.

### 4.9 Default configuration questions
- **Q-T5** Default formation approval chain (e.g. Department Director → General Manager) and the default reward matrix values — can be taken from the Salesforce custom settings.

**Decided:** evaluator configurable (default team leader); evaluation results visible only to leader roles and HR; reward matrix = grade × workload × role; formation approved in the system (also for project teams) with system-generated memo or uploaded external memo.

---

## 5. Shared Request Framework (used by Document Registry, Intellectual Property, Benchmarking and future request types)

Document Registry, Intellectual Property and Benchmarking are all "request → review → process → register" systems. Instead of three hand-built workflows, they are built on one **Request Framework**, with each system adding its own register, screens and rules. New request types can later be added by configuration.

### 5.1 Framework capabilities
- **Request types** (AR/EN), each with: code / numbering format; owning unit (system ownership, §0.2); who can submit; form definition; stages; approval chains per stage; SLA per stage; notifications; "Current status / next action" messages.
- **Form builder:** sections, field types (text, rich text, number, currency, date, picklist, multi-picklist, employee, unit, file, repeating table / child rows), required / conditional visibility rules, AR/EN labels and help text. Module-specific fields are defined in code; organisations can add extra fields without code.
- **Stages & actions:** configurable stage list per type; per stage the allowed actions (submit, approve, return for correction, reject, cancel, custom action such as "Publish" or "Update registration info") and who can perform them; each action can open a mini-form (e.g. approval form asking for document type and sensitivity).
- **Approvals:** through the shared approval engine (§0.2) — steps, approver resolution, entry conditions, delegation, comments, history.
- **Requester experience:** "New request" catalogue showing the request types the user may submit; "My requests" with live status and next action; return-for-correction loop; cancel own draft / pending request.
- **Processor experience:** queue per request type and stage, filters, bulk actions where safe, SLA indicators.
- **Documents out:** template-based bilingual PDF generation (registration forms, certificates, approval letters) with merge fields.
- **Intranet entry:** the old intranet forms are replaced by the in-app request catalogue; optional deep links / embeddable "submit request" pages for the organisation's intranet, using the same SSO login.
- Audit trail, AI (document-to-form extraction, §0.5) and MCP tools ("submit request draft", "my requests", "request status") available to every request type automatically.

---

## 6. Module: Document Registry (Corporate Document Management)

Source: Corporate Document Management User Guide V1.0, Process Flow and Data Model ERD. Standards applied: ISO 9001:2015 §7.5 (documented information), ISO 15489 (records management).

### 6.1 Purpose
Controlled register of the organisation's official documents (policies, procedures, legal documents, strategies, manuals, forms…): submission, review and approval, numbering and versioning, publication of the signed final version, periodic review, and controlled access by sensitivity. Publishing selected document types creates Intellectual Property records (decided, §0.0).

### 6.2 Workflow (baseline + best practice)
1. **Submit (any employee):** new document or new version of an existing document ("Is it a draft of an existing document?" → pick the document; becomes a new version). Fields: document name AR/EN, owner unit (default: requester's unit), description, draft file, reason for change (for new versions).
2. **Review (default: Strategy Office / PM Office; chain configurable, e.g. owner unit manager → Strategy Office → DG for Strategic-level documents):** reviewer completes classification — **Hierarchical level** (e.g. Strategic, Legislative, Operational…), **Document type** (e.g. Corporate Policy, Legal Documents, Procedure…), **Sensitivity** (Public, Internal, Confidential, Secret…), review period — then Approve, Return for correction, or Reject.
3. On approval the system generates the **Document ID** (default format `{ORG}-{UNITCODE}-{TYPECODE}-{YYYY}-{NN}`, e.g. DOF-LG-LEG-2026-01) and **Version** (major.minor; new version of an existing document keeps the Document ID).
4. **Publish (Strategy Office):** upload the signed / final file (PDF), set **Share to all employees?** (otherwise visibility follows sensitivity) → status Published → requester notified (bilingual email) → previous version automatically marked **Superseded** → if the document type is flagged "Creates IP register entry", an IP record is created (§7).
5. **Validity & expiry** (replaces a fixed review cycle): each **document type** has a validity rule — *valid until superseded* (no expiry) or *expires after N months* from the effective date. The expiry date is set on publish (editable by the Strategy Office).
   - Best-practice default (ISO 9001 document control; GEM): policies and procedures 24 months, manuals / guides 36 months, legal / legislative documents and forms *until superseded*. All editable per type.
   - Reminder to the document owner before expiry (default 60 and 30 days). Owner either **confirms still valid** (expiry extended by the type's period, logged), **submits a new version** (current version stays valid until the new one is published), or **requests withdrawal**.
   - At expiry without action → status *Expired* (still visible, marked "expired" in the library; flagged on the dashboard and to the Strategy Office).
   - A new version published at any time supersedes the previous one regardless of expiry.
6. **Withdraw / obsolete:** with reason and approval; document stays in the archive, not in the active library.

Statuses: Draft · Submitted · Under Review · Returned · Rejected · Approved (awaiting publish) · Published · Expired · Superseded · Under Revision · Withdrawn / Obsolete.

### 6.3 Data model
**Document:** Document ID; name AR/EN; description; owner unit; document owner (employee); hierarchical level; document type; sensitivity; current version; status; effective date; expiry date (from type rule, or blank = until superseded); related documents (links); tags; read-acknowledgement required?; linked IP record.
**Document Version:** version number; draft file(s); final signed file; change summary; requested by; reviewed / approved by and dates; published date; status (Current | Superseded | Rejected).
**Read acknowledgement** (optional): document version, employee or unit / audience, acknowledged date.

### 6.4 Screens
- **Document Library:** browse by hierarchical level → type → unit; full-text search (title, description, file content); filters; shows only documents the user's sensitivity clearance allows; download with "uncontrolled when printed" watermark (configurable).
- **Document page:** current version viewer, version history with change summaries and compare, approvals, related documents, IP link, acknowledgements.
- **My requests / review queue** (framework).
- **Dashboard:** documents by level / type / unit; expiring soon and expired documents; pending approvals; recently published.

### 6.5 Access
Sensitivity levels mapped to roles / units in Settings (e.g. Confidential → owner unit + Strategy Office + leadership). Published Internal documents visible to all employees; Public documents can be exposed through an optional public link.

### 6.6 AI
Auto-classify level, type and sensitivity from the uploaded draft; summary AR/EN; **duplicate / conflict check** against existing published documents; "what changed" summary between versions; Q&A over published policies the user is allowed to see ("what is our travel approval limit?") with links to the source document.

---

## 7. Module: Intellectual Property Registry

Source: Intellectual Property Management User Guide V1.0, Process Flow and Data Model ERD. Standards / references applied: WIPO IP management guidance; UAE Ministry of Economy (MOET) registration process.

### 7.1 Purpose
Register and protect intellectual property created by employees for the organisation (methodologies, systems, software, designs, documents, inventions, brands), manage registration with the Ministry of Economy, keep certificates, track expiry / renewal, and recognise the contributors.

### 7.2 Workflow (baseline + best practice)
1. **Submit (any employee, or automatically from Document Registry on publish):** IP name AR/EN, description, supporting files, **transferring parties / contributors** (employees, optionally externals) with role and contribution %; declaration that rights are assigned to the organisation.
2. **Strategy Office review:** Approve / Reject with **Property type** (e.g. Methodology, Software / System, Literary work, Design, Invention, Trademark…) and **Protection type** (e.g. Copyright, Industrial Design, Patent, Trademark, Trade secret / internal registration only), notes. Chain configurable.
3. **Prepare registration:** system generates the **auto-filled MOET registration form** (bilingual PDF template) for download; the Strategy Office files it on the MOET website and records filing date and application number (status *Filed*).
4. **MOET outcome:** Registered → enter registration number, registration date, **expiry date** (auto-proposed from a configurable term per protection type — default registration date + 20 years, editable), upload certificate → system emails the certificate to all contributors (status *Registered*). Rejected by MOET → reason recorded → close or resubmit.
5. **Internal-only protection:** for types not filed externally, the record is registered internally with an internal registration number.
6. **Renewal:** reminders before expiry (configurable, e.g. 6 and 3 months); renewal recorded as a new registration period; expired IP flagged.

Statuses: Draft · Submitted · Under Review · Returned · Rejected · Approved – To File · Filed with MOET · Registered · Rejected by MOET · Expired · Renewed · Withdrawn.

### 7.3 Data model
**IP Record:** IP number (default `IP-Registry-NNNN`); name AR/EN; description; property type; protection type; source (employee request / Document Registry → linked document version); owner unit; status; filing date; application number; registration number; registration date; expiry date; certificate file; notes; linked project / benchmark (optional).
**Contributor (transferring party):** IP record; employee (or external: name, organisation); **Emirates ID number** (needed on the MOET form — stored encrypted, shown masked, visible only to the IP administrators); role (author, inventor, designer…); contribution %; signature status (signed on the printed form, signed copy uploaded); certificate sent date.
**Registration period:** for renewals (from / to, registration number, certificate).

### 7.4 Screens & reports
IP register (filters by type, protection, status, unit, year); IP page with contributors, timeline, documents and certificates; expiring-soon list; dashboard (IP by type / unit / year, registered vs pending, upcoming expiries); employee profile → "My intellectual property" (feeds the future employee performance section).

### 7.5 AI
Suggest property and protection type from the description and files; similarity check against existing IP records and published documents; pre-fill the MOET form text (description of the work) in Arabic and English.

---

## 8. Module: Benchmarking

Source: Benchmarking System User Guide V1.0 (workflow, Current Status table), Benchmarking Process Flow and Data Model ERD.

**Actors:** Requester (anyone from the department / section / office) · Benchmarking Coordinator (from the organisational unit — fills Search, Visit and Analysis forms, updates actions) · Strategy & Project Management Office (PMO — approves gates, circulates the plan) · System (notifications, stage changes). Standards applied: APQC benchmarking methodology & Code of Conduct, UAE Government Excellence Model (learning & benchmarking).

### 8.1 Purpose
Plan and run benchmarking studies with external (or internal) comparator entities, capture the comparison and best practices, and turn them into an action plan that is tracked to completion with evidence.

### 8.2 Workflow — four stages (baseline) with PMO approval gates
**1. Plan**
- Unit submits a benchmarking request: responsible unit, relevant section, responsible employee, **target quarter / expected time period**, comparison type (e.g. internal, competitive, functional, generic), purpose of the comparison (e.g. Policy Development, Human Skills Development — configurable list), scope, and **comparison topics** (at least 1, each with an owner).
- PMO approves (or returns for correction / rejects). Approved requests form the **annual benchmarking plan**, circulated to all units (published plan view + notification).
- → Stage = Search.

**2. Search**
- **30 days before the target quarter** (configurable), the responsible unit is notified to fill the **Search form**: candidate comparator entities (local / regional / international), why each was selected, contact details, information already available (desk research).
- PMO approves → unit communicates with the selected entity (outreach recorded: date, channel, response; optional benchmarking code-of-conduct acknowledgement).
- → Stage = Acquire.

**3. Acquire**
- **Visit info** (before the visit): agreed visit date, implementation method (site visit | virtual meeting | questionnaire / document exchange), participants (benchmarking team — optionally a Team from §4), pre-visit questionnaire sent.
- The request is sent to the concerned unit for the next step → Stage = Analyze.

**4. Analyze**
- **Analysis form** after the visit: for each comparison topic — current practice at our organisation vs comparator practice (subject, our details, their details); non-applicable practices; other practices outside the comparison scope; **performance indicators** compared (optionally linked to KPI cards, §2); **development plan** = improvement actions (at least 1, each with owner, due date).
- PMO approves the analysis.
- PMO approval of the analysis creates a **Remarks Source in the Corrective Action Plan system (§9)** for this study, with one remark per development-plan item and proposed actions pre-filled, linked to the study and its comparison topics. They are managed, updated (status, completion %, evidence) and approved there — not in Benchmarking.
- The study shows its linked actions live (read-only, with progress). When all linked actions are completed with evidence → PMO final approval → **Completed & Closed**.

"Current status / next action" messages kept from Salesforce (add at least 1 topic, waiting PMO approval for <stage>, waiting user to submit, waiting search form, add at least 1 action, waiting visit info, waiting analysis form, completed & closed, cancelled).

### 8.3 Best-practice additions
- **Comparator register:** entities benchmarked with (sector, country, contacts, history of studies, rating of the experience) — reused across studies.
- **Best-practice library:** practices captured in analyses become searchable entries (topic, entity, description, applicability) for the whole organisation.
- **Action plan integration:** development-plan actions live in the Corrective Action Plan system; a larger improvement can instead become a Portfolio Management request (§1) or a KPI target change proposal — keeping the link back to the benchmark.
- **KPI link:** benchmark results can update the KPI Card's benchmark description / result fields (§2.3 A) after PMO approval.
- **Impact follow-up:** optional review N months after closure — were the improvements implemented and did the linked KPIs move?
- **Benchmarking team:** link a Team (§4) to the study; team evaluated on completion if configured.

### 8.4 Data model
**Benchmark Study** (`BMS-NNNN`): responsible unit, section, responsible employee, target year / quarter, comparison type, purpose, scope, request type, stage (Plan | Search | Acquire | Analysis | Completed), status (Active | Cancelled | Completed), current status message, linked team.
**Comparison Topic:** study, topic, owner, description.
**Comparator Entity** (shared register) + **Study–Entity link** (selected?, reason, outreach log).
**Visit:** study, entity, agreed date, method, participants, questionnaire, visit notes / report.
**Practice Comparison rows:** topic, subject, our practice, comparator practice, applicable? , category (compared | not applicable | outside scope).
**Indicator Comparison rows:** indicator (or KPI card link), our value, comparator value, period, source.
**Linked actions:** references to Corrective Action Plan records (source = this study / topic); no action data stored in Benchmarking.
**Best Practice** (library entry): title, description, source study / entity, topic, tags, applicability.

### 8.5 Screens
Benchmarking plan (by year / quarter / unit); study page with stage stepper and current status; forms per stage (wizards); linked actions view (from Corrective Action Plan); comparator register; best-practice library; dashboard (studies by stage / quarter / unit, actions completion, overdue actions, completed studies, practices adopted).

### 8.6 AI
Suggest comparator entities and draft the pre-visit questionnaire from the topics (using internal history; optional web research where the installation allows internet access); turn visit notes into structured practice comparisons, gap analysis and draft development-plan actions (created in Corrective Action Plan); summarise each study for leadership; search the best-practice library in natural language.

---

### 5–8 Default configuration questions
- **Q-RQ2** Sensitivity levels in use besides Confidential (default proposed: Public · Internal · Confidential · Secret).

**Decided:** single PMO step as default for all three, editable in the global Approval Settings centre; documents use expiry-by-type or valid-until-superseded (no separate review cycle); benchmark development-plan actions are managed in the Corrective Action Plan system.

---

## 9. Module: Corrective Action Plan (CAP)

Source documents: Action Plan System User Guide V1.0, Action Plan Workflow, Corrective Action Plan Data Model ERD.
Standards applied: ISO 9001:2015 §10.2 (nonconformity and corrective action) & §10.3 (continual improvement), CAPA good practice (root cause analysis, effectiveness verification), UAE Government Excellence Model (improvement opportunities).

### 9.1 Purpose
The organisation's single place for **improvement opportunities, observations and findings** from any source — workshops, benchmarking, audits, excellence assessments, KPI under-performance, risk events, surveys, complaints, management reviews — and the **corrective actions** taken on them: assignment to the responsible section, acceptance, action planning (with budget check), PMO validation, monthly implementation follow-up, evidence, completion and verification of effectiveness. Other systems create their actions here rather than tracking actions themselves.

**Changes from Salesforce (best practice):**
| Salesforce today | New system |
|---|---|
| Remarks entered manually by PM Office under a remark source | Remarks can also be **created automatically from other systems** (benchmarking development plan, audit findings, KPI misses, risk events) with a live link back to the source record |
| No root cause step | **Root cause analysis** on the remark (5 Whys / fishbone categories / free text), required by configurable rule (e.g. for audit findings and high-priority remarks) |
| Remark rejection is final by the section manager | Same by default. Optional setting (off by default): send rejections to PMO to confirm or send back |
| Completion = 100% + evidence approved | Plus optional **effectiveness verification** after N months — did the problem recur? If not effective, the remark is reopened with new actions |
| Due date changes untracked | **Extension requests** with justification and approval; original vs revised dates kept |
| No priority | Priority / severity on remarks drives due dates, reminders and escalation |

### 9.2 Actors
| Actor | Responsibilities |
|---|---|
| CAP Administrator — PM Office (role, not hard-coded) | Registers sources and remarks; validates actions; approves completion; verifies effectiveness; reports |
| Remark Owner — Section Manager / Director of the concerned unit (or the person they reassign to) | Accepts or rejects the remark (with reason); analyses root cause; defines actions; submits for validation |
| Assigned Employee (action owner) | Implements the action; monthly achievement updates; uploads evidence |
| Source systems | Benchmarking, Audit Management, Performance Management, Risk Management (and others) create remarks automatically |
| Viewers / Leadership | Dashboards and reports |

### 9.3 Data model
#### A. Remarks Source
Source number; title AR/EN; **source type** (configurable: Workshop, Benchmarking, Internal Audit, External Audit, Excellence Assessment (GEM), KPI Performance, Risk Event, Customer Complaint / Feedback, Employee Survey, Management Review, Other); description; received date; owner; **linked system record** (e.g. benchmark study `BMS-0036`, audit engagement, KPI result) when created from another system; attachments (report, assessment).

#### B. Remark (finding / improvement opportunity)
Remark number (default `CAP-{UNITCODE}-{YYYY}-{NNN}`, e.g. CAP-SPMO-2025-002); source; **improvement opportunity / observation** text AR/EN; criteria (configurable picklist, e.g. GEM criterion); type (Nonconformity | Observation | Improvement opportunity); **priority** (Low | Medium | High | Critical); concerned department; relevant section; remark owner (defaults to the section manager from the Organisation module; reassignable); assigned date; due date (default from priority); responded date; status; rejected reason (+ PMO decision, only if rejection confirmation is enabled); **root cause analysis** (method, causes, analysis text, attachments); linked source record; current status message.

#### C. Action (corrective action)
Action number (`{RemarkNo}-{n}`, e.g. CAP-SPMO-2025-002-1); corrective action / action plan details AR/EN; action type (Correction | Corrective action | Preventive action | Improvement); **budget required?**; **budget available this year?** (if required); estimated cost; related process (link to process / service, text if no process catalogue); assigned employee; planned start / end; revised end date (via approved extension); validation status (Pending | Approved | Rejected | Returned for correction) + PMO notes; linked PMS project / initiative (if the action is already part of one, or a program request was created from it); actual start / end; achieved %; status (Not Started | In Progress | Late | Completed | Cancelled); achievement updates (history: date, %, note); proof of completion (files); completion approval.
**Remark ↔ Action link (many-to-many):** one action can serve several remarks ("Is this an existing action related to another remark?" → select the action instead of creating a duplicate).

#### D. Effectiveness Verification (optional — off by default)
Remark; verification due date (e.g. completion + 3 months); verified by; effective? (Yes / No); evidence; comments. "No" → remark reopened for new actions.

#### E. Extension Request
Action; requested new end date; justification; status; approver; decision date.

### 9.4 Workflows
#### C1 — Analysis
1. PM Office creates a Remarks Source (or a source system creates it automatically) and adds remarks; each remark is assigned to the concerned section's manager → notification.
2. **Remark owner accepts or rejects** (rejection requires reason; rejection is final by default — optional PMO confirmation can be switched on). Accept / reject is the owner's response, not an approval step; **PMO is the only approval step** in CAP by default (action validation and completion approval), editable in the global Approval Settings centre. The owner may reassign the remark to another employee in their unit.
3. Root cause analysis captured (mandatory where the rule requires it).
4. Accepted → Stage = Corrective Action.

#### C2 — Corrective Action (planning)
1. Owner adds one or more actions — either a **new action** (details, type, budget required?, budget available?, related process, implementation dates, assigned employee) or **link an existing action** already serving another remark.
2. Owner submits for validation.
3. **PM Office validates each action:** Approve · Reject · Return for correction (with notes). If the action is already part of a project / initiative, PMO selects the PMS project and links it.
4. **Budget routing** (per action):
   - Budget not required, or required and available → Stage = Implementation.
   - **Budget required but not available** → system creates a **draft Program Request in Portfolio Management** (pre-filled from the action and remark; Source of Suggestion = "Action Plan"), linked to the action. The action follows the request / project from then on (its progress can mirror the project's progress — configurable).

#### C3 — Implementation
1. **Monthly notifications** (configurable day) to assigned employees with each action's status (Not Started, In Progress, Late).
2. Assigned employee updates achievement (%, actual dates, note); each update kept in history. Past planned end and < 100% → status Late, escalation to remark owner, then PMO.
3. Need more time → extension request → approval (default: PMO).
4. At 100% → upload evidence → **PMO approval** → action Completed.
5. All actions of a remark completed → remark Completed (or *Awaiting effectiveness verification* if enabled) → verification → Closed, or Reopened if not effective.
6. Remark completion is reported back to the source record (e.g. benchmark study, audit finding, KPI) through its live link.

**Current status messages** (kept from Salesforce, extended): waiting user to accept / reject remark · waiting user to fill actions and submit for approval · waiting PM Office approval · please correct and resubmit · actions approved · actions in progress · actions rejected · waiting effectiveness verification · remark completed · remark cancelled.

### 9.5 Integrations
| System | Integration |
|---|---|
| Benchmarking (§8) | PMO approval of the analysis creates a Remarks Source (type Benchmarking) for the study and one remark per development-plan item, with proposed actions pre-filled; the study shows the actions' live status and closes when they're all completed (§8.2) |
| Portfolio Management (§1) | Budget-needing actions create draft Program Requests; actions can link to existing projects / initiatives |
| Performance Management (§2) | KPI results below threshold (or red for N periods) can raise a remark (manual one-click or automatic — configurable); remark shows the KPI trend |
| Risk Management (§3) | Risk events and ineffective controls can raise remarks; corrective actions can be linked as risk treatment |
| Audit Management (§10) | Audit recommendations are followed up inside Audit Management by the Audit Office only; optionally, improvement-type audit observations can be sent here as remarks |
| Teams (§4) | A team can be linked to a remark source (e.g. a workshop committee) |

### 9.6 Screens
- **CAP dashboard:** remarks by source type, department, status, priority; actions by status (Not Started / In Progress / Late / Completed); overdue and ageing analysis; closure rate per unit; budget-dependent actions and their program requests; effectiveness results.
- **Sources** list → source page with its remarks.
- **Remark page:** stage stepper (Analysis › Corrective Action › Implementation › Verification › Closed), current status banner, root cause, actions (with progress), linked source record, history.
- **Validate Actions** screen (PMO): all actions of a remark side by side, approve / reject / return per action, link project.
- **My actions** (assigned employees): quick monthly update with evidence upload.
- **Reports:** action plan register by source (e.g. "all benchmarking actions 2026"), departmental action plan, overdue report — bilingual PDF / Excel.

### 9.7 Access (defaults, configurable — §0.2)
| Role | Scope | Permissions |
|---|---|---|
| CAP Administrator (PM Office) | All | Sources, remarks, validation, completion approval, verification, settings, reports |
| Remark Owner (record role) | Assigned remarks | Accept / reject, reassign, root cause, actions, submit |
| Assigned Employee (record role) | Assigned actions | Update achievement, evidence, extension request |
| Department Manager | My unit and below | View unit remarks and actions |
| Source-system roles | Linked records | e.g. Benchmarking Coordinator sees the actions of their study |
| Leadership / Viewer | All | Dashboards and reports |

### 9.8 AI features — CAP
- **Remark extraction:** upload a workshop report, assessment report, audit report or benchmark analysis → AI proposes the list of remarks (observation, criterion, suggested department, priority) for PMO to confirm.
- **Root cause assistant:** guides the owner through 5 Whys and suggests likely causes.
- **Action suggestions & duplicate check:** proposes actions for a remark and finds existing actions that already address it (to link instead of duplicating).
- **Action quality check for PMO:** flags vague actions, unrealistic dates or missing owners before validation.
- **Progress summary:** monthly bilingual summary of CAP status for leadership; highlights chronic late units.
- **MCP tools:** my remarks, my actions, update achievement (draft), remarks by source, overdue actions.

### 9.9 Default configuration questions
**Decided:** PMO is the only approver by default (action validation, completion); section manager accept / reject is a response and rejection is final by default (optional PMO confirmation setting, off); effectiveness verification available but **off by default**; default due dates by priority — Critical 30 days, High 60, Medium 90, Low 180 (editable).

---

## 10. Module: Audit Management

Source documents: Audit Management Technical Specification V1.0 (draft), Audit Management Admin User Guide V1.0, Audit Portal User Guide V1.0, Audit Process Flow, Audit Data Model ERD.
Standards applied: IIA Global Internal Audit Standards (2024), ISO 19011 (auditing management systems), Three Lines model.

### 10.1 Purpose & scope
Today the Salesforce system covers **observation follow-up**: the Audit Office records findings and recommended actions, departments report progress with evidence through a separate portal, and the Audit Office reviews each update until the finding is closed.

**Operating model (confirmed):** the system is **operated only by the Audit Office**. The Audit Office follows up with each department outside the system and records the status, progress, evidence and comments itself. **Department users are read-only** — they see the audits and findings of their own section / department, with status and Audit Office comments, and take no actions in the system.

The new module is built in two layers, both in the single workspace (the separate Audit Portal is replaced; department users use the same login / SSO as for every other system, read-only):
- **Layer 1 — Findings & follow-up (core, replaces Salesforce).** Always on.
- **Layer 2 — Audit lifecycle (best practice, switchable per installation).** Audit universe, risk-based annual plan, engagements, programmes and working papers, reports. Organisations that only track findings leave it off.

**Changes from Salesforce (best practice):**
| Salesforce today | New system |
|---|---|
| Separate Audit Portal with its own credentials; department managers update progress there | Single workspace; department users get **read-only** access to their own findings; all updates are recorded by the Audit Office |
| "Special report" picklist groups findings | **Audit Report / Engagement** record (internal audit, external audit, special report, revenue audit…) holds its findings, dates, report file and distribution |
| Review stored as an "approval status" text on each update row | Follow-up updates recorded by the Audit Office with status and comment; optional internal review step (e.g. auditor → Chief Audit Executive) via the approval engine, off by default |
| Implementation plan typed by the Audit Office | Same — the Audit Office records the department's implementation plan and (optionally) the management response it received (Agree / Partially agree / Disagree) |
| Due date changes untracked | Audit Office records revised due dates with reason; original and revised dates kept |
| No way to record "we won't implement" | Audit Office can mark a recommendation **Risk Accepted by management** with justification and reference (e.g. memo); reported separately |
| Up to 3 evidence files | Unlimited evidence files, previewable in-page |
| Findings stand alone | Findings carry condition / criteria / cause / effect, a risk rating, and links to risks, controls, documents and repeat findings |

### 10.2 Actors
| Actor | Responsibilities |
|---|---|
| Audit Office (Chief Audit Executive / auditors) — role | Creates reports and findings, recommendations; reviews responses and progress; closes findings; reports to leadership / audit committee |
| Responsible department (managers / staff of the audited unit) | **Read-only:** view their unit's audit reports, findings, recommendations, status, completion and Audit Office comments |
| Audit Committee / Leadership | Reviews dashboards and overdue / high-priority findings |
| Viewer (e.g. Strategy Office, Risk Office) | Read-only per configuration |

### 10.3 Layer 1 — Findings & follow-up

#### A. Data model
**Audit Report (engagement / source):** report number; title AR/EN; type (Internal Audit | External Audit — e.g. State Audit Institution | Special Report | Revenue Audit | Consultancy | Other — configurable); auditor / audit body; audited units; period covered; report date; report file; status (Draft | Issued | Under follow-up | Closed); distribution list.
**Finding / Observation** (`AR-NNNN` default): audit report; finding title AR/EN; **condition** (what was found = description); **criteria** (requirement / policy breached — optional link to a Document Registry document); **cause**; **effect / impact**; finding date; responsible department (Organisation module); priority / risk rating (configurable, e.g. Low | Medium | High | Critical); repeat finding? (+ link to earlier finding); linked risks / controls (Risk Management); expected closing date; actual closed date; year (computed); status (Open | Late | Closed | Risk Accepted | Cancelled — computed + manual cancel with reason); overall completion % (computed from recommendations).
**Recommendation / Action** (`AA-NNNN`): finding; recommended action (auditor); management response received (optional: Agree | Partially agree | Disagree + comment); implementation plan (as agreed with the department); department contact (employee, optional); implementation (due) date — entered per recommendation, **no default**; revised due date + reason; status (Not Started | Continuing | Late | Completed | Risk Accepted | Cancelled); latest approved completion %; completed date; notes.
**Progress Update** (`AAQ-NNNN`): recommendation; follow-up date; achieved date; achieve %; actions implemented (as reported by the department); evidence (text + files obtained from the department); next follow-up date (required if < 100%); follow-up comment (Audit Office); recorded by (auditor); optional internal review status.
**Due-date revision / Risk acceptance log:** recommendation; type; justification; reference; new date; recorded by; date.

#### B. Workflow (all steps performed by the Audit Office)
1. **Record:** create the audit report and its findings (form, **Excel import**, or AI extraction from the report file) with recommendations, implementation plan and due date per recommendation → responsible department notified (read-only link).
2. **Follow up:** the Audit Office follows up with the department (meeting, email, letter) and **records each follow-up**: achieved date, %, what was implemented, evidence received, next follow-up date if < 100%, follow-up comment. Reminders to the responsible auditor before each next follow-up date and before due dates.
3. **Late:** past due date and < 100% → status Late; shown on dashboards and the department's view.
4. **Revise / accept risk:** the Audit Office records a revised due date (with reason) or marks a recommendation Risk Accepted by management (with justification and reference).
5. **Complete:** follow-up at 100% → recommendation Completed → when all recommendations are Completed / Risk Accepted / Cancelled → finding **Closed**, actual closed date set → when all findings are closed → audit report Closed.
6. **Cancel:** finding or recommendation cancelled with reason (excluded from follow-up, kept in history).
7. Department users are notified (optional, configurable) when a finding concerning them is created, updated or closed.

#### C. Screens
- **Audit dashboard (Audit Office / leadership):** findings by status, year, department, priority, report type; open vs closed trend; overdue / late findings and ageing; closure rate by department; repeat findings; risk-accepted findings. Filters: department, status, report, year, priority.
- **Department view (read-only, replaces the portal dashboard):** my unit's audits — total, closed, active; active findings with status, completion and Audit Office comments; due-soon list.
- **Finding page:** header (number, priority, status, completion), condition / criteria / cause / effect, recommendations with progress history timeline, evidence viewer, linked risks / documents, history.
- **Record follow-up** form and **follow-up calendar** (Audit Office): what is due for follow-up this week / month, by auditor.
- **Reports:** findings register per report, department follow-up report, revenue audit full report (JS-0097), audit committee pack — bilingual PDF / Excel.

### 10.4 Layer 2 — Audit lifecycle (switchable)
- **Audit universe:** auditable entities (units, processes, services, systems, projects) with inherent risk rating — pulled from the Organisation module and Risk Management.
- **Risk-based annual audit plan:** engagements prioritised by risk and last-audit date; approval of the plan (default: audit committee / DG); quarterly status.
- **Engagements:** objectives, scope, audit team (Teams module, §4), timeline, budget hours; stages Planning → Fieldwork → Reporting → Follow-up.
- **Audit programme & working papers:** test steps / checklists, sample, results, evidence, reviewer sign-off (working papers visible to the Audit Office only).
- **Reporting:** draft report → management responses recorded → final report issued → findings move to follow-up.
- **Quality (QAIP):** post-engagement client satisfaction survey; internal audit KPIs (plan completion, report timeliness, finding closure rate) — can be published as KPIs in Performance Management (§2).

### 10.5 Integrations (proposed)
| System | Link |
|---|---|
| Organisation / Employees | Responsible departments, department managers, action owners, audit team |
| Risk Management (§3) | Findings linked to risks and controls; a finding can trigger risk reassessment and lowers the linked control's effectiveness; risk register feeds the risk-based audit plan; shared "Risk & Audit" report page (JS-0128) |
| Corrective Action Plan (§9) | Audit recommendations are followed up **inside Audit Management** by default, so the Audit Office (not PMO) validates them — this keeps audit independence. Optional: improvement-type observations (not formal findings) can be sent to CAP as remarks |
| Document Registry (§6) | "Criteria" can link to the policy / procedure breached; audit reports can be stored as controlled documents |
| Performance Management (§2) | Internal audit KPIs (closure rate, overdue findings) can be fed automatically |
| Teams (§4) | Audit team for an engagement (Layer 2) |

### 10.6 Access (defaults, configurable — §0.2)
| Role | Scope | Permissions |
|---|---|---|
| Audit Office | All | Reports, findings, recommendations, reviews, extensions, closure, import, settings |
| Department users (managers / staff) | Own section / department (configurable: section only, or department and below) | **Read-only**: audit reports, findings, recommendations, status, completion, Audit Office comments; no actions |
| Audit Committee / Leadership | All | Read, dashboards |
| Viewer | Configurable | Read-only (e.g. Risk Office sees findings linked to risks) |
Audit data is confidential by default: departments never see other departments' findings; working papers are Audit-Office-only.

### 10.7 AI features — Audit
- **Report-to-findings extraction:** upload an audit report (internal or external, AR/EN) → AI proposes findings with condition / criteria / cause / effect, recommendations, responsible departments and suggested priority for the Audit Office to confirm (replaces manual entry / Excel import).
- **Evidence review:** checks whether the evidence received supports the progress being recorded against the recommendation and drafts the follow-up comment for the auditor.
- **Repeat-finding detection:** flags findings similar to earlier ones in the same or other departments.
- **Audit committee summary:** bilingual narrative of status, overdue high findings, trends and risk acceptances.
- **MCP tools:** findings by department, finding details, follow-ups due, overdue findings (Audit Office); read-only finding status for department users.

### 10.8 Decisions
- System operated only by the Audit Office; department users read-only for their own section / department.
- Recommendations are followed up inside Audit Management by the Audit Office (not in Corrective Action Plan).
- No default due dates — set per recommendation. Priority levels configurable (default Low / Medium / High / Critical).
- Layer 2 (full audit lifecycle) is built as a switchable feature, **off by default**.

---

## 11. Module: Task Management (shared core service)

Source documents: Task Management Technical Specification V1.0, Task Management User Guide V2.0, Task Management Admin User Guide V1.0, Task Process Flow, Task Data Model ERD.

### 11.1 Purpose
One task engine for the whole suite. Managers assign work to employees and approve it; employees accept, work (with time tracking, comments, files, follow-up employees and sub-tasks) and submit. Tasks can stand alone **or be linked to a record in any system** (project, milestone, team / committee, meeting decision, risk, CAP remark, document, benchmark study, audit finding…). Portfolio Management's milestone tasks (§1.16), agile user-story tasks (§1.13) and team tasks (§4.4 C) all use this module — there is one Task object, one "My Tasks" list and one set of rules.

**Changes from Salesforce (best practice):**
| Salesforce today | New system |
|---|---|
| Separate Task Portal with its own credentials | Single workspace; tasks appear in each user's My Work |
| One assignee per task | **Multi-assign:** choosing several assignees creates one task per person (independent lifecycle), grouped so the creator sees combined progress (§11.4) |
| Tasks link only to a DOF Team ("Team Subject") | Tasks link to **any record in any system** (generic link) |
| Accounts Department needed a cloned "Internal Task" object (JS-0104) | No clones: configurable **task categories / sources** per department, with their own visibility rules |
| One fixed lifecycle | **Workflow templates per task type:** *Full* (accept → work → submit → approve — the Salesforce lifecycle) and *Simple* (To Do → In Progress → Done, no acceptance / approval) |
| Legacy workflow rules (2018 / 2023) | Removed; reminders and escalations configured in Settings |

### 11.2 Actors
| Actor | Responsibilities |
|---|---|
| Creator (assigner — manager / task owner) | Creates and assigns tasks; reassigns, cancels, approves with a feedback point |
| Assignee | Accepts or rejects (with reason); works; pauses / restarts; creates sub-tasks; adds follow-up employees; submits |
| Follow-up employee | Given access by the assignee or the department secretary; adds comments and files |
| Department secretary | Can add follow-up employees to tasks of their department; can create tasks on behalf of the manager (configurable) |
| Viewers | Creator's / assignee's manager (configurable: see team's tasks); administrators |

### 11.3 Lifecycle — *Full* workflow (default, matches Salesforce)
| From | Action | By | To | System |
|---|---|---|---|---|
| (new) | Create & assign | Creator | Assigned | Notify assignee (email + in-app) |
| Assigned | Accept | Assignee | In Progress | Starting = now; timesheet period opened |
| Assigned | Reject (reason required) | Assignee | Rejected | Notify creator |
| Rejected | Reassign | Creator | Assigned | New assignee notified |
| Rejected | Cancel | Creator | Cancelled | Closed |
| In Progress | Pause (hold reason) | Assignee | On Hold | Timesheet period closed |
| On Hold | Start | Assignee | In Progress | New timesheet period |
| In Progress | Submit (completion comments, files) | Assignee | Submitted | Completion = now; notify creator |
| Submitted | Approve (manager feedback point) | Creator | Approved | Approved = now |
| Submitted | Return / reassign (comment) | Creator | Assigned or In Progress | Notify assignee |
| Submitted / any open | Cancel (reason) | Creator | Cancelled | — |
| In Progress / On Hold | Create sub-task | Assignee | (sub-task Assigned) | Sub-task linked to parent |

- **Draft** status available for tasks prepared but not yet assigned.
- *Simple* workflow: To Do → In Progress → Done (→ Cancelled); optional pause; no acceptance or approval. Status names configurable per template.
- Which template applies is set per **task type** (e.g. Manager assignment = Full; Project / milestone task = configurable per installation; personal to-do = Simple).
- A parent task cannot be approved / done while it has open sub-tasks (warning; creator may close or cancel them).

### 11.4 Multi-assignment
- In the New Task form the creator can pick **several employees** (or a whole unit, or a team's members).
- The system creates **one task per assignee** (e.g. 2 assignees → 2 tasks), each with its own status, timesheet, comments, submission and approval. Shared content (title, description, attachments, due date, priority, link) is copied to each.
- The tasks are tied by an **Assignment Group** record: the creator sees one group view with each assignee's status and overall progress ("3 of 5 approved"), and can message all, extend the due date for all, or cancel all in one action.
- Optional group setting: "Group complete when **all** / **any one** is approved".

### 11.5 Data model
**Task** (`TASK-NNNNN`): title AR/EN; description; task type (drives workflow template); task source (configurable picklist, e.g. Manager Assignment, DG Office, Committee decision, Project…); category / department scope; priority (High | Medium | Low); assigned to (employee); assigned by (creator); on behalf of (if created by a secretary); assignment group (if multi-assigned); parent task (sub-tasks); **linked record** (system + record, e.g. PMS project / milestone, Team, meeting decision, CAP remark…); planned start; due date; status; status indicator (on time / due soon / overdue / on hold); starting / completion / approved date-times; rejection reason; hold reason (on timesheet); completion comments; manager feedback point (configurable scale, e.g. 1–5) + comment; % complete (optional); estimated effort (h); actual effort (from timesheets); time to accept / time to complete (computed); checklist items; dependencies (optional, for project tasks); recurrence rule (optional).
**Assignment Group:** title, creator, created date, completion rule, member tasks.
**Timesheet period** (`TS-NNNNNNN`): task; start; end; duration; hold reason.
**Comment / communication:** task; text; attachments; @mentions; sent by; sent at.
**Follow-up employee:** task; employee; added by; access (comment / upload).
**Attachments:** task files (versioned).

### 11.6 Integration with other systems
- **Generic link:** any system can register record types as "task-able". From such a record users can create tasks (single or multi-assign); the record shows its tasks with status; the task shows a link back. Closing / cancelling the source record can prompt to close open tasks.
- **Portfolio Management:** tasks under milestones (§1.16) and **project-level tasks not tied to a milestone** (e.g. preparing a steering committee pack); agile user-story tasks (§1.13). PMS task progress can inform milestone progress (task-driven option, §1.16).
- **Teams & committees (§4):** tasks from meeting decisions / recommendations, assigned to members; the committee's open tasks and recommendations closure % feed the team evaluation.
- **Other systems:** e.g. a task to prepare evidence for a KPI result, gather information for a benchmark study, or draft a document revision.
- **Standalone:** personal and manager-to-employee tasks with no linked record.

### 11.7 Screens
- **My Tasks** (inside My Work): tabs *Assigned to me* / *Created by me* / *Follow-up* / *Team's tasks* (managers); counts of open, overdue, submitted-awaiting-my-approval; list, **Kanban** (by status), **calendar** views; quick actions (accept, start, pause, submit, approve).
- **New Task** form: title, type, source, priority, one or many assignees, dates, description, files, linked record, checklist, recurrence.
- **Task page:** header (ID, status, priority, due, assignee, creator), description, checklist, sub-tasks, **timeline** (assigned → accepted → paused / restarted → submitted → approved, with durations), comments, files, follow-up employees, linked record, history.
- **Assignment group page** (multi-assign).
- **Dashboard:** open / overdue tasks by department, source, status, priority; average time to accept and to complete; on-time completion rate; average feedback point; workload per employee.

### 11.8 Reminders & escalation (Settings)
Reminder before due date (e.g. 2 days); overdue notice to assignee and creator; escalation to the assignee's manager after N days overdue; reminder to accept if still *Assigned* after N hours; daily / weekly digest email option.

### 11.9 Access (defaults, configurable — §0.2)
| Role | Scope | Permissions |
|---|---|---|
| Any employee | Own | Create tasks (to whom: anyone / own unit only / direct reports — configurable); personal tasks |
| Creator (record role) | Tasks they created | Edit, reassign, cancel, approve, feedback point |
| Assignee (record role) | Their tasks | Accept / reject, pause / start, sub-tasks, follow-up employees, comments, submit |
| Follow-up employee (record role) | Tasks they're added to | Comment, upload |
| Department secretary | Own unit | Add follow-up employees; create on behalf of the manager |
| Manager | Direct reports / unit (configurable) | View team's tasks and dashboard |
| Linked-record roles | Linked tasks | e.g. PM sees all tasks of the project |
| Administrator | All | Settings, task types, sources, templates |
Confidential task option: visible only to creator, assignee and follow-up employees.

### 11.10 AI features — Tasks
- **Tasks from text:** turn meeting minutes, an email or a memo into a list of tasks with suggested assignees, due dates and priorities for the creator to confirm.
- **Assignee suggestion:** based on unit, skills, current workload and past similar tasks.
- **Task summary:** summarise a long comment thread; draft the submission note or the return comment.
- **Workload insights:** overloaded employees and chronic overdue patterns for managers.
- **MCP tools:** my tasks, create task (single / multi-assign), update status (accept, start, pause, submit — approve only if enabled), add comment.

### 11.11 Employee performance input
Time to accept, on-time completion rate and feedback points per employee are stored for the future **employee performance section** (§0.1) — shown only to the employee, their manager and authorised HR roles.

### 11.12 Decisions
- Project, milestone and project-level tasks use the **Full** workflow by default (Simple stays available per task type).
- The approver of a task is always its **creator** (not a fixed role). In projects the PM normally creates tasks and therefore approves them; any project member allowed to create tasks (§1.9) approves the tasks they create. The PM can see and follow all tasks linked to the project.

---

## Appendix A — Build defaults for open configuration items

Every item below is **configurable in Settings**. Where the customer hasn't confirmed a value, Claude Code implements the configuration and seeds the default shown. Each organisation can change them after installation.

| ID | Item | Build default |
|---|---|---|
| Q-P1 | Monthly milestone update approval | Department Director → PM Office (BRD V1.1) |
| Q-P2 | Initiative lifecycle | Request → Planning → Execution → Completed. Initiatives keep Take Ownership (to assign the PM) and the Planning approval chain; they **skip** charter approval, LPO, team formation, closure approval and Impact. When total achievement = 100%, PM Office confirms → Completed. Stage templates per classification are configurable |
| Q-P3 | After project closure approval | → Impact stage (BRD). Impact stage can be switched off per installation (closure approval → Completed) |
| Q-P4 | Conditional approvers | Section Manager step **included** when the record's unit is a section with a manager different from the Department Director and from the submitter; otherwise skipped. Change Requests: Finance Service Section step when the change affects cost or budget; GM Office step when change type is Cancellation or Merge, or the cost change exceeds a configurable amount. All conditions editable in the Approval Settings centre |
| Q-P5 | Project vs Initiative classification | **Project** when all screening answers are Yes **and** estimated budget > threshold (default AED 50,000); otherwise **Initiative**. PM Office can override with a reason |
| Q-P6 | Formulas | Evaluation Score = average of the four 1–5 criteria ÷ 5 × 100 (equal weights, weights configurable). Priority bands: High ≥ 80%, Medium 60–79%, Low < 60%. Project risk scoring uses the shared Risk framework (§3.3). Project KPI Goal Status uses the KPI performance calculator (§2, K5) with the RAG bands. Progress Status = **Delayed** if approved progress is more than 5 points (tolerance configurable) below planned-to-date progress or any milestone is past its planned end below 100%; otherwise **On Track** |
| Q-P7 / Q-K6 | Picklist values | Seed a neutral, generic set (strategic objectives empty; common categories such as risk categories, KPI types, units, document types). Provide Excel import templates so each organisation (incl. ADoF from its Salesforce values) loads its own lists — master data only, no transactional migration |
| Q-P8 | Reports & dashboards | As specified in §0.4 and each module's Screens section |
| Q-P9 | Applying an approved change request | Semi-automatic: after approval the PM opens "Apply changes", pre-filled from the change request (new PM / owner, dates, budget, scope, re-baseline); PM confirms → system applies and logs each change → status Changes Implemented |
| Q-K2 | KPI period windows | Opens the day after the period ends; due on the 15th day. Q1 (Jan–Mar) opens 1 Apr, due 15 Apr; H1 opens 1 Jul; H2 / Full Year open 1 Jan of the following year, due 31 Jan. Late submission allowed (flagged) |
| Q-R3 | Risk appetite per category | Empty — each organisation sets it |
| Q-T5 | Team formation approval & reward matrix | Formation: Department Director of the owning unit → General Manager. Reward matrix seeded empty (administrator fills amounts) |
| Q-RQ2 | Document sensitivity levels | Public · Internal · Confidential · Secret |

## Open questions
- None. New requirements will be added as further modules or changes to this document.
