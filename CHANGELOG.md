# Changelog

All notable changes to the Integrated Strategy System. Format: [Keep a Changelog](https://keepachangelog.com/en/1.1.0/); versions follow
[Semantic Versioning](https://semver.org/). Release notes for users: *About* in the application
(`src/Host/Strategy.Host/ReleaseNotes/{en,ar}/`). Details per build phase: `docs/phase-summaries/`.

## [1.0.0] — unreleased

Not tagged yet: version 1.0.0 is released (tag `v1.0.0`) at the end of Phase 23 and this entry is then completed to cover Phases 0–23
(`docs/SPEC-ADDENDUM.md` A7). Phases 0–18 below were accepted by the owner on 7 Nov 2026.

First release. Self-hosted, bilingual (Arabic / English, full RTL) integrated strategy management suite: one application, one database,
one login, eleven systems on a shared platform.

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
