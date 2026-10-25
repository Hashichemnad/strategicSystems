# 0012 — Data Protection key storage and secret encryption

- **Status:** Accepted (Phase 1); closes the Phase 0 known gap
- **Context:**
  - Cookies, two-factor state and stored secrets depend on ASP.NET Core Data Protection keys.
  - Keys held in memory or in a container's file system are lost on restart, which signs everyone out and makes stored secrets unreadable.
  - CLAUDE.md §6 requires secrets to be encrypted at rest and keys to be backed up.

## Decision
- **Key storage.** Keys are persisted in PostgreSQL (`core.data_protection_keys`) via `Microsoft.AspNetCore.DataProtection.EntityFrameworkCore`. They are therefore part of every database backup. The application name is fixed to `Strategy`, so key isolation does not depend on the install path.
- **Key encryption at rest** (optional, recommended for production): if `DataProtection:CertificatePath` (+ `DataProtection:CertificatePassword`) is set, the key ring is encrypted with that X.509 certificate (`ProtectKeysWithCertificate`). The certificate must be backed up together with the database. The install guide documents this.
- **Application secrets** use `ISecretProtector` (`Protect` / `Unprotect` with a purpose string per kind):

  | Kind | Purpose string |
  |---|---|
  | provider secret | `identity.provider.secret.v1` |
  | SMTP password | `identity.smtp.password.v1` |
  | SAML SP key | `identity.saml.spkey.v1` |

  - Ciphertext is stored, and the API returns only `hasSecret`.
  - Authenticator (TOTP) keys are stored through `IProtectedUserStore` with `ProtectPersonalData`-style encryption via the same protector.
- **Never logged.** Secrets are excluded from logs and Problem Details.

## Consequences
- Restoring a database without its certificate (when one is configured) makes secrets unreadable. The upgrade/backup guide (Phase 18) repeats this.
- Tests prove that keys survive a host restart, and that a stored secret is not plaintext in the database.
