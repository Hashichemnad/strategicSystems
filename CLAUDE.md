# CLAUDE.md — Integrated Strategy System

This file tells Claude Code **how** to build this product. **What** to build is in `docs/SPEC.md` (the source of truth). **In which order** is in `docs/BUILD_PLAN.md`.

## 1. What this product is

A self-hosted, bilingual (Arabic / English, full RTL) **integrated strategy management suite**, sold one-time to government and enterprise organisations. Each customer installs it on their own servers. One application, one database, one login, containing 11 systems on a shared platform:

| # | System | Spec |
|---|---|---|
| Core | Organisation, Employees / Users, Access control, Approval engine, Audit trail, Notifications, Files, Settings, Dashboards, AI, MCP | SPEC §0 |
| 1 | Portfolio Management (incl. Agile / Hybrid, Wasel vendor portal) | SPEC §1 |
| 2 | Performance Management (KPI) | SPEC §2 |
| 3 | Risk Management | SPEC §3 |
| 4 | Teams Management (teams, committees, evaluation) — shared | SPEC §4 |
| 5 | Request Framework — shared engine for 6–8 | SPEC §5 |
| 6 | Document Registry | SPEC §6 |
| 7 | Intellectual Property Registry | SPEC §7 |
| 8 | Benchmarking | SPEC §8 |
| 9 | Corrective Action Plan | SPEC §9 |
| 10 | Audit Management | SPEC §10 |
| 11 | Task Management — shared | SPEC §11 |

The original systems ran on Salesforce. They are a **functional reference, not a blueprint**. The SPEC already records where best practice replaces the Salesforce behaviour; follow the SPEC.

## 2. Non-negotiable rules

1. **Read before you build.** Before any work on a module, read its SPEC section, SPEC §0 (global), and SPEC Appendix A (defaults). Restate the scope in a short design note (`docs/design/<module>.md`) and get it right before writing code.
2. **Nothing organisation-specific in code.** No department names, approval chains, picklist values, thresholds, numbering formats, email texts or labels hard-coded. They are configuration (database + Settings UI) with seeded defaults. Code uses stable internal **codes**; humans see translatable **labels**.
3. **Bilingual and RTL everywhere.** Every user-visible string comes from translation resources (`ar` and `en`). Every screen, email, PDF and export must work and look right in both directions.
4. **Security is checked on the server, every time.** Every endpoint, background job, API call and MCP tool goes through the same access service (§7). The UI hiding a button is never the protection.
5. **Everything is audited.** All create / update / delete on business data is captured automatically by the audit interceptor (§8). Never bypass EF Core change tracking for business writes (no raw `UPDATE` / `DELETE` SQL on business tables).
6. **AI drafts, people decide.** AI never approves, rejects, submits or saves on its own. AI output is always a draft the user accepts (§12).
7. **Only free / OSI-licensed dependencies** (MIT, Apache-2.0, BSD, MPL, ISC, OFL for fonts). This product is sold; a paid licence would be a liability. Check §4.3 before adding any package.
8. **Tests prove it works.** A feature is not done until its tests pass, including authorization tests that prove the wrong user is refused (§16).
9. **When the SPEC is ambiguous**, do not invent business rules silently. Use the default in SPEC Appendix A if one exists; otherwise choose the safest configurable option, record it as an ADR in `docs/decisions/`, and list it in the phase summary.
10. **Keep to the build plan.** Finish and verify one phase before starting the next. Do not build features of later phases "while you're there".

## 3. Repository layout

```
/                         CLAUDE.md, README.md, Directory.Build.props, Directory.Packages.props, .editorconfig
/docs/SPEC.md             requirements (source of truth)
/docs/BUILD_PLAN.md       phases, prompts, acceptance checks, progress checklist
/docs/design/             one design note per module (written before coding)
/docs/decisions/          ADRs (NNNN-title.md)
/src/Host/                Strategy.Host — ASP.NET Core host: DI composition, middleware, auth schemes, serves the SPA
/src/Platform/            Strategy.Platform.* — shared kernel and platform services (one project each, see §5)
/src/Modules/             Strategy.Modules.<Name> + Strategy.Modules.<Name>.Contracts per system
/web/                     React + TypeScript SPA (Vite)
/tests/                   Unit, Integration (Testcontainers), Architecture, E2E (Playwright)
/deploy/                  docker-compose.yml, Dockerfiles, .env.example, install / backup / upgrade scripts
/tools/                   seeders, import templates, dev scripts
```

## 4. Technology stack

### 4.1 Back end
- **.NET 10 (LTS)**, C# latest, `Nullable` enabled, `TreatWarningsAsErrors=true`, central package management (`Directory.Packages.props`).
- **ASP.NET Core** Web API (minimal APIs grouped per module, or controllers — pick one in Phase 0 and use it everywhere).
- **EF Core** with **Npgsql** on **PostgreSQL 16+**. Migrations per module.
- **FluentValidation** for command validation.
- **Quartz.NET** (persistent ADO store in PostgreSQL) for scheduled jobs; **transactional outbox** table for integration events, emails and other reliable async work.
- **SignalR** for in-app notifications and live counters.
- **ASP.NET Core Identity** (local users, lockout, TOTP + email OTP MFA), **OpenID Connect** handler (Entra ID / ADFS / UAE Pass), **Sustainsys.Saml2** (SAML 2.0), **System.DirectoryServices.Protocols** (LDAP / AD). **OpenIddict** (Apache-2.0) as the built-in OAuth 2.1 authorization server for MCP clients (and future API clients).
- **Microsoft.Extensions.AI** (`IChatClient`) with providers: **Anthropic** official C# SDK (`Anthropic` NuGet), **OpenAI / Azure OpenAI**, OpenAI-compatible endpoint for self-hosted models (Ollama / vLLM).
- **ModelContextProtocol.AspNetCore** (official MCP C# SDK) — Streamable HTTP transport.
- **MailKit** (SMTP), **ClosedXML** (Excel), **Serilog** (structured logs), **OpenTelemetry** (traces / metrics).
- **NCalc** (or equivalent MIT expression evaluator) for admin-defined formulas (risk scoring, reward rules, approval entry conditions) — sandboxed, whitelisted variables only.
- PDF: HTML templates rendered to PDF by **Gotenberg** (headless Chromium) sidecar container — correct Arabic shaping; fonts: Noto Naskh Arabic / Noto Sans Arabic / IBM Plex Sans Arabic (OFL).

### 4.2 Front end
- **React 18+ with TypeScript** (`strict: true`), **Vite**.
- **TanStack Query** (server state), **TanStack Router** or React Router (pick one in Phase 0), **react-hook-form + zod** (forms / validation), **i18next** (translations).
- **Tailwind CSS** using **logical properties only** (`ms-/me-/ps-/pe-/start-/end-`, never `ml-/mr-/left-/right-` for layout) + **Radix UI** primitives (with `DirectionProvider`) for accessible components; build an in-house component library in `/web/src/ui` (shadcn-style, owned code).
- **Apache ECharts** (charts, heat-maps; RTL-tested), **dnd-kit** (Kanban / drag-and-drop), **Tiptap** core (rich text, MIT), **pdf.js** (in-page document / evidence preview), **DOMPurify** (sanitise any rendered HTML).
- Gantt: build a read-only SVG Gantt component in-house (milestones, phases, quarters, planned vs actual, RTL mirrored). Do not add a commercial Gantt.

### 4.3 Licensing guardrails (sold product)
- Allowed: MIT, Apache-2.0, BSD, ISC, MPL-2.0, Unlicense, OFL (fonts). Anything else → stop and write an ADR asking the owner.
- **Do not use** (commercial or restrictive licences for this use): MediatR (v13+), AutoMapper (v15+), FluentAssertions (v8+), MassTransit (v9+), QuestPDF (community licence limits), Hangfire Pro, Telerik / DevExpress / Syncfusion / Kendo, AG Grid Enterprise, Highcharts, DHTMLX, Bryntum, Duende IdentityServer (commercial licence).
- Use instead: a small in-house dispatcher / handler pattern, explicit mapping methods, **Shouldly** for test assertions, Quartz.NET + outbox, Gotenberg for PDFs, ECharts, in-house Gantt.
- Keep `docs/THIRD_PARTY_LICENSES.md` updated whenever a dependency is added.

### 4.4 Infrastructure (docker-compose for dev and customer installs)
`app` (Host + SPA) · `postgres` · `gotenberg` · `mailpit` (dev only) · optional `meilisearch` (Arabic full-text inside documents) · optional `clamav` (virus scan) · optional `ollama` (local AI). File storage: local volume by default, S3-compatible (MinIO) optional.

## 5. Architecture — modular monolith

One deployable, strictly modular inside.

- **Platform projects** (`src/Platform/`):
  `Kernel` (base entity, result / error types, clock, IDs, domain events, paging, dispatcher) · `Persistence` (module DbContext base, EF conventions, migrations runner) · `Http` (module contract, endpoint conventions, Problem Details) — ADR 0006 · `Identity` (users, employees link, auth providers, MFA, API tokens) · `Organisation` (units, hierarchy with history, employees) · `Access` (permissions, roles, record roles, data scope, system ownership) · `Workflow` (approval engine, delegation, current-status engine) · `Audit` (interceptor, hash chain, audit queries) · `Notifications` (templates, email, in-app, SignalR, outbox dispatch) · `Files` (storage, versions, scanning, preview) · `Settings` (typed settings, lookups / picklists, numbering, branding, feature flags, localisation overrides) · `Scheduling` (Quartz jobs) · `Reporting` (dashboard widgets, aggregates, exports, PDF rendering) · `Links` (generic cross-system links registry: "team-able", "task-able", "remark-source" records) · `AI` (provider registry, feature routing, prompt templates, usage log) · `Mcp` (MCP server, tool registry) · `Search`.
- **Module projects** (`src/Modules/<Name>/`): one project per system with folders `Domain/`, `Application/` (commands, queries, handlers, validators, DTOs), `Infrastructure/` (DbContext, configurations, migrations), `Api/` (endpoints), `Jobs/`, `Ai/`, `Mcp/`. Plus `<Name>.Contracts` (public interfaces, DTOs and integration events other modules may use).
- **Boundaries (enforced by architecture tests):**
  - A module may reference Platform projects and other modules' `.Contracts` only — never another module's internals or DbContext.
  - Each module owns its **PostgreSQL schema** (`portfolio`, `kpi`, `risk`, `teams`, `requests`, `docreg`, `ip`, `bench`, `cap`, `audit_mgmt`, `tasks`; platform schemas `core`, `access`, `workflow`, `audit`, `notify`, `files`, `settings`, `reporting`, `ai`). No foreign keys across module schemas; reference other modules by ID and validate through Contracts.
  - Cross-module reactions use **integration events** via the outbox (e.g. `ProjectClosureApproved` → Teams opens the project-team evaluation; `BenchmarkAnalysisApproved` → CAP creates remarks; `KpiResultApproved` → Risk checks KRI thresholds).
  - Synchronous cross-module reads use Contracts interfaces (e.g. `ITeamsDirectory.GetTeam(id)`).
- **Stage / state machines** for each record lifecycle are explicit code with guards (one class per lifecycle, unit-tested transition tables). Approvers and approval steps are **configuration** in the approval engine; stages themselves are code unless the SPEC says they are configurable (Request Framework types, task workflow templates, initiative stage template, KPI period windows).

## 6. Data conventions

- Primary keys: `Guid` v7 (`Guid.CreateVersion7()`). Human-readable numbers (e.g. `PR-0001`, `PROJ-SPMO-2026-001`, `CAP-SPMO-2025-002-1`) come from the **Numbering service** using configurable formats; unique index per format scope; generated inside the same transaction.
- Every business table: `created_at`, `created_by`, `updated_at`, `updated_by` (set by interceptor), optimistic concurrency (`xmin` row version). Return HTTP 409 on conflicts.
- **No hard deletes** of business records. Use status (Cancelled / Withdrawn / Inactive) with reason. Hard delete only for drafts never submitted, and only through an explicit, audited action.
- Timestamps: `timestamptz` (UTC) ↔ `DateTimeOffset`. Calendar dates: `date` ↔ `DateOnly`. Display in the installation's time zone (Settings).
- Money: `numeric(18,2)` + currency code (default AED). Percentages: `numeric(9,4)` stored as ratio (1.0 = 100%); format in UI.
- Bilingual text fields: two columns `*_ar`, `*_en` (both nullable unless the SPEC requires one); UI shows the user's language with fallback to the other.
- Picklists: central `settings.lookup_type` / `settings.lookup_value` (code, label_ar, label_en, sort, active, optional parent / metadata JSON). Business logic only ever checks **codes**. Fixed logic enums (e.g. KPI direction codes) are C# enums mapped to lookup codes.
- Flexible form fields (Request Framework, custom fields): `jsonb` with a schema stored alongside; validated server-side.
- Sensitive values (Emirates ID, API keys, provider secrets): encrypted at rest with ASP.NET Core Data Protection (keys persisted to the DB / volume and backed up); masked in UI and logs.
- Arabic search: normalise text for search columns (remove diacritics / tatweel, unify alef forms, ya / alef maqsura, ta marbuta / ha) into a generated `*_search` column; PostgreSQL full-text + trigram indexes.

## 7. Access control (implement exactly once, in `Platform.Access`)

Three layers (SPEC §0.2):
1. **System access** — may the user open system X at all.
2. **System roles** — permissions per system: `<system>.<object>.<action>` (e.g. `portfolio.project.submit`, `kpi.result.approve`), each with a **data scope**: Own · Team · Unit · Unit-and-below · All.
3. **Record roles** — generic table `access.record_role(resource_type, resource_id, principal_id, role_code, from, to)` (e.g. Project Manager, Project Owner, Team Member, Risk Owner, Remark Owner, Task Creator / Assignee / Follow-up). Granted and revoked by the owning module, never by hand-edited SQL.

Rules:
- One entry point: `IAccessService.CanAsync(principal, permission, resource?)` and `IAccessService.ApplyScope(query, permission)` for lists. Every endpoint declares its permission; every handler that loads a record checks it. Lists are always filtered by scope **in the database query**.
- Return **404** (not 403) when a user asks for a record outside their scope, so record existence doesn't leak; 403 for actions they can see but may not perform.
- Approval engine grants temporary "approver" rights for the record of an open approval task, including delegation ("on behalf of").
- **Vendor (Wasel) users** are a separate principal type with their own authentication scheme, route group (`/api/wasel/*`, SPA area `/wasel`), mandatory MFA, and **vendor-visible DTOs** (separate response models — never reuse internal DTOs). They have no MCP or AI access.
- Every permission exists in a seeded catalogue with AR / EN labels so the permission-matrix UI can show it. Default role → permission mappings per SPEC module "Access" sections are seed data, editable in Settings.
- **System ownership**: each system has an owning-unit setting; the approval engine can resolve "owning unit of this system".

## 8. Audit trail (`Platform.Audit`)

- EF Core `SaveChanges` interceptor records every insert / update / delete on audited entities: entity, record id, field, old value, new value, user, on-behalf-of, timestamp, IP, user agent, **channel** (`ui`, `api`, `mcp`, `ai`, `job`, `import`, `wasel`, `system`), correlation id, optional reason.
- Also logged explicitly: logins (success / failure), MFA events, logout, permission / role / settings changes, approvals and workflow transitions, file upload / download / delete, exports, AI feature calls, MCP tool calls.
- Table `audit.entry` is **append-only**: the application DB role has INSERT + SELECT only. Each row stores `prev_hash` and `hash = SHA-256(prev_hash || canonical_json(row))`; a verification job and admin screen check the chain.
- Read side: History tab on every record (timeline, filters), global Audit Log screen (search across systems, export), "view record as of date" (rebuild from entries).
- Never write secrets or full file contents into audit values; mask sensitive fields.

## 9. Approval engine & workflow (`Platform.Workflow`)

- **Process definitions** (versioned, editable in the global *Approval Settings* centre): process key (registered by modules, e.g. `portfolio.request`, `portfolio.charter`, `portfolio.monthly-update`, `kpi.result`, `teams.formation`, `teams.evaluation`, `cap.action-validation`, …), ordered steps, per step: approver rule, entry condition (expression), allowed actions (Approve / Reject / Return for correction / custom), comment required on reject / return, SLA (hours / days) and escalation target, notification templates.
- **Approver rules**: role (system role), org relation (`SectionManagerOf(subject)`, `DepartmentDirectorOf(subject)`, `HeadOfUnit(unit)`, `OwningUnitOfSystem`), specific employee, record role (e.g. Project Owner), team role, "submitter's manager". Rules resolve at runtime from the Organisation module; resolved approvers are snapshotted on the task.
- **Instances**: one approval instance per submission; tasks per step; history (who, action, comment, on behalf of, time); resubmission after Return creates a new instance linked to the previous one.
- **Hooks**: modules subscribe to `ApprovalCompleted / Rejected / Returned / StepEntered` for their process key to move their own state machines.
- **Delegation**: date-ranged; delegate acts "on behalf of"; recorded on every action.
- **Current-status engine**: each module implements `ICurrentStatusProvider` per record type returning `{messageKey, params, actorDescription, nextActions[]}`; used by record headers and the My Work inbox.
- **My Work**: aggregates pending approval tasks, assigned tasks, due items and current-status "your action" items across all modules the user can access.

## 10. Localisation, RTL and UX

- Translation files: `/web/src/i18n/{ar,en}/<namespace>.json`; server returns **message keys + params** (validation errors, status messages), never English sentences. Admin translation overrides stored in DB (Settings → Translations) and merged at runtime.
- `<html dir>` and `lang` switch with the user's language (stored in profile); switching is instant, no reload of data.
- Use logical CSS properties only; mirror directional icons; numbers via `Intl.NumberFormat` (Arabic-Indic digits optional per Settings); dates via `Intl.DateTimeFormat`, Hijri display only when the Settings toggle is on.
- Emails: bilingual side-by-side templates (Arabic right, English left), rendered server-side, branding from Settings.
- PDFs and exports: bilingual, Arabic fonts embedded, RTL tables correct.
- **UX patterns (use the shared components, don't re-invent):** app shell (sidebar shows only accessible systems, top bar: global search, language toggle, notifications, profile); **list page** (saved views, filters, column chooser, export); **record page** (header with number / title / status chips, **stage stepper**, **current-status banner with the next actions the user can take**, tabs: Overview, related items, Documents, History); **wizard** (multi-step, save-as-draft, inline validation); **approval panel** (status, history timeline, action buttons for the current approver); empty states, skeleton loaders, toasts, confirm dialogs for irreversible actions.
- Accessibility WCAG 2.1 AA; keyboard operable; responsive down to 360 px; light / dark themes from design tokens; identical quality in RTL and LTR (E2E screenshots in both).

## 11. API conventions

- REST under `/api/v1/<module>/...`; Wasel under `/api/wasel/v1/...`; MCP at `/mcp`.
- OpenAPI document generated and published at `/openapi`; typed TS client generated into `/web/src/api` (never hand-write fetch types).
- Commands (`POST`) for state changes named by intent (`/projects/{id}/submit-charter`), not generic PATCHes of status fields.
- Errors: RFC 9457 Problem Details with a stable `code` and translatable `messageKey`.
- Paging (`page`, `pageSize` ≤ 200), sorting, filtering parameters consistent across lists.
- Idempotency key header supported on submit / approve endpoints.
- Personal access tokens (hashed, scoped, expiring) for API use; OAuth 2.1 for MCP clients (per MCP spec).

## 12. AI (`Platform.AI`)

- Providers configured in Settings → AI (provider, endpoint, encrypted key, model per feature, limits, **Test connection**). Resolve an `IChatClient` per feature key; no feature talks to a provider directly.
- Master switch + per-feature switches + per-role access; when off, the UI hides AI actions and everything works without AI.
- Prompt templates live in code as versioned resources per feature (`<module>/Ai/Prompts/*.md`), bilingual instructions, and request **structured JSON output** validated against a schema before use. Untrusted document text is passed as data, clearly delimited, never as instructions.
- Data controls: only fields allowed by Settings are sent; optional PII masking (names, Emirates ID, emails, phones).
- Every AI call logged (`ai.usage_log`: user, feature, provider, model, tokens, latency, record ref) and audited with channel `ai`. Results shown as **drafts** with "AI-generated" labels until accepted / edited.
- Time-outs and graceful errors; never block a business action on AI availability.

## 13. MCP server (`Platform.Mcp`)

- Official MCP C# SDK, Streamable HTTP at `/mcp`. Authentication: OAuth 2.1 (OpenIddict authorization server, authorization-code + PKCE) or personal access token → resolves to the user principal → **the same `IAccessService`** as the UI.
- Each module registers tools in its `Mcp/` folder with metadata: read / write, required permission, default enabled. Read tools on by default; write tools off by default; approve / reject tools are never registered as enabled by default.
- Admin settings: enable MCP, enable individual tools, rate limits, token lifetimes. Every tool call audited (channel `mcp`, parameters, result status).
- Vendor (Wasel) principals cannot use MCP.

## 14. Notifications, files, jobs

- **Notifications**: template key per event with AR / EN subject and body (editable in Settings), channels email + in-app (+ digest), user preferences, delivery via outbox with retry; deep links open the record. Target: sent within 5 minutes.
- **Files**: stored outside the web root through `IFileStore`; allow-listed types, size limits, magic-number check, optional ClamAV scan before available; versioned; downloads only through an authorised endpoint that checks access and audits; PDF / image preview via pdf.js.
- **Jobs (Quartz)**: monthly reminders (PMS, CAP), KPI period window open / close, SLA escalations, review / expiry reminders (documents, IP renewals, risk reviews), team evaluation triggers, dashboard aggregate refresh, audit hash-chain verification, outbox dispatcher, dormant vendor-account disable. Every job is idempotent and logged.

## 15. Calculations (single source of truth, unit-tested)

Implement as pure, deterministic services in the owning module with table-driven tests copied from the SPEC:
- KPI `PerformanceCalculator` (actual and capped, SPEC §2 K5 — all 14 test rows must pass), annual aggregation by Measuring Method.
- Risk scoring (configurable formula via sandboxed expression, bands, appetite).
- PMS: milestone completion rate, project performance, planned-to-date progress, progress status, evaluation score / priority band, classification, EVM (optional feature).
- Teams: team score, member points and grade, reward calculation from the matrix.
- Any formula configurable by admins is evaluated by the shared sandboxed expression service (whitelisted variables and functions, no reflection, time-limited).

## 16. Testing and definition of done

- **Unit tests** (xUnit + Shouldly + NSubstitute): domain rules, state machines (every allowed and every forbidden transition), calculators.
- **Integration tests** (WebApplicationFactory + **Testcontainers PostgreSQL**): every endpoint — happy path, validation errors, **authorization negative cases** (wrong system role, out-of-scope record → 404, vendor user → refused), audit entries written.
- **Architecture tests**: module boundaries (§5), no forbidden packages (§4.3).
- **E2E** (Playwright): the main flow of each module, run in **Arabic (RTL) and English (LTR)**, with screenshots.
- Front end: `tsc --noEmit` strict, ESLint, component tests (Vitest + Testing Library) for shared UI.
- Quality gates: `dotnet build` with zero warnings, `dotnet format --verify-no-changes`, `dotnet test`, `npm run lint`, `npm run typecheck`, `npm test`, `dotnet list package --vulnerable` and `npm audit` with no high / critical issues.

**A phase / feature is done only when:** acceptance checks in `docs/BUILD_PLAN.md` pass · tests above pass · migrations created and applied cleanly from empty DB · seed data / lookups / permissions / notification templates / translations (AR + EN) added · OpenAPI and TS client regenerated · design note and ADRs updated · `THIRD_PARTY_LICENSES.md` updated · phase checklist ticked in `BUILD_PLAN.md`.

## 17. Working method for Claude Code

1. Start each phase in **plan mode**: read CLAUDE.md, the phase in BUILD_PLAN.md and the referenced SPEC sections; produce a plan (entities, endpoints, screens, jobs, events, permissions, tests); wait for approval.
2. Write / update `docs/design/<module>.md` first (data model, state machine table, approval processes used, permissions, events in / out, screens).
3. Implement in vertical slices: domain → persistence + migration → application handlers + validation + access checks → API → TS client → UI (AR + EN) → tests. Commit per slice with Conventional Commits (`feat(portfolio): submit charter for approval`).
4. Run all quality gates locally before declaring a slice done. Never disable, skip or weaken a failing test to make it pass; fix the cause or report it.
5. At the end of a phase, write a short summary: what was built, decisions taken (ADRs), deviations from the SPEC (should be none without an ADR), known gaps, and how to demo it.

## 18. Commands

```bash
# Dev dependencies (PostgreSQL 16, Gotenberg, Mailpit). Behind a Docker Hub limit: REGISTRY_PREFIX=mirror.gcr.io/
docker compose -f deploy/docker-compose.yml up -d postgres gotenberg mailpit

# Back end (http://localhost:5080; Development applies migrations on start)
dotnet tool restore
dotnet run --project src/Host/Strategy.Host
dotnet run --project src/Host/Strategy.Host -- --migrate                      # apply migrations and exit
dotnet run --project src/Host/Strategy.Host -- --create-admin --email <e> --password-file <f>   # first administrator
# Development seed (empty DB): mariam@strategy.local (admin, must use MFA) / ahmed@strategy.local, password Strategy#Demo2026.
# Seed__AdminTotpKey=<base32> gives the admin a known authenticator key (E2E uses JBSWY3DPEHPK3PXPJBSWY3DPEHPK3PXP).

# Module migration (one DbContext + schema per module, ADR 0006)
dotnet ef migrations add <Name> -p src/Modules/<M>/Strategy.Modules.<M> -s src/Host/Strategy.Host -c <M>DbContext -o Infrastructure/Migrations

# Front end (http://localhost:5173, proxies /api to :5080)
cd web && npm ci && npm run dev
npx npm@11 install <pkg>                                                      # change npm dependencies (ADR 0008)

# API client after any endpoint change (CI fails on drift: tools/check-api-client.sh)
dotnet build src/Host/Strategy.Host && (cd web && npm run api:generate)

# Quality gates (CLAUDE.md §16, same as .github/workflows/ci.yml)
dotnet build Strategy.slnx -c Release && dotnet format Strategy.slnx --verify-no-changes
dotnet test --solution Strategy.slnx                                          # unit + architecture + integration (Docker)
tools/check-vulnerable.sh && tools/check-api-client.sh
cd web && npm run lint && npm run format:check && npm run typecheck && npm test
npm run build && npm audit --audit-level=high && npm run lint:licenses
npx playwright test                                                           # E2E, projects "ar" and "en" (needs postgres + mailpit; starts the host)

# Containerised install (http://localhost:8080)
docker compose -f deploy/docker-compose.yml --profile app up -d --build
```
CI resolves `global.json` (`rollForward: latestFeature`) to the newest .NET 10 SDK, whose analyzers can be stricter than an
older local SDK (e.g. IDE0005): check `dotnet --version` and keep the local SDK current.
Integration tests: `TESTCONTAINERS_HUB_IMAGE_NAME_PREFIX=mirror.gcr.io/` behind a Docker Hub limit, or
`STRATEGY_TEST_POSTGRES=<admin connection string>` to reuse a running server. Keep this section accurate.
