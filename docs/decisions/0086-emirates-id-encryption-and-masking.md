# 0086 — Emirates ID: encryption, masking and who may see it

- **Status:** Proposed (Phase 14) — for owner confirmation
- **Context:** SPEC §7.3: contributor Emirates ID needed on the MOET form, stored encrypted, shown masked, visible only to IP administrators.
  CLAUDE.md §6: sensitive values encrypted with Data Protection, masked in UI and logs.

## Decision
- Request forms have a `secret` field type (optional `format: emiratesId`, `784-YYYY-NNNNNNN-N`). The framework encrypts the value before
  it reaches the entity (`{"$enc": cipher, "last": "N"}`, purpose `requests.form.secret.v1`); responses, request events, audit values
  (ciphertext only), notifications and AI payloads carry the mask `784-****-*******-N`. Sending the mask back keeps the stored value.
- IP contributors store the value encrypted (`ip.contributor.emirates-id.v1`, `[AuditMasked]`) with its last digit for the mask.
- Clear value only through: **Reveal** (`ip.contributor.sensitive`, audited `ip.emiratesId.revealed`) and the **MOET form PDF**
  (same permission, audited export). The permission is in the *IP administrator* role only (MFA).
- Request form values are never written to logs; tests check API responses, audit entries, outbox payloads and log output.

## Consequences
Restoring a database requires its Data Protection keys (ADR 0012). Searching by Emirates ID is not possible (by design).
