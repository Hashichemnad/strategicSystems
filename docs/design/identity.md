# Design note — Identity and sign-in (`Strategy.Platform.Identity`)

**Purpose.** SPEC §0 (Authentication, Single workspace) and §0.1 (Employee/User): one login for everyone.
- Local accounts always exist.
- SSO (OpenID Connect: Entra ID / ADFS / UAE Pass; SAML 2.0) and LDAP / Active Directory are switched on in Settings at runtime. They are **disabled by default**, and no configuration-file edits are needed.
- Personal access tokens serve API clients, and later MCP.
- Vendor (Wasel) principals arrive in Phase 11. The `principal_type` column is reserved for them.

## Data model (schema `identity`)
| Table | Notes |
|---|---|
| `users` | ASP.NET Core Identity user (Guid v7 key) + `employee_id` (unique; null only for the bootstrap admin), `principal_type` (`internal`), `language`, `theme`, `active`, `last_sign_in_at`, `created_at`, `password_changed_at`, `totp_last_step` (last accepted authenticator time step; replay protection) |
| `user_logins` | External logins: `login_provider` = provider code, `provider_key` = subject / NameID / LDAP DN |
| `user_tokens` | Authenticator key (protected), recovery codes (hashed) |
| `user_claims` | Unused (roles come from Platform.Access, ADR 0016) |
| `password_history` | `user_id`, `hash`, `created_at` — supports the "no reuse of the last N passwords" rule |
| `security_settings` | Singleton: password policy, lockout, session timeouts, MFA policy, SSO-MFA rule, PAT max lifetime |
| `smtp_settings` | Singleton: host, port, security, user, protected password, sender address, AR/EN sender names |
| `identity_provider` | `code` (= auth scheme name), `type` (`oidc`, `uaepass`, `saml2`, `ldap`), AR/EN display names, `enabled` (default false), `sort`, `settings` jsonb, `secret` (protected), linking rules, `last_test_at`, `last_test_ok`, `last_test_message_key` |
| `user_session` | Server-side ticket store: `user_id`, protected ticket, `method`, `created_at`, `last_seen_at`, `expires_at`, `ip`, `user_agent`, `revoked_at` |
| `personal_access_token` | `user_id`, `name`, `prefix` (unique), `secret_hash` (SHA-256), `scopes`, `expires_at`, `last_used_at`, `last_used_ip`, `revoked_at` |
| `auth_event` | Append-only: `type`, `user_id`, `user_name`, `provider`, `ip`, `user_agent`, `success`, `detail_code`, `at`. Moves into the audit trail in Phase 3 |

**Secrets** (provider client secrets, LDAP bind password, SAML SP key, SMTP password) are encrypted with ASP.NET Core Data Protection.
- Each kind uses its own purpose string (ADR 0012).
- Keys persist in `core.data_protection_keys`.
- The API returns only `hasSecret`. A blank secret on update keeps the stored value.

## Defaults (seeded, editable under Administration → Security)
| Setting | Default |
|---|---|
| Password | min length 12, upper, lower, digit, symbol required; history 5; max age 0 (off) |
| Lockout | 5 failures → 15 minutes |
| Sessions | idle 30 min, absolute 12 h |
| MFA policy | `admins` (holders of a role that requires MFA, ADR 0016); options `optional`, `admins`, `all` |
| MFA methods | TOTP on, email code on |
| Require local MFA after SSO | off (the IdP's MFA applies) |
| PAT lifetime | default 90 days, max 365 |

## Sign-in state machine (local / LDAP)
| State | Input | Next |
|---|---|---|
| anonymous | `POST /auth/login` with valid credentials, MFA not needed | **signedIn** (session cookie) |
| anonymous | valid credentials, user has MFA | **mfaRequired** (two-factor cookie, 5 min) |
| anonymous | valid credentials, policy requires MFA, none set up | **mfaEnrolmentRequired** (enrolment cookie: TOTP or email only) |
| anonymous | invalid credentials / inactive / locked | stays anonymous; 400 `auth.invalidCredentials` or 423-style 400 `auth.lockedOut` (no user enumeration) |
| mfaRequired | `POST /auth/mfa/verify` with a correct code (an authenticator code only for a time step later than the last one used) | signedIn |
| mfaEnrolmentRequired | `POST /auth/mfa/enrol/totp/confirm` or `.../email/confirm` | signedIn (+ recovery codes shown once) |
| signedIn | `POST /auth/logout` | anonymous (session revoked) |
| any | password expired | **passwordChangeRequired** (Phase 1: only when max age > 0) |

Every transition writes an `auth_event` and a structured log line.

## External providers
- **Dynamic schemes.** `IdentityProviderSchemes` registers each enabled provider as an authentication scheme named after its `code`, at startup and after every change. It uses `IAuthenticationSchemeProvider.AddScheme` / `RemoveScheme` and clears the named-options cache.
- **OIDC / UAE Pass.** Handled by `OpenIdConnectHandler` using the authorization code flow with PKCE.
  - Settings: authority, client id, secret, scopes, claim mapping (subject, email, employee number, display name), response mode.
  - The UAE Pass preset fills in the staging/production endpoints, the `urn:uae:digitalid:profile:general` scope and `acr_values`, and allows user types SOP2/SOP3 by default.
  - Callback: `/signin-ext/{code}`.
- **SAML 2.0.** Handled by Sustainsys.Saml2 with one scheme per provider.
  - Settings: IdP entity id, metadata URL or XML, SP entity id, attribute mapping, whether to sign requests, NameID format.
  - SP metadata is served at `/saml2/{code}/metadata` (Sustainsys module path `/saml2/{code}`).
- **LDAP / AD.** Uses `System.DirectoryServices.Protocols`.
  - Settings: host(s), port, security (`none` / `starttls` / `ldaps`), service account DN + protected password, base DN, user filter (default `(&(objectClass=person)(|(uid={0})(sAMAccountName={0})))`), attribute mapping.
  - The flow is: search the user with the service account, bind as the user's DN with the password, then link.
- **Linking (all external types)**, after the provider authenticates:
  1. An existing external login (provider + subject) means that user.
  2. Otherwise, if `auto_link` is on, find the **active** employee whose `match_field` (`email` / `employee_no` / `username`) equals the provider's mapped value.
     - If that employee has a user, attach the login.
     - If not, and `jit_create_user` is on, create a user without a password.
  3. Otherwise refuse (`auth.external.noMatch`). Employees are never created by sign-in.
- **Test connection**:
  - OIDC: fetches the discovery document.
  - SAML: loads and parses the metadata.
  - LDAP: service bind plus an optional test-user search.

  The result (`ok`, `messageKey`) is stored on the provider.

## Sessions and tokens
- **Cookie** `strategy.session`: HttpOnly, `SameSite=Lax`, Secure on HTTPS. The session store is `user_session` (`ITicketStore`).
  - Idle timeout uses sliding expiry; the absolute timeout is checked on validation.
  - The security stamp is validated every 5 minutes.
  - "Sign out everywhere" revokes all sessions and rotates the security stamp.
- **PATs** look like `sst_<prefix8>_<secret32>` (base32). Only the SHA-256 hash is stored, and the value is shown once.
  - Scopes: `api.read` (GET/HEAD only), `api.write`, `mcp` (used in Phase 5).
  - Expiry is required and capped by settings.
  - A PAT is refused when it is revoked or expired, or when its user is inactive or locked.
  - PAT requests are exempt from antiforgery. Their channel is `api`.

## Permissions (catalogue and default role since Phase 2: ADR 0016, docs/design/access.md)
| Permission | Who by default |
|---|---|
| self-service `/me/*` | any signed-in user (ownership checked in handlers; another user's session / token → 404) |
| `identity.user.manage` | role `identity.administrator` (requires MFA) |
| `identity.provider.manage` | role `identity.administrator` |
| `identity.security.manage` | role `identity.administrator` |
| `identity.smtp.manage` | role `identity.administrator` |

Phase 2 changes: the `is_system_admin` column, the `set-admin` endpoint and `isSystemAdmin` in `/me` and user creation are gone (roles are assigned in Settings → Access). The last active access administrator cannot be deactivated (`identity.user.lastAdmin`). Personal access tokens can be switched off in Settings → Features (`identity.access-tokens`). Identity e-mails carry the branding header, footer and organisation name from Settings → Branding.

## API (`/api/v1/identity`)
| Group | Routes |
|---|---|
| Public `auth/*` (rate limit `auth`) | `GET providers`, `POST login`, `POST mfa/verify`, `POST mfa/email-code`, `POST mfa/enrol/totp`, `POST mfa/enrol/totp/confirm`, `POST mfa/enrol/email`, `POST mfa/enrol/email/confirm`, `POST password/forgot`, `POST password/reset`, `GET external/{code}/challenge`, `GET external/callback` |
| `POST auth/logout` | signed-in |
| `me` | `GET me`, `PUT me/preferences`, `POST me/password`, `GET me/mfa`, `POST me/mfa/totp`, `POST me/mfa/totp/confirm`, `POST me/mfa/totp/disable`, `POST me/mfa/email/enable`, `POST me/mfa/email/disable`, `POST me/mfa/recovery-codes`, `GET me/sessions`, `POST me/sessions/{id}/revoke`, `POST me/sessions/revoke-all`, `GET me/tokens`, `POST me/tokens`, `POST me/tokens/{id}/revoke`, `GET me/logins` |
| `admin/users` | list, get, create, `activate`, `deactivate`, `unlock`, `reset-mfa`, `send-password-reset`, `set-admin`, `revoke-sessions`, tokens list / revoke |
| `admin/providers` | list, get, create, update, `enable`, `disable`, `test` |
| `admin/security-settings` | GET, PUT |
| `admin/smtp` | GET, PUT, `test` |
| `admin/auth-events` | GET (paged; the audit UI comes in Phase 3) |

## Email (interim, ADR 0014)
`IIdentityEmailSender` sends password-reset, email-OTP, invitation and test messages through MailKit with the SMTP settings.
- Templates are bilingual and side by side (Arabic right, English left), stored as embedded resources.
- Phase 3 replaces the sender with the notification templates and the outbox.

## Screens
| Screen | Contents |
|---|---|
| Login | Username/email + password; one button per enabled external provider; language toggle |
| MFA | Code entry (TOTP / email / recovery) |
| MFA enrolment | QR code + manual key; email option |
| Forgot / reset password | |
| Profile | Preferences (language, theme); Security (password, MFA, recovery codes, linked logins); Sessions; Access tokens; Delegations |
| Administration → Users | List, details, actions |
| Administration → Identity providers | List + typed editor, Test, Enable / Disable, SP metadata link |
| Administration → Security | Policy form |
| Administration → Email server | Form + Send test |
