# 0151 — Administrator Health page

- **Status:** Proposed (Phase 18)
- **Context:** SPEC §0.5 asks for a health page for administrators (queue, scheduler, e-mail, AI provider, storage); the Phase 18 prompt adds
  database, migrations, job last / next run and failures, OCR, disk space, certificate expiry and version. `/health/live` and `/health/ready`
  (ASP.NET Core health checks, anonymous, for container orchestration) must stay small and must not reveal details. The facts live in
  different platform components (Scheduling, Notifications, AI, Files, Audit, Reporting, Host), which must not reference each other for this.

## Decision
- **Contributor pattern.** `IAdminHealthContributor` in `Strategy.Platform.Http` (`Health/`), registered with `AddAdminHealthContributor<T>()`
  by the component that owns the facts: Settings (database latency / version / size, pending migrations per schema through
  `ModuleMigrator.PendingMigrationsAsync`), Scheduling (scheduler state, every declared job: last run and status, next fire time, failures in
  24 h, paused; outbox pending / dead / oldest pending age), Notifications (SMTP configured, deliveries in 24 h), AI (provider on and active,
  calls / failures from the usage log; Tesseract and its languages), Files (probe write, free disk space of the files volume and the
  temporary folder, ClamAV answers when scanning is on), Reporting (Gotenberg `/health`), Audit (last hash-chain verification; stale after
  48 h), Host (version, uptime, Data Protection key ring, certificate expiry of `DataProtection:CertificatePath` and of every PEM / DER file in
  `Health:CertificatePaths` — 30-day warning).
- **Shape.** `AdminHealthReport { status, checkedAt, checks[] }`; each check `{ code, group, status, messageKey, params, items[] }` with status
  `ok | warning | error | notConfigured` and the overall status the worst one. Server sends **message keys and invariant parameters only**
  (numbers, ISO dates, codes, host names); the SPA translates (`health` namespace, AR / EN) and formats. No secrets, no connection strings, no
  key material, no exception messages (only the exception type name).
- **Robustness.** Contributors run in parallel, each in its own DI scope with a 10-second time-out; one failing or hanging contributor becomes
  an `error` check under its own code and never hides the others. Contributors only read: no "live" test against SMTP or the AI provider
  (those are explicit *Send test e-mail* / *Test connection* actions, audited, on their own pages).
- **Access.** `GET /api/v1/settings/health`, permission **`settings.health.view`** (on the Settings administrator role, hence on every
  platform administrator). The design note named it `platform.health.view` at `/api/v1/admin/health`; the permission catalogue requires
  `<system>.<object>.<action>` with an existing system code and platform routes are `/api/v1/<component>`, so the page lives in the Settings
  component. `Cache-Control: no-store`. SPA: *Settings → System health* (`/settings/health`), refreshed every minute.

## Consequences
New platform services add a contributor instead of editing the page. The page shows configuration problems (no SMTP, Gotenberg down, OCR
missing, pending migrations, a paused or failing job, late outbox, low disk, expiring certificate) before users notice them. Disk thresholds:
warning below 15 % (`Health:DiskWarningPercent`) or 2 GB, error below 5 % or 500 MB.
