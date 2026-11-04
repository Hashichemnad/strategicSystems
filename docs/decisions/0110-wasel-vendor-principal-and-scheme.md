# 0110 — Wasel: vendor principal and a separate authentication scheme

- **Status:** Proposed (Phase 11) — awaiting owner acceptance
- **Context:** SPEC §1.18 / §0.0 / CLAUDE.md §7: vendors are external users with their own entry URL, separate session, mandatory MFA,
  optional UAE Pass, no internal SSO, no MCP / AI. The identity store already has `PrincipalType` (`internal` / `vendor`).

## Decision
- Vendor accounts are `StrategyUser` rows with `PrincipalType = vendor` (same store, lockout, password policy, recovery codes, session table).
- A separate cookie scheme `wasel` (`strategy.wasel`, SameSite Strict) with a separate two-factor state cookie (`strategy.wasel.mfa`). The
  default policy scheme forwards **every `/api/wasel/*` request to `wasel` and nothing else**: a vendor session is anonymous on internal
  endpoints and an internal session is anonymous on Wasel. `AuthenticationRequirementMiddleware` additionally refuses a vendor principal outside
  `/api/wasel` and an internal principal inside it (403 `auth.principalRefused`).
- Internal sign-in refuses vendor accounts (generic "invalid credentials", no enumeration) and Wasel sign-in refuses internal accounts. LDAP /
  OIDC / SAML never link to a vendor account.
- MFA is always required for vendors (enrolment on first sign-in), independent of the installation MFA policy. TOTP and e-mail OTP as allowed
  by Settings → Security; replay protection and lockout as for internal users. Sign-in routes use the `auth` rate-limit policy.
- Invitation e-mail links to `/wasel/set-password` (3-day token, works once). Antiforgery token for Wasel at `/api/wasel/v1/antiforgery/token`.
- UAE Pass for vendors is a setting (`uaePassForVendors`, default off). The vendor-side UAE Pass flow is not built in Phase 11 (the internal
  OIDC handlers sign into the internal scheme); when it is built it must match a vendor account by e-mail and still require the second factor.

## Consequences
No code path can turn a vendor cookie into an internal principal; the security tests enumerate every endpoint with a vendor session.
