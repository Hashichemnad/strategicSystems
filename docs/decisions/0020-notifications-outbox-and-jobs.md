# 0020 — Notifications, outbox and scheduled jobs

- **Status:** Accepted (Phase 3)
- **Context:** CLAUDE.md §5 (transactional outbox), §14 (templates, e-mail + in-app, preferences, digest, retry, < 5 min; Quartz jobs with a
  dashboard); ADR 0014 left the identity e-mails on a direct SMTP sender with fixed templates.

## Decision
- **Templates:** declared in code (`NotificationTemplateDefinition`), seeded into `notify.template`, editable AR / EN (plain text lines with
  `{{placeholders}}`, `{{button}}`, `{{code}}`; no HTML from administrators, every value HTML-encoded), "reset to default". Names and the
  mandatory flag follow the code; text follows the code until customised. `notifications.layout` holds the default organisation name and
  footer; branding overrides them.
- **E-mail layout:** one bilingual table, Arabic column on the right (`dir="rtl"`), English on the left, subject `AR | EN`, text/plain part
  in both languages.
- **Outbox:** `scheduling.outbox_message`; `IOutbox.EnqueueAsync(DbContext, …)` inserts on the caller's connection and transaction when one
  is open (else commits at once). A background worker (signalled on enqueue, polling every 5 s) claims messages with
  `FOR UPDATE SKIP LOCKED`, retries with back-off 30 s → 2 min → 10 min → 30 min → 2 h → every 6 h, and gives up after 10 attempts
  (dead messages are listed and re-queueable). Sign-in codes carry an expiry and are dropped after it. Sensitive values (codes, reset
  links) are encrypted with Data Protection while queued.
  The dispatcher is a hosted worker rather than a Quartz job: it needs sub-second latency, and the job dashboard shows the queue state.
- **Identity e-mails** (invitation, password reset, e-mail code, test) go through templates `identity.*` and the outbox; the API still
  answers "e-mail not configured" synchronously; delivery failures are retried and logged (`notify.delivery`). The SMTP connection
  settings stay in `identity.smtp_settings` and their screen (Settings → Email); Notifications read them through `ISmtpSettingsSource`.
  Moving the table would change endpoints and screens for no functional gain.
- **In-app:** `notify.notification`, SignalR hub `/hubs/notifications` (signed-in internal users only, own group), bell with live count,
  notification centre; preferences per event (e-mail / in-app) and a daily digest (`notifications.digest` job). Mandatory templates ignore
  preferences and the digest.
- **Quartz.NET 3.22** (Apache-2.0; 4.x was released weeks ago and is not yet adopted), clustered ADO store in schema `scheduling` (official
  table script embedded in the migration), System.Text.Json serializer, jobs declared with `AddScheduledJob(JobDefinition)`, a listener
  writing `scheduling.job_run`. Cron in the time zone `Scheduling:TimeZone` (default UTC; Asia/Dubai in the compose file). Jobs: audit
  chain verification, digest, file re-scan, directory comparison, clean-up.
- Schema **`scheduling`** is added to the platform schemas listed in CLAUDE.md §5 (outbox and Quartz tables).

## Consequences
Integration events between modules (Phase 4+) reuse the outbox with their own handler types.
