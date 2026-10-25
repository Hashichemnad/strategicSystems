# Phase 1 — Identity, Organisation and Employees: summary

**Status:** built and verified in the build session · awaiting owner acceptance
**Branch:** `claude/quirky-clarke-wsm17a`
**Owner decisions taken in planning:**
- interim access by a system-administrator flag;
- a small SMTP screen now;
- two-step verification required for administrators by default.

## What was built

| Area | Result |
|---|---|
| Organisation (`Strategy.Platform.Organisation`, schema `core`) | **Levels:** configurable; seeded Organisation › Sector › Department › Section › Unit; child-below-parent rule, which can be switched off. **Units:** effective-dated history (`org_unit_version`) and a materialised path for "unit and below". Edit, move and deactivate take an **effective date**; a move re-paths the whole subtree in one transaction; cycles are refused; tree and path can be read **as of any date**. **Employees:** unit, manager (no reporting cycles), status, placement history; photo (PNG / JPEG / WebP, magic-number check, 1 MB). Arabic-normalised trigram search. **Delegations:** date range, scope; no self-delegation, overlap or cycles. Contracts `IOrgDirectory`, `IEmployeeDirectory` and `IDelegationQuery` serve later phases. |
| Import | Bilingual Excel templates (ClosedXML) and CSV for units and employees. Every row is validated first; the report (row, column, translated message) is shown and can be downloaded as .xlsx. The commit is all-or-nothing and upserts by unit code / employee number. Parents and managers are resolved in any order within the file; user accounts can optionally be created. |
| Identity (`Strategy.Platform.Identity`, schema `identity`) | **Local accounts and sessions:** ASP.NET Core Identity with Guid v7 keys. Password policy, lockout and session limits are editable at runtime (**Administration → Security**). Server-side sessions: list, end one, sign out everywhere. **Two-step verification:** TOTP with QR, email code, single-use recovery codes; enforced for administrators (or everyone) by setting. Each authenticator code is accepted **once**: the last accepted time step is stored per user (`identity.users.totp_last_step`), and only a later step is accepted (same ±2-step drift window as ASP.NET Core Identity). This applies to sign-in, enrolment and profile set-up, and a concurrent second use of one code fails on the user row's concurrency stamp. **Mail and accounts:** password reset and invitation by email (MailKit, bilingual template, links only to the configured public address). **Access tokens:** `sst_…`, SHA-256 hashed, scoped `api.read` / `api.write` / `mcp`, lifetime capped by setting; the read scope is refused on writes; tokens skip antiforgery, cookies never do. **Auth events:** an append-only `auth_event` log, also written as structured Serilog events. |
| External sign-in (runtime, **disabled by default**) | **OpenID Connect** (Entra ID / ADFS / Keycloak), **UAE Pass** (staging / production presets, user-type filter), **SAML 2.0** (Sustainsys, signed AuthnRequests, generated SP certificate, SP metadata) and **LDAP / AD** (service bind, then user bind; escaped filters). Each provider is a scheme added or removed **while the app runs**; nothing goes in appsettings. Linking matches an **active** employee by email, employee number or user name; just-in-time account creation is optional and never creates employees. **Test connection** works for each type. Secrets are encrypted and never returned. |
| Data Protection | Key ring persisted in PostgreSQL (`core.data_protection_keys`), with an optional certificate encryption. Purpose-scoped `ISecretProtector` for TOTP keys, provider secrets, SMTP password, SAML key and session tickets. This closes the Phase 0 gap. |
| Interim access (ADR 0009) | `InterimAccessService` in every environment. Anonymous callers get 401. Org and identity directory reads need a signed-in internal user; every `manage` permission needs `is_system_admin`. Self-service checks ownership (another user's token / session / delegation → 404). |
| Host | Authentication runs before the request context (audit stamps see the user). Every non-public `/api` endpoint answers 401 `auth.required`. The `auth` rate-limit policy covers the sign-in endpoints. Bootstrap: `--create-admin --email --password-file`; a Development demo seed. |
| Web | **Sign-in area:** login (local + LDAP + provider buttons), MFA step, MFA enrolment, forgot / reset / invitation. **Profile:** preferences saved to the profile, password, MFA, recovery codes, sessions, tokens, delegations. **Organisation:** units tree + details + history + move, org chart **as of a date**, levels, employees (list, record, photo, placement history), import wizard. **Administration:** users, identity providers (typed editor per type, test, enable), security settings, email server. **New shared components:** Checkbox, Switch, Avatar, OtpInput, CopyField, FileDrop, TreeView (RTL arrow keys), OrgChart (CSS connectors, mirrored). AR / EN for all of it. |
| Docs | Design notes `docs/design/identity.md` and `docs/design/organisation.md`; ADRs 0009–0014; customer guide [`docs/identity-providers.md`](../identity-providers.md); `deploy/README.md` (first administrator, Data Protection keys); `.env.example` / compose (certificate settings); CLAUDE.md §18; THIRD_PARTY_LICENSES. |

Screenshots (generated by the E2E suite) are in [`docs/screenshots/phase-1/`](../screenshots/phase-1/), as `ar-*` and `en-*` pairs:
- sign-in: login, wrong password, MFA;
- invitation and password reset;
- profile: TOTP set-up, recovery codes, tokens;
- organisation: unit move, history, chart today and as of a past date, employee;
- import: validation report, done;
- administration: providers (editor, saved, list), security, email, users;
- the Phase 0 shell screens.

## Acceptance checks (BUILD_PLAN Phase 1)

| Check | Evidence |
|---|---|
| 4-level structure, re-parent, old structure kept | E2E `org.spec` (ar + en): Organisation › Sector › 2 Departments › Section; the section is moved in the UI; the history tab shows both placements; the chart today shows the new parent and the chart as of 2025-06-01 the old one. Integration tests check the as-of tree and path before and after a move. |
| Import from templates with validation report | E2E `import.spec`: the template downloads; a CSV with an unknown unit is refused with a row / column report; the corrected file is imported. Integration tests cover xlsx and csv, units and employees, and the report download. |
| Local login with MFA, lockout, reset by email (Mailpit) | E2E `auth.spec` / `profile.spec`: wrong password; administrator TOTP sign-in; TOTP enrolment and then sign-in with the code; invitation and password-reset emails read from **Mailpit**. Integration tests cover lockout after N failures and unlock, email OTP, single-use recovery codes, enforced enrolment, and **TOTP replay**: the same code a second time, an unused code of an earlier step, and the code that confirmed enrolment are all refused, while the next code works. |
| OIDC and SAML configured and tested against a test IdP; LDAP against a container | `ExternalProviderTests`: **Keycloak 26** container as the OIDC IdP, as UAE Pass (endpoint overrides) and as the SAML IdP; **OpenLDAP** container for LDAP. Each run covers configure through the admin API, test connection, enable, full browser-style flow, linking to the employee and session. The customer set-up for each provider is in [`docs/identity-providers.md`](../identity-providers.md); E2E `admin.spec` configures a provider in the UI. |
| PAT created, used, revoked | E2E `profile.spec`: the token is shown once; GET with the bearer token → 200; POST with the read scope → 403; revoked → 401. Integration tests cover expiry, a wrong secret, an inactive user and the scope rules. |
| Auth events visible in logs | Every sign-in, failure, lockout, MFA, reset, token, session and provider change → `identity.auth_event` and a structured log line `Auth event {AuthEventType} success=… user=… provider=… ip=…` (EventId 4100). Admin API `GET /api/v1/identity/admin/auth-events`; asserted in `SignInTests`. The screen comes with the audit UI (Phase 3). |

## Quality gates run (all green)

| Gate | Result |
|---|---|
| `dotnet build Strategy.slnx -c Release` | 0 warnings, 0 errors |
| `dotnet format --verify-no-changes` | clean |
| `dotnet test --solution Strategy.slnx` | **375 / 375** passed: unit 156 · architecture 30 · integration 189 on real PostgreSQL, including 5 external-IdP tests against Keycloak and OpenLDAP containers |
| `tools/check-vulnerable.sh` | no High / Critical advisories |
| `tools/check-api-client.sh` | OpenAPI document and TS client up to date |
| `npm run lint` (ESLint + RTL guard) · `format:check` · `typecheck` | clean |
| `npm test` (Vitest) | 64 / 64 passed. Includes AR / EN key parity, and a check that every server message key found in the C# sources has AR and EN text |
| `npm run build` · `npm audit --audit-level=high` · `npm run lint:licenses` | OK · 0 vulnerabilities · 580 packages allowed |
| `npx playwright test` | **26 / 26** passed (13 scenarios × ar + en), run twice with no flakes; the host is started by Playwright as in CI |
| `--migrate` on an empty database | all schemas (`core`, `identity`, `sample`) applied, including `identity` migration `TotpReplayProtection`, exit 0 |

## Decisions (ADRs)
- [0009](../decisions/0009-interim-system-admin-access.md) Interim access by system-admin flag until Phase 2 (supersedes 0005)
- [0010](../decisions/0010-platform-components-and-identity-schema.md) Platform components and the `identity` schema
- [0011](../decisions/0011-runtime-identity-providers.md) Runtime identity providers (dynamic schemes) and linking rules
- [0012](../decisions/0012-data-protection-and-secrets.md) Data Protection key storage and secret protection
- [0013](../decisions/0013-org-history-model.md) Organisation history: effective-dated versions + path
- [0014](../decisions/0014-interim-placements-phase-1.md) Interim placements:
  - photo bytes until Files;
  - grade as a code until lookups;
  - the auth event log until the audit trail;
  - the email sender until Notifications.

## Deviations
- **From the SPEC:** none.
- **From the plan:**
  - The external-provider tests use **Keycloak** for OIDC, UAE Pass and SAML instead of the planned mock-oauth2 and SimpleSAMLphp images. One realistic IdP covers all three protocols, and the image pulls through the mirror.
  - Identity tables are mapped by hand on `IdentityStoreDbContext` rather than `IdentityDbContext`, so they follow the snake_case and audit conventions.

## Fixes found along the way (by the new tests)
- Arabic "Revoke" (token) read the same as "Cancel" (إلغاء) in the same dialog. It is now "إبطال".
- Danger banners were announced politely (`status`). They are now alerts.
- Opening a unit by link did not expand the tree down to it. It does now.
- The file picker lost its accessible name once a file was chosen. It keeps it now.
- Dotted translation keys (`api.read`) were fragile with i18next's key separator. They are now plain keys.
- **TOTP replay (owner review):** ASP.NET Core Identity's authenticator provider accepted a code again within its ±90 s window. It is replaced by `ReplayProtectedAuthenticatorTokenProvider` (the same RFC 6238 codes, with each time step accepted once per user). The new integration tests fail without it. The test helpers (`Totp.NextCodeAsync` in the integration tests, `nextTotp` in E2E) now behave like an authenticator app and never reuse a step. E2E signs the administrator in once per worker and shares that session.
- **Licence rule:** the architecture test flagged six old transitive packages from Sustainsys.Saml2 2.11 that have no SPDX metadata. Four were MIT. Two, `NETStandard.Library` 1.6.1 and `Microsoft.NETCore.Platforms` 1.1.0, carry the non-OSI .NET Library licence (CLAUDE.md §4.3). The fix references the current MIT releases `Microsoft.IdentityModel.Tokens.Saml` 8.19.2 (aligned with the IdentityModel 8.x already used for OIDC) and `System.Configuration.ConfigurationManager` 10.0.13. The old chain is gone, and the SAML tests against Keycloak still pass. No ADR is needed because no non-allowed licence remains.
- Earlier, in the back end: the current-session claim was missing with server-side tickets; the auth rate limiter ran after authentication; hosted services started during build-time OpenAPI generation. All fixed, with tests.

## Known gaps (by design: later phases)
- **Access:** roles, data scopes and the permission catalogue come in Phase 2. Until then, administration is the system-admin flag (ADR 0009), and the sidebar hides administration from non-admins only cosmetically (the server refuses it).
- **Audit:** auth events are in `identity.auth_event` and the logs; the hash-chained audit trail and its UI come in Phase 3. Phase 1 business writes are stamped `created_*` / `updated_*` only.
- **Files and notifications:** photos are stored in `core.employee_photo` until the Files service (Phase 3). Emails are sent directly by the identity email sender (no outbox or retry) until Notifications (Phase 3).
- **Picklists:** grade is a free code until lookups (Phase 2).
- Vendor (Wasel) principals are reserved in the model only (Phase 11). OAuth 2.1 for MCP comes with OpenIddict (Phase 5).
- The GitHub Actions workflow now also runs a Mailpit service for the E2E job. It runs on GitHub when the pull request is opened.

## How to demo
```bash
docker compose -f deploy/docker-compose.yml up -d postgres mailpit
dotnet run --project src/Host/Strategy.Host          # http://localhost:5080 (Development: migrations + demo seed)
cd web && npm ci && npm run dev                       # http://localhost:5173
```
1. Sign in as `ahmed@strategy.local` / `Strategy#Demo2026` (a regular user, no administration).
2. Sign in as `mariam@strategy.local` (an administrator). Two-step verification is required, so the first sign-in walks through enrolment. Scan the QR code with any authenticator app. Save the recovery codes.
3. **Administration → Email server:**
   - Host `localhost`, port `1025`, security *None*, sender `strategy@strategy.local`;
   - Application address `http://localhost:5173`;
   - Send a test email, then open Mailpit at http://localhost:8025.
4. **Organisation → Units:**
   - add units below *Demo Organisation*;
   - **Move** one with today's date and open its **History** tab;
   - **Org chart → As of date** before today shows the old structure.
5. **Organisation → Import:** download the employees template, fill two rows (one with a wrong unit code), upload, read the report, fix it and import.
6. **Administration → Users → New account:** pick an employee and send an invitation. Open the link from Mailpit and set a password.
7. **Profile → Access tokens:** create a read token, then:
   ```bash
   curl -H "Authorization: Bearer sst_…" http://localhost:5080/api/v1/org/units/tree
   ```
   Revoke the token and run the call again: it answers 401.
8. **Administration → Identity providers:** follow [`docs/identity-providers.md`](../identity-providers.md). A provider is offered on the sign-in page only after **Enable**.
9. Switch **English / العربية** anywhere. The choice is saved to the profile and follows the user.

## Cloud environment notes
- As in Phase 0, the Docker daemon is started manually: `(dockerd > /tmp/dockerd.log 2>&1 &)`. Images are pulled through `mirror.gcr.io/` (`REGISTRY_PREFIX`, `TESTCONTAINERS_HUB_IMAGE_NAME_PREFIX`).
- quay.io and ghcr.io blobs are not reachable from the sandbox, so test IdPs come from Docker Hub images through the mirror: `keycloak/keycloak:26.0` and `osixia/openldap:1.5.0`.
