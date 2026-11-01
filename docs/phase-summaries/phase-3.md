# Phase 3 — Audit trail, Files, Notifications, Jobs: summary

**Status:** built and verified in the build session · awaiting owner acceptance
**Branch:** `claude/dreamy-allen-e6e1jg`
**Planning:** the plan is the design note [`docs/design/audit-files-notifications-jobs.md`](../design/audit-files-notifications-jobs.md),
written before the code. This session ran without an interactive approval step, so the plan was written first and then implemented as it
stands; its open choices are recorded as ADRs 0018–0021.

## What was built

| Area | Result |
|---|---|
| Audit trail (`Strategy.Platform.Audit` + `.Contracts`, schema `audit`) | **Interceptor** on every module DbContext: one row per changed field (old → new), a JSON snapshot on create / delete, user, on-behalf-of, time, IP, user agent, **channel**, correlation id, change set, optional reason. Written **in the same transaction** as the business change (the interceptor opens one when the caller has none). Attributes `[Audited]`, `[NotAudited]`, `[AuditMasked]`, `[AuditParent]` steer it; secrets and hashes are `***`.<br>**Explicit events:** sign-in / MFA / tokens (all former `auth_event` types), file upload / download / refused download / deletion, exports, chain verification, job run / pause / resume, outbox re-queue.<br>**Hash chain:** `SHA-256(prev_hash ‖ canonical JSON)`, gap-free `seq` under an advisory lock, verification (full / incremental from a checkpoint; detects changed values, broken links, missing rows, truncated tail), daily job, alert to verifiers.<br>**Append-only:** triggers refuse UPDATE / DELETE / TRUNCATE for every role; the runtime role gets only SELECT + INSERT (two-role install, ADR 0018).<br>**Read side:** History endpoints per record (`…/{id}/audit`, `…/audit/as-of`) for sample items, employees, units and roles; global search, Excel and PDF export. |
| Files (`Strategy.Platform.Files` + `.Contracts`, schema `files`) | Purposes declared in code; **content sniffing** (magic numbers, OOXML main part, UTF-8 text) that must match the extension; size limits (purpose + Settings → Files); local volume or S3-compatible store; immutable versions; soft delete with reason; optional **ClamAV** (in-house INSTREAM client, pending + re-scan job when required); owner access hooks; downloads only through an authorised, audited endpoint (`sandbox` CSP, `no-store`). |
| Notifications (`Strategy.Platform.Notifications` + `.Contracts`, schema `notify`) | Templates in code, **editable AR / EN** in Settings → Notifications with preview and reset; bilingual side-by-side e-mail (Arabic right, English left) with branding; **outbox** delivery with retry and back-off, dead messages re-queueable; sensitive values encrypted while queued; delivery log; **in-app centre** with a live SignalR bell; preferences per event and a **daily digest**. The sample module notifies a user when an item is shared with them (worked example). |
| Outbox and jobs (`Strategy.Platform.Scheduling`, schema `scheduling`) | Transactional outbox (`FOR UPDATE SKIP LOCKED`, safe with several instances); **Quartz.NET** clustered PostgreSQL store; run log; **job dashboard** (Settings → Jobs: schedule, next / last run, recent runs, run now, pause / resume, queue status, re-queue). Jobs: audit chain verification, digest, file re-scan, directory comparison, clean-up. |
| PDF and Excel (`Strategy.Platform.Reporting`) | `IPdfRenderer` through **Gotenberg** with embedded IBM Plex Sans Arabic (OFL); bilingual `ReportDocument`; `ExcelExport` (right-to-left sheet for Arabic). |
| Interim placements closed (ADR 0014, 0017) | **Employee photos** and **branding logos** → Files (endpoints unchanged, bytes carried over by migration + seeder). **`identity.auth_event`** → audit trail (rows carried into the chain with their time, user and IP; `/identity/admin/auth-events` reads the trail). **Access / role / permission / settings changes** now show old → new (role permission rows appear in the role's history). **Identity e-mails** (invitation, reset, e-mail code, test) → editable templates + outbox with retry. **Directory sync** → nightly comparison job. |
| Web | Shared **History tab** (timeline grouped per save, field / user filters, "view as of date") on the sample item and employee pages; **Audit Log** (`/audit`: search, system, object, action, channel, dates, Excel / PDF export); **Audit verification** (`/audit/verification`); **Documents tab** (upload with comment, versions, pdf.js / image preview, download, delete with reason, the server's refusal reason shown in the dialog); sample **record page**; **notification bell** (live) and centre; profile **notification preferences**; Settings → **Notifications**, **Files**, **Jobs**. AR + EN, RTL / LTR throughout. |

## Acceptance checks (BUILD_PLAN Phase 3)

| Check | Evidence |
|---|---|
| Editing a record shows old → new in its History tab with user, time and channel | Integration `AuditTrailTests`: an update writes **exactly the changed fields** (`NameEn`, `DescriptionAr`) with old / new value, user id and name, channel `ui`, the request's correlation id, one change set; the create snapshot; **as-of** before / between / after; outside the scope → **404**, no permission → 403; a refused save (409) **writes nothing**; role permission scope change `unit → all` in the role's history; settings document old → new. **E2E** (ar + en): History tab after an edit (`*-record-history.png`). Vitest: `HistoryTab` grouping, labels in both languages. |
| Manually altering an audit row in the DB is flagged by the verification screen | Integration `AuditChainTests`: a DBA disables the trigger and edits a value → `hashMismatch` at that `seq` and an in-app alert to the verifiers; a deleted row → `missingEntry`; a cut-off tail after a checkpoint → `truncated`; the **runtime role** cannot update / delete / truncate (42501) nor disable the trigger, the owner hits the trigger. Unit: tampering table (value, user, time, delete, reorder, re-hash). **E2E**: verification screen (`*-audit-verification.png`). |
| Audit Log searches across systems and exports | Integration: text, action prefix and kind filters; Excel export opened with ClosedXML (Arabic headers right-to-left, English left-to-right); export is itself audited; non-holders 403; auditors may search but not verify. PDF export through Gotenberg checked with PdfPig. **E2E**: search, Excel download (`*-audit-log.png`). |
| Upload of a disallowed type is rejected; allowed file downloads only for authorised users | Integration `FileServiceTests`: exe, SVG named `.png`, HTML, PDF named `.docx`, fake ZIP named `.xlsx` → `files.errors.typeNotAllowed`, each refusal audited; too large / empty refused; versions, comment, download of each version; an out-of-scope user gets **404** on content / list / versions / upload / delete and a **`file.download.denied`** audit entry with their user id, and no download entry is written; readers cannot upload or delete. Unit: sniffing table, OOXML, safe names, ClamAV protocol against a fake clamd. **E2E**: upload, preview, refusal of a disguised file (`*-record-documents.png`, `*-document-preview.png`). |
| Bilingual email and bilingual PDF render correctly (Arabic shaping, RTL) | **Mailpit** (real SMTP, MailKit): password reset arrives with subject `AR \| EN`, Arabic column `dir="rtl"`, English column, link button, text part in both languages. **PDF** (real Gotenberg): embedded IBM Plex Sans Arabic subsets only; Arabic glyphs mapped to contextual presentation forms incl. the lam-alef ligature (proof of shaping); Arabic lines read right to left give back the logical text; Arabic title ends at the right margin; Arabic table's first column on the right; **layout snapshot** committed (`tests/Strategy.IntegrationTests/Reporting/Snapshots/bilingual-report.layout.json`). **E2E**: the share e-mail in Mailpit in both languages; the PDF rendered by pdf.js. |
| Job dashboard shows scheduled jobs and their runs | Integration `JobDashboardTests`: five jobs with names and next run; "run now" records a succeeded run with its result and who started it, audited; pause / resume; unknown job 404; view-only holder cannot run. **E2E**: run now, dashboard (`*-job-dashboard.png`). |

**Also asked for in this phase:**

| Request | Evidence |
|---|---|
| A notification is delivered in both languages to Mailpit | `MailpitDeliveryTests` (above) and E2E `notifications` scenario |
| An upgraded Phase 2 database keeps its photos, logos and history | `MigrationTests.Upgrading_a_phase_2_database_keeps_photos_logos_and_sign_in_history`: Phase 2 schema + a photo, a logo and a sign-in event; after `--migrate` the photo and logo bytes are identical in the file store (original dates on the versions), the sign-in event is in the audit trail with its time, IP and user (channel `import`), the employee's placement history is intact, staging tables are empty, old tables dropped, and the full chain verifies |
| Identity e-mails through Notifications with retry | `SignInTests` (codes, reset, invitation via templates and the outbox), `NotificationTests` (a failed delivery is retried with back-off, delivery log `failed` then `sent`; codes never stored in clear in the outbox) |

## Quality gates run

| Gate | Result |
|---|---|
| `dotnet build Strategy.slnx -c Release` | 0 warnings, 0 errors |
| `dotnet format --verify-no-changes` | clean |
| `dotnet test --solution Strategy.slnx` | **832 / 832** passed: unit 281 · architecture 30 · integration 521 on real PostgreSQL, plus Mailpit and Gotenberg containers, and the Phase 1 Keycloak / OpenLDAP tests. The generated authorization matrix covers every new endpoint |
| `tools/check-vulnerable.sh` · `tools/check-api-client.sh` | no High / Critical · OpenAPI document and TS client up to date |
| `npm run lint` · `format:check` · `typecheck` | clean |
| `npm test` (Vitest) | **72 / 72** (new: History tab in both languages; every server message key has AR + EN text) |
| `npm run build` · `npm audit --audit-level=high` · `npm run lint:licenses` | OK · no High / Critical (the same 14 moderate dev-tool advisories as Phase 2) · 610 packages allowed |
| `npx playwright test` | **48 / 48** (24 scenarios × ar + en; 4 new Phase 3 scenarios) |
| `--migrate` on an empty database and on a Phase 2 database | all schemas applied (`audit`, `scheduling`, `files`, `settings`, `notify`, `access`, `core`, `identity`, `sample`), then the seeders |

**New dependencies** (all in `THIRD_PARTY_LICENSES.md`): Quartz.Extensions.Hosting + Quartz.Serialization.SystemTextJson (Apache-2.0),
AWSSDK.S3 (Apache-2.0), PdfPig (Apache-2.0, tests only), `@microsoft/signalr` (MIT), `pdfjs-dist` (Apache-2.0). The IBM Plex Sans Arabic
WOFF2 files (OFL) are embedded for PDFs.

## Decisions (ADRs)
- [0018](../decisions/0018-audit-trail-hash-chain.md) Audit trail: one table and one gap-free chain, rows written in the business
  transaction, canonical hashing, triggers + two database roles, legacy records chained through a staging table.
- [0019](../decisions/0019-files-service.md) Files: purposes in code, content sniffing, local / S3 store, ClamAV, owner access hooks,
  pdf.js settings forced by the CSP.
- [0020](../decisions/0020-notifications-outbox-and-jobs.md) Notifications, outbox and Quartz: templates in code + DB overrides, the
  dispatcher as a hosted worker, SMTP settings stay in `identity`, schema `scheduling`.
- [0021](../decisions/0021-pdf-rendering.md) Gotenberg with embedded Arabic fonts and the PDF snapshot test.
- ADR 0014 is **closed**; ADR 0017's interim logo table is gone.

## Deviations
- **From the SPEC:** none.
- **From the plan:**
  - the outbox dispatcher is a hosted worker (signalled, 5 s poll), not a Quartz job (sub-second delivery; the dashboard shows the queue);
  - the SMTP connection settings stay in `identity.smtp_settings` (screen unchanged) and Notifications read them through a contract;
  - History UI is on the sample item and employee pages; units and roles have the audit endpoints, their UI comes with later screens.
- **CLAUDE.md:** the platform schema list now includes `scheduling`; the commands section mentions the migrations connection and the
  sidecar containers of the integration tests.

## Fixes found along the way
- **User picker race (Phase 2 component):** closing a dropdown could hand the focus back for a moment and close the user list right after
  typing started (an intermittent E2E failure). The picker now closes on an outside pointer press, Escape or Tab, not on blur.
- **SignalR negotiate returned 500**: the pipeline had no authorization middleware for `[Authorize]`; `UseAuthorization()` added, hub
  covered by a test (anonymous 401, signed in 200).
- **pdf.js preview** drew wrong glyphs: it substituted the SPA's own IBM Plex web font for the PDF's embedded subsets, and the CSP blocked its
  inline `@font-face`. Now `useSystemFonts: false` + `disableFontFace: true`.
- **jsonb audit values** compared unequal after a PostgreSQL round trip (spacing, key order); they are compacted with sorted keys before
  comparing. Audit snapshots keep Arabic readable (no `\uXXXX` escapes).
- **OpenAPI schema name clash** (`PreferencesInput` in Identity and Notifications) silently replaced the identity type in the TS client;
  the notification types are now `NotificationPreferences*`.

## Known gaps (by design: later phases, or environment)
- **Retention purge** of the audit trail is not offered (default "keep forever"; an owner-run archive procedure later). External anchoring
  of the chain head is a possible hardening step.
- **S3 provider** is implemented but not tested here (no MinIO image through the registry mirror). **ClamAV** is tested against a fake clamd,
  not the real container.
- **Approval / workflow notifications** and integration events arrive with the approval engine (Phase 4), using the same outbox.
- **Directory sync** compares the configured source with the employees and reports; applying changes is the customer connector's job.
- **On-behalf-of** is stored on every entry; it is filled once delegation is used by the approval engine (Phase 4).
- History UI for units and roles (endpoints exist).

## How to demo
```bash
REGISTRY_PREFIX=mirror.gcr.io/ docker compose -f deploy/docker-compose.yml --profile dev up -d postgres gotenberg mailpit
dotnet run --project src/Host/Strategy.Host          # Development: migrations, seeders, demo seed
cd web && npm ci && npm run dev                       # http://localhost:5173 ; Mailpit UI http://localhost:8025
```
1. **Ahmed** (`ahmed@strategy.local` / `Strategy#Demo2026`): Sample module → open an item → *Edit* the English name → **History** tab shows
   the old value struck through and the new one, with user, time and channel; pick a date in "View as of".
2. **Documents** tab: upload a PDF with a comment, preview it, upload a new version, look at the versions; try a `.pdf` that is really HTML —
   the dialog explains the refusal.
3. **Mariam** (administrator, MFA): Settings → Email: point it at Mailpit (`localhost:1025`, no security). Share an item with Ahmed (item
   editors API or the sample flow) → Ahmed's **bell** shows it live; the bilingual e-mail is in Mailpit.
4. **Audit log** (sidebar): search, filter by system / action / channel, export to Excel and PDF (Arabic PDF with correct shaping).
5. **Audit verification**: *Verify now* → intact. In `psql`, `ALTER TABLE audit.entry DISABLE TRIGGER entry_append_only; UPDATE audit.entry SET new_value = 'x' WHERE seq = 10;`
   and verify again → broken at entry 10, and the verifiers get an alert.
6. Settings → **Notifications**: edit the Arabic text of "Password reset", see the preview, reset to default; **Deliveries** tab.
   Settings → **Jobs**: run "Verify the audit chain" and open its runs. Profile → **Notifications**: switch on the daily digest.

## Cloud environment notes
- Docker is started manually (`dockerd`); images come through `mirror.gcr.io/`. Integration tests reuse one PostgreSQL with
  `STRATEGY_TEST_POSTGRES`; Mailpit and Gotenberg are started by Testcontainers.
- The sandbox's Chromium differs from `@playwright/test` 1.56.1: `E2E_CHROMIUM_PATH=/opt/pw-browsers/chromium`.
- Screenshots (generated by the E2E suite) are in [`docs/screenshots/phase-3/`](../screenshots/phase-3/), as `ar-*` and `en-*` pairs.
  **New in Phase 3:** `record-history`, `audit-log`, `audit-verification`, `record-documents`, `document-preview`, `notification-bell`,
  `notification-centre`, `notification-preferences`, `notification-templates`, `notification-template-editor`, `files-settings`,
  `job-dashboard`. **Earlier phases, retaken** with the new top bar and sidebar.
