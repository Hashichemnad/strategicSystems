# OWASP ASVS 4.0.3 — Level 2 verification checklist

Integrated Strategy System, release 1.0 (Phase 18). Every Level 2 requirement of ASVS 4.0.3 (chapters V1–V14) with how it is met, the
evidence (automated test, code or configuration) and its status. Requirements are paraphrased; the numbering is ASVS's.

**Status:** **Met** · **Partial** (met with a documented limitation) · **N/A** (the feature does not exist in the product) ·
**Residual** (not met; risk accepted and listed in the release notes with the reason). Deployment duties of the customer (TLS
certificate, reverse proxy, OS hardening) are marked **Operator** and are part of the installation guide.

Test names refer to `tests/Strategy.IntegrationTests` (IT) and `tests/Strategy.UnitTests` (UT). "Arch" = `tests/Strategy.ArchitectureTests`.
Threat model: [`threat-model.md`](threat-model.md). Scan results: [`scan-results.md`](scan-results.md).

## Summary

| Chapter | Met | Partial | N/A | Residual | Operator |
|---|---|---|---|---|---|
| V1 Architecture | 37 | 3 | 1 | 0 | 0 |
| V2 Authentication | 40 | 7 | 5 | 2 | 0 |
| V3 Session management | 17 | 1 | 2 | 0 | 0 |
| V4 Access control | 9 | 0 | 0 | 0 | 0 |
| V5 Validation, sanitisation, encoding | 30 | 2 | 2 | 0 | 0 |
| V6 Stored cryptography | 12 | 1 | 1 | 0 | 0 |
| V7 Error handling and logging | 11 | 1 | 0 | 0 | 0 |
| V8 Data protection | 13 | 2 | 0 | 0 | 0 |
| V9 Communication | 3 | 0 | 0 | 0 | 4 |
| V10 Malicious code | 5 | 1 | 0 | 0 | 0 |
| V11 Business logic | 7 | 1 | 0 | 0 | 0 |
| V12 Files and resources | 14 | 1 | 2 | 0 | 0 |
| V13 API and web service | 10 | 0 | 2 | 0 | 0 |
| V14 Configuration | 26 | 2 | 0 | 0 | 2 |
| **Total** | **234** | **22** | **15** | **2** | **6** |

No open **high** finding. The two residual items (V2.1.7 breached-password check, V2.1.9 composition rules on by default) are low risk with
the compensating controls listed below and are configurable or planned.

Findings fixed in Phase 18: SSRF on administrator-entered URLs (ADR 0155, V5.2.6 / V12.6.1); no `Secure` flag on session cookies behind a
TLS proxy that forwards plain HTTP without `X-Forwarded-Proto` (`Security:SecureCookies`, V3.4.1); base-image `apt` HIGH CVE
(`apt-get upgrade` in the runtime stage, V14.2.1); no administrator overview of certificate expiry, failed jobs and late notifications
(Health page, ADR 0151, V14.1 / V7.x operational monitoring).

---

## V1 Architecture, design and threat modelling

| # | Requirement | How met | Evidence | Status |
|---|---|---|---|---|
| 1.1.1 | Secure SDLC | Phased build plan, design note and ADR before code, quality gates in CI (build with warnings as errors, format, tests, vulnerable-package and licence checks) | `docs/BUILD_PLAN.md`, `.github/workflows/ci.yml`, CLAUDE.md §16–17 | Met |
| 1.1.2 | Threat modelling per design change | One threat model per external surface; ADRs record security decisions per feature | `threat-model.md`, `docs/decisions/` | Met |
| 1.1.3 | Security constraints in user stories | Every SPEC module has an "Access" section; authorisation negative tests are part of the definition of done | SPEC §1.9, §2.6…; CLAUDE.md §16 | Met |
| 1.1.4 | Trust boundaries documented | Trust boundaries per surface (browser, Wasel, PAT, MCP, AI, IdP, sidecars) | `threat-model.md` §1 | Met |
| 1.1.5 | Security architecture analysis of components | Modular monolith; one access service; module boundaries enforced | CLAUDE.md §5, §7; Arch `RepositoryRulesTests.Project_references_respect_module_boundaries` | Met |
| 1.1.6 | Centralised, vetted security controls | `IAccessService`, audit interceptor, antiforgery, Data Protection, problem details, outbound policy — each implemented once | `Platform.Access`, `Platform.Audit`, `Kernel.Net.OutboundUrlPolicy` | Met |
| 1.1.7 | Secure-coding checklist available | CLAUDE.md §2 / §4.3 / §6–8 and this checklist | CLAUDE.md | Met |
| 1.2.1 | Unique low-privilege OS accounts for components | Container runs as non-root `$APP_UID`; PostgreSQL runtime role separate from owner (ADR 0018) | `deploy/Dockerfile` (`USER $APP_UID`), `deploy/postgres/init-roles.sh` | Met |
| 1.2.2 | Communication between components authenticated | PostgreSQL password auth per role; sidecars (Gotenberg, ClamAV, Mailpit) on the private compose network | `deploy/docker-compose.yml` | Partial — Gotenberg / ClamAV have no authentication of their own; isolated by the compose network; the host port mapping of Gotenberg (`GOTENBERG_PORT`) is for development and must be bound to 127.0.0.1 or removed in production (installation guide) |
| 1.2.3 | Single vetted authentication mechanism | ASP.NET Core Identity + cookie / PAT / OAuth handlers, one policy scheme selects | `IdentityComponent.cs` | Met |
| 1.2.4 | All authentication pathways equally strong | Local, LDAP, OIDC, SAML, UAE Pass all end in the same session store and MFA policy (`RequireLocalMfaAfterSso` optional); PATs scoped, expiring | `SignInService.cs`, `SecuritySettings` | Met |
| 1.4.1 | Trusted enforcement points for access control | Server-side only: endpoint `RequirePermission`, handler checks, scope in queries | `PermissionEndpointExtensions.cs`, IT `AuthorizationMatrixTests`, `EndpointCatalogueTests` | Met |
| 1.4.2 | Access control design documented | Three layers (system access, roles with data scope, record roles) | `docs/design/access.md` | Met |
| 1.4.3 | Least privilege | Roles seeded per system; deny by default (fail-closed in Production) | IT `EnvironmentBehaviourTests.Production_module_access_fails_closed…` | Met |
| 1.4.4 | Single well-vetted access control mechanism | `IAccessService.CanAsync` / `ApplyScope` | `AccessService.cs` | Met |
| 1.4.5 | Attribute / feature-based access control | Permission per object and action with data scope; record roles | `docs/design/access.md` | Met |
| 1.5.1 | Input / output requirements defined | Validators per command; message keys | FluentValidation validators per module | Met |
| 1.5.2 | No deserialisation of untrusted data with type info | System.Text.Json without polymorphic type names; no BinaryFormatter | code search: no `TypeNameHandling`, no `BinaryFormatter` | Met |
| 1.5.3 | Validation enforced on a trusted layer | Server-side validation pipeline (`ValidationBehavior`) | `Kernel/Validation` | Met |
| 1.5.4 | Output encoding near the interpreter | React escaping; e-mail renderer HTML-encodes values; PDF HTML built with encoding | `EmailRenderer.cs`, `PdfRendering.cs` | Met |
| 1.6.1 | Key management policy | Data Protection key ring in DB, optional certificate encryption, rotation by the framework (90 days) | ADR 0012, `DataProtectionSetup.cs`, deploy/README "Data Protection keys" | Met |
| 1.6.2 | Consumers of key material protected | Keys only through `ISecretProtector`; never returned by APIs | IT `Smtp_password_is_stored_encrypted_and_never_returned`, `Provider_keys_are_never_returned_and_never_written_to_the_audit_trail` | Met |
| 1.6.3 | No hard-coded keys or passwords | Secrets from environment / `.env` / DB (encrypted); demo seed only in Development | `appsettings.json`, `Bootstrap.SeedDevelopmentAsync` (Development only) | Met |
| 1.6.4 | Symmetric keys not used for sensitive data in transit only | Data Protection (AES-256-CBC + HMACSHA256) at rest; TLS in transit | ADR 0012 | Met |
| 1.7.1 | Common logging format | Serilog JSON to stdout with correlation id | `appsettings.json` Serilog section | Met |
| 1.7.2 | Logs sent securely to a remote system | stdout collected by the container runtime; OTLP optional | `Observability.cs` | Partial — shipping to a SIEM is the operator's choice (documented) |
| 1.8.1 | Sensitive data identified and classified | Emirates ID, keys, secrets, personal data in AI masking settings; document sensitivity levels | ADR 0086, AI data controls | Met |
| 1.8.2 | Protection levels per class | Encryption for secrets / Emirates ID; masking in UI / logs / audit; sensitivity-based access | ADR 0086, docreg sensitivity | Met |
| 1.9.1 | Encrypted communication between components | TLS at the reverse proxy; DB and sidecars on private network | installation guide | Partial — PostgreSQL TLS optional (`SSL Mode` in the connection string) for remote DB servers |
| 1.9.2 | Components verify the other side | Npgsql verifies the server certificate when TLS is on; outbound HTTPS validates certificates (no custom validation callbacks) | code search: no `ServerCertificateCustomValidationCallback` | Met |
| 1.10.1 | Source code control with check-in review | Git + pull requests + CI | GitHub repository | Met |
| 1.11.1 | Business logic flows documented | State machines per lifecycle, SPEC workflows | `docs/design/*.md` state tables | Met |
| 1.11.2 | High-value flows not shared unsynchronised state | Optimistic concurrency (`xmin`, 409) on every business table; idempotency keys on submit / approve | `ModuleDbContext`, IT conflict tests | Met |
| 1.11.3 | High-value flows thread safe | Numbering in the same transaction with unique index; advisory locks for reporting refresh | `NumberingService`, ADR 0142 | Met |
| 1.12.2 | Uploaded files served safely | Downloads only through an authorised endpoint (attachment or, for previews, the detected content type) with `nosniff` and the global CSP | `FileEndpoints.cs`, IT `FileServiceTests` | Met |
| 1.14.1 | Segregation of components with different trust | App, DB, PDF renderer, antivirus, AI host in separate containers | `deploy/docker-compose.yml` | Met |
| 1.14.2 | Signed, verified builds | Images built from source by the customer or CI; SBOM produced | `deploy/` SBOM (release) | N/A — no third-party binary delivery channel in 1.0 (images built locally) |
| 1.14.3 | Compiler flags / runtime protections | .NET managed code; `TreatWarningsAsErrors`; analyzers | `Directory.Build.props` | Met |
| 1.14.4 | Repeatable deployment | Docker Compose + scripts; migrations versioned | `deploy/`, `--migrate` | Met |
| 1.14.5 | Application isolation | Container sandbox, non-root | `deploy/Dockerfile` | Met |
| 1.14.6 | No unsupported client-side technology | React SPA only (no Flash, Java applets…) | `web/` | Met |

## V2 Authentication

| # | Requirement | How met | Evidence | Status |
|---|---|---|---|---|
| 2.1.1 | Passwords ≥ 12 characters | `PasswordMinLength` default 12 (min enforced by settings validation) | `SecuritySettings`, UT `IdentityUnitTests` defaults | Met |
| 2.1.2 | Passwords of 64+ characters allowed | No maximum in Identity; inputs accept long values | IT `Password_reset_by_email_link…` (policy) | Met |
| 2.1.3 | No truncation | Identity hashes the full string | ASP.NET Core Identity | Met |
| 2.1.4 | Any Unicode character allowed | No character restrictions | ASP.NET Core Identity | Met |
| 2.1.5 | Users can change their password | Profile → change password (current password required) | IT `Password_change_needs_the_current_password_and_signs_out_other_sessions` | Met |
| 2.1.6 | Change requires current and new password | as above | same | Met |
| 2.1.7 | Check against breached / common passwords | Not implemented: installations are often offline (no HIBP); history (5) and minimum length mitigate | — | **Residual** (planned: bundled common-password list) |
| 2.1.8 | Password strength meter | Policy hints shown on the set-password forms | `web/src/features/auth` | Partial — rule list, no strength estimator |
| 2.1.9 | No composition rules | Upper / lower / digit / symbol required **by default** (customer request, SPEC identity defaults); each can be switched off in Settings → Security | `SecuritySettings` | **Residual** (configurable; documented in the administrator guide) |
| 2.1.10 | No periodic rotation or history requirement imposed | No forced expiry; history only prevents reuse | `SecuritySettings.PasswordHistory` | Met |
| 2.1.11 | Paste and password managers allowed | Standard `<input type="password" autocomplete>`; paste not blocked | `web/src/features/auth/LoginPage.tsx` | Met |
| 2.1.12 | Show password option | Plain password inputs (browser / password-manager reveal only); no in-app show / hide toggle | `web/src/features/auth/LoginPage.tsx` | Partial — toggle planned for 1.1 |
| 2.2.1 | Anti-automation (lockout, rate limit) | Lockout after 5 failures for 15 min; `auth` rate-limit policy 30/min/IP | IT `Account_locks_after_the_configured_failures…`, `EnvironmentBehaviourTests.Rate_limit_returns_429…` | Met |
| 2.2.2 | Weak authenticators (SMS / e-mail) secondary only | E-mail OTP only as a second factor | IT `Email_code_works_as_second_factor…` | Met |
| 2.2.3 | Notification after credential changes | Password change / reset and MFA changes are recorded as auth events and in the audit trail; the reset itself is e-mailed; no separate "your password was changed" e-mail | `AuthEventLog.cs`, `IdentityEmails.cs` | Partial — confirmation e-mail planned for 1.1 |
| 2.2.4 | Phishing-resistant MFA (L3) | — | — | N/A (Level 3) |
| 2.2.5 | Mutual TLS with credential service provider | LDAPS / StartTLS for LDAP; HTTPS for IdPs | `LdapDirectory.cs` | Met |
| 2.2.6 | Replay resistance of OTP | TOTP codes accepted once (replay-protected provider) | IT `A_totp_code_is_accepted_once…` | Met |
| 2.2.7 | Intent to authenticate (OTP entry) | Code must be typed | — | Met |
| 2.3.1 | Initial passwords / activation codes random and expire | Invitation link (single use, expiring token); `--create-admin` / setup wizard take a chosen password | IT `Admin_creates_an_account_for_an_employee_and_the_invitation_sets_the_password_once` | Met |
| 2.3.2 | Enrolment of user-provided authenticators (TOTP) | Authenticator enrolment with QR and confirmation code | IT `Administrator_without_mfa_must_enrol…` | Met |
| 2.3.3 | Renewal instructions for time-bound authenticators | PAT expiry shown; renewal = new token | Profile → tokens | Met |
| 2.4.1 | Passwords stored with an approved KDF | ASP.NET Core Identity v3 hasher (PBKDF2-HMAC-SHA512, 100 000 iterations, salted) | ASP.NET Core Identity | Met |
| 2.4.2 | Salt ≥ 32 bits, unique | Identity v3 format (128-bit salt) | same | Met |
| 2.4.3 | PBKDF2 iterations ≥ 100 000 | Identity v3 default 100 000 | same | Met |
| 2.4.4 | bcrypt work factor | — | — | N/A (PBKDF2 used) |
| 2.4.5 | Additional KDF with secret salt (pepper) | Not used | — | N/A (L2 "should"; not required with PBKDF2 at 100k) |
| 2.5.1 | Activation / recovery secrets not sent in clear | Reset links carry single-use tokens over HTTPS; passwords never e-mailed | `IdentityEmails.cs` | Met |
| 2.5.2 | No password hints / knowledge questions | none | — | Met |
| 2.5.3 | Recovery does not reveal the current password | Reset sets a new password | — | Met |
| 2.5.4 | No shared / default accounts | No built-in accounts; first administrator created by wizard or `--create-admin`; demo users only with the optional demo pack | `Bootstrap.cs` | Met |
| 2.5.5 | Notification on authentication factor change | MFA enable / disable / reset recorded as auth events (visible in the profile's activity and to administrators); no e-mail | `AuthEventLog.cs` | Partial — as 2.2.3 |
| 2.5.6 | Secure recovery (reset token, MFA kept) | Reset does not disable MFA; recovery codes single use | IT `Users_with_mfa_must_give_a_valid_second_factor_and_recovery_codes_work_once` | Met |
| 2.5.7 | Lost OTP: re-enrolment as strong as enrolment | Administrator reset of MFA is audited; user re-enrols with a confirmation code | Administration → Users | Met |
| 2.6.1 | Look-up secrets single use | Recovery codes single use | same test as 2.5.6 | Met |
| 2.6.2 | Look-up secrets random ≥ 112 bits | Identity recovery codes (10 × 8 chars base32-like) — ~40 bits each | ASP.NET Core Identity | Partial — Identity's built-in format; codes are one-time and rate-limited / locked out |
| 2.6.3 | Look-up secrets resistant to offline attacks | Stored hashed by the store | `StrategyUserStore.cs` | Met |
| 2.7.1 | Out-of-band via SMS / PSTN not offered by default | No SMS | — | Met |
| 2.7.2 | OOB code expires in 10 minutes | E-mail OTP expires (5 min token lifetime) | Identity token provider | Met |
| 2.7.3 | OOB code single use | E-mail OTP accepted once | Identity | Met |
| 2.7.4 | Authenticated secure channel for OOB | E-mail over SMTP TLS (STARTTLS / TLS settings) | SMTP settings | Partial — depends on the customer's SMTP server supporting TLS |
| 2.7.5 | OOB codes hashed | Identity data-protection tokens | Identity | Met |
| 2.7.6 | OOB code ≥ 20 bits entropy | 6 digits (~20 bits) | Identity | Met |
| 2.8.1 | Time-based OTP has a defined lifetime | 30 s step, ±1 window | `TotpCode.cs` | Met |
| 2.8.2 | OTP seeds protected | Authenticator keys encrypted with Data Protection | `StrategyUserStore.cs` | Met |
| 2.8.3 | Approved crypto for OTP | RFC 6238 HMAC-SHA1 | `TotpCode.cs` | Met |
| 2.8.4 | TOTP used once in validity period | replay protection | IT `A_totp_code_is_accepted_once…` | Met |
| 2.8.5 | Reused OTP is logged and notified | Refused, logged as failed MFA auth event | auth events | Partial — logged, no user notification |
| 2.8.6 | Physical OTP revocable | — | — | N/A (no hardware tokens) |
| 2.9.x | Cryptographic authenticators (L2: 2.9.1–2.9.3) | — | — | N/A (no smart cards / FIDO devices in 1.0) |
| 2.10.1 | No unchanging intra-service secrets | Service-to-DB credentials from environment, two roles | ADR 0018 | Met |
| 2.10.2 | No default credentials for services | `.env.example` contains `change-me`; install scripts generate random passwords | `deploy/.env.example`, install scripts | Met |
| 2.10.3 | Service passwords stored with protection | Environment / `.env` (root-only), DB secrets encrypted | deploy/README | Met |
| 2.10.4 | Secrets not in source code | none in repository (Trivy secret scan clean) | `scan-results.md` | Met |

## V3 Session management

| # | Requirement | How met | Evidence | Status |
|---|---|---|---|---|
| 3.1.1 | No session tokens in URLs | Cookies only; PATs / OAuth in `Authorization` header | `IdentityComponent.cs` | Met |
| 3.2.1 | New token on authentication | New server-side session (new id) at every sign-in | `SessionTicketStore.cs` | Met |
| 3.2.2 | ≥ 64 bits of entropy | Session id = GUID v7 inside a Data-Protection-encrypted cookie; ticket stored server side | `SessionTicketStore.cs` | Met |
| 3.2.3 | Tokens stored securely in the browser | HttpOnly cookies | IT `Session_and_antiforgery_cookies_are_http_only…` | Met |
| 3.2.4 | Approved algorithms | Data Protection | ADR 0012 | Met |
| 3.3.1 | Logout invalidates the session | Session revoked server side | IT `Local_sign_in_creates_a_session_that_me_reports_and_logout_ends` | Met |
| 3.3.2 | Re-authentication / idle timeout (L2: 12 h absolute, 30 min idle) | Defaults 30 min idle, 12 h absolute, enforced by the session store | IT `Sessions_end_after_the_idle_and_the_absolute_limit` | Met |
| 3.3.3 | Option to end all other sessions after a password change | Done automatically | IT `Password_change_needs_the_current_password_and_signs_out_other_sessions` | Met |
| 3.3.4 | Users can view and end their sessions | Profile → sessions | IT `Sessions_are_listed_and_revoking_others_ends_them_immediately` | Met |
| 3.4.1 | `Secure` attribute | Over HTTPS always; `Security:SecureCookies=true` forces it behind a proxy | IT `Session_and_antiforgery_cookies…secure_over_https`, `With_secure_cookies_on_the_session_cookie_is_secure…` | Met |
| 3.4.2 | `HttpOnly` | all auth cookies | same | Met |
| 3.4.3 | `SameSite` | Lax (session — needed for SSO callbacks), Strict (MFA, Wasel, antiforgery) | same | Met |
| 3.4.4 | `__Host-` prefix | Not used (cookie names `strategy.*`); Path=/ and no Domain attribute, so the cookies are already host-only | `IdentityComponent.cs` | Partial — prefix not applied because plain-HTTP first start must still work; host-only in effect |
| 3.4.5 | Path set when sharing a domain | Wasel cookies scoped to `/api/wasel`; installation owns its host | `IdentityComponent.cs` | Met |
| 3.5.1 | OAuth refresh tokens revocable | OpenIddict tokens revocable by the user / admin | `docs/mcp-guide.md`, IT `McpServerTests` | Met |
| 3.5.2 | Session tokens rather than static API secrets | PATs are user-scoped, expiring, hashed, revocable | IT `AccessTokenAndSettingsTests` | Met |
| 3.5.3 | Stateless tokens signed / encrypted | OpenIddict tokens encrypted and signed | OpenIddict defaults | Met |
| 3.6.1 | Federated re-authentication (L3) | — | — | N/A |
| 3.7.1 | Re-authentication before sensitive changes | Password change requires the current password; MFA settings require a code | IT 2.1.5 | Met |
| 3.6.2 | Relying parties (L3) | — | — | N/A |

## V4 Access control

| # | Requirement | How met | Evidence | Status |
|---|---|---|---|---|
| 4.1.1 | Enforced on a trusted service layer | Every endpoint declares a permission or `AsPublic()`; a catalogue test fails for undeclared endpoints | IT `EndpointCatalogueTests` | Met |
| 4.1.2 | Attributes used by access control not changeable by users | Roles, scopes and record roles only through audited admin endpoints | `AccessAdministration.cs` | Met |
| 4.1.3 | Least privilege | Seeded roles per SPEC; fail-closed in Production | IT `EnvironmentBehaviourTests` | Met |
| 4.1.5 | Fail securely | Missing permission → 403; out-of-scope record → 404 | IT `AuthorizationMatrixTests`, module `*ApiTests` negative cases | Met |
| 4.2.1 | IDOR protection | Records loaded through scoped queries; 404 outside scope | IT module tests (`…_404`), `AuthorizationMatrixTests` | Met |
| 4.2.2 | CSRF protection | Antiforgery header token bound to the user on every state-changing cookie request | IT `PipelineTests.State_changing_call_without_antiforgery_token_is_refused`, `Antiforgery_token_from_another_session_is_refused` | Met |
| 4.3.1 | Administrative interfaces use MFA | MFA policy `admins` by default; platform roles `RequiresMfa` | IT `Administrator_without_mfa_must_enrol_before_getting_a_session` | Met |
| 4.3.2 | Directory browsing disabled | Static files only from `wwwroot`; no directory browsing; unknown `/api` paths are 404 problems | IT `Unknown_api_route_is_a_404_problem_not_the_spa` | Met |
| 4.3.3 | Additional authorisation for high-value actions | Approvals by the resolved approver only; delegation recorded; MCP approve tools off by default | `docs/design/workflow.md`, ADR on MCP | Met |

## V5 Validation, sanitisation and encoding

| # | Requirement | How met | Evidence | Status |
|---|---|---|---|---|
| 5.1.1 | HTTP parameter pollution | Minimal API binding to typed models; duplicates fail binding | ASP.NET Core | Met |
| 5.1.2 | Mass assignment | Input records per command (no entity binding) | `*Input` records per module | Met |
| 5.1.3 | Positive validation | FluentValidation per command; lookups by code | validators | Met |
| 5.1.4 | Structured data strongly typed | Records, enums, `DateOnly`, JSON schema for flexible forms | Request Framework schema validation | Met |
| 5.1.5 | URL redirects to allow-listed destinations | External sign-in `returnUrl` reduced to local paths (`LocalUrl`) | `ExternalEndpoints.cs` | Met |
| 5.2.1 | Untrusted HTML sanitised | Rich text shown as plain text (no `dangerouslySetInnerHTML` with user data); e-mail values encoded | code search `dangerouslySetInnerHTML` | Met |
| 5.2.2 | Unstructured data sanitised | Lengths and characters validated | validators | Met |
| 5.2.3 | Mail injection | MailKit `MailboxAddress` objects; no header concatenation | `SmtpEmailTransport.cs` | Met |
| 5.2.4 | No eval / dynamic code | Admin formulas run in the sandboxed expression engine (whitelisted variables / functions, depth and time limits, no reflection) | `Kernel/Expressions`, UT `ExpressionEngineTests` | Met |
| 5.2.5 | Template injection | Notification templates use `{{name}}` substitution of encoded values only | `EmailRenderer.cs` | Met |
| 5.2.6 | SSRF | Outbound URL policy on save and at connect time (ADR 0155) | UT `OutboundUrlPolicyTests`, IT `Ai_provider_endpoints_on_the_host_itself_or_the_metadata_service_are_refused`, `Identity_provider_urls_and_ldap_hosts_follow_the_outbound_policy` | Met |
| 5.2.7 | SVG scriptable content | SVG not in the upload allow-list | `ContentInspector.cs` | Met |
| 5.2.8 | Expression / Markdown template languages | Markdown not rendered as HTML from users; expression engine sandboxed | as 5.2.4 | Met |
| 5.3.1 | Context-aware output encoding | React, Razor-less HTML builders encode | `EmailRenderer.cs`, `PdfRendering.cs` | Met |
| 5.3.2 | Unicode preserved | UTF-8 throughout, Arabic normalisation only in search columns | CLAUDE.md §6 | Met |
| 5.3.3 | XSS protection | React escaping + CSP without inline scripts | IT `PipelineTests.Security_headers_are_on_every_response` | Met |
| 5.3.4 | SQL injection | EF Core parameterised queries; the few raw SQL commands use constants or parameters | code review (`SqlQueryRaw`, `ExecuteSqlRaw` call sites) | Met |
| 5.3.5 | Other injections (ORM) | LINQ only | — | Met |
| 5.3.6 | JSON injection | System.Text.Json serialisation | — | Met |
| 5.3.7 | LDAP injection | User names escaped with RFC 4515 filter escaping before `{0}` substitution | `LdapDirectory.cs` | Met |
| 5.3.8 | OS command injection | Only Tesseract is started, with an argument list (no shell) and temp files | `OcrEngine.cs` | Met |
| 5.3.9 | Local / remote file inclusion | File store keys generated by the server; path traversal check | `FileStores.cs` (`PathOf`) | Met |
| 5.3.10 | XPath / XML injection | No XPath with user input | — | Met |
| 5.4.1 | Memory-safe string handling | Managed code | — | Met |
| 5.4.2 | Format strings | No format strings from input | — | Met |
| 5.4.3 | Integer overflow | Checked domain limits (amounts `numeric(18,2)`, pagination ≤ 200) | paging | Met |
| 5.5.1 | Serialised objects integrity | Only JSON DTOs; no object graphs from clients | — | Met |
| 5.5.2 | XML parsers restrict XXE | `XDocument.Parse` / `XmlReader` with DTD processing prohibited (.NET default); Sustainsys.Saml2 hardened parser; Excel via OpenXML SDK | `ExternalEndpoints.cs` (`ProviderTester`) | Met |
| 5.5.3 | Deserialisation of untrusted data avoided / protected | as 1.5.2 | — | Met |
| 5.5.4 | JSON.parse in the browser | Only API JSON; no `eval` | `web/` | Met |
| 5.1.x / 5.2.x CSV / formula injection in exports | Excel exports via ClosedXML write values as typed cells (strings, not formulas) | `AuditExport.cs`, report exports | Partial — values beginning with `=`, `+`, `-`, `@` are written as text cells, not escaped with a leading quote in CSV (CSV export is not offered) | Partial |
| 5.2.x Rich text editor (Tiptap) | Not yet used for stored HTML in 1.0 (plain text) | Phase 14 summary | N/A |
| 5.3.x Client-side templating (Angular etc.) | — | — | N/A |
| 5.1.x Upload names | Original names stored as data, never used as paths | `FileService.cs` | Partial — names shown in UI encoded; long-name limit 255 |

## V6 Stored cryptography

| # | Requirement | How met | Evidence | Status |
|---|---|---|---|---|
| 6.1.1 | Regulated private data encrypted at rest | Emirates ID encrypted (Data Protection) and masked | ADR 0086 | Met |
| 6.1.2 | Regulated health data | — | — | N/A |
| 6.1.3 | Regulated financial data | Budgets are not regulated cardholder data; DB volume encryption is an operator option | installation guide | Partial — no column encryption for budget figures (not regulated) |
| 6.2.1 | Crypto modules fail securely | .NET crypto; exceptions surface as errors, no fallback to plain | — | Met |
| 6.2.2 | Industry-proven crypto | Data Protection, PBKDF2, SHA-256, HMAC | — | Met |
| 6.2.3 | Approved modes / padding | Data Protection authenticated encryption | — | Met |
| 6.2.4 | Algorithms replaceable | Data Protection algorithms configurable; audit hash algorithm versioned in entries | ADR 0012 / audit design | Met |
| 6.2.5 | No insecure algorithms | No MD5 / SHA-1 except TOTP (RFC 6238, accepted) | — | Met |
| 6.2.6 | Nonces / IVs not reused | Framework generated | — | Met |
| 6.3.1 | Random values from a CSPRNG | `RandomNumberGenerator` for tokens (PAT, setup token, codes); GUID v7 only as identifiers | `AccessTokens.cs` | Met |
| 6.3.2 | GUIDs not used as secrets | PAT secrets are 32 CSPRNG characters, not GUIDs | `AccessTokens.cs` | Met |
| 6.4.1 | Secrets management solution | Data Protection key ring + optional certificate; `.env` root-only; secrets never in images | ADR 0012, deploy/README | Met |
| 6.4.2 | Key material not exposed to the application | Keys used via the protector API only | `SecretProtector.cs` | Met |
| 6.2.7 | Encrypted data authenticated | Data Protection includes HMAC | — | Met |

## V7 Error handling and logging

| # | Requirement | How met | Evidence | Status |
|---|---|---|---|---|
| 7.1.1 | No credentials / session tokens in logs | Request logging logs method, path (no query), status, time; no secrets in log templates; EF sensitive data logging off | `HostSetup.cs` (`UseSerilogRequestLogging`), code review of `LoggerMessage` templates | Met |
| 7.1.2 | No other sensitive data in logs | Personal data not logged; audit masks sensitive fields | audit interceptor masking | Met |
| 7.1.3 | Security events logged | Logins, failures, MFA, lockouts, permission changes, approvals, file downloads, exports, AI and MCP calls | `AuthEventLog.cs`, audit trail | Met |
| 7.1.4 | Logs contain context for investigation | user, channel, IP, user agent, correlation id, time (UTC) | `AuditEntities.cs` | Met |
| 7.2.1 | Authentication decisions logged | auth events table / audit | IT `Auth_events_are_listed_for_administrators` | Met |
| 7.2.2 | Access control failures logged | Refusals of record access to files are audited; general 403s logged by request logging | IT `A_download_by_an_unauthorised_user_is_refused_as_not_found_and_audited` | Partial — 403 / 404 on ordinary endpoints are in the request log, not in the audit trail |
| 7.3.1 | Log injection prevented | Structured JSON logging (values are fields, not concatenated) | Serilog JSON formatter | Met |
| 7.3.3 | Logs protected | Audit trail append-only (runtime role SELECT + INSERT, triggers), hash chain verified daily | IT `AuditChainTests`, health check `auditChain` | Met |
| 7.3.4 | Time synchronised | Containers use host time (NTP is the operator's duty); timestamps UTC | installation guide | Met |
| 7.4.1 | Generic error message with a unique id | Problem Details with `traceId` and `correlationId`, no stack traces in Production | `ProblemDetailsSetup.cs`, `ProblemAssertions` | Met |
| 7.4.2 | Exception handling across the code base | Global exception handler → problem details | `ProblemDetailsSetup.cs` | Met |
| 7.4.3 | Last-resort handler | same | same | Met |

## V8 Data protection

| # | Requirement | How met | Evidence | Status |
|---|---|---|---|---|
| 8.1.1 | Sensitive data not cached in server components | Access snapshot cache holds permissions only; reporting cache is scope-partitioned (ADR 0141) | ADR 0141 | Met |
| 8.1.2 | Cached sensitive data purged | Caches expire / are invalidated on change | `AccessModel` invalidation | Met |
| 8.1.3 | Minimal parameters | Paging ≤ 200; field-level DTOs; vendor DTOs separate | ADR 0113 | Met |
| 8.1.4 | Abnormal request volumes detected | Rate limits for API / MCP / auth; MCP per-user limits | `RateLimiting.cs`, `McpRateLimiter.cs` | Met |
| 8.1.5 | Backups of important data | Backup / restore scripts with verification (Phase 18) | `deploy/` backup scripts | Met |
| 8.1.6 | Backups stored securely | Operator stores them; Data Protection certificate kept with backups | installation guide | Partial — encryption of the backup archive is left to the operator's storage |
| 8.2.1 | Anti-caching headers on sensitive data | `Cache-Control: no-store` on API JSON with personal data, tokens, health | `SecurityHeadersMiddleware` / endpoints | Partial — set on token, health, file download and auth responses; ordinary JSON relies on browser defaults for authenticated XHR (no `Cache-Control` header) |
| 8.2.2 | No sensitive data in browser storage | localStorage only holds language, theme, UI layout; no tokens | `web/src/lib/preferences.ts` | Met |
| 8.2.3 | Client storage cleared after session | Nothing sensitive stored | — | Met |
| 8.3.1 | Sensitive data in body / headers, not URL | Searches by text are GET parameters on audit / lists (not credentials); tokens never in URLs | API design | Met |
| 8.3.2 | Users can export / delete their data | Personal data export by administrators; deletion = deactivation (no hard deletes of business records, CLAUDE.md §6) | ADR / identity | Met |
| 8.3.3 | Clear privacy language | Customer's privacy notice; AI data controls list what is sent | AI settings page | Met |
| 8.3.4 | Sensitive data identified | as 1.8.1 | — | Met |
| 8.3.5 | Access to sensitive data audited | Emirates ID reveal, file downloads, exports audited | ADR 0086, file audit | Met |
| 8.3.6 | Sensitive data cleared from memory | Managed memory; no long-lived plaintext caches of secrets | — | Met |

## V9 Communication

| # | Requirement | How met | Evidence | Status |
|---|---|---|---|---|
| 9.1.1 | TLS for all client connectivity | TLS terminated at the customer's reverse proxy (nginx / IIS / F5); HSTS sent in Production | installation guide, IT `Production_sends_hsts_over_https` | Operator |
| 9.1.2 | Strong cipher suites | reverse proxy configuration (sample nginx config with TLS 1.2+ / 1.3) | installation guide | Operator |
| 9.1.3 | Only TLS 1.2 / 1.3 | same | same | Operator |
| 9.2.1 | Trusted certificates for outbound connections | Default .NET validation; organisation CA added to the image trust store when needed | — | Met |
| 9.2.2 | Encrypted outbound connections | AI providers and IdPs over HTTPS (`RequireHttpsMetadata` unless the admin explicitly allows http for an internal IdP); SMTP TLS settings; LDAPS default | `ProviderSchema.cs` (`allowInsecureHttp`), LDAP `security=ldaps` default | Met |
| 9.2.3 | Encrypted connections to back ends | DB on the private network or with `SSL Mode=Require` for remote servers | installation guide | Operator |
| 9.2.4 | Certificate revocation checking | .NET default chain policy (online revocation where reachable) | — | Met |

## V10 Malicious code

| # | Requirement | How met | Evidence | Status |
|---|---|---|---|---|
| 10.2.1 | No unauthorised phone-home | No telemetry; outbound only to configured providers; OTLP only when set | `Observability.cs` | Met |
| 10.2.2 | No unnecessary permissions | Container non-root, no capabilities added | `deploy/Dockerfile` | Met |
| 10.3.1 | Automatic updates verified | No auto-update; upgrades are explicit scripts | upgrade procedure | Met |
| 10.3.2 | Integrity protection | Images built from source; SBOM; dependency lock files (`packages.lock` via central versions, `package-lock.json`) | `deploy/` SBOM | Partial — images not signed in 1.0 |
| 10.3.3 | Sub-domain takeover | Single host name per installation | — | Met |
| 10.2.x Back doors / time bombs | No licence checks or hidden accounts (SPEC §0) | code review | Met |

## V11 Business logic

| # | Requirement | How met | Evidence | Status |
|---|---|---|---|---|
| 11.1.1 | Sequential step order | Explicit state machines with guards | UT state-machine tests per module | Met |
| 11.1.2 | Realistic human timing / limits | Rate limits; MCP per-user limits; AI monthly limits | `RateLimiting.cs`, AI limits | Met |
| 11.1.3 | Business limits enforced | Thresholds as configuration, validated server side | Settings per module | Met |
| 11.1.4 | Anti-automation for business flows | Rate limits; idempotency keys on submit / approve | IT idempotency tests | Met |
| 11.1.5 | Business logic threats documented | Threat model and ADRs (AI drafts only; approvals never by AI / MCP by default) | `threat-model.md` | Met |
| 11.1.6 | TOCTOU / concurrency | Optimistic concurrency (409) | conflict tests | Met |
| 11.1.7 | Monitoring of unusual activity | Audit trail, failed login events, Health page | — | Partial — no automatic anomaly alerts apart from lockouts and the audit chain alert |
| 11.1.8 | Configurable alerting | Notification templates for chain breaks, SLA escalations | `ChainAlert` | Met |

## V12 Files and resources

| # | Requirement | How met | Evidence | Status |
|---|---|---|---|---|
| 12.1.1 | Large files refused | Upload limit (Settings → Files, default 25 MB) | IT `Too_large_and_empty_uploads_are_refused` | Met |
| 12.1.2 | Compressed file limits (zip bombs) | Only OOXML containers accepted; ClosedXML / OpenXML reads streamed with size limits on imports | `ContentInspector.cs` | Partial — OOXML decompression size not separately capped |
| 12.1.3 | Per-user quota | No per-user quota; total upload limit per file | — | N/A (L2 "should" for quotas; not a requirement of the SPEC) |
| 12.2.1 | Files from untrusted sources validated by content | Extension allow-list + magic number match | IT `Disallowed_and_disguised_types_are_refused_and_the_refusal_is_audited` | Met |
| 12.3.1 | User file names not used as paths | Generated storage keys | `FileStores.cs` | Met |
| 12.3.2 | Path traversal | `PathOf` refuses keys outside the root | `FileStores.cs` | Met |
| 12.3.3 | No remote file inclusion | — | — | Met |
| 12.3.4 | No RFD (reflected file download) | Downloads carry `Content-Disposition: attachment` with the stored name; the inline preview (PDF / images for pdf.js) has the server-detected content type and `nosniff` | `FileEndpoints.cs` | Met |
| 12.3.5 | No OS command with file names | Tesseract gets temp file paths generated by the server | `OcrEngine.cs` | Met |
| 12.3.6 | No inclusion of untrusted code | — | — | Met |
| 12.4.1 | Files stored outside the web root | `/var/lib/strategy/files` volume or S3 | `deploy/Dockerfile` | Met |
| 12.4.2 | Antivirus scanning | Optional ClamAV, "scan required" mode holds files until clean | `ClamAvScanner.cs`, IT `FileServiceTests` | Met |
| 12.5.1 | Web tier serves only allowed extensions | SPA static files only; no directory listing | `SpaHosting.cs` | Met |
| 12.5.2 | Direct requests to uploads never execute | Uploads only via the authorised download endpoint | `FileEndpoints.cs` | Met |
| 12.6.1 | SSRF allow-list | Outbound URL policy (ADR 0155) | UT `OutboundUrlPolicyTests` | Met |
| 12.1.x Image re-encoding | Images served as attachments, not re-encoded | — | N/A |
| 12.2.x Executable uploads | Not in allow-list | `ContentInspector.cs` | Met |

## V13 API and web service

| # | Requirement | How met | Evidence | Status |
|---|---|---|---|---|
| 13.1.1 | Same encoding / parsers across components | UTF-8 JSON everywhere | — | Met |
| 13.1.3 | No sensitive data in API URLs | tokens in headers | — | Met |
| 13.1.4 | Authorisation at URI and resource level | Endpoint permission + record checks | IT `AuthorizationMatrixTests` | Met |
| 13.1.5 | Requests with unexpected content types rejected | Minimal APIs accept `application/json` (and multipart where declared) | ASP.NET Core | Met |
| 13.2.1 | HTTP methods restricted | Each route maps explicit verbs; others 405 | — | Met |
| 13.2.2 | JSON schema validation | OpenAPI-typed inputs + validators; flexible forms validated against stored JSON schema | Request Framework | Met |
| 13.2.3 | CSRF on cookie-authenticated REST | Antiforgery header | IT `PipelineTests` | Met |
| 13.2.5 | Content-Type checked | as 13.1.5 | — | Met |
| 13.2.6 | Message headers and payload trustworthy | HTTPS at the proxy; forwarded headers only from the configured proxy | `ForwardedHeadersSetup.cs` | Met |
| 13.3.x SOAP / XML web services | — | — | N/A |
| 13.4.x GraphQL | — | — | N/A |
| 13.1.x MCP and OAuth APIs | Same access service, PKCE required, rate limits, tool switches, audit | `McpComponent.cs`, IT `McpServerTests` | Met |

## V14 Configuration

| # | Requirement | How met | Evidence | Status |
|---|---|---|---|---|
| 14.1.1 | Secure, repeatable builds | Multi-stage Dockerfile, CI | `deploy/Dockerfile`, CI | Met |
| 14.1.2 | Compiler hardening | managed code, analyzers, warnings as errors | `Directory.Build.props` | Met |
| 14.1.3 | Server configuration hardened | Kestrel server header off; minimal image; non-root | `Program.cs`, Dockerfile | Met |
| 14.1.4 | Automated redeploy / restore | install, upgrade, backup / restore scripts | `deploy/` | Met |
| 14.1.5 | Integrity of configuration verified | Health page checks migrations, keys, certificates, jobs | ADR 0151, IT `AdminHealthTests` | Met |
| 14.2.1 | Components up to date | `tools/check-vulnerable.sh`, `npm audit`, Trivy image scan; base image packages upgraded at build | `scan-results.md` | Met |
| 14.2.2 | Unneeded features removed | Sample module off in Production; dev seed only in Development; Mailpit dev profile only | `appsettings.json`, compose profiles | Met |
| 14.2.3 | External assets with SRI | No CDN assets: fonts and scripts bundled | `web/` | Met |
| 14.2.4 | Components from trusted sources | NuGet / npm with lock files; licence allow-list | `lint:licenses`, CLAUDE.md §4.3 | Met |
| 14.2.5 | Inventory of third-party components | `docs/THIRD_PARTY_LICENSES.md` + CycloneDX SBOM | release | Met |
| 14.2.6 | Third-party components sandboxed | Gotenberg, ClamAV, Ollama in their own containers | compose | Met |
| 14.3.1 | Generic error pages | Problem Details; SPA error boundary | `ProblemDetailsSetup.cs` | Met |
| 14.3.2 | Debug modes off in production | `ASPNETCORE_ENVIRONMENT=Production` default; developer exception page only in Development | `deploy/.env.example` | Met |
| 14.3.3 | No version disclosure in headers | `Server` header removed; no `X-Powered-By` | IT `PipelineTests.Security_headers…` (`Server` absent) | Met |
| 14.4.1 | Content-Type with charset | JSON responses `application/json; charset=utf-8` | ASP.NET Core | Met |
| 14.4.2 | API responses with `Content-Disposition` where downloaded | Files and exports | `FileEndpoints.cs`, export endpoints | Met |
| 14.4.3 | CSP | `default-src 'self'; script-src 'self'; object-src 'none'; frame-ancestors 'none'…` on every response | IT `PipelineTests` | Met |
| 14.4.4 | `X-Content-Type-Options: nosniff` | every response | same | Met |
| 14.4.5 | HSTS | Production, 365 days, includeSubDomains | IT `Production_sends_hsts_over_https` | Met |
| 14.4.6 | Referrer-Policy | `strict-origin-when-cross-origin` | same | Met |
| 14.4.7 | Framing restricted | `frame-ancestors 'none'`, `X-Frame-Options: DENY` | same | Met |
| 14.5.1 | Only required HTTP methods | as 13.2.1 | — | Met |
| 14.5.2 | `Origin` not used for authentication | — | — | Met |
| 14.5.3 | CORS allow-list | No CORS policy registered (same-origin SPA); MCP clients are servers, not browsers | code search `AddCors` (none) | Met |
| 14.5.4 | Trusted proxy headers | Forwarded headers only when enabled, from `ForwardedHeaders:KnownProxies` | `ForwardedHeadersSetup.cs` | Partial — if no proxy address is given every sender is trusted (documented: only when the app port is reachable by the proxy alone) |
| 14.3.x OS / proxy hardening | Customer's servers | installation guide | Operator |
| 14.1.x Secrets in configuration | `.env` permissions 600 created by the install script | install scripts | Operator |
| 14.2.x Unused dependencies | Architecture tests forbid listed packages | Arch `RepositoryRulesTests.Packages_are_allowed_and_not_banned` | Met |
| 14.4.x Permissions-Policy | camera, microphone, geolocation, payment, usb off | IT `PipelineTests` | Met |
| 14.5.x Rate limits on unauthenticated endpoints | global per-IP limit on `/api`, `/mcp`, `/connect`; auth policy | IT `Rate_limit_returns_429_problem_with_retry_after` | Partial — static files and health endpoints are not limited (by design, for probes) |

---

## Residual risks (release notes)

| Item | Risk | Why accepted / mitigation | Plan |
|---|---|---|---|
| V2.1.7 breached-password check | Users can pick a well-known password that meets the rules | Offline installations cannot call breach APIs; minimum length 12, complexity, history 5, lockout, MFA for administrators by default; SSO / LDAP common in target customers | Bundle an offline common-password list (OSI-licensed) in 1.1 |
| V2.1.9 composition rules on by default | Users choose predictable patterns | Matches customers' existing policies; can be switched off in Settings → Security | Recommend "length only" in the administrator guide |
| SAML metadata, LDAP, SMTP outbound connections (ADR 0155) | DNS rebinding could reach a refused address after the save-time check | Administrator-only settings, audited; save-time check on literal addresses and `localhost` | Wrap when the libraries expose their HTTP / socket factories |
| Sidecars without authentication (Gotenberg, ClamAV) | A process on the compose network can call them | Internal compose network; do not publish `GOTENBERG_PORT` beyond 127.0.0.1 in production | — |
| Images not signed | Tampering between build and run | Customer builds images on their own server from source; SBOM provided | Sign images when a distribution registry is introduced |
| V2.2.3 / V2.5.5 / V2.1.12 (partial) | A user is not e-mailed when their password or second factor changes; no in-app "show password" toggle | Changes are audited and listed as auth events; administrators see them; password managers and browser reveal work | Confirmation e-mail and toggle in 1.1 |
