# 0044 — Formation memo, certificates and the "head of the organisation" approver

- **Status:** Accepted (Phase 7)
- **Context:** SPEC §4.5 T1.3: on formation approval the system generates a numbered bilingual memo PDF from a template, or the user uploads
  an external memo. T4: certificates of appreciation as bilingual PDFs. Q-T5: formation chain Department Director of the owning unit →
  **General Manager**. The approver rules (ADR 0025) had no way to say "the head of the organisation" without a hard-coded unit id.

## Decision
- **Memo:** the approval hook (inside the engine's transaction) sets the memo number (numbering `teams.memo`, default
  `MEMO-{YYYY}-{SEQ:4}`) and date and enqueues outbox message `teams.render-memo`. The handler builds the HTML (organisation names and colour
  from branding; Arabic column right, English left; members table; approval history) and renders it through the Phase 3 `IPdfRenderer`
  (Gotenberg), then stores it through the Files service (purpose `teams.generated`, owner the team) and sets `memo_file_id`. A rendering
  failure retries via the outbox and never rolls the approval back. `memo_mode = external` skips generation: the requester / administrator
  uploads the signed memo with its number and date. Administrators may regenerate.
- **Certificates:** same mechanism after rewards approval, one PDF per member with a certificate (`teams.certificate`,
  `CERT-{YYYY}-{SEQ:4}`).
- **Approver subject `organisation`:** `ApproverRuleSubjects.Organisation` = the top unit of the record's unit path (the root of the
  organisation tree). `headOfUnit` with `Of = organisation` resolves the General Manager / Director General without a unit id; the Approval
  Settings centre offers it. Seeded Q-T5 chain: `departmentDirector` (of record) → `headOfUnit` (of organisation).

## Consequences
The memo is available a few seconds after approval (the team page shows "Memo being generated"). Templates are code (HTML builder) with
labels from the database / branding; an installation changes texts through the team's fields and branding, not by editing HTML.
