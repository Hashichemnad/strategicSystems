# 0150 — First-run setup wizard and the one-time setup token

- **Status:** Proposed (Phase 18)
- **Context:** SPEC §0.5 asks for a guided installer and a first-run setup wizard (organisation profile, branding, languages, admin
  account, email server, authentication provider, AI provider (skippable), org-structure import, modules to enable). A fresh installation
  has no account at all, so the first step must be reachable without signing in — the most sensitive unauthenticated surface of the
  product. `--create-admin` (Phase 17) stays the scripted alternative.

## Decision
- **Two stages.** Stage 1 is public but needs the **one-time setup token**: organisation profile (names AR / EN, short code, time zone,
  currency, date format), branding (primary colour, e-mail header / footer), languages (default interface language, Hijri, Arabic-Indic
  digits) and the first administrator (employee + account) are sent in **one** call (`POST /api/v1/setup/bootstrap`). Stage 2 runs
  **signed in**, after the administrator's mandatory MFA enrolment, and reuses the existing, permission-checked Settings screens and
  endpoints (e-mail server with test e-mail, sign-in providers, AI provider with test connection, business hours and holidays,
  organisation import with templates, systems, optional demo pack). Every stage-2 step can be skipped; `POST /api/v1/setup/complete`
  (`settings.setup.manage`, new permission on the Settings administrator role) closes it.
- **The wizard is open only while no administrator exists**: no active user holds the `access.administrator` role (directly or through a
  unit / everyone assignment), new `IUserRoleQuery.AnyPlatformAdministratorAsync`. Once one exists — through the wizard, `--create-admin`
  or the Development seed — the token is refused and no new one is issued.
- **Token.** 32 random bytes, base64url. Only its SHA-256 is stored (`identity.setup_state`, one row); compared in constant time. Issued at
  every start-up while no administrator exists (the previous one stops working) and written **once to the log** at warning level in a
  framed block; `--setup-token` issues and prints a new one (exit 1 when an administrator exists, 3 when not migrated). Valid for
  `Setup:TokenLifetimeHours` (24, 1–168); consumed by a successful bootstrap. Sent in the `X-Setup-Token` header (never in a URL, so it
  does not reach proxy logs or browser history). `verify-token` and `bootstrap` use the sign-in rate limit; every verification,
  bootstrap (success / failure) and issue is audited (`setup.token.*`, `setup.bootstrap`, entity `identity.setup`). The token value is
  never audited.
- **Status** (`GET /api/v1/setup/status`, public) returns only `needsSetup` and `setupCompleted` — not the version (ASVS 14.3.3).
- **Order of writes.** Everything is validated first (required fields, regional and branding rules, password policy, user name free), so
  a refused bootstrap leaves nothing behind; then Regional and Branding settings, the root unit (highest active level, or the existing
  root), the administrator's employee, the account (language = the default language) and the platform administrator roles.
- **Existing installations** get a completed `setup_state` row from the migration when accounts exist; the Development seed marks the
  setup completed, so neither is sent to the wizard. An administrator created by `--create-admin` is sent to stage 2 (from the home page
  only) until it is finished.
- **Default language.** `RegionalSettings.DefaultLanguage` (null = follow the browser, as before) applies until a person chooses a
  language; editable in Settings → Regional.

## Consequences
- The only unauthenticated write is guarded by a secret that exists only in the server log / console of whoever installed the system.
- Several application instances started at once each issue a token; only the last one works (use `--setup-token`). Documented in the
  installation guide.
- The organisation-structure import, e-mail and identity-provider screens are not duplicated; changes to them show in the wizard.
