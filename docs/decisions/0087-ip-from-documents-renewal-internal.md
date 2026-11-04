# 0087 — IP from published documents, expiry, renewal and internal-only protection

- **Status:** Accepted by the owner (Phase 14, before the pull request)
- **Context:** SPEC §0.0 and §6.2.4: publishing a document whose type is flagged "Creates IP register entry" creates an IP record linked to
  the document; SPEC §7.2.4–6: expiry default registration date + 20 years per protection type, reminders before expiry (e.g. 6 and 3
  months), renewal as a new period, internal-only protection registered internally.

## Decision
- Document Registry publishes `DocumentPublished` (Contracts) through the outbox in the publish transaction (ADR 0043). IP handles it
  idempotently (unique on the document version): record `source = document`, status `approved_to_file` (already reviewed), property type
  `document`, owner unit from the document, requester and owner as contributors (author, equal shares, Emirates ID to be completed),
  Links-registry link to the document. Types not flagged never create records.
- Term in years, **the date the term counts from** (`registration` or `publication`) and an internal-only flag per protection type
  (Settings → IP). Seed, as decided by the owner: **copyright 50 years from the first publication** (works owned by the organisation);
  patent 20 and industrial design 20 years from registration; **utility model 10 years** from registration; trademark 10 years from
  registration, renewable; trade secret internal-only, no expiry. The registration action records the first publication date (records
  created from a published document take the document's publication date) and proposes the expiry from the rule; for a publication-based
  term without that date the office must give it or an explicit expiry. The office may always edit the proposed expiry.
- Internal-only: action "Register internally" (only offered for internal-only protection types) gives an internal number
  (`IP-INT-{YYYY}-{SEQ:4}`) and status `registered`.
- Daily job `ip.renewals`: reminders at 6 and 3 months (settings) once per expiry; at expiry → `expired`. Renewal adds a registration
  period and sets `renewed`.

## Consequences
An IP record created from a document still needs the office to complete contributors before filing; the MOET form shows the gaps.
