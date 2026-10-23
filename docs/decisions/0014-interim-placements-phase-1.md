# 0014 — Interim placements until later platform services exist

- **Status:** Accepted (Phase 1, each item temporary)
- **Context:** Phase 1 needs a few things whose permanent home is built in a later phase. Waiting would block the acceptance checks, so each one gets an interim home behind an interface that the later phase replaces.

## Decision
| Need | Phase 1 | Permanent home |
|---|---|---|
| Employee photo | `core.employee_photo` (bytea, PNG/JPEG/WebP by magic number, ≤ 1 MB), served through an authorised endpoint | Files service (Phase 3): migrated into `files`, row removed |
| Employee grade | free code text (e.g. `G10`) | Lookup `employee.grade` (Phase 2) |
| Auth events (logins, MFA, lockout, tokens, provider changes) | `identity.auth_event` (append-only by convention) + structured Serilog events | `audit.entry` (Phase 3); the table is then migrated into the audit chain |
| Email (password reset, email code, invitation, test) | `IIdentityEmailSender` with MailKit and the SMTP settings screen (owner choice: screen now) | Notifications templates + outbox (Phase 3) reuse the same `identity.smtp_settings`, moved to `notify` |
| Org settings (`enforce_level_order`) and security settings | singleton tables with their own screens | Settings centre (Phase 2) groups the screens; storage stays |
| Directory (AD / HR) sync | `IDirectorySyncSource` + no-op stub; manual import works | Quartz job (Phase 3); connectors per customer |

## Consequences
Each later phase lists the matching item in its plan and removes the interim code.
