# Changelog

All notable changes to the Integrated Strategy System. Format: [Keep a Changelog](https://keepachangelog.com/en/1.1.0/); versions follow
[Semantic Versioning](https://semver.org/). Release notes for users: *About* in the application
(`src/Host/Strategy.Host/ReleaseNotes/{en,ar}/`). Details per build phase: `docs/phase-summaries/`.

## [1.0.1] — 2026-11-13

Tag `v1.0.1`. Fixes from testing the Salesforce standby sync (Phase 23) against a real Salesforce sandbox. ADRs 0210–0214; summary:
`docs/phase-summaries/v1.0.1.md`. Upgrade with `./deploy/upgrade.sh` (one migration, `SalesforceErrorDetails`); then validate and enable the
`user`, `contact` and `project-risk` mappings again.

### Fixed — Salesforce standby sync
- **`/limits` refused** (the *Salesforce Integration* licence cannot have *View Setup and Configuration*): the connection test and every run
  continue with a warning (401 / 403 / `INSUFFICIENT_ACCESS` / `FUNCTIONALITY_NOT_ENABLED`); API usage comes from the `Sforce-Limit-Info`
  header (`api-usage=used/max`) of normal calls and the "stop below 10 % remaining" rule is checked before every object (ADR 0210).
- **Error details**: HTTP status, the call (`/limits`, `/sobjects`, the query shape), Salesforce's `errorCode` and message on the Connection
  tab, in the run log, on the Status tab and in the server log, with AR / EN labels; never tokens, keys or record data (ADR 0211; new column
  `integration.sync_connection.last_test_detail`).
- **SOQL with fields that do not exist**: `IsDeleted` only where the object has it (not `User`; a User is never "Deleted in source");
  `PeopleOptions` no longer default `EmployeeNumber` / `FederationIdentifier`; people fields only when present; *Validate* checks every field
  the queries use — system, watermark, external-ID template, people, files and history fields (ADR 0212).
- **Picklist → number**: picklist value maps convert to the target field type (whole number, number, percentage, yes/no, date); *Validate*
  refuses maps whose outputs do not convert (ADR 0212).
- **ADoF pack 2**: `PMS_Project_Risk__c.Probability_Level__c` / `Impact_Level__c` as picklist → number (`1`–`5`); Contact matched by e-mail,
  then `Employee_Number__c`. Installed packs 1 are upgraded object by object while unchanged (ADR 0213).
- **Unknown persons**: bulk re-process by reason handles people mappings first and every page; runs retry unknown-person record
  exceptions once the User / Contact matches an employee.
- **Project risks** could not be saved with their external reference (`integration.errors.unknownType`): every sync target is now
  referenceable (ADR 0214).

### Changed
- Guides `docs/guides/{en,ar}/salesforce-sync.md`: *Salesforce Integration* licence with the *Salesforce API Integration* permission set
  licence, read-only permission set, **External Client App** (Spring '26), certificate and key with one `docker run alpine/openssl` line,
  load order (departments → employees → users and contacts → the rest), mapping picklists to numbers, limits and error details.
- Tests: the fake Salesforce org uses User and Contact describes recorded from real orgs, answers unknown columns with `INVALID_FIELD`, sends
  the usage header and can refuse `/limits`; the demo org (Development) is validated and fully loaded in CI.

## [1.0.0] — 2026-11-12

Tag `v1.0.0`. Covers build Phases 0–23 (`docs/SPEC.md` and `docs/SPEC-ADDENDUM.md`); Phases 0–18 were accepted by the owner on 7 Nov 2026,
Phases 19–23 are built and awaiting owner acceptance (`docs/BUILD_PLAN-PART2.md`).

First release. Self-hosted, bilingual (Arabic / English, full RTL) integrated strategy management suite: one application, one database,
one login, eleven systems on a shared platform.

### Added — Salesforce standby sync (Phase 23)
- **One-way copy from Salesforce** of all eleven systems' data plus departments, users (matched to employees), teams and tasks, so this
  system can take over when Salesforce is unavailable. **Never writes to Salesforce** (read-only REST / Bulk API 2.0 client, enforced at
  the connector boundary and tested).
- **Connector framework** in Settings → Integrations → **Salesforce sync**: OAuth 2.0 JWT bearer with a Connected App and an integration user
  (private key encrypted, thumbprint only shown), API version, Bulk API 2.0 for full / large loads and REST for deltas, incremental by
  `SystemModstamp`, deletions marked "Deleted in Salesforce", API limit tracking, retries; schedule every N hours / daily / weekly, Sync now,
  full load, per-object enable.
- **Mapping editor** reading the live Salesforce describe (fields, picklists): field mappings, picklist → code maps, user / contact → employee
  matching (e-mail, employee number, Federation ID), lookups → external references, record types, defaults, transforms, filters, parent-child
  load order; validation against the live describe before an object can be enabled; **"ADoF Salesforce" mapping pack** (export / import)
  covering the objects of SPEC-ADDENDUM G3 with the documented model differences.
- Files (ContentVersion / ContentDocumentLink / Attachment) into the Files service; approval history (ProcessInstance / Step) and field
  history (`*__History`) as read-only **source history** on the record page.
- **Exceptions queue** (unmapped value, unknown user, missing parent, invalid value…) with *Map value*, *Map person*, re-process and ignore.
- **Standby**: synced records read-only on every channel (UI, REST, MCP, imports, jobs) with a badge; approvals, jobs and notifications of
  the business systems suspended while Salesforce is primary.
- **Failover**: *Make this system primary* (reason, MFA re-prompt) with outage period and "Resubmit required"; **outage changes report** from
  the audit trail (old → new values with Salesforce IDs, Excel / PDF, re-entered marks); *Return to standby* with keep-local / take-Salesforce
  per record and *Close outage*.
- Monitoring: status page, run history and log, e-mail alerts on failure or late sync, health-page check. New audit channel `sync`.
- Guides: `docs/guides/{en,ar}/salesforce-sync.md` (Connected App, integration user, first full load, failover and return-to-standby runbooks).

### Added — integrations for analytics and other systems (Phase 22)
- **Integration clients** (service accounts with API keys or OAuth 2.0 client credentials): permissions and data scope through the access
  model, allowed systems / record types, IP allow-list, rate limits, expiry, rotation, usage log.
- **OData v4 feeds** per system (`/odata/v1/{system}`) with `$select`, `$filter`, `$orderby`, `$top`, `$skip`, `$count`, `$expand`, every row
  permission-filtered; **Power BI / Excel** connect with Basic credentials.
- **Incremental extracts** (changed since, deleted feed; JSON / CSV) and scheduled exports to a folder.
- **Signed webhooks** (HMAC-SHA256 with timestamp) for integration events, retries with back-off, dead letters, replay, SSRF guard.
- **Developer portal** (OpenAPI, OData metadata, webhook catalogue, Power BI guide in Arabic and English).

### Added — acceptance fixes, external references, members and responsibilities (Phase 19)
- **Global search** in the top bar (and Ctrl+K) across every system the user can access, Arabic-normalised, with recent items.
- **Rich text** (Tiptap) for minutes, descriptions, justifications and comments, sanitised on the server.
- **Security e-mails** (password, MFA, new sign-in, lock, access token); Windows PowerShell 5.1 / 7 install scripts; ZIP releases without
  `.git`; retries for slow networks.
- **External references** (source systems; external IDs on every record, shown, searchable, in exports and reports; upsert by external ID
  in imports, REST and MCP).
- **Members** and **Responsibilities** pages per system for simple administration of who may do what; KPI picker beyond 200 entries;
  "take over as creator" for a leaver's tasks.

### Fixed (Phase 23)
- Reports, charts, dossiers and exports showed raw codes for fields without a lookup type (e.g. project stage, milestone status): every code
  field now has Arabic and English labels (ADR 0209).

### Added — Reporting 2.0 (Phase 21)
- **Report types**: a primary record type with related types up to three levels — also across systems (project ↔ team members, project
  KPI ↔ KPI results, benchmark ↔ CAP remarks and actions, finding ↔ risk, risk ↔ KRI results, document ↔ IP) — "with" or "with or without"
  related records; 17 seeded per system, administered in **Settings → Report types**. Every row and related row is read through its own
  system's access; fields a person may not see are neither offered nor returned.
- **Report builder**: columns from every object of the type, filters with relative dates, grouping up to three levels with subtotals, summary
  formulas (sum, average, minimum, maximum), charts (bar, stacked bar, line, pie, donut), conditional highlighting, Excel / PDF / CSV export in
  Arabic or English.
- **My reports for everyone** with private folders; shared folders, sharing and publishing for Report authors.
- **Record dossiers**: PDF and Word documents of one record from templates designed in **Settings → Dossier templates** (cover, header /
  footer, sections bound to the record, related lists, charts, Gantt, KPI trend, risk matrix, approval history, attachments, conditions);
  seven seeded templates (project, KPI card, risk profile, audit report pack, team evaluation, benchmark study, CAP source); one record, a
  filtered set as one merged PDF or a ZIP, and monthly e-mail schedules (e.g. to each project manager), always with the reader's access.
- Reportable metadata contract for integrations (Phase 22 OData `$expand`).

### Added — themes and application workspaces (Phase 20)
- Three themes — **Classic** (unchanged, the default), **Enterprise** (compact workspaces: per-app tabs, highlights panel, path bar,
  related lists, utility bar) and **Modern** (calm workspaces: collapsible app sidebar, command palette Ctrl+K, list + preview, cards,
  Arabic-first Kufi headings) — each in light and dark, Arabic and English.
- **Settings → Appearance**: available themes, default theme and mode, "Allow users to choose their theme" (off by default), choice of
  light / dark, landing app per role, per-app name / icon / accent colour / tabs / home dashboard. Branding applies in every theme.
- **App launcher** and a **workspace per system** (header, tabs, home page with quick actions, the user's items and the system dashboard);
  shared apps My Work, Dashboards, Tasks, Teams, Organisation, Settings; favourites and a default app in the profile. Deep links keep working.

### Added — installation and operations (Phase 18)
- Production image `strategy-app:1.0.0` (non-root, read-only root file system, healthcheck, OCI labels), compose file with loopback-only
  database / PDF ports, CycloneDX SBOM and Trivy scan (`deploy/sbom.sh`).
- One-command install for Linux (`deploy/install.sh`) and Windows Server / Docker Desktop (`deploy/install.ps1`) with generated passwords,
  port checks and offline image loading; documented non-Docker install (systemd, PostgreSQL 16, Gotenberg, Tesseract, nginx HTTPS proxy).
- First-run **setup wizard** protected by a one-time setup token (organisation, branding, languages, first administrator with MFA, e-mail,
  sign-in provider, AI, business hours, organisation import, systems, demo data); `--setup-token`; `--create-admin` remains.
- **Backup, restore (verified: checksums, row counts, audit hash chain) and upgrade** scripts with rollback; `--verify-audit-chain`.
- Administrator **System health** page (database, migrations, jobs, outbox, e-mail, AI, OCR, storage, disk, certificates, audit chain).
- Version and release notes in the application (*About*).
- Seed packs: verified neutral defaults; optional, removable **demo pack** (`--seed-demo`, `--remove-demo`, Settings → Demo data).
- UAE Pass sign-in for Wasel vendors (matched by e-mail, second factor still required).
- Security: outbound URL policy against SSRF, `Security:SecureCookies`, offline common-password check, ASVS 4.0.3 L2 checklist and threat
  model (`docs/security/`).
- Accessibility audit with axe-core (`docs/accessibility.md`) and k6 load tests (`docs/performance.md`).
- Documentation in Arabic and English: installation, administrator, per-system user guides and MCP guide (`docs/guides/`).

### Fixed (Phase 18)
- S3 uploads to S3-compatible servers other than MinIO / AWS stored chunk signatures in the file content (now plain signed uploads).
- Intermittent `CapApiTests.Due_dates_follow_the_priority_in_working_time` (test world created before the frozen date).
- Integration tests reusing an external PostgreSQL server left their databases behind.
- Image build with the current .NET 10 SDK (stricter analyzers).

### Added — systems and platform (Phases 0–17)
- **Platform:** organisation hierarchy with history, employees, import; local sign-in with MFA, OpenID Connect (Entra ID / ADFS / UAE
  Pass), SAML 2.0, LDAP; three-layer access control with data scope and record roles; Settings centre (lookups, numbering, branding,
  translations, features); hash-chained audit trail with History tab and *view as of date*; files with versions, type sniffing and virus
  scan; bilingual notifications with outbox; Quartz jobs; configurable approval engine with delegation, SLA and the Approval Settings
  centre; business hours and holidays; links registry; AI platform (Anthropic, OpenAI, Azure OpenAI, OpenAI-compatible) with data controls
  and usage limits; MCP server (OAuth 2.1 + personal access tokens).
- **Portfolio Management** with requests, project lifecycle, change control, Agile / Hybrid, PM standards (RAG, baselines, EVM, RAID…),
  Gantt, status report PDF, capacity view and the **Wasel** vendor portal.
- **Performance Management (KPI)**, **Risk Management**, **Teams Management**, **Task Management**, **Request Framework** with **Document
  Registry** and **Intellectual Property**, **Benchmarking**, **Corrective Action Plan**, **Audit Management**.
- **Dashboards and reporting:** My Work, role dashboards, system dashboards, executive dashboard with department scorecard and AI brief,
  dashboard builder, report builder, scheduled delivery.

### Notes for upgrading from pre-release builds
- Scheduled jobs (reminders, digests, SLA escalations, KPI windows) run on their schedules from this version on — announce it to users.
- Run `./deploy/upgrade.sh` (pre-upgrade backup, migrations as the owner, runtime-role grants).
