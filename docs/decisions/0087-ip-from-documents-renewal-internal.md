# 0087 — IP from published documents, expiry, renewal and internal-only protection

- **Status:** Proposed (Phase 14) — for owner confirmation
- **Context:** SPEC §0.0 and §6.2.4: publishing a document whose type is flagged "Creates IP register entry" creates an IP record linked to
  the document; SPEC §7.2.4–6: expiry default registration date + 20 years per protection type, reminders before expiry (e.g. 6 and 3
  months), renewal as a new period, internal-only protection registered internally.

## Decision
- Document Registry publishes `DocumentPublished` (Contracts) through the outbox in the publish transaction (ADR 0043). IP handles it
  idempotently (unique on the document version): record `source = document`, status `approved_to_file` (already reviewed), property type
  `document`, owner unit from the document, requester and owner as contributors (author, equal shares, Emirates ID to be completed),
  Links-registry link to the document. Types not flagged never create records.
- Term in years and internal-only flag per protection type (Settings → IP; seed 20 years; trade secret internal-only, no expiry). The
  registration form proposes registration date + term; the office may edit it.
- Internal-only: action "Register internally" (only offered for internal-only protection types) gives an internal number
  (`IP-INT-{YYYY}-{SEQ:4}`) and status `registered`.
- Daily job `ip.renewals`: reminders at 6 and 3 months (settings) once per expiry; at expiry → `expired`. Renewal adds a registration
  period and sets `renewed`.

## Consequences
An IP record created from a document still needs the office to complete contributors before filing; the MOET form shows the gaps.
