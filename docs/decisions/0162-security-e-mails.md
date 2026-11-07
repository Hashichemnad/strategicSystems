# 0162 — Security e-mails (password, MFA, lockout, tokens, new device)

- **Status:** Accepted (Phase 19, stream M)
- **Context:** SPEC-ADDENDUM A3; Phase 18 residual "no e-mail on password / MFA change". The audit trail records every account event
  (ADR 0018), but the account holder is not told, so a takeover can go unnoticed. The e-mails must be bilingual, in the recipient's
  language, sent once, never carry secrets, and the user must not be able to switch them off.

## Decision

1. **Events and templates.** Eight mandatory templates `identity.security.*` (password changed, password reset, MFA enrolled, MFA removed /
   reset, recovery codes regenerated, locked out, personal access token created, new device) seeded with AR / EN subject and body, editable
   in Settings → Notifications. One sender, `SecurityNotifier` in Platform.Identity, called where the event happens (next to the existing
   `AuthEventLog` write). E-mail channel only. No button, no link, no code, no token value: only time, IP, device family, method / token
   name + prefix and the "if this wasn't you" guidance.
2. **Guidance text** is a setting (Administration → Security, AR / EN, optional) rather than text repeated in eight templates, so one change
   reaches every security e-mail; empty = the seeded default.
3. **Exactly once.** `NotificationRequest.IdempotencyKey`: the notification service records the key in `notify.sent_request`
   (`INSERT … ON CONFLICT DO NOTHING` in the transaction that queues the outbox messages) and queues nothing for a key it has seen. The e-mail
   payload carries `{key}:{address}`; the e-mail handler records it on `notify.email_delivery` (unique) and skips a delivery already made,
   which covers an outbox message handled twice. The event id is new per action; the lockout uses `lockout:{user}:{lockout end}` so the
   attempts made while the account stays locked are one event.
4. **Mandatory.** The templates are `Mandatory`, so preferences and the digest never suppress them and the preference screen shows them
   locked on (existing behaviour of mandatory templates).
5. **Recipient's language.** E-mails stay bilingual side by side (CLAUDE.md §10). The renderer now puts the recipient's language first in
   the subject and sets the document `lang` / `dir`; this applies to every notification e-mail (users without a language: Arabic first, as
   before).
6. **New device / IP — optional, off by default** (`notify_new_device`). Known devices are stored per user as a SHA-256 fingerprint of
   browser family, OS family and IP prefix (/24, /48), at most 20; no raw IP or user agent beyond what the audit and sessions already keep.
   Devices are remembered even while the switch is off (so turning it on does not mail every user at their next sign-in); the first device
   of an account never triggers a mail.
7. **Who gets what.** Accounts of external providers get the events that apply to them (no local password → no password e-mails); vendor
   (Wasel) accounts get them like internal users.

## Consequences
- One extra e-mail per security action; a failing mail server never blocks the action (the outbox retries).
- The idempotency table grows by one row per security e-mail (tiny); it is a technical table like the outbox.
- Changing the guidance text needs the security-settings permission, the template text the notification-template permission.
