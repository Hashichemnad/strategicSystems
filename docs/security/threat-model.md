# Threat model per external surface (release 1.0)

Method: STRIDE per surface — **S**poofing, **T**ampering, **R**epudiation, **I**nformation disclosure, **D**enial of service, **E**levation of
privilege. Each line: threat → control in the product (evidence) → residual risk. ASVS references: [`asvs-l2.md`](asvs-l2.md).

## 1. System context and trust boundaries

```
 Browser (staff)  ──TLS──►  Reverse proxy (customer)  ──HTTP──►  app container (Strategy.Host, non-root)
 Browser (vendor) ──TLS──►        "                          │      ├─ PostgreSQL (runtime role; audit append-only)
 API clients (PAT)──TLS──►        "                          │      ├─ files volume / S3-compatible store
 MCP clients (OAuth)─TLS─►        "                          │      ├─ Gotenberg (PDF), ClamAV (optional), Ollama (optional)
                                                             │      └─ outbound: AI providers, IdPs (OIDC/SAML/UAE Pass), LDAP, SMTP
```

| Boundary | Crossing | Trust |
|---|---|---|
| B1 Internet / intranet → reverse proxy | every request | untrusted |
| B2 Reverse proxy → app | `X-Forwarded-*` only from `ForwardedHeaders:KnownProxies` | proxy trusted, clients not |
| B3 App → PostgreSQL | runtime role (no DDL, audit SELECT + INSERT only) | trusted, least privilege |
| B4 App → sidecars | private compose network | trusted operator configuration |
| B5 App → external providers | administrator-entered URLs (ADR 0155) | untrusted responses, guarded destinations |
| B6 Internal user ↔ vendor (Wasel) | separate principal type, cookie path, DTOs | vendors untrusted |

## 2. Web UI and REST API (staff)

| STRIDE | Threat | Control | Residual |
|---|---|---|---|
| S | Credential stuffing, brute force | Lockout (5 / 15 min), auth rate limit (30 / min / IP), MFA required for administrators, generic sign-in errors (IT `SignInTests`) | No breached-password list (V2.1.7) |
| S | Session hijacking | HttpOnly, SameSite, Secure over HTTPS / `Security:SecureCookies`; server-side sessions with idle 30 min / absolute 12 h; revoke on logout and password change | — |
| T | CSRF | Antiforgery header token bound to the user on every state-changing request (IT `PipelineTests`) | — |
| T | XSS | React escaping, CSP `script-src 'self'` without inline scripts, rich text shown as text | — |
| T | Parameter tampering / mass assignment | Command input records, server validation, optimistic concurrency (409) | — |
| R | User denies an action | Hash-chained, append-only audit trail with user, on-behalf-of, channel, IP, correlation id; daily chain verification and Health check | — |
| I | IDOR / out-of-scope records | Scope applied in queries; 404 outside scope (IT `AuthorizationMatrixTests`, module negative tests) | — |
| I | Error details leak | Problem Details with message keys and trace id; no stack traces in Production | — |
| D | Request floods | Per-IP fixed window on `/api`, `/mcp`, `/connect`; paging ≤ 200; upload size limit | Volumetric DDoS is the network's job |
| E | Privilege escalation through role editing | Role / permission changes require `access.*` permissions, audited; administrators need MFA | — |

## 3. Wasel vendor portal

| STRIDE | Threat | Control | Residual |
|---|---|---|---|
| S | Vendor account takeover | Mandatory MFA for vendors, own cookie (`/api/wasel` path, SameSite Strict), lockout; UAE Pass sign-in matches the vendor by e-mail and still requires the second factor | — |
| S | Vendor uses internal APIs | Policy scheme forwards `/api/wasel` to the vendor cookie only; the vendor cookie is never read elsewhere; vendor principals have no MCP / AI access (ADR 0110) | — |
| I | Vendor sees internal fields or other vendors' data | Separate vendor-visible DTOs (ADR 0113); authorisation through vendor record roles per project (ADR 0111) (IT `WaselTests`) | — |
| T | Vendor modifies approved data | Vendor submissions are drafts reviewed by the PM before they change the project (ADR 0114) | — |
| D | Dormant accounts | Access expiry and dormant-account job (`portfolio.wasel-access`) | — |
| R | Vendor disputes a submission | Audit channel `wasel` on every write | — |

## 4. Personal access tokens (REST API)

| STRIDE | Threat | Control | Residual |
|---|---|---|---|
| S | Token theft / guessing | `sst_` prefix (secret scanning) + 32-character CSPRNG secret, stored as SHA-256 only, shown once; expiry capped (default 90 / max 365 days) (IT `AccessTokenAndSettingsTests`) | A leaked token works until expiry or revocation |
| E | Token does more than intended | Scopes (`api.read`, write); the token acts as the user through `IAccessService` | — |
| R | Unattributed API calls | Audit channel `api` with the token id | — |
| I | Token in logs or URLs | Only in the `Authorization` header; request logs contain path, no headers | — |

## 5. MCP server (OAuth 2.1)

| STRIDE | Threat | Control | Residual |
|---|---|---|---|
| S | Malicious client registration | Dynamic registration allowed but every grant needs the user's consent screen and sign-in (with MFA); PKCE required; redirect URIs exact match | Users can approve a malicious local client — consent screen shows client name and redirect host |
| E | AI agent exceeds the user's rights | Tools call the same access service as the user; write tools off by default; approve / reject tools never enabled by default (CLAUDE.md §13) (IT `McpServerTests`) | Prompt injection may make an agent call allowed tools the user did not intend; write tools must be enabled consciously |
| D | Tool-call floods | Per-user MCP rate limits | — |
| R | Agent actions unattributed | Audit channel `mcp` with tool, parameters, result | — |
| I | Token leakage | Short access tokens, revocable refresh tokens; HTTPS required (`Mcp:RequireHttps`) | — |

## 6. AI providers

| STRIDE | Threat | Control | Residual |
|---|---|---|---|
| I | Personal / restricted data sent to a cloud provider | Master and per-feature switches, per-system field exclusions, PII masking, self-hosted option (ADR 0028–0030) | Text inside uploaded documents is sent when document extraction is on |
| T | Prompt injection from documents | Document text passed as delimited data; structured JSON output validated against a schema; AI output is a draft a person accepts (CLAUDE.md §12) | Misleading drafts — users decide |
| S / I | SSRF through the provider endpoint | Outbound URL policy at save and at connect time, no redirects (ADR 0155) | — |
| I | Key disclosure | Keys encrypted with Data Protection, never returned, not audited (IT `Provider_keys_are_never_returned_and_never_written_to_the_audit_trail`) | — |
| D | Cost / quota exhaustion | Monthly token and per-user request limits; time-outs | — |

## 7. Identity providers (OIDC, SAML 2.0, UAE Pass, LDAP)

| STRIDE | Threat | Control | Residual |
|---|---|---|---|
| S | Forged assertion / token | Standard handlers validate signatures, issuer, audience, nonce, PKCE; SAML signature required (Sustainsys) | — |
| S | Account linking to the wrong user | Match field configurable (e-mail / employee number); auto-link and JIT creation off by default; local MFA after SSO optional | — |
| I / T | SSRF through metadata / authority URLs | Save-time policy; OIDC / UAE Pass back channel uses the guarded handler (ADR 0155) | SAML metadata download and LDAP connections are checked at save time only |
| T | LDAP injection | RFC 4515 escaping of user input | — |
| I | Credentials in transit to LDAP | LDAPS default, StartTLS option; `none` only by explicit choice | — |

## 8. Outbound e-mail (SMTP) and other outbound calls

| STRIDE | Threat | Control | Residual |
|---|---|---|---|
| I | E-mail content intercepted | STARTTLS / TLS settings; links instead of content for sensitive records | Depends on the customer's mail server |
| T | Header injection | MailKit address objects | — |
| I | SMTP password disclosure | Encrypted, never returned (IT `Smtp_password_is_stored_encrypted_and_never_returned`) | — |
| D | Mail outage blocks business | Outbox with retry; e-mails never block a transaction; Health page shows failures and late messages | — |

## 9. Files

| STRIDE | Threat | Control | Residual |
|---|---|---|---|
| T | Malware upload | Extension allow-list + magic numbers, size limit, optional ClamAV with "scan required" hold (IT `FileServiceTests`) | ClamAV optional |
| I | Unauthorised download | Download endpoint checks the owner record's access, 404 outside scope, audited | — |
| E | Path traversal / execution | Server-generated keys, root check, store outside web root | — |
| D | Disk exhaustion | Upload limit; Health page disk warning (15 % / 2 GB) and error (5 % / 500 MB) | No per-user quota |

## 10. Installation and operations

| STRIDE | Threat | Control | Residual |
|---|---|---|---|
| E | First-run takeover before the administrator exists | Setup wizard only while no administrator exists, one-time setup token printed in the logs (ADR 0150) | Anyone with log access during first start can complete setup |
| I | Secrets in images or logs | `.env` outside the image, Data Protection key ring in the DB; Trivy secret scan clean | `.env` file permissions are the operator's duty |
| T | Audit tampering by a DBA | Runtime role cannot UPDATE / DELETE; triggers; hash chain with checkpoints; Health check | The owner role can disable triggers — chain verification detects edits |
| D | Loss of data | Backup / restore scripts with verification; upgrade with mandatory pre-upgrade backup | Off-site copies are the operator's duty |
