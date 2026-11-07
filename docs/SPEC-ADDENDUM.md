# Integrated Strategy System — Requirements Addendum (part of version 1.0)

Status: approved by the owner (7 Nov 2026). Extends `docs/SPEC.md`. Both together are the requirements of **version 1.0** — there is no 1.1; version 1.0.0 is released only after Phases 19–23. Where this addendum and SPEC.md differ, **this addendum wins**.
Build order and prompts: `docs/BUILD_PLAN-PART2.md` (Phases 19–23).

---

## A. Fixes found during owner acceptance

| # | Item | Requirement |
|---|---|---|
| A1 | Global search | Search box in the top bar (and Ctrl+K in the Modern theme, §C): searches every system the user can access — records by number, title, external ID (§B) and key fields; results grouped by system, permission-filtered (404 semantics), Arabic-normalised; keyboard navigable; recent items. |
| A2 | Rich text | Fields that are rich text in the SPEC (minutes, descriptions, justifications, comments, closure summaries, policy text…) use the Tiptap editor (bold, italic, lists, headings, links, tables, RTL / LTR per paragraph); stored as sanitised HTML (server-side allow-list), rendered with DOMPurify; plain-text fallback for exports, emails, search and AI. Existing plain-text values migrate unchanged. |
| A3 | Security e-mails | E-mail (bilingual) to the user when: password changed or reset, MFA enrolled / removed / recovery codes regenerated, new sign-in from a new device / IP (optional), account locked, personal access token created. |
| A4 | Install scripts on Windows | All `.ps1` scripts work in **Windows PowerShell 5.1 and PowerShell 7** (native stderr from `docker compose` must not be treated as an error); tested on a real Windows machine with Docker Desktop. |
| A5 | ZIP releases | install / upgrade scripts (`.ps1` and `.sh`) work from a folder **without `.git`** even when git is installed (a failed `git rev-parse` = no revision). |
| A6 | Slow networks | Docker build: npm and NuGet retries and longer timeouts (`fetch-retries`, `fetch-retry-maxtimeout`, NuGet retry), so a slow connection does not fail the build. |
| A7 | Housekeeping | No release tag yet: the version stays **1.0.0** and the `v1.0.0` tag, CHANGELOG and release notes are produced at the end of Phase 23 (they cover Phases 0–23). Remove any `v1.0.0` tag if one exists. BUILD_PLAN progress table: rows 2 and 3 ticked; Phases 0–18 marked accepted by the owner. |
| A8 | Known limitations from Phases 0–18 | Close where small: KPI picker beyond 200 entries everywhere (search-as-you-type), "take over as creator" for a leaver's tasks, e-mail on password / MFA change (A3). |

---

## B. External references (all systems)

Purpose: keep data from other systems unique and traceable (first use: Salesforce standby sync, §G); reusable by any customer for their current system.

- **Source systems** (Settings → Integrations → Source systems): code, name AR/EN, type (Salesforce, SAP, Oracle, Custom…), description, active.
- **External reference** = (source system, record type, external ID) → internal record. Shared platform table `integration.external_ref`: source_system, entity_type, external_id, record_id, first_seen_at, last_synced_at, source_modified_at, source_hash, sync_status. **Unique** on (source_system, entity_type, external_id) and on (source_system, entity_type, record_id). A record may have references from several sources.
- Shown on the record page (details section "External references"), searchable (global search), included in exports, reports and the audit trail.
- **Upsert by external ID** everywhere:
  - Excel / CSV imports in every system: optional "Source system" + "External ID" columns → update if found, else create; validation report.
  - REST API: `GET/PUT /api/v1/{module}/{type}/by-external/{source}/{externalId}`.
  - MCP: find record by external reference (read).
- Records created or updated by a sync carry **origin** = source system and are shown with a badge "from {source}".

---

## C. Themes and application workspaces

### C1. Theme management (Settings → Appearance)
- Three themes, each in light and dark mode and fully RTL / LTR:
  1. **Classic** — the current UI unchanged (single sidebar for all systems).
  2. **Enterprise** — Lightning-style working patterns familiar to Salesforce users (see C3). **No Salesforce name, logos, icons or fonts** (trademarks / licences): open-source icons (Lucide) and fonts (IBM Plex Sans Arabic / Noto); not called "Salesforce" in the product.
  3. **Modern** — the product's own best design (see C4).
- Admin enables themes, sets the **default theme** and default mode (light / dark / system).
- Setting **"Allow users to choose their theme"** — **off by default**. When on, users pick among the enabled themes (and mode) in their profile.
- Organisation branding (logo, primary colour) applies inside every theme. Per-system accent colours (C2) apply in Enterprise and Modern.
- Themes are design-token sets **plus layout variants** (navigation layout, density, record-page layout, list / table style, card style, typography scale) — not only colours. One component library; themes switch tokens and layout slots, never duplicate screens.

### C2. Application workspaces (Enterprise and Modern themes)
Each system feels like a **separate application**:
- **App launcher** (grid icon in the top bar): tiles for the systems the user can access, with icon, accent colour, short description; search; favourites.
- **Workspace per system**: header with the system's name and icon, **its own navigation tabs** (only that system's areas), its own **home page** (system dashboard from Phase 17 + "my items in this system" + quick actions), its accent colour.
- **Shared apps** in the launcher: **My Work** (cross-system), **Dashboards** (executive / cross-system), **Settings** (admins), plus Tasks and Teams as their own apps.
- Shared top bar in every workspace: launcher, global search (A1), notifications, Ask, language, profile.
- **Default landing app** per role (admin sets; e.g. PMO → Portfolio, Audit Office → Audit); users with several apps can set their own default and pin favourites.
- **Per-system appearance settings**: display name AR/EN, icon (from the icon set), accent colour, tab order / visibility, home-page dashboard.
- Deep links keep working across themes; the Classic theme keeps the single sidebar.

### C3. Enterprise theme — patterns
Compact density; top navigation tabs per app; **record page**: highlights panel (key fields + primary actions) at the top, **path / stage bar** with guidance, two-column body (details left, related lists right), tabs for related records, activity / history timeline; **list views** with saved views, inline edit, mass actions, split view; utility bar (bottom) for quick notes / recent items; modals for quick create; consistent "New / Edit / Submit" action placement.

### C4. Modern theme — patterns
Calm, spacious but efficient layout; collapsible left sidebar inside each app with favourites and recent items; **command palette (Ctrl+K)** for records, pages and actions; split-view lists (list + preview); cards with clear status chips and progress visuals; sticky action bars; Arabic-first typography; motion kept subtle (respects reduced-motion).

### C5. Quality bar
WCAG 2.1 AA in all three themes and both directions; no layout shift at 360 px; Playwright screenshots of every main screen in every theme × AR/EN × light/dark; visual regression baseline per theme.

---

## D. Permissions and responsibilities — simple administration

### D1. Members page per system (System → Settings → Members)
Simple screen on top of the existing access model (no matrix needed):
- **System administrators** of this system — list of people (e.g. 3 from Strategy for Portfolio; 3 from Audit for Audit). Maps to the system's office / admin role with scope All.
- **Users** of this system — individuals, whole units (incl. sub-units) or "all employees"; maps to system access + the system's standard user role(s).
- Optional extra roles of the system (e.g. Viewer, Report author for this system) as further lists.
- Add / remove with search; effective-date optional; every change audited and notified.
- The permission matrix stays for advanced cases ("Advanced permissions").
- A system administrator can manage Members of **their** system only (delegated administration), never other systems or platform settings.

### D2. Responsibilities page per system (System → Settings → Responsibilities)
Assign / reassign who is responsible for updating records, singly or in bulk, with filters and preview:
- KPI: responsible person / unit of KPI cards (and future results).
- Risk: risk owner, action owners, control owners.
- Portfolio: project manager / owner (through the change-request rules where the SPEC requires approval), task assignees.
- CAP: remark owners, action assignees.
- Audit: **responsible auditor** of reports / findings (Audit Office members only); departments stay read-only.
- Benchmarking: study coordinator; Teams: leader / evaluator; Tasks: reassign open tasks.
- Each reassignment: reason, notification to old and new person, audit entry, open approvals / tasks re-routed where applicable.

---

## E. Reporting 2.0

### E1. Report types (multi-record reports)
- A **report type** = primary record type + related record types along defined relationships ("with" = inner, "with or without" = outer), up to 3 levels (e.g. Projects → Milestones → Tasks).
- Relationships within a system and **across systems** where the systems are linked through Contracts (project ↔ team members, project KPI ↔ KPI card results, benchmark ↔ CAP actions, finding ↔ risk, risk ↔ KRI KPI, document ↔ IP).
- Seeded report types for every system (e.g. Projects with Milestones; Projects with Risks and KPIs; Projects with Change Requests; Requests with Approvals; KPI Cards with Results by period; Risks with Controls and Actions; Findings with Recommendations and Follow-ups; Remarks with Actions; Studies with Topics and Actions; Teams with Members and Evaluations; Documents with Versions).
- Admins create / edit report types in Settings (choose primary, related objects, join type, available fields).
- **Permissions at every level**: each row and each related row filtered by the user's access; fields the user may not see are not offered.
- Implementation: each module exposes reportable record types, fields and relationships through Contracts; the reporting engine composes queries per module and joins in the reporting layer (no cross-schema SQL), with paging and limits.

### E2. Report builder
Columns from all record types in the report type; filters (incl. relative dates), grouping up to 3 levels with subtotals, sorting, summary formulas (count, sum, avg, min, max), **charts** (bar, line, pie / donut, stacked) on top of the table, conditional highlighting; export Excel / PDF / CSV (bilingual, RTL); **folders** (private, shared with people / roles / units); **"My reports"** for every user (private, no role needed); sharing / publishing needs Report author; scheduling by e-mail (existing).

### E3. Record dossiers (document reports)
- Formatted **PDF and Word** report of one record with all its sections, in Arabic or English (or bilingual).
- **Templates** designed by admins (Settings → Reporting → Dossier templates): cover page, branding, sections (each a block bound to the record or a related list / chart / Gantt / KPI trend / risk matrix / approval history / attachments list), order, conditional sections, page header / footer.
- Seeded templates: **Project dossier** (overview, charter, stage and RAG health, budget vs actual, milestones + Gantt, KPIs with results, risks and issues, change requests, team and stakeholders, meeting decisions, lessons learned, closure and impact), **KPI card dossier**, **Risk profile**, **Audit report pack**, **Team evaluation report**, **Benchmark study report**, **CAP source report**.
- Run for one record, or for a filtered set → merged PDF or ZIP; schedule (e.g. monthly dossier of each active project e-mailed to its owner); permissions applied to every section.
- Word output via an MIT / Apache library (e.g. DocumentFormat.OpenXml); PDF via Gotenberg.

---

## F. Integrations for analytics and other systems

- **Integration clients** (Settings → Integrations → Clients): named service accounts (e.g. "Power BI — Strategy Office"), OAuth 2.0 client-credentials (OpenIddict) or API key, each with its own permissions and **data scope** (same access model), allowed systems / record types, IP allow-list, rate limit, expiry, rotation, usage log; audited with channel `api`. A person's personal access token is not used for system integrations.
- **Analytics feeds**: read-only **OData v4** endpoints per system (`/odata/v1/{system}`), with `$select`, `$filter`, `$top`, `$skip`, `$orderby`, `$expand` along the report-type relationships, paging, `$count`, metadata document; works with Power BI, Excel, Tableau (OData connector) and Qlik. Labels in AR / EN via a `lang` parameter; codes always included.
- **Incremental extracts**: `changed since` (modified timestamp + deleted-records feed) in JSON / CSV for ETL tools; optional scheduled exports to a folder / SFTP / S3.
- **Webhooks**: subscriptions per integration client to integration events (e.g. request approved, project stage changed, KPI result approved, risk rating changed, finding closed, remark completed, task completed); JSON payload with IDs and changed fields; HMAC signature, timestamp, retries with back-off, dead-letter list, replay, delivery log; SSRF guard on target URLs.
- **Developer portal page**: OpenAPI docs, OData metadata, webhook event catalogue with sample payloads, how to connect Power BI.

---

## G. Salesforce standby sync (Salesforce → this system, one-way)

### G1. Purpose and mode
- Salesforce remains the **primary** system. This system receives a regular **one-way** copy of **all 11 systems' data** (and departments, users / employees, teams, tasks) so it can take over when Salesforce is unavailable.
- **Never writes to Salesforce.** After an outage, changes made here are re-entered in Salesforce manually, helped by the outage changes report (G6).

### G2. Connector framework (general, reusable)
- `Integration` platform module: connectors, connection settings, mapping packs, sync runs, run log, errors, external references (§B).
- Salesforce connector: **OAuth 2.0 JWT bearer** with a Connected App and a dedicated integration user (no password stored; private key encrypted); configurable API version; **Bulk API 2.0** for large / initial loads and REST for small deltas; incremental by `SystemModstamp`; deleted records via `queryAll` / `IsDeleted` (or getDeleted) → mark "Deleted in source" (never hard-delete here); respects API limits (shown on the status page).
- **Schedule** set in Settings: every N hours, daily at a time, or weekly on chosen days at a time (business-hours aware optional); **Sync now** button; initial full load; per-object enable / disable.
- **Mapping pack** = mappings per Salesforce object → internal record type: field mappings (direct, picklist value → lookup code, lookup → external reference, user → employee by e-mail / employee number / Federation ID, transformations), parent-child order, record-type mapping, default values, ignore list. Mappings are **data** (editable in Settings with a mapping editor that reads the Salesforce describe live), shipped as an **"ADoF Salesforce" mapping pack** that can be exported / imported.
- **Unmapped values** (new picklist value, unknown user, missing parent): record goes to an **exceptions queue** with the reason; admin fixes the mapping or maps the value, then re-processes.

### G3. ADoF mapping pack (initial)
Objects visible to the connected org user today: `Department__c`, `User`, `Project_Team__c`, `Project_Team_Association__c` (+ Team member evaluation), `PMS_Project_Request__c`, `PMS_Project__c`, `PMS_Work_Plan__c`, `PMS_Project_Kpi__c`, `PMS_Project_Risk__c`, `PMS_Project_Stakeholder__c`, `PMS_Project_Planning_Item__c`, `PMS_Challenges_Constraints__c`, `PMS_Change_Request__c`, `PMS_Contract__c`, `PMS_Lesson_Learned__c`, `PMS_Team_Meeting__c`, `ContentVersion` / `ContentDocumentLink` / `Attachment` (files), `ProcessInstance` / `ProcessInstanceStep` (approval history, read-only), `*__History` (field history, read-only import into a "source history" tab).
Objects of the other systems (KPI `Org_Kpi_Goal__c` / `Org_Kpi_Measure__c` / `Org_Kpi_Process__c`, Risk incl. `Risk_Kpi_Relation__c`, Audit, CAP, Benchmarking, Document Registry, IP, Tasks / timesheets, Teams evaluation): mapped from the ERDs and technical specifications in the 1.0 source documents; **the integration user must be granted read access to all of them**; the mapping editor confirms field names from the live describe before activation.
Model differences are handled explicitly by the pack (documented per object), e.g.: Salesforce additive risk scores → re-rated by the framework, with the source score kept as a field; evaluation scores stored on Salesforce team / member rows → one evaluation record per year; risk "actions that became controls" → controls; KPI text targets with "-" → Range target type; Project `Approval_Status__c` → stage + approval history snapshot (no live approval instance is created for synced records).

### G4. Standby behaviour
- While the sync is **active**, synced records are **read-only** in this system (badge "Synced from Salesforce — read-only"); workflow actions, approvals, jobs that change synced records, reminders and notifications to users about synced records are **suspended** (no duplicate e-mails while Salesforce is primary). Local-only data (settings, users' preferences) stays editable.
- Users can browse, search, report and use dashboards on the synced data at any time.

### G5. Switch to primary (failover)
- Admin action **"Make this system primary"** (with confirmation and reason; MFA re-prompt): stops the sync, unlocks records, re-enables workflows / jobs / notifications, and starts recording an **outage period**.
- Open Salesforce approvals at the time of switch: the record is placed at the corresponding stage with a **"Resubmit required"** status (approval history kept), so approvers act here.
- **"Return to standby"** (when Salesforce is back): requires the outage changes report to be exported / acknowledged; resumes sync; records changed here during the outage are flagged so a later sync from Salesforce does not silently overwrite them (choose per record: keep local, take Salesforce) until the admin closes the outage.

### G6. Outage changes report
List of every create / update / delete / approval made here during the outage (from the audit trail), grouped by system and record, with old → new values and the Salesforce ID → for manual re-entry in Salesforce; export Excel / PDF; mark items "re-entered".

### G7. Monitoring
Sync status page (last run, duration, records per object created / updated / deleted / failed, API usage, next run), run history, exceptions queue, e-mail alert to admins on failure or when the sync is late, health page check (§ Phase 18).
