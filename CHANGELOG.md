# Changelog

All notable changes to the Integrated Strategy System. Format: [Keep a Changelog](https://keepachangelog.com/en/1.1.0/); versions follow
[Semantic Versioning](https://semver.org/). Release notes for users: *About* in the application
(`src/Host/Strategy.Host/ReleaseNotes/{en,ar}/`). Details per build phase: `docs/phase-summaries/`.

## [1.0.0] — 2026-11-06

First release. Self-hosted, bilingual (Arabic / English, full RTL) integrated strategy management suite: one application, one database,
one login, eleven systems on a shared platform.

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
