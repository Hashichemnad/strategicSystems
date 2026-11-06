# 0154 — UAE Pass sign-in for Wasel vendors

- **Status:** Proposed (Phase 18)
- **Context:** SPEC §1.18 lists UAE Pass as an optional sign-in for vendors. ADR 0110 stored the switch (`uaePassForVendors`, Settings →
  Portfolio → Wasel) but did not build the flow, and required that it "match a vendor account by e-mail and still require the second factor".
  The internal UAE Pass provider (ADR 0011) signs into the internal scheme only.

## Decision
- **One UAE Pass client registration.** The vendor flow uses the installation's enabled UAE Pass provider (lowest sort order) with its existing
  scheme and callback path (`/signin-ext/{code}`), so the customer registers one redirect URI with UAE Pass. What differs is the step *after* the
  provider: the challenge `GET /api/wasel/v1/auth/uae-pass/challenge` marks the ticket `flow=wasel` and returns to
  `GET /api/wasel/v1/auth/uae-pass/callback`. The internal callback refuses any ticket marked `flow=wasel`, and the Wasel callback refuses any
  ticket not marked, so neither flow can be completed through the other.
- **Offered only when** the UAE Pass provider is enabled **and** the Wasel system is on **and** `uaePassForVendors` is on
  (`IVendorUaePassPolicy`, implemented by Portfolio, which owns the vendor register). `GET /api/wasel/v1/auth/options` (public) tells the sign-in
  page whether to show the button; the challenge answers 404 otherwise; the option is checked again in the callback.
- **Matching:** the provider's mapped e-mail claim (`claimEmail`), compared case-insensitively with the e-mail of a `vendor` principal only. An
  explicit `email_verified=false` is refused. The provider's `allowedUserTypes` rule applies as for internal users. **Never** creates or links an
  account; internal accounts are never matched. Unknown, internal, inactive, suspended / dormant and not-allowed accounts all get the same message
  (`wasel.auth.errors.uaePassNoAccount`) — no account enumeration. Lockout is respected.
- **Second factor still required:** the callback only writes the short-lived Wasel two-factor state cookie (exactly as after a correct password) and
  redirects to `/wasel/login/mfa` (or enrolment); the Wasel session cookie is issued only after TOTP / e-mail OTP / recovery code.
- **Audited:** every attempt is an `auth.*` audit event with provider `wasel:uaepass` (`external.failed` with the reason, `mfa.challenge`, then the
  usual `mfa.succeeded` / `login`).
- **Testing without the real UAE Pass:** `MockUaePassIssuer` (Host) is a stand-in OAuth issuer (authorize form, token, userinfo). It is mapped only
  in the **Development** environment with `Development:MockUaePass=true` (E2E, demonstrations) and started on its own loopback port by the
  integration tests; a test proves it is not mapped in Production even when the flag is set.

## Consequences
- Vendors whose UAE Pass e-mail differs from the invited e-mail cannot use UAE Pass (they keep the password sign-in); the PM can re-invite with
  the right address.
- Visitors (UAE Pass `SOP1`) are refused while the provider's `allowedUserTypes` excludes them; an installation that wants foreign vendors to use
  UAE Pass visitor accounts adds `SOP1` to the provider (this also allows them internally, where they still need a matching employee account).
- Project access still ends with the assignment's access date (record roles, ADR 0111) whatever the sign-in method.
