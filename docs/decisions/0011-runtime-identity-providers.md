# 0011 — Runtime-configured identity providers and account linking

- **Status:** Accepted (Phase 1)
- **Context:** SPEC §0 requires SSO (OIDC, SAML), LDAP / AD and UAE Pass to be switchable from Settings. The customer must not edit configuration files, and providers are disabled by default.

## Decision
- **Storage.** Providers are rows in `identity.identity_provider`: a type, a JSON settings document validated per type, a secret protected by Data Protection, and linking rules. The `code` is also the authentication scheme name and appears in callback URLs.
- **Dynamic schemes.**
  - `IdentityProviderSchemes` loads enabled OIDC / UAE Pass / SAML providers at startup and after every create, update, enable or disable. It adds or removes them in `IAuthenticationSchemeProvider` and clears the named options from `IOptionsMonitorCache`.
  - Options are built by `IConfigureNamedOptions<OpenIdConnectOptions>` and `IConfigureNamedOptions<Saml2Options>` from the stored row.
  - The host is single-instance today. A multi-instance install reloads schemes on each node through a periodic version check of the provider table (60 s).
- **LDAP** is not an ASP.NET scheme. The local login endpoint tries enabled LDAP providers for users that have an LDAP login, or that match the provider's linking rule.
- **Linking.** Order: an existing external login, then automatic linking to an existing **active** employee by a configured field (email / employee number / username), then optional just-in-time creation of a *user* for that employee. Employees are never created by sign-in. Default: auto-link on, JIT off.
- **Callbacks.**
  - OIDC: `/signin-ext/{code}`.
  - SAML: Sustainsys module path `/saml2/{code}` (ACS `/saml2/{code}/Acs`, metadata `/saml2/{code}`).
  - These are outside `/api`, so antiforgery does not apply. The protocols carry their own state, nonce and correlation protection.
- **UAE Pass** is the OIDC handler with a preset: staging and production endpoints, scope `urn:uae:digitalid:profile:general`, `acr_values`, and a user-type allow-list (default SOP2, SOP3).

## Consequences
- The integration tests run OIDC and SAML against Keycloak and LDAP against OpenLDAP in containers. `docs/identity-providers.md` explains customer configuration.
- External sign-in skips local MFA unless "Require local MFA after SSO" is on.
