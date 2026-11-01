# Design note — Phase 3: Audit trail, Files, Notifications, Jobs, PDF

**Scope.** BUILD_PLAN Phase 3; SPEC §0.2 (audit trail, notifications, scheduler, documents); CLAUDE.md §8, §14.
Also closes the interim placements of Phases 1–2 (ADR 0014, ADR 0017):

| Interim placement | Phase 3 home |
|---|---|
| `core.employee_photo` (bytea) | Files service: purpose `org.employee-photo`, owner = employee. Migration carries the bytes over |
| `settings.branding_asset` (bytea) | Files service: purpose `settings.branding-logo`, owner = logo kind. Migration carries the bytes over |
| `identity.auth_event` | `audit.entry` (kind `event`, action `auth.*`). Old rows are carried into the hash chain; `/identity/admin/auth-events` reads the audit trail |
| Access / settings changes "logged" (Serilog + `created_*`/`updated_*`) | the audit interceptor records every field, old → new, for roles, role permissions, assignments, system access, record roles, systems, settings areas, lookups, numbering formats, feature flags, translations, identity settings |
| `IIdentityEmailSender` (direct SMTP, fixed templates) | Notifications: editable AR/EN templates `identity.*`, queued in the outbox, delivered with retry |

The SMTP connection settings stay in `identity.smtp_settings` and keep their screen (Settings → Email). Notifications read
them through a contract (ADR 0020). Moving the table would change endpoints and screens for no functional gain.

## 1. Projects

| Project | Contents | Referenced by |
|---|---|---|
| `Strategy.Platform.Audit.Contracts` | `IAuditLog` (explicit events), `AuditEvent`, `IAuditSink`, `AuditRecord`, `IAuditQuery` (history, events), attributes `[Audited]`, `[NotAudited]`, `[AuditMasked]`, `[AuditParent]` | Persistence, every component and module |
| `Strategy.Platform.Audit` | `AuditDbContext` (schema `audit`), hash chain writer, verification, legacy import, audit API, Excel / PDF export | Host |
| `Strategy.Platform.Files.Contracts` | `IFileService`, `FilePurpose` registry, `IFileOwnerAccess`, DTOs | Settings, Organisation, modules |
| `Strategy.Platform.Files` | `FilesDbContext` (schema `files`), `IFileStore` (local, S3), content inspection, ClamAV client, files API | Host |
| `Strategy.Platform.Notifications.Contracts` | `INotificationService`, `NotificationTemplateDefinition`, `IEmailTransport`, `ISmtpSettingsSource` | Identity, modules |
| `Strategy.Platform.Notifications` | `NotifyDbContext` (schema `notify`), templates, renderer, in-app centre (SignalR hub), preferences, digest, email delivery | Host |
| `Strategy.Platform.Scheduling` | Quartz.NET (PostgreSQL store, schema `scheduling`), `IOutbox` + outbox table, job registry, run log, jobs API | Host; `IOutbox` / `IJobDefinition` used by components |
| `Strategy.Platform.Reporting` | `IPdfRenderer` (Gotenberg), bilingual HTML document builder with embedded Arabic fonts, `ExcelExport` helpers | Audit, modules |

Dependency direction stays acyclic: Kernel ← Audit.Contracts ← Persistence ← Settings ← … Files / Notifications implementations
reference Settings (typed settings); Settings and Organisation reference only `Files.Contracts`.

## 2. Audit trail (`Platform.Audit`, schema `audit`)

### 2.1 `audit.entry` (append-only)
| Column | Type | Notes |
|---|---|---|
| `seq` | bigint identity | chain order |
| `id` | uuid (v7) | |
| `occurred_at` | timestamptz | truncated to microseconds before hashing |
| `kind` | text | `change` or `event` |
| `action` | text | `create` / `update` / `delete` for changes; event code for events (`auth.login`, `file.download`, `file.download.denied`, `export`, `audit.verify`, …) |
| `system_code` | text | system of the record (`sample`, `org`, `access`, `settings`, `identity`, …) — Audit Log filter |
| `entity_type` | text | stable code, e.g. `sample.item`, `access.role` |
| `record_id` | text | the record key (Guid, or composite `a/b` for link rows) |
| `parent_type`, `parent_id` | text | the owning record of a child row (a role's permission row → the role) so the owner's History tab shows it |
| `field` | text null | one row **per changed field** on update; null on create / delete / event |
| `old_value`, `new_value` | text null | update: the two values; create: `new_value` = JSON snapshot of all audited fields; delete: `old_value` = snapshot; masked fields store `***` |
| `change_set_id` | uuid | groups the rows of one `SaveChanges` |
| `user_id`, `user_name` | uuid / text | actor (name snapshotted) |
| `on_behalf_of_id`, `on_behalf_of_name` | uuid / text | delegation (set by the approval engine in Phase 4; the column and context value exist now) |
| `channel` | text | `ui` · `api` · `mcp` · `ai` · `job` · `import` · `wasel` · `system` |
| `correlation_id`, `ip_address`, `user_agent` | text | from `IRequestContext` |
| `reason` | text null | optional reason / comment |
| `success` | boolean null | events only |
| `data` | jsonb null | event detail (never secrets or file contents) |
| `prev_hash`, `hash` | bytea | `hash = SHA-256(prev_hash ‖ canonical_json(row))`, genesis `prev_hash` = 32 zero bytes |

Indexes: `(entity_type, record_id, seq)`, `(parent_type, parent_id, seq)`, `(occurred_at)`, `(user_id, occurred_at)`, `(system_code, occurred_at)`, `(action)`.

**Canonical JSON** (`AuditCanonical.Serialize`): a fixed property order (all columns above except `seq`'s position first, `prev_hash`, `hash`),
UTF-8, no whitespace, `occurred_at` as `yyyy-MM-ddTHH:mm:ss.ffffffZ`, Guids lower-case `D`, nulls written as `null`, `data`
re-serialised from the parsed JSON with sorted keys so PostgreSQL's `jsonb` normalisation cannot change the hash.

### 2.2 Writing — one chain, inside the business transaction
- `AuditTrailInterceptor` (Persistence, registered on every module DbContext):
  1. `SavingChanges`: collects audited entries (types that are `IAuditable`, or carry `[Audited]`; `[NotAudited]` excludes a type or property). For each Added / Modified / Deleted entry it captures the field values (Modified: only properties whose value really changed). If the context has no transaction yet it **begins one** and owns it.
  2. `SavedChanges`: writes the rows through `IAuditSink` **on the same connection and transaction**, then commits the owned transaction. `SaveChangesFailed` rolls it back. A business change and its audit rows commit together or not at all.
- `AuditChainWriter` (`IAuditSink`): `SELECT pg_advisory_xact_lock(<audit key>)`, read the last `hash`, compute each row's hash, multi-row `INSERT`. The lock is held until the business transaction ends, so the chain is strictly serial (audited writes are low-volume business operations; ADR 0018).
- Explicit events (`IAuditLog.WriteAsync(AuditEvent)`): logins, logouts, failed logins, MFA, token use, file upload / download / deletion, refused downloads, exports, verification runs, job "run now". They open their own connection + transaction from the data source.
- Values: `DateTimeOffset` ISO 8601, `DateOnly` `yyyy-MM-dd`, enums by name, `byte[]` → `<n bytes>`, strings truncated to 4000 chars. `[AuditMasked]` (password hashes, security stamps, authenticator keys, encrypted secrets) → `***`. Identity `users` columns that change on every sign-in (`access_failed_count`, `last_sign_in_at`, `concurrency_stamp`) are `[NotAudited]`.
- `user_name` is the `name` claim at write time (no lookup, so it also works for jobs and failed logins).

### 2.3 Append-only at the database
- Migration: trigger `entry_append_only` (BEFORE UPDATE OR DELETE, and BEFORE TRUNCATE) raises `audit.entry is append-only`; `REVOKE UPDATE, DELETE, TRUNCATE ON audit.entry FROM PUBLIC`.
- Two database roles (recommended for installs, ADR 0018): `ConnectionStrings:Strategy` (runtime) and optional `ConnectionStrings:StrategyMigrations` (owner; used by `--migrate`). When the runtime role differs from the owner, the audit seeder grants it `USAGE` on schema `audit`, `SELECT, INSERT` on `audit.entry` and nothing else (`REVOKE UPDATE, DELETE, TRUNCATE`). The runtime role is then physically unable to alter rows; the trigger blocks the owner too (only a superuser disabling the trigger could, and the chain detects that).
- `deploy/` creates the two roles in the PostgreSQL container on first start (`deploy/postgres/init-roles.sh`).

### 2.4 Verification
- `AuditVerifier` walks the chain in `seq` order from the last good checkpoint (or from genesis for a full run), recomputes every hash and checks every `prev_hash`. Result in `audit.verification_run` (started, finished, mode, entries checked, ok, first broken `seq`, `messageKey`). `audit.checkpoint` keeps the last verified `(seq, hash)`; a later run that cannot find that row at that position reports **truncation**.
- Quartz job `audit.verify-chain` (daily 02:10, incremental) + "Verify now" (full) on the verification screen. A failure notifies the holders of `audit.chain.verify` (in-app + email, template `audit.chainBroken`).

### 2.5 Read side
- `IAuditQuery.HistoryAsync(entityType, recordId)` — the record and its child rows. Modules expose `GET …/{id}/history` **after their own record check** (404 outside scope). Done for sample items, employees, units, roles.
- `IAuditQuery.AsOfAsync(entityType, recordId, at)` — rebuilds the field values at a date from the create snapshot plus later field updates. `GET …/{id}/history/as-of?at=`.
- Global Audit Log: `GET /api/v1/audit/entries` (filters: text, user, system, entity type, record id, action, channel, kind, date range; paged), `GET /api/v1/audit/entries/export?format=xlsx|pdf` (bilingual; logged as an `export` event).
- Retention: setting `audit.retentionDays` (default **0 = keep forever**, SPEC). Purge is not offered in the UI (an append-only table cannot be pruned by the application role); recorded as a known gap.

### 2.6 Permissions (system `audit`, platform)
| Permission | Default roles |
|---|---|
| `audit.log.view` — global Audit Log | Audit administrator, Auditor |
| `audit.log.export` | Audit administrator, Auditor |
| `audit.chain.verify` — verification screen, run verification | Audit administrator |

"Audit administrator" joins the platform administrator roles (ADR 0016); existing administrators receive it on upgrade.

## 3. Files (`Platform.Files`, schema `files`)
| Table | Columns |
|---|---|
| `file` | `id`, `purpose`, `owner_type`, `owner_id`, `name`, `current_version_id`, `status` (`active` / `deleted`), `deleted_at`, `deleted_by`, `delete_reason`, audit columns, `xmin` |
| `file_version` | `id`, `file_id`, `version_no`, `storage_key`, `size_bytes`, `content_type`, `sha256`, `scan_status` (`not_scanned` / `clean` / `infected` / `pending` / `failed`), `uploaded_at`, `uploaded_by`, `comment` |
| `pending_import` | staging for the upgrade (photos, logos): bytes + target ids; drained into the store by the Files seeder |

- **Purposes** registered in code (`FilePurpose`): key, owner type, allowed content types, max size, versioned?, single file per owner?, whether scanning is required. Global limits in Settings → Files (`FilesSettings`: max upload MB, ClamAV on/off and endpoint, blocked extensions).
- **Checks on upload:** size ≤ min(purpose, global) · extension and declared type in the purpose allow-list · **magic number** sniffing (`ContentInspector`: PDF, PNG, JPEG, GIF, WebP, ZIP-based Office (docx/xlsx/pptx, checked by the inner `[Content_Types].xml`), legacy OLE Office, plain text/CSV (valid UTF-8, no NUL)) — the sniffed type must match the extension · SVG / HTML / executables never allowed · file name sanitised. Optional **ClamAV** (`INSTREAM` over TCP, in-house client): `infected` → refused and audited; ClamAV unreachable while required → version kept `pending` and not downloadable until a rescan job succeeds.
- **Storage** `IFileStore`: `LocalFileStore` (volume `Files:LocalPath`, outside the web root, keys `yyyy/MM/<guid>`), `S3FileStore` (S3-compatible, e.g. MinIO; `Files:Provider = s3`). Content is addressed by version id; nothing is overwritten.
- **Access:** owners register `IFileOwnerAccess` for their owner type (`CanReadAsync`, `CanWriteAsync` through `IAccessService`). Generic attachments API `/api/v1/files/{ownerType}/{ownerId}` (list, upload, new version, delete with reason) and `GET /api/v1/files/{fileId}/versions/{versionId}/content` (download / inline preview). Out of scope → **404** and an audit event `file.download.denied` (success = false); audited `file.upload`, `file.download`, `file.delete`.
- Employee photo and branding logos keep their endpoints (the SPA does not change) but store through `IFileService`.
- **Preview:** SPA `FilePreview` — images inline, PDFs with **pdf.js** (`pdfjs-dist`, Apache-2.0); other types download.

## 4. Notifications (`Platform.Notifications`, schema `notify`)
| Table | Columns |
|---|---|
| `template` | `key`, `subject_ar/en`, `body_ar/en` (plain text lines with `{{placeholders}}`, `{{button}}`), `customised`, audit columns. Defaults declared in code (`NotificationTemplateDefinition`), seeded when missing, "reset to default" restores them |
| `notification` | in-app items: `id`, `user_id`, `template_key`, `params` (jsonb), `link`, `created_at`, `read_at` |
| `preference` | `user_id`, `event_key` (or `*`), `email`, `in_app`, `digest` |
| `delivery` | e-mail delivery log: `outbox_id`, `to`, `template_key`, `status`, `attempts`, `last_error`, `sent_at` (for the admin delivery screen) |

- `INotificationService.SendAsync(NotificationRequest)` (template key, recipients = user ids or addresses with language, params, link, channels, `mandatory` for security mails that ignore preferences) writes **one outbox message per recipient and channel in the caller's transaction**.
- **Email layout** (`EmailRenderer`, replaces `IdentityEmailRenderer`): one HTML table, Arabic column on the right (`dir="rtl"`, Arabic font stack), English column on the left, subject `AR | EN`, branding header / footer / organisation names / primary colour from Settings, link button; text/plain alternative. Placeholders are HTML-encoded.
- **In-app:** `notify.notification` + SignalR hub `/hubs/notifications` (cookie auth, user group). Top-bar bell with unread count, dropdown, "mark all read", deep link opens the record. Notification centre page `/notifications`.
- **Preferences** (Profile → Notifications): per event e-mail / in-app; "daily digest" sends one bilingual summary of unread in-app items instead of individual e-mails (job `notifications.digest`, daily 07:20).
- Identity templates: `identity.passwordReset`, `identity.invitation`, `identity.emailCode`, `identity.test`; marked `mandatory` (never suppressed by preferences, never digested). The e-mail code expires in minutes, so its outbox message carries `expires_at` and is dropped after expiry.

## 5. Outbox and jobs (`Platform.Scheduling`, schema `scheduling`)
- `scheduling.outbox_message`: `id`, `type` (`email`, `in-app`, integration event names later), `payload` jsonb, `created_at`, `available_at`, `attempts`, `last_error`, `processed_at`, `dead_at`, `expires_at`, `correlation_id`. `IOutbox.EnqueueAsync(DbContext, message)` inserts on the **caller's DbContext connection and transaction** (infrastructure table, like the numbering counter).
- **Dispatcher:** Quartz job `outbox.dispatch` (every 15 s, `[DisallowConcurrentExecution]`) + an in-process nudge after commit so a message is normally handled within a second. Rows are claimed with `FOR UPDATE SKIP LOCKED` (safe with several app instances). Handlers per type (`IOutboxHandler`); retry with exponential back-off (30 s, 2 min, 10 min, 30 min, 2 h, then every 6 h) up to 10 attempts → dead; dead messages listed and re-queueable by admins.
- **Quartz.NET** (Apache-2.0): persistent ADO store on PostgreSQL (tables `scheduling.qrtz_*`, created by migration from the official script), clustered, System.Text.Json serializer. `IJobDefinition` (key, AR/EN name, cron, job type) registered by components; a listener writes `scheduling.job_run` (job, fired at, finished at, status, message, `triggered_by`).
- Jobs in Phase 3: `outbox.dispatch`, `audit.verify-chain`, `notifications.digest`, `files.rescan-pending`, `scheduling.cleanup` (old runs / processed outbox rows after 30 days), `org.directory-sync` (calls the existing `IDirectorySyncSource`, closes the last ADR 0014 item).
- **Job dashboard** (Settings → Jobs): job, schedule, next run, last run (status, duration, message), recent runs, "Run now", pause / resume. Permissions `settings.job.view`, `settings.job.manage`.

## 6. PDF and Excel (`Platform.Reporting`)
- `IPdfRenderer.RenderAsync(HtmlDocument)` → `POST {Gotenberg:Url}/forms/chromium/convert/html` (multipart: `index.html`, font files, `preferCssPageSize`, `printBackground`). Fonts **IBM Plex Sans Arabic** and **IBM Plex Sans** (OFL, already used by the SPA) are embedded resources, referenced with `@font-face` so Chromium shapes Arabic correctly.
- `BilingualDocument` builder: title AR/EN, header / footer branding, tables rendered with `dir` per language.
- `ExcelExport.Sheet(rows, columns, language)`: right-to-left sheet for Arabic (the access review already does this).
- Health: `gotenberg` readiness check (tagged `optional`, does not fail `/health/ready` when PDFs are not configured).

## 7. Settings / web screens
| Screen | Route | Permission |
|---|---|---|
| Record **History** tab (timeline, field / user filters, "as of date") | sample item, employee, unit, role pages | the record's read permission |
| Global **Audit Log** (search, filters, export xlsx / pdf) | `/audit` | `audit.log.view` (+ export) |
| **Audit verification** | `/audit/verification` | `audit.chain.verify` |
| **Documents** tab (upload, versions, preview, download, delete) | sample item page | record permission |
| Notification bell + centre | top bar, `/notifications` | signed in |
| Notification preferences | Profile → Notifications | signed in |
| Notification templates (edit AR/EN, preview, reset) + deliveries | Settings → Notifications | `settings.notification.manage` |
| Files settings | Settings → Files | `settings.file.manage` |
| Job dashboard | Settings → Jobs | `settings.job.view` / `manage` |

The sample module gets a **record page** (`/sample/items/{id}`) with Overview, Documents and History tabs, as the worked example for modules.

## 8. Events in / out
- Out: e-mail and in-app messages (outbox). Integration events use the same outbox from Phase 4.
- In: none.

## 9. Tests
- **Unit:** canonical JSON + hash; chain verification on a tampered list; interceptor value formatting / masking; content sniffing table (allowed, renamed, disguised); template rendering (AR / EN, encoding, branding); back-off schedule; cron registry.
- **Integration (real PostgreSQL):** an update writes exactly the changed fields with old → new, user, channel, correlation id; create / delete snapshots; rollback leaves no audit rows; `UPDATE audit.entry` refused (trigger; and permission denied for a runtime role); tampering detected by the verification job (altered value, deleted row, truncated tail); upload of a disallowed / disguised type rejected; download by an unauthorised user → 404 + `file.download.denied`; notification delivered **to Mailpit** with AR and EN parts; outbox retry after an SMTP failure; Phase 2 database upgrade keeps photos, logos, auth events and history; job dashboard lists jobs and runs; every new endpoint in the generated authorization matrix.
- **PDF:** Gotenberg container renders a bilingual document; PdfPig (Apache-2.0, tests only) checks embedded Arabic font, Arabic text extracted in logical order, and a **layout snapshot** (words, fonts, rounded positions) committed under `tests/…/Snapshots/`.
- **E2E (ar + en):** History tab after an edit, Audit Log search + export, verification screen, Documents tab upload / preview, notification bell, templates, job dashboard; screenshots in `docs/screenshots/phase-3/`.
