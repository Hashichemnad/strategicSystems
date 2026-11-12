# Build Plan — Part 2 (Phases 19–23, still version 1.0)

Requirements: `docs/SPEC-ADDENDUM.md` (addendum to `docs/SPEC.md`). These phases complete **version 1.0** — the version stays 1.0.0 and the v1.0.0 release is made at the end of Phase 23. Same working method as Phases 0–18: one phase per new Claude Code session, Plan mode first, review the plan, build, check the screenshots and summary, Create PR → green checks → Merge.

**Order**
| Phase | Name | Can run in parallel with |
|---|---|---|
| 19 | Acceptance fixes, external references, Members & Responsibilities | — (run first) |
| 20 | Themes & application workspaces | — (touches every screen: run alone) |
| 21 | Reporting 2.0 | 22 |
| 22 | Integrations for analytics and other systems | 21 |
| 23 | Salesforce standby sync + v1.0.0 release | — (needs 19 and 22) |

ADR number ranges: 19 → 0160+, 20 → 0170+, 21 → 0180+, 22 → 0190+, 23 → 0200+.

---

## Phase 19 — Acceptance fixes, external references, Members & Responsibilities

**Prompt**
```
Phase 19 — Acceptance fixes, external references, Members & Responsibilities.
Follow CLAUDE.md strictly (rules §2, working method §17). Read CLAUDE.md, docs/SPEC-ADDENDUM.md (sections A, B, D — this addendum wins over SPEC.md where they differ), docs/BUILD_PLAN-PART2.md "Phase 19", docs/release-notes/known-gaps-review.md, docs/phase-summaries/phase-17.md and phase-18.md, and the ADRs they reference.
First: add SPEC-ADDENDUM.md and BUILD_PLAN-PART2.md to the docs index and CLAUDE.md §1 (one line each: both are part of version 1.0), and do the housekeeping in SPEC-ADDENDUM A7 (no release tag yet — the version stays 1.0.0 until the end of Phase 23; remove a v1.0.0 tag if one exists; BUILD_PLAN.md progress table rows 0–18 accepted by the owner, rows 2 and 3 ticked).
Plan first, then implement:
1. SPEC-ADDENDUM A1–A6 and A8: global search (top bar, all accessible systems, grouped results, external IDs, Arabic normalisation, permission-filtered with 404 semantics, keyboard navigation, recent items); rich text with Tiptap for the SPEC's rich-text fields (sanitised HTML allow-list on the server, DOMPurify on render, plain-text projections for exports / e-mails / search / AI, migration of existing values); security e-mails (password changed / reset, MFA changes, account locked, new personal access token, optional new-device sign-in); install / upgrade / backup / restore scripts working in Windows PowerShell 5.1 and PowerShell 7 and from a ZIP folder without .git; npm / NuGet retries and longer timeouts in the Docker build; KPI picker search everywhere; "take over as creator" for a leaver's tasks.
2. SPEC-ADDENDUM B external references: Settings → Integrations → Source systems; shared integration.external_ref table with the uniqueness rules; record-page "External references" section; global search by external ID; upsert by external ID in every Excel / CSV import (optional Source system + External ID columns, validation report) and in the REST API (GET / PUT by-external); MCP read tool to find a record by external reference; origin badge for records from a source system; included in exports, reports and the audit trail.
3. SPEC-ADDENDUM D Members and Responsibilities pages per system: Members (system administrators, users — individuals / units incl. sub-units / all employees — and optional extra roles), mapped onto the existing access model (system access, system roles, scopes) without changing its rules; delegated administration (a system administrator manages Members of that system only); Responsibilities with bulk reassignment per system (KPI responsible person, risk / action / control owners, PM / owner via the change-request rules where required, CAP remark owners / action assignees, responsible auditor for Audit Office members only, benchmark coordinator, team leader / evaluator, open tasks), each with reason, notifications, audit entries and re-routing of open approvals / tasks where applicable. Keep the permission matrix as "Advanced permissions".
Implement with tests: search never returns out-of-scope records (incl. by external ID); rich text rejects scripts and unsafe attributes; each security e-mail sent once in the user's language; external_ref uniqueness and upsert (import, API) create no duplicates; a system administrator cannot manage another system's Members or platform settings; bulk reassignment moves only the selected records and notifies both people; the .ps1 scripts run under Windows PowerShell 5.1 (CI job on windows-latest with Docker if feasible, otherwise a documented manual run) and from a folder without .git.
Use ADR numbers from 0160 upwards. Finish with docs/phase-summaries/phase-19.md and Playwright screenshots (Arabic and English) in docs/screenshots/phase-19/ (global search, rich-text editor, external references on a record, import with external IDs, Members page, Responsibilities bulk reassignment, security e-mail).
```
**Check in the plan:** search across all systems; rich text in minutes and descriptions; external IDs on every record with upsert in import and API; a simple Members page (admins / users) and a Responsibilities page per system; Windows script fixes.

---

## Phase 20 — Themes & application workspaces

**Prompt**
```
Phase 20 — Themes and application workspaces.
Follow CLAUDE.md strictly (rules §2, §10 localisation / RTL / UX, working method §17). Read CLAUDE.md, docs/SPEC-ADDENDUM.md section C (all), docs/BUILD_PLAN-PART2.md "Phase 20", docs/phase-summaries/phase-0.md, phase-17.md, phase-18.md and phase-19.md, and docs/accessibility.md.
Load the frontend-design skill if available. Write docs/design/themes.md first: design tokens per theme (colour, typography, spacing, radius, elevation, density) for Classic / Enterprise / Modern × light / dark; layout variants (navigation: single sidebar vs app launcher + per-app tabs vs per-app collapsible sidebar; record page: current vs highlights panel + path + two columns + related lists vs Modern card layout; list page: table vs split view; density); how one component library serves all three themes through tokens and layout slots (no duplicated screens); per-system app definitions (display name AR/EN, icon, accent colour, tabs, home dashboard) and the app launcher; default landing app per role; Settings → Appearance (enabled themes, default theme and mode, "Allow users to choose their theme" — off by default, branding applied in every theme); Classic stays exactly as today.
Enterprise theme: Lightning-style working patterns for users coming from Salesforce, but no Salesforce name, logos, icons or fonts (use Lucide icons and IBM Plex Sans Arabic / Noto); not called "Salesforce" anywhere in the product.
Modern theme: your own best design for this product — calm, efficient, Arabic-first typography, command palette (Ctrl+K) wired to the Phase 19 global search and actions, split-view lists, status chips and progress visuals, subtle motion respecting reduced-motion.
Every module gets its app workspace: header with name / icon / accent, its own tabs, its home page (system dashboard + my items + quick actions). Shared apps: My Work, Dashboards, Settings, Tasks, Teams. Deep links keep working in every theme.
Implement with tests: theme and mode resolution (admin default, user choice only when allowed); app launcher shows only accessible systems; landing app per role; every main screen renders in every theme × AR / EN × light / dark without horizontal scroll at 360 px; axe WCAG 2.1 AA passes in all three themes; visual-regression baseline screenshots per theme.
Use ADR numbers from 0170 upwards. Finish with docs/phase-summaries/phase-20.md and screenshots in docs/screenshots/phase-20/ for each theme (Arabic and English, light and dark): app launcher, a system home page, a list page, a record page (project), a form / wizard, My Work, executive dashboard, Settings → Appearance.
```
**Check in the plan:** three themes with Classic unchanged; Enterprise = Lightning-style patterns without Salesforce branding; Modern with command palette; app launcher + separate workspace per system with its own tabs and home; admin-controlled theme, user choice only if enabled.

---

## Phase 21 — Reporting 2.0 (run in parallel with Phase 22)

**Prompt**
```
Phase 21 — Reporting 2.0: report types, report builder, record dossiers.
Follow CLAUDE.md strictly (rules §2, working method §17). Read CLAUDE.md, docs/SPEC-ADDENDUM.md section E (all), docs/BUILD_PLAN-PART2.md "Phase 21", docs/phase-summaries/phase-17.md to phase-20.md, ADRs of Phase 17 (reporting), and docs/SPEC.md §0.4.
Write docs/design/reporting-2.md first: reportable record types, fields and relationships exposed by each module through Contracts (incl. cross-system relationships listed in SPEC-ADDENDUM E1); report types (primary + related up to 3 levels, inner / outer join, available fields), seeded per system and editable by admins; query composition per module with joins in the reporting layer (no cross-schema SQL), paging, limits and timeouts; permission filtering of every row and related row and of fields; report builder (columns across the report type, filters incl. relative dates, grouping up to 3 levels with subtotals, summary formulas, charts with ECharts, conditional highlighting, exports Excel / PDF / CSV bilingual and RTL, folders private / shared, "My reports" for every user, publish / share needs Report author, scheduling); record dossiers (template designer with sections bound to the record or related lists / charts / Gantt / KPI trend / risk matrix / approval history / attachments; seeded templates listed in E3; PDF via Gotenberg and Word via DocumentFormat.OpenXml or another MIT / Apache library; one record or a filtered set → merged PDF or ZIP; schedules; permissions per section).
Note: Phase 22 (integrations) is built in a parallel session and will reuse the report-type relationships for OData $expand — keep the reportable-metadata contract clean and documented. Keep changes to shared files minimal and additive.
Implement with tests: a multi-level report returns only rows and related rows the user may see; inner vs outer join results; a field the user may not see is not offered and not returned; subtotals and charts correct on a worked example; dossier for a project renders every seeded section in Arabic and English (PDF and Word) and omits sections the user may not see; bulk dossier for a filtered set; scheduled dossier e-mail.
Use ADR numbers from 0180 upwards. Finish with docs/phase-summaries/phase-21.md and screenshots (Arabic and English) in docs/screenshots/phase-21/ (report type editor, report builder with grouping and chart, My reports and folders, project dossier PDF and Word, dossier template designer, scheduled dossier).
```
**Check in the plan:** reports combining several record types (e.g. projects with milestones, risks and KPIs) incl. across systems; charts; My reports for everyone; project dossier in PDF and Word from admin-designed templates; permissions at every level.

---

## Phase 22 — Integrations for analytics and other systems (run in parallel with Phase 21)

**Prompt**
```
Phase 22 — Integrations: integration clients, OData analytics feeds, incremental extracts, webhooks.
Follow CLAUDE.md strictly (rules §2, §7 access control, §11 API, working method §17). Read CLAUDE.md, docs/SPEC-ADDENDUM.md sections F and B, docs/BUILD_PLAN-PART2.md "Phase 22", docs/phase-summaries/phase-5.md, phase-17.md to phase-19.md, docs/security/asvs-l2.md, and the ADRs on OpenIddict, personal access tokens, integration events (0043) and the SSRF guard (0155).
Write docs/design/integrations.md first: integration clients (OAuth 2.0 client-credentials via OpenIddict and API keys; permissions and data scope through the existing access model; allowed systems / record types; IP allow-list; rate limits; expiry and rotation; usage log; audit channel api); OData v4 read-only endpoints per system (/odata/v1/{system}) with $select / $filter / $top / $skip / $orderby / $count / $expand along the reportable relationships (coordinate with Phase 21's reportable-metadata contract — if it is not merged yet, define the minimal shared contract and record it in an ADR), metadata document, paging, labels by lang, codes always included, every query permission-filtered (choose a free OData library or implement the subset; licence per CLAUDE.md §4.3); incremental extracts (changed since + deleted feed, JSON / CSV; optional scheduled export to folder / SFTP / S3); webhooks (subscriptions per client to integration events, JSON payloads, HMAC-SHA256 signature with timestamp, retries with exponential back-off, dead-letter list, replay, delivery log, SSRF guard); Settings → Integrations pages; developer portal page (OpenAPI, OData metadata, webhook catalogue with sample payloads, "connect Power BI" guide in Arabic and English).
Note: Phase 21 (Reporting 2.0) is built in a parallel session. Keep changes to shared files minimal and additive.
Implement with tests: an integration client only reads its allowed systems and scope (out-of-scope rows never returned via OData or extracts); expired / revoked clients refused; rate limit enforced; OData $filter / $expand / paging correct and permission-filtered; deleted feed lists soft-deleted / cancelled records; webhook signature verifiable, retries and dead-letter with the fake clock, SSRF guard blocks internal targets; Power BI connects to the OData feed (document the manual check).
Use ADR numbers from 0190 upwards. Finish with docs/phase-summaries/phase-22.md and screenshots (Arabic and English) in docs/screenshots/phase-22/ (integration clients, client permissions, webhook subscriptions and delivery log, developer portal, Power BI connected — or a documented manual check).
```
**Check in the plan:** service accounts for other systems with their own permissions; OData feeds for Power BI / Excel / Tableau; changed-since extracts; signed webhooks with retries; a developer page with a Power BI guide.

---

## Phase 23 — Salesforce standby sync

**Prompt**
```
Phase 23 — Salesforce standby sync (one-way, Salesforce → this system) and the version 1.0.0 release.
Follow CLAUDE.md strictly (rules §2, working method §17). Read CLAUDE.md, docs/SPEC-ADDENDUM.md sections G and B (all), docs/BUILD_PLAN-PART2.md "Phase 23", docs/phase-summaries/phase-19.md and phase-22.md, the 1.0 source documents' data models as described in docs/SPEC.md (each module's data-model section lists the Salesforce objects it replaces), and the ADRs on external references, integration events, jobs and the audit trail.
Write docs/design/salesforce-sync.md first: Integration platform module (connectors, connection settings, mapping packs, sync runs, run log, exceptions queue) reusing the Phase 19 external references; Salesforce connector (OAuth 2.0 JWT bearer with a Connected App and integration user, private key encrypted, API version setting, Bulk API 2.0 for full / large loads and REST for deltas, incremental by SystemModstamp, deleted records → "Deleted in source", API limit tracking, retries); schedule settings (every N hours / daily at a time / weekly on days at a time, Sync now, initial full load, per-object enable); mapping editor reading the live Salesforce describe (fields, picklists) with field mappings, picklist → lookup code maps, user → employee matching by e-mail / employee number / Federation ID, lookups → external references, record types, defaults, transformations, parent-child load order; the "ADoF Salesforce" mapping pack (export / import) covering every object in SPEC-ADDENDUM G3 with the documented model differences; files (ContentVersion / ContentDocumentLink / Attachment) into the Files service; approval history (ProcessInstance / ProcessInstanceStep) and field history (*__History) as read-only source history; exceptions queue with re-process; standby behaviour (synced records read-only with a badge, workflows / approvals / jobs / notifications for synced records suspended); "Make this system primary" (MFA re-prompt, reason, unlock, re-enable workflows, open Salesforce approvals → "Resubmit required"), outage period tracking, outage changes report (from the audit trail, old → new values with Salesforce IDs, export, mark re-entered), "Return to standby" with conflict choice per record (keep local / take Salesforce); monitoring page, alerts on failure or late sync, health-page check. Never write to Salesforce.
Tests: use a fake Salesforce (recorded describe and query responses / WireMock-style stub) in CI — no live org calls in CI; idempotent re-runs create no duplicates (external_ref); incremental sync picks only changed records; deletions marked not removed; picklist and user mapping incl. an unmapped value → exceptions queue → fix → re-process; parent-child order; files and approval history imported; standby read-only enforced on API, UI, MCP and imports; failover unlocks and re-enables jobs; outage report lists every change; return to standby honours keep-local choices; schedule settings with the fake clock. Provide docs/guides/{en,ar}/salesforce-sync.md: creating the Connected App and integration user in Salesforce (read-only permission set covering all mapped objects), certificate / key, first full load, schedule, monitoring, failover runbook, return-to-standby runbook.
Use ADR numbers from 0200 upwards. At the end, release version 1.0.0: tag v1.0.0, and update the CHANGELOG and the 1.0.0 release notes (Arabic and English) so they cover Phases 0–23. Finish with docs/phase-summaries/phase-23.md and screenshots (Arabic and English) in docs/screenshots/phase-23/ (connection settings, schedule, mapping editor, exceptions queue, sync status, read-only synced record, make primary, outage changes report, return to standby).
```
**Check in the plan:** one-way only; schedule set in Settings; mapping editable with the live Salesforce field list; ADoF mapping pack for all systems; synced data read-only while standby; "Make this system primary" and "Return to standby"; outage changes report for manual re-entry in Salesforce.

---

## Progress checklist (Part 2)
| Phase | Name | Status | Accepted on |
|---|---|---|---|
| 19 | Acceptance fixes, external references, Members & Responsibilities | ☑ built — awaiting owner acceptance | |
| 20 | Themes & application workspaces | ☑ built — awaiting owner acceptance | |
| 21 | Reporting 2.0 | ☑ built — awaiting owner acceptance | |
| 22 | Integrations | ☑ built — awaiting owner acceptance | |
| 23 | Salesforce standby sync + v1.0.0 release | ☑ built — awaiting owner acceptance (tag `v1.0.0`) | |
